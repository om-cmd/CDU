from __future__ import annotations

from typing import Any, Callable

import numpy as np
import pandas as pd
from catboost import CatBoostRegressor
from lightgbm import LGBMRegressor
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score
from xgboost import XGBRegressor


RegressorFactory = Callable[[], Any]


def build_paper_feature_frames(series: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    index = series.index
    iso_week = index.isocalendar().week.astype(float).to_numpy()
    calendar = {
        "week_sin": np.sin(2 * np.pi * iso_week / 52.1775),
        "week_cos": np.cos(2 * np.pi * iso_week / 52.1775),
    }

    severity = series["severity_fbi_bc"]
    severity_features = pd.DataFrame(
        {
            **calendar,
            "severity_lag_1": severity.shift(1),
            "severity_lag_4": severity.shift(4),
            "severity_roll_4": severity.shift(1).rolling(4).mean(),
        },
        index=index,
    ).fillna(0.0)

    count = series["crime_count"]
    count_features = pd.DataFrame(
        {
            **calendar,
            "count_lag_1": count.shift(1),
            "count_lag_4": count.shift(4),
            "count_roll_4": count.shift(1).rolling(4).mean(),
        },
        index=index,
    ).fillna(0.0)
    return severity_features, count_features


def build_catboost() -> CatBoostRegressor:
    # The paper does not publish CatBoost hyperparameters. Library defaults
    # are therefore the least assumptive reproducible interpretation.
    return CatBoostRegressor(
        random_seed=42,
        verbose=False,
        allow_writing_files=False,
        thread_count=4,
    )


def build_xgboost() -> XGBRegressor:
    return XGBRegressor(
        objective="reg:squarederror",
        random_state=42,
        n_jobs=4,
        verbosity=0,
    )


def build_lightgbm() -> LGBMRegressor:
    return LGBMRegressor(
        random_state=42,
        n_jobs=4,
        verbosity=-1,
    )


def paper_model_candidates() -> list[tuple[str, RegressorFactory]]:
    # The paper evaluates these three gradient-boosted tree families but does
    # not publish their tuned hyperparameters. Reproducible library defaults
    # are used so the documented model comparison can still be repeated.
    return [
        ("CatBoostRegressor", build_catboost),
        ("XGBRegressor", build_xgboost),
        ("LGBMRegressor", build_lightgbm),
    ]


def chronological_stage_one_predictions(
    factory: RegressorFactory,
    features: pd.DataFrame,
    target: pd.Series,
    end: int,
) -> np.ndarray:
    baseline = (
        target.shift(1)
        .rolling(4)
        .mean()
        .fillna(target.shift(1).expanding().mean())
        .fillna(0.0)
    )
    predictions = baseline.iloc[:end].to_numpy(dtype=float)

    minimum_train = max(52, int(end * 0.25))
    boundaries = np.linspace(minimum_train, end, num=6, dtype=int)
    for train_end, validation_end in zip(boundaries[:-1], boundaries[1:]):
        if validation_end <= train_end:
            continue
        model = factory()
        model.fit(features.iloc[:train_end], target.iloc[:train_end])
        predictions[train_end:validation_end] = np.maximum(
            model.predict(features.iloc[train_end:validation_end]),
            0.0,
        )
    return predictions


def fit_two_stage(
    factory: RegressorFactory,
    severity_features: pd.DataFrame,
    count_features: pd.DataFrame,
    severity_target: pd.Series,
    count_target: pd.Series,
    development_end: int,
    evaluation_end: int,
) -> tuple[np.ndarray, np.ndarray, Any, Any]:
    stage_one_training = chronological_stage_one_predictions(
        factory,
        severity_features,
        severity_target,
        development_end,
    )

    stage_one = factory()
    stage_one.fit(
        severity_features.iloc[:development_end],
        severity_target.iloc[:development_end],
    )
    stage_one_evaluation = np.maximum(
        stage_one.predict(severity_features.iloc[development_end:evaluation_end]),
        0.0,
    )

    stage_two_training = count_features.iloc[:development_end].copy()
    stage_two_training["pred_fbi_bc"] = stage_one_training
    stage_two_evaluation = count_features.iloc[development_end:evaluation_end].copy()
    stage_two_evaluation["pred_fbi_bc"] = stage_one_evaluation

    stage_two = factory()
    stage_two.fit(stage_two_training, count_target.iloc[:development_end])
    predictions = np.maximum(stage_two.predict(stage_two_evaluation), 0.0)
    return predictions, stage_one_evaluation, stage_one, stage_two


def regression_metrics(actual: pd.Series, predicted: np.ndarray) -> dict[str, float | None]:
    actual_values = actual.to_numpy(dtype=float)
    predicted_values = np.maximum(np.asarray(predicted, dtype=float), 0.0)
    mae = float(mean_absolute_error(actual_values, predicted_values))
    rmse = float(np.sqrt(mean_squared_error(actual_values, predicted_values)))
    r2 = None if len(actual_values) < 2 else float(r2_score(actual_values, predicted_values))

    nonzero = np.abs(actual_values) > 1e-9
    mape = None
    if nonzero.any():
        mape = float(
            np.mean(
                np.abs(
                    (actual_values[nonzero] - predicted_values[nonzero])
                    / actual_values[nonzero]
                )
            )
            * 100.0
        )

    denominator = np.abs(actual_values) + np.abs(predicted_values)
    valid = denominator > 1e-9
    smape = (
        0.0
        if not valid.any()
        else float(
            np.mean(
                2.0
                * np.abs(predicted_values[valid] - actual_values[valid])
                / denominator[valid]
            )
            * 100.0
        )
    )
    return {
        "mae": mae,
        "rmse": rmse,
        "r2": r2,
        "mape": mape,
        "smape": smape,
        "accuracyPercent": max(0.0, 100.0 - smape),
    }


def _future_feature_row(
    next_index: pd.Timestamp,
    count: pd.Series,
    severity: pd.Series,
) -> tuple[pd.DataFrame, pd.DataFrame]:
    week = float(next_index.isocalendar().week)
    calendar = {
        "week_sin": np.sin(2 * np.pi * week / 52.1775),
        "week_cos": np.cos(2 * np.pi * week / 52.1775),
    }
    severity_row = pd.DataFrame(
        {
            **calendar,
            "severity_lag_1": float(severity.iloc[-1]),
            "severity_lag_4": float(severity.iloc[-4]),
            "severity_roll_4": float(severity.iloc[-4:].mean()),
        },
        index=[next_index],
    )
    count_row = pd.DataFrame(
        {
            **calendar,
            "count_lag_1": float(count.iloc[-1]),
            "count_lag_4": float(count.iloc[-4]),
            "count_roll_4": float(count.iloc[-4:].mean()),
        },
        index=[next_index],
    )
    return severity_row, count_row


def recursive_two_stage_forecast(
    series: pd.DataFrame,
    stage_one: Any,
    stage_two: Any,
    periods: int,
) -> list[dict]:
    count = series["crime_count"].copy()
    severity = series["severity_fbi_bc"].copy()
    output: list[dict] = []

    for _ in range(periods):
        next_index = count.index[-1] + pd.Timedelta(days=7)
        severity_row, count_row = _future_feature_row(next_index, count, severity)
        predicted_severity = max(float(stage_one.predict(severity_row)[0]), 0.0)
        count_row["pred_fbi_bc"] = predicted_severity
        predicted_count = max(float(stage_two.predict(count_row)[0]), 0.0)
        severity.loc[next_index] = predicted_severity
        count.loc[next_index] = predicted_count
        output.append(
            {
                "period": next_index.date().isoformat(),
                "predicted": predicted_count,
            }
        )
    return output


def train_paper_style_forecasts(
    series: pd.DataFrame,
    periods: int,
) -> tuple[dict, dict]:
    severity_features, count_features = build_paper_feature_frames(series)
    severity_target = series["severity_fbi_bc"]
    count_target = series["crime_count"]
    train_end = int(len(series) * 0.70)
    validation_end = min(int(len(series) * 0.85), len(series) - 2)

    scored_candidates: list[dict[str, Any]] = []
    for family, factory in paper_model_candidates():
        count_predictions, severity_predictions, _, _ = fit_two_stage(
            factory,
            severity_features,
            count_features,
            severity_target,
            count_target,
            train_end,
            validation_end,
        )
        scored_candidates.append(
            {
                "family": family,
                "factory": factory,
                "countValidation": regression_metrics(
                    count_target.iloc[train_end:validation_end],
                    count_predictions,
                ),
                "severityValidation": regression_metrics(
                    severity_target.iloc[train_end:validation_end],
                    severity_predictions,
                ),
            }
        )

    best_count = min(
        scored_candidates,
        key=lambda item: item["countValidation"]["mae"],
    )
    best_severity = min(
        scored_candidates,
        key=lambda item: item["severityValidation"]["mae"],
    )

    count_predictions, count_stage_one_predictions, _, _ = fit_two_stage(
        best_count["factory"],
        severity_features,
        count_features,
        severity_target,
        count_target,
        validation_end,
        len(series),
    )
    if best_severity is best_count:
        severity_predictions = count_stage_one_predictions
    else:
        severity_evaluation_model = best_severity["factory"]()
        severity_evaluation_model.fit(
            severity_features.iloc[:validation_end],
            severity_target.iloc[:validation_end],
        )
        severity_predictions = np.maximum(
            severity_evaluation_model.predict(
                severity_features.iloc[validation_end:]
            ),
            0.0,
        )

    count_metrics = paper_test_metrics(
        target=count_target,
        predictions=count_predictions,
        validation_end=validation_end,
        train_end=train_end,
        series=series,
        candidate_scores=[
            {
                "model": item["family"],
                **item["countValidation"],
                "selectionScore": item["countValidation"]["mae"],
                "selected": item is best_count,
            }
            for item in sorted(
                scored_candidates,
                key=lambda item: item["countValidation"]["mae"],
            )
        ],
    )
    severity_metrics = paper_test_metrics(
        target=severity_target,
        predictions=severity_predictions,
        validation_end=validation_end,
        train_end=train_end,
        series=series,
        candidate_scores=[
            {
                "model": item["family"],
                **item["severityValidation"],
                "selectionScore": item["severityValidation"]["mae"],
                "selected": item is best_severity,
            }
            for item in sorted(
                scored_candidates,
                key=lambda item: item["severityValidation"]["mae"],
            )
        ],
    )

    final_stage_one_training = chronological_stage_one_predictions(
        best_count["factory"],
        severity_features,
        severity_target,
        len(series),
    )
    final_stage_one = best_count["factory"]()
    final_stage_one.fit(severity_features, severity_target)
    final_stage_two_features = count_features.copy()
    final_stage_two_features["pred_fbi_bc"] = final_stage_one_training
    final_stage_two = best_count["factory"]()
    final_stage_two.fit(final_stage_two_features, count_target)

    if best_severity is best_count:
        final_severity_model = final_stage_one
    else:
        final_severity_model = best_severity["factory"]()
        final_severity_model.fit(severity_features, severity_target)

    count_importance = paper_feature_importance(
        final_stage_two,
        list(final_stage_two_features.columns),
    )
    severity_importance = paper_feature_importance(
        final_severity_model,
        list(severity_features.columns),
    )
    test_index = series.index[validation_end:]

    count_result = {
        "target": "crime_count",
        "model": f"TwoStage+{best_count['family']}",
        "metrics": count_metrics,
        "featureImportance": [
            {"feature": feature, "importance": float(score)}
            for feature, score in count_importance
        ],
        "backtest": [
            {
                "period": index.date().isoformat(),
                "actual": float(actual),
                "predicted": float(predicted),
            }
            for index, actual, predicted in zip(
                test_index[-120:],
                count_target.iloc[validation_end:].iloc[-120:],
                count_predictions[-120:],
            )
        ],
        "history": [
            {"period": index.date().isoformat(), "actual": float(value)}
            for index, value in count_target.tail(60).items()
        ],
        "future": recursive_two_stage_forecast(
            series,
            final_stage_one,
            final_stage_two,
            periods,
        ),
        "methodology": {
            "label": "Paper two-stage count forecast",
            "candidateModels": ["CatBoost", "XGBoost", "LightGBM"],
            "severityVariant": "FBI Box-Cox",
            "boxCoxLambda": 0.3229,
            "countFeatures": ["lag 1", "lag 4", "rolling mean 4"],
            "severityFeature": "chronologically predicted FBI Box-Cox severity",
            "exactPaperHyperparametersAvailable": False,
        },
    }
    severity_result = {
        "target": "severity_fbi_bc",
        "model": f"PaperSeverity+{best_severity['family']}",
        "metrics": severity_metrics,
        "featureImportance": [
            {"feature": feature, "importance": float(score)}
            for feature, score in severity_importance
        ],
        "backtest": [
            {
                "period": index.date().isoformat(),
                "actual": float(actual),
                "predicted": float(predicted),
            }
            for index, actual, predicted in zip(
                test_index[-120:],
                severity_target.iloc[validation_end:].iloc[-120:],
                severity_predictions[-120:],
            )
        ],
        "history": [
            {"period": index.date().isoformat(), "actual": float(value)}
            for index, value in severity_target.tail(60).items()
        ],
        "future": recursive_severity_forecast(
            series,
            final_severity_model,
            periods,
        ),
        "methodology": {
            "label": "Paper stage-one severity forecast",
            "candidateModels": ["CatBoost", "XGBoost", "LightGBM"],
            "severityVariant": "FBI Box-Cox",
            "boxCoxLambda": 0.3229,
            "severityFeatures": ["lag 1", "lag 4", "rolling mean 4"],
            "exactPaperHyperparametersAvailable": False,
        },
    }
    return count_result, severity_result


def train_and_forecast_paper_style(series: pd.DataFrame, periods: int) -> dict:
    count_result, _ = train_paper_style_forecasts(series, periods)
    return count_result


def paper_test_metrics(
    target: pd.Series,
    predictions: np.ndarray,
    validation_end: int,
    train_end: int,
    series: pd.DataFrame,
    candidate_scores: list[dict[str, Any]],
) -> dict[str, Any]:
    test_actual = target.iloc[validation_end:]
    metrics = regression_metrics(test_actual, predictions)
    baseline_predictions = target.shift(4).iloc[validation_end:].to_numpy()
    baseline_metrics = regression_metrics(test_actual, baseline_predictions)
    baseline_mae = baseline_metrics["mae"]
    metrics.update(
        {
            "baselineModel": "SeasonalNaive-4",
            "baselineMae": baseline_mae,
            "baselineRmse": baseline_metrics["rmse"],
            "baselineR2": baseline_metrics["r2"],
            "maeImprovementPct": (
                None
                if not baseline_mae
                else float((baseline_mae - metrics["mae"]) / baseline_mae * 100.0)
            ),
            "selectionMetric": "Validation MAE",
            "validationFolds": 1,
            "trainPeriods": train_end,
            "validationPeriods": validation_end - train_end,
            "testPeriods": len(series) - validation_end,
            "testDateFrom": series.index[validation_end].date().isoformat(),
            "testDateTo": series.index[-1].date().isoformat(),
            "candidateScores": candidate_scores,
        }
    )
    return metrics


def recursive_severity_forecast(
    series: pd.DataFrame,
    model: Any,
    periods: int,
) -> list[dict]:
    severity = series["severity_fbi_bc"].copy()
    output: list[dict] = []
    for _ in range(periods):
        next_index = severity.index[-1] + pd.Timedelta(days=7)
        week = float(next_index.isocalendar().week)
        feature_row = pd.DataFrame(
            {
                "week_sin": [np.sin(2 * np.pi * week / 52.1775)],
                "week_cos": [np.cos(2 * np.pi * week / 52.1775)],
                "severity_lag_1": [float(severity.iloc[-1])],
                "severity_lag_4": [float(severity.iloc[-4])],
                "severity_roll_4": [float(severity.iloc[-4:].mean())],
            },
            index=[next_index],
        )
        prediction = max(float(model.predict(feature_row)[0]), 0.0)
        severity.loc[next_index] = prediction
        output.append(
            {
                "period": next_index.date().isoformat(),
                "predicted": prediction,
            }
        )
    return output


def paper_feature_importance(
    model: Any,
    feature_names: list[str],
) -> list[tuple[str, float]]:
    if hasattr(model, "get_feature_importance"):
        values = model.get_feature_importance()
    elif hasattr(model, "feature_importances_"):
        values = model.feature_importances_
    else:
        values = np.zeros(len(feature_names), dtype=float)

    return sorted(
        zip(feature_names, np.asarray(values, dtype=float)),
        key=lambda item: item[1],
        reverse=True,
    )
