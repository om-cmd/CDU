from __future__ import annotations

import io
from datetime import timedelta
from typing import Any

import numpy as np
import pandas as pd
from sklearn.ensemble import HistGradientBoostingRegressor, RandomForestRegressor
from sklearn.inspection import permutation_importance
from sklearn.metrics import mean_absolute_error, r2_score

from app.schemas import CrimeAnalysisRequest
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
    for column in ["file_number", "crime_type", "neighborhood", "reporting_area", "location"]:
        if column not in result:
            result[column] = None

    date_source = result["crime_date_time"] if "crime_date_time" in result else result.get("date_of_report")
    result["incident_at"] = pd.to_datetime(date_source, errors="coerce")
    fallback = pd.to_datetime(result.get("date_of_report"), errors="coerce")
    result["incident_at"] = result["incident_at"].fillna(fallback)
    result = result.dropna(subset=["incident_at"])

    result["crime_type"] = result["crime_type"].fillna("Unknown").astype(str)
    result["neighborhood"] = result["neighborhood"].fillna("Unknown").astype(str)
    result["reporting_area"] = result["reporting_area"].fillna("Unknown").astype(str)
    result["latitude"] = pd.to_numeric(result.get("latitude"), errors="coerce")
    result["longitude"] = pd.to_numeric(result.get("longitude"), errors="coerce")
    return result


def analyze_dataframe(frame: pd.DataFrame, frequency: str = "D", forecast_periods: int = 30) -> dict:
    if frame.empty:
        raise ValueError("No usable crime records were supplied.")

    frequency = "W" if frequency.upper().startswith("W") else "D"
    forecast_periods = int(max(1, min(forecast_periods, 90)))

    enriched = add_severity_columns(frame)
    series = build_time_series(enriched, frequency)
    count_forecast = train_and_forecast(series, target="crime_count", periods=forecast_periods, frequency=frequency)
    severity_forecast = train_and_forecast(series, target="severity_total", periods=forecast_periods, frequency=frequency)

    return {
        "summary": build_summary(enriched),
        "severity": build_severity_summary(enriched),
        "timeSeries": series.tail(120).reset_index().to_dict(orient="records"),
        "forecasts": {
            "crimeCount": count_forecast,
            "severityTotal": severity_forecast,
        },
        "hotspots": build_hotspots(enriched),
        "methodology": {
            "severityScoring": "Rule-based NIBRS-style offence labelling, imprisonment-day harm weights, ONS-inspired proportionality multipliers, log-normalised CSS.",
            "forecasting": "Chronological split with lag, rolling, cyclical calendar, count, and severity features. Primary model is HistGradientBoostingRegressor with RandomForest fallback.",
            "frequency": frequency,
        },
    }


def build_summary(frame: pd.DataFrame) -> dict:
    yearly = frame.groupby(frame["incident_at"].dt.year).size().sort_index()
    top_type = frame["crime_type"].value_counts().head(1)
    top_neighborhood = frame["neighborhood"].value_counts().head(1)
    return {
        "totalReports": int(len(frame)),
        "dateFrom": frame["incident_at"].min().date().isoformat(),
        "dateTo": frame["incident_at"].max().date().isoformat(),
        "uniqueCrimeTypes": int(frame["crime_type"].nunique()),
        "uniqueNeighborhoods": int(frame["neighborhood"].nunique()),
        "topCrimeType": None if top_type.empty else top_type.index[0],
        "topCrimeTypeCount": 0 if top_type.empty else int(top_type.iloc[0]),
        "highestCrimeNeighborhood": None if top_neighborhood.empty else top_neighborhood.index[0],
        "highestCrimeNeighborhoodCount": 0 if top_neighborhood.empty else int(top_neighborhood.iloc[0]),
        "yearlyCounts": [{"year": int(year), "count": int(count)} for year, count in yearly.items()],
    }


def build_severity_summary(frame: pd.DataFrame) -> dict:
    by_type = (
        frame.groupby("crime_type", dropna=False)
        .agg(count=("crime_type", "size"), meanCss=("css", "mean"), totalCss=("css", "sum"))
        .sort_values(["totalCss", "count"], ascending=False)
        .head(15)
        .reset_index()
    )
    return {
        "meanCss": float(frame["css"].mean()),
        "medianCss": float(frame["css"].median()),
        "maxCss": float(frame["css"].max()),
        "topSeverityCrimeTypes": by_type.to_dict(orient="records"),
    }


def build_hotspots(frame: pd.DataFrame) -> list[dict]:
    grouped = (
        frame.groupby(["neighborhood", "crime_type"], dropna=False)
        .agg(
            count=("crime_type", "size"),
            totalCss=("css", "sum"),
            meanCss=("css", "mean"),
            latitude=("latitude", "mean"),
            longitude=("longitude", "mean"),
        )
        .reset_index()
    )
    grouped["riskScore"] = grouped["count"] * 0.45 + grouped["totalCss"] * 0.55
    return grouped.sort_values("riskScore", ascending=False).head(20).to_dict(orient="records")


