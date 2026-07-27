from __future__ import annotations

import io
from datetime import datetime, timedelta, timezone
from typing import Any

import numpy as np
import pandas as pd
from catboost import CatBoostError, CatBoostRegressor
from sklearn.base import clone
from sklearn.ensemble import ExtraTreesRegressor, GradientBoostingRegressor, HistGradientBoostingRegressor, RandomForestRegressor
from sklearn.inspection import permutation_importance
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import RobustScaler, StandardScaler

from app.schemas import CrimeAnalysisRequest
from app.services.adaptive_forecasting import train_source_aware_forecasts
from app.services.preprocessing import preprocess_for_analysis
from app.services.severity import add_severity_columns


CSV_COLUMN_MAP = {
    "File Number": "file_number",
    "Date of Report": "date_of_report",
    "Crime Date Time": "crime_date_time",
    "Crime": "crime_type",
    "Reporting Area": "reporting_area",
    "Neighborhood": "neighborhood",
    "Location": "location",
    "Reporting Area Lat": "latitude",
    "Reporting Area Lon": "longitude",
}

JSON_COLUMN_MAP = {
    "crimeReportId": "crime_report_id",
    "fileNumber": "file_number",
    "dateOfReport": "date_of_report",
    "crimeDateTime": "crime_date_time",
    "crimeDateTimeRaw": "crime_date_time_raw",
    "crimeType": "crime_type",
    "reportingArea": "reporting_area",
    "neighborhood": "neighborhood",
    "jurisdiction": "jurisdiction",
    "dataSource": "data_source",
    "eventCount": "event_count",
}


def request_to_dataframe(request: CrimeAnalysisRequest) -> pd.DataFrame:
    records = [record.model_dump(mode="json") for record in request.records]
    frame = pd.DataFrame(records).rename(columns=JSON_COLUMN_MAP)
    return normalize_frame(frame)


async def parse_csv_upload(file: Any) -> pd.DataFrame:
    content = await file.read()
    frame = pd.read_csv(io.BytesIO(content), low_memory=False).rename(columns=CSV_COLUMN_MAP)
    return normalize_frame(frame)


def normalize_frame(frame: pd.DataFrame) -> pd.DataFrame:
    result = frame.copy()
    for column in [
        "file_number",
        "crime_type",
        "neighborhood",
        "reporting_area",
        "location",
        "jurisdiction",
        "data_source",
    ]:
        if column not in result:
            result[column] = None
    if "event_count" not in result:
        result["event_count"] = 1

    date_source = result["crime_date_time"] if "crime_date_time" in result else result.get("date_of_report")
    result["incident_at"] = pd.to_datetime(date_source, errors="coerce")
    fallback = pd.to_datetime(result.get("date_of_report"), errors="coerce")
    result["incident_at"] = result["incident_at"].fillna(fallback)
    result = result.dropna(subset=["incident_at"])

    result["crime_type"] = result["crime_type"].fillna("Unknown").astype(str)
    result["neighborhood"] = result["neighborhood"].fillna("Unknown").astype(str)
    result["reporting_area"] = result["reporting_area"].fillna("Unknown").astype(str)
    result["jurisdiction"] = result["jurisdiction"].fillna("Unspecified").astype(str)
    result["data_source"] = result["data_source"].fillna("Unspecified").astype(str)
    result["event_count"] = (
        pd.to_numeric(result["event_count"], errors="coerce")
        .fillna(1)
        .clip(lower=1)
        .astype(int)
    )
    result["latitude"] = pd.to_numeric(result.get("latitude"), errors="coerce")
    result["longitude"] = pd.to_numeric(result.get("longitude"), errors="coerce")
    return result


