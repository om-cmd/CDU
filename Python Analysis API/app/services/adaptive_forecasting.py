from __future__ import annotations

from typing import Any

import numpy as np
import pandas as pd

from app.services.paper_forecasting import (
    paper_feature_importance,
    paper_model_candidates,
    regression_metrics,
)


def build_adaptive_feature_frame(
    series: pd.DataFrame,
    target: str,
) -> pd.DataFrame:
    values = series[target].astype(float)
    changes = values.diff()
    iso_week = series.index.isocalendar().week.astype(float).to_numpy()
    features: dict[str, Any] = {
        "week_sin": np.sin(2 * np.pi * iso_week / 52.1775),
        "week_cos": np.cos(2 * np.pi * iso_week / 52.1775),
        "time_index": np.arange(len(series), dtype=float),
        "level_lag_1": values.shift(1),
        "level_lag_2": values.shift(2),
        "level_lag_4": values.shift(4),
        "level_roll_4": values.shift(1).rolling(4).mean(),
        "delta_lag_1": changes.shift(1),
        "delta_lag_4": changes.shift(4),
        "delta_roll_4": changes.shift(1).rolling(4).mean(),
    }
    for context in ["active_jurisdictions", "active_data_sources"]:
        if context in series:
            features[f"{context}_lag_1"] = series[context].shift(1)
            features[f"{context}_change_1"] = series[context].shift(1).diff()

    return (
        pd.DataFrame(features, index=series.index)
        .replace([np.inf, -np.inf], np.nan)
        .fillna(0.0)
    )


def train_source_aware_forecasts(
    series: pd.DataFrame,
    periods: int,
) -> tuple[dict, dict]:
    count = train_source_aware_target(
        series,
        target="crime_count",
        periods=periods,
        label="Source-aware weekly incident forecast",
    )
    severity = train_source_aware_target(
        series,
        target="severity_fbi_bc",
        periods=periods,
        label="Source-aware FBI Box-Cox harm forecast",
    )
    return count, severity


def train_source_aware_target(
    series: pd.DataFrame,
    target: str,
    periods: int,
    label: str,
) -> dict:
    features = build_adaptive_feature_frame(series, target)
    level_target = series[target].astype(float)
    delta_target = level_target.diff().fillna(0.0)
    train_end = int(len(series) * 0.70)
    validation_end = min(int(len(series) * 0.85), len(series) - 2)

    candidates: list[dict[str, Any]] = []
    for family, factory in paper_model_candidates():
        model = factory()
        model.fit(features.iloc[:train_end], delta_target.iloc[:train_end])
        validation_delta = model.predict(features.iloc[train_end:validation_end])
        validation_predictions = np.maximum(
            features["level_lag_1"].iloc[train_end:validation_end].to_numpy()
            + validation_delta,
            0.0,
        )
        validation_metrics = regression_metrics(
            level_target.iloc[train_end:validation_end],
            validation_predictions,
        )
        candidates.append(
            {
                "family": family,
                "factory": factory,
                "validation": validation_metrics,
            }
        )

    best = min(candidates, key=lambda item: item["validation"]["mae"])
    evaluation_model = best["factory"]()
    evaluation_model.fit(
        features.iloc[:validation_end],
        delta_target.iloc[:validation_end],
    )
    test_delta = evaluation_model.predict(features.iloc[validation_end:])
    test_predictions = np.maximum(
        features["level_lag_1"].iloc[validation_end:].to_numpy() + test_delta,
        0.0,
    )
    metrics = regression_metrics(
        level_target.iloc[validation_end:],
        test_predictions,
    )
    baseline_predictions = features["level_lag_1"].iloc[validation_end:].to_numpy()
    baseline_metrics = regression_metrics(
        level_target.iloc[validation_end:],
        baseline_predictions,
    )
    baseline_mae = baseline_metrics["mae"]
    metrics.update(
        {
            "baselineModel": "LastWeekNaive",
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
            "candidateScores": [
                {
                    "model": f"Delta{item['family']}",
                    **item["validation"],
                    "selectionScore": item["validation"]["mae"],
                    "selected": item is best,
                }
                for item in sorted(
                    candidates,
                    key=lambda item: item["validation"]["mae"],
                )
            ],
        }
    )

    final_model = best["factory"]()
    final_model.fit(features, delta_target)
    future = recursive_delta_forecast(
        series,
        final_model,
        target,
        periods,
    )
    importance = paper_feature_importance(final_model, list(features.columns))
    test_index = series.index[validation_end:]
    family_label = best["family"].replace("Regressor", "")

    return {
        "target": target,
        "model": f"SourceAwareDelta+{family_label}",
        "metrics": metrics,
        "featureImportance": [
            {"feature": feature, "importance": float(score)}
            for feature, score in importance
        ],
        "backtest": [
            {
                "period": index.date().isoformat(),
                "actual": float(actual),
                "predicted": float(predicted),
            }
            for index, actual, predicted in zip(
                test_index[-120:],
                level_target.iloc[validation_end:].iloc[-120:],
                test_predictions[-120:],
            )
        ],
        "history": [
            {"period": index.date().isoformat(), "actual": float(value)}
            for index, value in level_target.tail(60).items()
        ],
        "future": future,
        "methodology": {
            "label": label,
            "candidateModels": ["CatBoost", "XGBoost", "LightGBM"],
            "targetTransformation": "First weekly difference with previous observed level restored after prediction",
            "sourceFeatures": [
                "active jurisdiction lag/change",
                "active data-source lag/change",
            ],
            "historyFeatures": [
                "level lags 1, 2, and 4",
                "four-week level mean",
                "delta lags 1 and 4",
                "four-week delta mean",
                "cyclic week",
            ],
            "severityVariant": "FBI Box-Cox" if target == "severity_fbi_bc" else None,
            "boxCoxLambda": 0.3229 if target == "severity_fbi_bc" else None,
        },
    }


def recursive_delta_forecast(
    series: pd.DataFrame,
    model: Any,
    target: str,
    periods: int,
) -> list[dict]:
    working = series.copy()
    output: list[dict] = []
    for _ in range(periods):
        next_index = working.index[-1] + pd.Timedelta(days=7)
        next_row = working.iloc[-1].copy()
        next_row[target] = np.nan
        working.loc[next_index] = next_row
        feature_row = build_adaptive_feature_frame(working, target).loc[[next_index]]
        previous_level = float(working[target].iloc[-2])
        prediction = max(
            previous_level + float(model.predict(feature_row)[0]),
            0.0,
        )
        working.loc[next_index, target] = prediction
        output.append(
            {
                "period": next_index.date().isoformat(),
                "predicted": prediction,
            }
        )
    return output