def build_time_series(frame: pd.DataFrame, frequency: str) -> pd.DataFrame:
    indexed = frame.set_index("incident_at").sort_index()
    series = indexed.resample(frequency).agg(
        crime_count=("crime_type", "size"),
        severity_total=("css", "sum"),
        severity_mean=("css", "mean"),
        unique_crime_types=("crime_type", "nunique"),
        unique_neighborhoods=("neighborhood", "nunique"),
    )
    series["severity_mean"] = series["severity_mean"].fillna(0)
    series = series.fillna(0)
    return add_features(series)


def add_features(series: pd.DataFrame) -> pd.DataFrame:
    result = series.copy()
    index = result.index
    result["year"] = index.year
    result["month"] = index.month
    result["day_of_week"] = index.dayofweek
    result["is_weekend"] = (index.dayofweek >= 5).astype(int)
    result["month_sin"] = np.sin(2 * np.pi * result["month"] / 12)
    result["month_cos"] = np.cos(2 * np.pi * result["month"] / 12)
    result["dow_sin"] = np.sin(2 * np.pi * result["day_of_week"] / 7)
    result["dow_cos"] = np.cos(2 * np.pi * result["day_of_week"] / 7)

    for column in ["crime_count", "severity_total"]:
        for lag in [1, 2, 3, 7, 14]:
            result[f"{column}_lag_{lag}"] = result[column].shift(lag)
        for window in [3, 7, 14, 28]:
            result[f"{column}_roll_mean_{window}"] = result[column].shift(1).rolling(window).mean()
            result[f"{column}_roll_std_{window}"] = result[column].shift(1).rolling(window).std()

    return result.fillna(0)


def train_and_forecast(series: pd.DataFrame, target: str, periods: int, frequency: str) -> dict:
    feature_columns = [column for column in series.columns if column != target]
    model_frame = series.copy()
    x = model_frame[feature_columns]
    y = model_frame[target]

    if len(model_frame) < 30:
        return naive_forecast(series, target, periods, frequency, reason="Not enough periods for machine-learning split.")

    split = max(1, int(len(model_frame) * 0.85))
    x_train, x_test = x.iloc[:split], x.iloc[split:]
    y_train, y_test = y.iloc[:split], y.iloc[split:]

    model = HistGradientBoostingRegressor(max_iter=250, learning_rate=0.06, l2_regularization=0.05, random_state=42)
    try:
        model.fit(x_train, y_train)
    except ValueError:
        model = RandomForestRegressor(n_estimators=200, min_samples_leaf=2, random_state=42, n_jobs=-1)
        model.fit(x_train, y_train)

    predictions = np.maximum(model.predict(x_test), 0) if len(x_test) else np.array([])
    metrics = {
        "mae": None if len(y_test) == 0 else float(mean_absolute_error(y_test, predictions)),
        "r2": None if len(y_test) < 2 else float(r2_score(y_test, predictions)),
    }

    importance = feature_importance(model, x_test if len(x_test) else x_train, y_test if len(y_test) else y_train, feature_columns)
    future_rows = recursive_forecast(series, model, feature_columns, target, periods, frequency)

    return {
        "target": target,
        "model": model.__class__.__name__,
        "metrics": metrics,
        "featureImportance": importance,
        "history": [
            {"period": idx.date().isoformat(), "actual": float(value)}
            for idx, value in series[target].tail(60).items()
        ],
        "future": future_rows,
    }


def feature_importance(model, x: pd.DataFrame, y: pd.Series, feature_columns: list[str]) -> list[dict]:
    if len(x) < 5:
        return []
    try:
        result = permutation_importance(model, x, y, n_repeats=5, random_state=42)
        pairs = sorted(zip(feature_columns, result.importances_mean), key=lambda item: item[1], reverse=True)
        return [{"feature": name, "importance": float(score)} for name, score in pairs[:12]]
    except Exception:
        return []


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
        next_row = pd.DataFrame(
            {
                "crime_count": [working["crime_count"].tail(14).mean()],
                "severity_total": [working["severity_total"].tail(14).mean()],
                "severity_mean": [working["severity_mean"].tail(14).mean()],
                "unique_crime_types": [working["unique_crime_types"].tail(14).mean()],
                "unique_neighborhoods": [working["unique_neighborhoods"].tail(14).mean()],
            },
            index=[next_index],
        )
        working = pd.concat([working[["crime_count", "severity_total", "severity_mean", "unique_crime_types", "unique_neighborhoods"]], next_row])
        working = add_features(working)
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
        "metrics": {"mae": None, "r2": None},
        "warning": reason,
        "featureImportance": [],
        "history": [{"period": idx.date().isoformat(), "actual": float(value)} for idx, value in series[target].tail(60).items()],
        "future": [
            {"period": (start + step * i).date().isoformat(), "predicted": baseline}
            for i in range(1, periods + 1)
        ],
    }