def analyze_dataframe(frame: pd.DataFrame, frequency: str = "D", forecast_periods: int = 30) -> dict:
    if frame.empty:
        raise ValueError("No usable crime records were supplied.")

    frequency = "W" if frequency.upper().startswith("W") else "D"
    forecast_periods = int(max(1, min(forecast_periods, 90)))

    preprocessed = preprocess_for_analysis(frame)
    enriched = add_severity_columns(preprocessed.frame)
    for column in ["css", "css_fbi_bc", "css_ons_bc"]:
        enriched[f"{column}_weighted"] = enriched[column] * enriched["event_count"]
    input_events = int(frame["event_count"].sum())
    usable_events = int(enriched["event_count"].sum())
    preprocessed.report.update(
        {
            "inputAggregateRows": int(len(frame)),
            "usableAggregateRows": int(len(enriched)),
            "inputRows": input_events,
            "usableRows": usable_events,
            "droppedRows": input_events - usable_events,
        }
    )
    series = build_time_series(enriched, frequency)
    if frequency == "W" and len(series) >= 60:
        preprocessed.report["scaling"] = {
            "appliedToModelFeatures": True,
            "method": "Paper FBI Box-Cox harm transformation plus first-difference level adaptation.",
            "fitScope": "The fixed paper transformation and all delta models are rebuilt from each chronological development split. CatBoost, XGBoost, and LightGBM do not use StandardScaler.",
        }
        count_forecast, severity_forecast = train_source_aware_forecasts(
            series,
            periods=forecast_periods,
        )
    else:
        count_forecast = train_and_forecast(
            series,
            target="crime_count",
            periods=forecast_periods,
            frequency=frequency,
        )
        severity_forecast = train_and_forecast(
            series,
            target="severity_total",
            periods=forecast_periods,
            frequency=frequency,
        )
    completed_at = datetime.now(timezone.utc).isoformat()

    result = {
        "trainingRun": {
            "retrained": True,
            "completedAtUtc": completed_at,
            "inputRows": input_events,
            "usableRows": usable_events,
            "aggregateRowsReceived": int(len(frame)),
            "timeSeriesPeriods": int(len(series)),
            "latestIncidentDate": enriched["incident_at"].max().date().isoformat(),
            "splitStrategy": "Chronological 70% train / 15% validation / 15% untouched test",
            "selectionStrategy": "Both weekly targets use source-aware first-difference variants of the paper's CatBoost, XGBoost, and LightGBM families. Each target selects its lowest validation-MAE family before the untouched test period.",
            "scalingStrategy": "Weekly harm retains the paper's fixed-power FBI Box-Cox severity transformation (lambda 0.3229). Level and source-coverage changes are modelled directly; scale-invariant tree candidates remain unscaled and are rebuilt for every request.",
        },
        "summary": build_summary(enriched),
        "dataQuality": preprocessed.report,
        "severity": build_severity_summary(enriched),
        "timeSeries": series.tail(120).reset_index().to_dict(orient="records"),
        "forecasts": {
            "crimeCount": count_forecast,
            "severityTotal": severity_forecast,
        },
        "hotspots": build_hotspots(enriched),
        "methodology": {
            "severityScoring": "Rule-based NIBRS-style offence labelling, imprisonment-day harm weights, ONS-inspired proportionality multipliers, log-normalised harm targets, and fixed-power FBI/ONS Box-Cox forecasting features.",
            "preprocessing": "Rejects columns/rows over 60% missingness, removes invalid dates/coordinates/negative age anomalies, de-duplicates records, label-encodes categorical crime fields, and refits applicable scalers inside each chronological training fold.",
            "forecasting": "Worldwide-ready weekly evaluation first aggregates weighted incidents by time while retaining jurisdiction and data-source coverage. It predicts the change from the previous observed level using CatBoost, XGBoost, or LightGBM, then restores the level. This preserves the research model families while allowing adaptation when a new jurisdiction changes the scale.",
            "evaluation": "MAE and RMSE are scale-dependent errors; R-squared measures variance explained; sMAPE is a zero-safe percentage error; displayed forecast accuracy is max(0, 100 - sMAPE); baseline improvement compares MAE with a last-week adaptive forecast.",
            "frequency": frequency,
        },
    }
    return make_json_safe(result)


def make_json_safe(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: make_json_safe(item) for key, item in value.items()}
    if isinstance(value, list):
        return [make_json_safe(item) for item in value]
    if isinstance(value, tuple):
        return [make_json_safe(item) for item in value]
    if isinstance(value, (float, np.floating)) and not np.isfinite(value):
        return None
    return value


def build_summary(frame: pd.DataFrame) -> dict:
    yearly = frame.groupby(frame["incident_at"].dt.year)["event_count"].sum().sort_index()
    type_counts = frame.groupby("crime_type")["event_count"].sum().sort_values(ascending=False)
    neighborhood_counts = (
        frame.groupby("neighborhood")["event_count"].sum().sort_values(ascending=False)
    )
    return {
        "totalReports": int(frame["event_count"].sum()),
        "dateFrom": frame["incident_at"].min().date().isoformat(),
        "dateTo": frame["incident_at"].max().date().isoformat(),
        "uniqueCrimeTypes": int(frame["crime_type"].nunique()),
        "uniqueNeighborhoods": int(frame["neighborhood"].nunique()),
        "topCrimeType": None if type_counts.empty else type_counts.index[0],
        "topCrimeTypeCount": 0 if type_counts.empty else int(type_counts.iloc[0]),
        "highestCrimeNeighborhood": None if neighborhood_counts.empty else neighborhood_counts.index[0],
        "highestCrimeNeighborhoodCount": 0 if neighborhood_counts.empty else int(neighborhood_counts.iloc[0]),
        "yearlyCounts": [{"year": int(year), "count": int(count)} for year, count in yearly.items()],
        "jurisdictions": build_jurisdiction_summary(frame),
    }


def build_jurisdiction_summary(frame: pd.DataFrame) -> list[dict]:
    grouped = (
        frame.groupby(["jurisdiction", "data_source"], dropna=False)
        .agg(
            eventCount=("event_count", "sum"),
            aggregateRows=("event_count", "size"),
            dateFrom=("incident_at", "min"),
            dateTo=("incident_at", "max"),
        )
        .reset_index()
        .sort_values("eventCount", ascending=False)
    )
    return [
        {
            "jurisdiction": str(row.jurisdiction),
            "dataSource": str(row.data_source),
            "eventCount": int(row.eventCount),
            "aggregateRows": int(row.aggregateRows),
            "dateFrom": row.dateFrom.date().isoformat(),
            "dateTo": row.dateTo.date().isoformat(),
        }
        for row in grouped.itertuples(index=False)
    ]


def build_severity_summary(frame: pd.DataFrame) -> dict:
    by_type = (
        frame.groupby("crime_type", dropna=False)
        .agg(
            count=("event_count", "sum"),
            totalCss=("css_weighted", "sum"),
        )
        .sort_values(["totalCss", "count"], ascending=False)
        .head(15)
        .reset_index()
    )
    by_type["meanCss"] = by_type["totalCss"] / by_type["count"].clip(lower=1)
    event_total = float(frame["event_count"].sum())
    return {
        "meanCss": float(frame["css_weighted"].sum() / max(event_total, 1.0)),
        "medianCss": float(
            np.median(
                np.repeat(
                    frame["css"].to_numpy(),
                    frame["event_count"].to_numpy(),
                )
            )
        )
        if event_total <= 1_000_000
        else float(frame["css"].median()),
        "maxCss": float(frame["css"].max()),
        "topSeverityCrimeTypes": by_type.to_dict(orient="records"),
    }


def build_hotspots(frame: pd.DataFrame) -> list[dict]:
    frame = frame.copy()
    for coordinate in ["latitude", "longitude"]:
        if coordinate not in frame.columns:
            frame[coordinate] = np.nan
    grouped = (
        frame.groupby(["neighborhood", "crime_type"], dropna=False)
        .agg(
            count=("event_count", "sum"),
            totalCss=("css_weighted", "sum"),
            latitude=("latitude", "mean"),
            longitude=("longitude", "mean"),
        )
        .reset_index()
    )
    grouped["meanCss"] = grouped["totalCss"] / grouped["count"].clip(lower=1)
    grouped["riskScore"] = grouped["count"] * 0.45 + grouped["totalCss"] * 0.55
    return grouped.sort_values("riskScore", ascending=False).head(20).to_dict(orient="records")


def build_time_series(frame: pd.DataFrame, frequency: str) -> pd.DataFrame:
    indexed = frame.set_index("incident_at").sort_index()
    aggregations = {
        "crime_count": ("event_count", "sum"),
        "severity_total": ("css_weighted", "sum"),
        "severity_fbi_bc": ("css_fbi_bc_weighted", "sum"),
        "severity_ons_bc": ("css_ons_bc_weighted", "sum"),
        "unique_crime_types": ("crime_type", "nunique"),
        "unique_neighborhoods": ("neighborhood", "nunique"),
        "active_jurisdictions": ("jurisdiction", "nunique"),
        "active_data_sources": ("data_source", "nunique"),
    }
    for column in ["crime_type_code", "neighborhood_code", "reporting_area_code"]:
        if column in indexed.columns:
            aggregations[f"{column}_mean"] = (column, "mean")
            aggregations[f"{column}_diversity"] = (column, "nunique")

    series = indexed.resample(frequency).agg(
        **aggregations
    )
    series["severity_mean"] = (
        series["severity_total"] / series["crime_count"].replace(0, np.nan)
    ).fillna(0.0)
    if frequency == "W" and len(series) > 1:
        # Pandas labels weekly bins by their Sunday end date. Do not evaluate
        # a final partial week as though it were a complete observed target.
        last_incident_day = indexed.index.max().normalize()
        if last_incident_day < series.index.max().normalize():
            series = series.iloc[:-1]
    series["severity_mean"] = series["severity_mean"].fillna(0)
    series = series.fillna(0)
    return add_features(series, frequency)


def add_features(series: pd.DataFrame, frequency: str = "D") -> pd.DataFrame:
    index = series.index
    month = index.month.astype(float)
    day_of_week = index.dayofweek.astype(float)
    iso_week = index.isocalendar().week.astype(float).to_numpy()
    feature_data: dict[str, Any] = {
        "time_index": np.arange(len(series), dtype=float),
        "year": index.year.astype(float),
        "month": month,
        "day_of_week": day_of_week,
        "is_weekend": (day_of_week >= 5).astype(int),
        "month_sin": np.sin(2 * np.pi * month / 12),
        "month_cos": np.cos(2 * np.pi * month / 12),
        "dow_sin": np.sin(2 * np.pi * day_of_week / 7),
        "dow_cos": np.cos(2 * np.pi * day_of_week / 7),
        "week_sin": np.sin(2 * np.pi * iso_week / 52.1775),
        "week_cos": np.cos(2 * np.pi * iso_week / 52.1775),
    }

    if frequency == "W":
        lags = [1, 2, 3, 4, 5, 6, 7, 8, 13, 26, 52, 53]
        windows = [2, 4, 8, 13, 26, 52]
        context_window = 4
    else:
        lags = [1, 2, 3, 7, 14, 28]
        windows = [3, 7, 14, 28]
        context_window = 7

    history_columns = [
        column
        for column in ["crime_count", "severity_total", "severity_fbi_bc", "severity_ons_bc"]
        if column in series.columns
    ]
    for column in history_columns:
        shifted = series[column].shift(1)
        for lag in lags:
            feature_data[f"{column}_lag_{lag}"] = series[column].shift(lag)
        for window in windows:
            feature_data[f"{column}_roll_mean_{window}"] = shifted.rolling(window).mean()
            feature_data[f"{column}_roll_std_{window}"] = shifted.rolling(window).std()
        if frequency == "W":
            for span in [4, 13, 26, 52]:
                feature_data[f"{column}_ewm_{span}"] = shifted.ewm(span=span, adjust=False).mean()
            feature_data[f"{column}_change_1"] = shifted.diff(1)
            feature_data[f"{column}_change_4"] = shifted.diff(4)
            feature_data[f"{column}_change_52"] = shifted.diff(52)

    context_columns = [
        column
        for column in series.columns
        if column
        in {
            "severity_mean",
            "unique_crime_types",
            "unique_neighborhoods",
            "active_jurisdictions",
            "active_data_sources",
        }
        or column.endswith("_code_mean")
        or column.endswith("_code_diversity")
    ]
    for column in context_columns:
        shifted = series[column].shift(1)
        feature_data[f"{column}_lag_1"] = shifted
        feature_data[f"{column}_roll_mean_{context_window}"] = shifted.rolling(context_window).mean()

    features = pd.DataFrame(feature_data, index=index)
    return pd.concat([series.copy(), features], axis=1).fillna(0)


def train_and_forecast(series: pd.DataFrame, target: str, periods: int, frequency: str) -> dict:
    feature_columns = forecast_feature_columns(series, target, frequency)
    model_frame = series.copy()
    x = model_frame[feature_columns]
    y = model_frame[target]

    if len(model_frame) < 60:
        return naive_forecast(series, target, periods, frequency, reason="Not enough periods for machine-learning split.")

    train_end = max(1, int(len(model_frame) * 0.70))
    validation_end = max(train_end + 1, int(len(model_frame) * 0.85))
    validation_end = min(validation_end, len(model_frame) - 2)
    x_train, y_train = x.iloc[:train_end], y.iloc[:train_end]
    x_validation, y_validation = x.iloc[train_end:validation_end], y.iloc[train_end:validation_end]
    x_test, y_test = x.iloc[validation_end:], y.iloc[validation_end:]

    candidates = build_model_candidates(len(model_frame))
    validation_folds = rolling_validation_splits(len(model_frame), validation_end)
    scored_models: list[dict[str, Any]] = []
    for candidate in candidates:
        try:
            fold_metrics: list[dict[str, float | None]] = []
            for fold_train_end, fold_validation_end in validation_folds:
                fold_model = clone(candidate)
                fold_model.fit(x.iloc[:fold_train_end], y.iloc[:fold_train_end])
                fold_predictions = np.maximum(
                    fold_model.predict(x.iloc[fold_train_end:fold_validation_end]),
                    0,
                )
                fold_metrics.append(
                    regression_metrics(
                        y.iloc[fold_train_end:fold_validation_end],
                        fold_predictions,
                    )
                )

            validation_metrics = {
                key: float(np.mean([score[key] for score in fold_metrics if score[key] is not None]))
                for key in ["mae", "rmse", "r2", "mape", "smape", "accuracyPercent"]
            }
            mae_std = float(np.std([score["mae"] for score in fold_metrics]))
            scored_models.append(
                {
                    "model": candidate,
                    "name": model_name(candidate),
                    "validation": validation_metrics,
                    "maeStd": mae_std,
                    "selectionScore": validation_metrics["mae"] + 0.25 * mae_std,
                }
            )
        except (ValueError, CatBoostError):
            continue

    if not scored_models:
        return naive_forecast(series, target, periods, frequency, reason="Machine-learning models could not fit this dataset.")

    best = min(scored_models, key=lambda item: item["selectionScore"])
    evaluation_model = clone(best["model"])
    x_train_validation = pd.concat([x_train, x_validation])
    y_train_validation = pd.concat([y_train, y_validation])
    evaluation_model.fit(x_train_validation, y_train_validation)

    predictions = np.maximum(evaluation_model.predict(x_test), 0)
    metrics = regression_metrics(y_test, predictions)
    baseline_predictions = seasonal_naive_predictions(y, validation_end, frequency)
    baseline_metrics = regression_metrics(y_test, baseline_predictions)
    baseline_mae = baseline_metrics["mae"]
    metrics.update(
        {
            "baselineModel": "SeasonalNaive-7" if frequency == "D" else "SeasonalNaive-4",
            "baselineMae": baseline_mae,
            "baselineRmse": baseline_metrics["rmse"],
            "baselineR2": baseline_metrics["r2"],
            "maeImprovementPct": None if not baseline_mae else float((baseline_mae - metrics["mae"]) / baseline_mae * 100),
            "selectionMetric": "Rolling validation MAE plus stability penalty",
            "validationFolds": int(len(validation_folds)),
            "trainPeriods": int(len(x_train)),
            "validationPeriods": int(len(x_validation)),
            "testPeriods": int(len(x_test)),
            "testDateFrom": x_test.index.min().date().isoformat(),
            "testDateTo": x_test.index.max().date().isoformat(),
            "candidateScores": [
                {
                    "model": item["name"],
                    "mae": item["validation"]["mae"],
                    "rmse": item["validation"]["rmse"],
                    "r2": item["validation"]["r2"],
                    "smape": item["validation"]["smape"],
                    "maeStd": item["maeStd"],
                    "selectionScore": item["selectionScore"],
                    "selected": item is best,
                }
                for item in sorted(scored_models, key=lambda item: item["selectionScore"])
            ],
        }
    )

    importance = feature_importance(evaluation_model, x_test, y_test, feature_columns)
    final_model = clone(best["model"])
    final_model.fit(x, y)
    future_rows = recursive_forecast(series, final_model, feature_columns, target, periods, frequency)
    backtest = [
        {"period": idx.date().isoformat(), "actual": float(actual), "predicted": float(predicted)}
        for idx, actual, predicted in zip(x_test.index[-120:], y_test.iloc[-120:], predictions[-120:])
    ]

    return {
        "target": target,
        "model": model_name(final_model),
        "metrics": metrics,
        "featureImportance": importance,
        "backtest": backtest,
        "history": [
            {"period": idx.date().isoformat(), "actual": float(value)}
            for idx, value in series[target].tail(60).items()
        ],
        "future": future_rows,
    }


def rolling_validation_splits(period_count: int, development_end: int) -> list[tuple[int, int]]:
    proposed = [
        (int(period_count * 0.55), int(period_count * 0.65)),
        (int(period_count * 0.65), int(period_count * 0.75)),
        (int(period_count * 0.75), development_end),
    ]
    return [
        (train_end, validation_end)
        for train_end, validation_end in proposed
        if train_end > 0 and validation_end > train_end
    ]


def forecast_feature_columns(series: pd.DataFrame, target: str, frequency: str) -> list[str]:
    unavailable_at_forecast_time = {
        "crime_count",
        "severity_total",
        "severity_fbi_bc",
        "severity_ons_bc",
        "severity_mean",
        "unique_crime_types",
        "unique_neighborhoods",
    }
    if frequency == "W" and target == "crime_count":
        exact_columns = {
            "time_index",
            "year",
            "month_sin",
            "month_cos",
            "week_sin",
            "week_cos",
        }
        allowed_prefixes = (
            "crime_count_",
            "severity_fbi_bc_",
            "severity_total_",
            "severity_mean_",
            "unique_crime_types_",
            "unique_neighborhoods_",
            "reporting_area_code_diversity_",
        )
        return [
            column
            for column in series.columns
            if column in exact_columns or column.startswith(allowed_prefixes)
        ]

    columns: list[str] = []
    for column in series.columns:
        if column == target:
            continue
        if column in unavailable_at_forecast_time:
            continue
        if column.endswith("_mean") or column.endswith("_diversity"):
            continue
        columns.append(column)
    return columns


def build_model_candidates(period_count: int) -> list[Any]:
    leaf_size = 2 if period_count >= 90 else 1
    return [
        Pipeline(
            steps=[
                ("scaler", StandardScaler()),
                ("model", HistGradientBoostingRegressor(max_iter=350, learning_rate=0.045, l2_regularization=0.03, random_state=42)),
            ]
        ),
        Pipeline(
            steps=[
                ("scaler", RobustScaler()),
                ("model", HistGradientBoostingRegressor(max_iter=250, learning_rate=0.065, l2_regularization=0.08, random_state=73)),
            ]
        ),
        RandomForestRegressor(
            n_estimators=600,
            min_samples_leaf=leaf_size,
            max_features=0.75,
            random_state=42,
            n_jobs=4,
        ),
        ExtraTreesRegressor(
            n_estimators=260,
            min_samples_leaf=leaf_size,
            max_features="sqrt",
            random_state=84,
            n_jobs=-1,
        ),
        GradientBoostingRegressor(
            n_estimators=260,
            learning_rate=0.035,
            max_depth=3,
            loss="huber",
            random_state=126,
        ),
        CatBoostRegressor(
            iterations=350,
            learning_rate=0.045,
            depth=6,
            loss_function="RMSE",
            random_seed=168,
            verbose=False,
            allow_writing_files=False,
            thread_count=4,
        ),
    ]


def regression_metrics(actual: pd.Series, predicted: np.ndarray) -> dict[str, float | None]:
    actual_values = np.asarray(actual, dtype=float)
    predicted_values = np.asarray(predicted, dtype=float)
    mae = float(mean_absolute_error(actual_values, predicted_values))
    rmse = float(np.sqrt(mean_squared_error(actual_values, predicted_values)))
    r2 = None if len(actual_values) < 2 else float(r2_score(actual_values, predicted_values))

    nonzero = np.abs(actual_values) > 1e-9
    mape = None
    if nonzero.any():
        mape = float(np.mean(np.abs((actual_values[nonzero] - predicted_values[nonzero]) / actual_values[nonzero])) * 100)

    denominator = np.abs(actual_values) + np.abs(predicted_values)
    valid = denominator > 1e-9
    smape = 0.0 if not valid.any() else float(np.mean(2 * np.abs(predicted_values[valid] - actual_values[valid]) / denominator[valid]) * 100)
    return {
        "mae": mae,
        "rmse": rmse,
        "r2": r2,
        "mape": mape,
        "smape": smape,
        "accuracyPercent": max(0.0, 100.0 - smape),
    }


def seasonal_naive_predictions(series: pd.Series, test_start: int, frequency: str) -> np.ndarray:
    season_length = 7 if frequency == "D" else 4
    predictions: list[float] = []
    for position in range(test_start, len(series)):
        source_position = position - season_length
        if source_position >= 0:
            predictions.append(max(float(series.iloc[source_position]), 0.0))
        else:
            predictions.append(max(float(series.iloc[:position].mean()), 0.0))
    return np.asarray(predictions)


def feature_importance(model, x: pd.DataFrame, y: pd.Series, feature_columns: list[str]) -> list[dict]:
    if len(x) < 5:
        return []
    try:
        sample_x = x.tail(300)
        sample_y = y.tail(300)
        result = permutation_importance(model, sample_x, sample_y, n_repeats=3, random_state=42)
        pairs = sorted(zip(feature_columns, result.importances_mean), key=lambda item: item[1], reverse=True)
        return [{"feature": name, "importance": float(score)} for name, score in pairs[:12]]
    except Exception:
        return []


def model_name(model) -> str:
    if hasattr(model, "named_steps") and "model" in model.named_steps:
        scaler = model.named_steps.get("scaler")
        scaler_name = scaler.__class__.__name__ if scaler is not None else "NoScaler"
        return f"{scaler_name}+{model.named_steps['model'].__class__.__name__}"
    return model.__class__.__name__


def recursive_forecast(
    series: pd.DataFrame,
    model,
    feature_columns: list[str],
    target: str,
    periods: int,
    frequency: str,
) -> list[dict]:
    working = series.copy()
    step = timedelta(days=7 if frequency == "W" else 1)

    output: list[dict] = []
    for _ in range(periods):
        next_index = working.index.max() + step
        base_columns = [
            column
            for column in working.columns
            if not any(
                marker in column
                for marker in ["_lag_", "_roll_mean_", "_roll_std_", "_ewm_", "_change_"]
            )
            and column not in {
                "time_index",
                "year",
                "month",
                "day_of_week",
                "is_weekend",
                "month_sin",
                "month_cos",
                "dow_sin",
                "dow_cos",
                "week_sin",
                "week_cos",
            }
        ]
        next_values = {column: [working[column].tail(14).mean()] for column in base_columns}
        next_row = pd.DataFrame(next_values, index=[next_index])
        working = pd.concat([working[base_columns], next_row])
        working = add_features(working, frequency)
        prediction = max(float(model.predict(working[feature_columns].tail(1))[0]), 0.0)
        working.loc[next_index, target] = prediction
        output.append({"period": next_index.date().isoformat(), "predicted": prediction})

    return output


def naive_forecast(series: pd.DataFrame, target: str, periods: int, frequency: str, reason: str) -> dict:
    step = timedelta(days=7 if frequency == "W" else 1)
    baseline = float(series[target].tail(min(len(series), 7)).mean())
    start = series.index.max()
    return {
        "target": target,
        "model": "RollingMeanBaseline",
        "metrics": {"mae": None, "rmse": None, "r2": None, "mape": None, "smape": None, "accuracyPercent": None, "candidateScores": []},
        "warning": reason,
        "featureImportance": [],
        "backtest": [],
        "history": [{"period": idx.date().isoformat(), "actual": float(value)} for idx, value in series[target].tail(60).items()],
        "future": [
            {"period": (start + step * i).date().isoformat(), "predicted": baseline}
            for i in range(1, periods + 1)
        ],
    }
