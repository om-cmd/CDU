from __future__ import annotations

import sys
import unittest
from pathlib import Path

import numpy as np
import pandas as pd


API_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(API_ROOT))

from app.services.analysis_service import (  # noqa: E402
    add_features,
    build_time_series,
    build_model_candidates,
    forecast_feature_columns,
    make_json_safe,
    model_name,
    rolling_validation_splits,
)
from app.services.adaptive_forecasting import (  # noqa: E402
    build_adaptive_feature_frame,
    train_source_aware_forecasts,
)
from app.services.paper_forecasting import (  # noqa: E402
    build_paper_feature_frames,
    paper_model_candidates,
)
from app.services.severity import (  # noqa: E402
    IMPRISONMENT_DAYS,
    ONS_MULTIPLIERS,
    add_severity_columns,
    label_nibrs,
    minmax_boxcox,
)


class ForecastingPipelineTests(unittest.TestCase):
    def test_boxcox_is_bounded_and_monotonic(self) -> None:
        values = pd.Series([1.0, 7.0, 45.0, 730.0, 5475.0])
        transformed = minmax_boxcox(values, 0.3229)

        self.assertAlmostEqual(float(transformed.min()), 0.0)
        self.assertAlmostEqual(float(transformed.max()), 1.0)
        self.assertTrue(np.all(np.diff(transformed.to_numpy()) > 0))

    def test_weekly_history_features_use_only_earlier_periods(self) -> None:
        index = pd.date_range("2024-01-07", periods=60, freq="W")
        values = np.arange(60, dtype=float)
        series = pd.DataFrame(
            {
                "crime_count": values,
                "severity_total": values * 0.5,
                "severity_fbi_bc": values * 0.25,
                "severity_ons_bc": values * 0.2,
                "severity_mean": np.ones(60),
                "unique_crime_types": np.full(60, 5.0),
                "unique_neighborhoods": np.full(60, 4.0),
                "reporting_area_code_diversity": np.full(60, 3.0),
            },
            index=index,
        )

        featured = add_features(series, "W")

        self.assertEqual(featured.iloc[10]["crime_count_lag_1"], 9.0)
        self.assertEqual(featured.iloc[10]["crime_count_lag_4"], 6.0)
        self.assertAlmostEqual(
            featured.iloc[10]["crime_count_roll_mean_4"],
            float(np.mean([6.0, 7.0, 8.0, 9.0])),
        )

    def test_weekly_count_feature_allowlist_excludes_unavailable_targets(self) -> None:
        index = pd.date_range("2024-01-07", periods=60, freq="W")
        series = pd.DataFrame(
            {
                "crime_count": np.arange(60, dtype=float),
                "severity_total": np.arange(60, dtype=float),
                "severity_fbi_bc": np.arange(60, dtype=float),
                "severity_ons_bc": np.arange(60, dtype=float),
                "severity_mean": np.ones(60),
                "unique_crime_types": np.ones(60),
                "unique_neighborhoods": np.ones(60),
            },
            index=index,
        )
        featured = add_features(series, "W")

        columns = forecast_feature_columns(featured, "crime_count", "W")

        self.assertNotIn("crime_count", columns)
        self.assertNotIn("severity_total", columns)
        self.assertNotIn("severity_ons_bc_lag_1", columns)
        self.assertIn("crime_count_lag_4", columns)
        self.assertIn("severity_fbi_bc_lag_4", columns)
        self.assertIn("week_sin", columns)

    def test_paper_features_are_lagged_without_target_leakage(self) -> None:
        index = pd.date_range("2024-01-07", periods=20, freq="W")
        series = pd.DataFrame(
            {
                "crime_count": np.arange(20, dtype=float),
                "severity_fbi_bc": np.arange(20, dtype=float) * 2.0,
            },
            index=index,
        )

        severity_features, count_features = build_paper_feature_frames(series)

        self.assertEqual(count_features.iloc[10]["count_lag_1"], 9.0)
        self.assertEqual(count_features.iloc[10]["count_lag_4"], 6.0)
        self.assertAlmostEqual(
            count_features.iloc[10]["count_roll_4"],
            float(np.mean([6.0, 7.0, 8.0, 9.0])),
        )
        self.assertEqual(severity_features.iloc[10]["severity_lag_1"], 18.0)
        self.assertEqual(severity_features.iloc[10]["severity_lag_4"], 12.0)
        self.assertAlmostEqual(
            severity_features.iloc[10]["severity_roll_4"],
            float(np.mean([12.0, 14.0, 16.0, 18.0])),
        )

    def test_paper_gradient_boosting_families_are_configured(self) -> None:
        names = [name for name, _ in paper_model_candidates()]

        self.assertEqual(
            names,
            [
                "CatBoostRegressor",
                "XGBRegressor",
                "LGBMRegressor",
            ],
        )

    def test_both_weekly_targets_use_source_aware_paper_families(self) -> None:
        index = pd.date_range("2024-01-07", periods=60, freq="W")
        x = np.arange(60, dtype=float)
        series = pd.DataFrame(
            {
                "crime_count": 120.0 + 8.0 * np.sin(x / 4.0) + x % 5,
                "severity_fbi_bc": 42.0 + 3.0 * np.cos(x / 6.0) + (x % 3) / 5.0,
                "active_jurisdictions": np.ones(60),
                "active_data_sources": np.ones(60),
            },
            index=index,
        )

        count_result, severity_result = train_source_aware_forecasts(series, 2)
        expected = {
            "DeltaCatBoostRegressor",
            "DeltaXGBRegressor",
            "DeltaLGBMRegressor",
        }

        self.assertEqual(
            {row["model"] for row in count_result["metrics"]["candidateScores"]},
            expected,
        )
        self.assertEqual(
            {row["model"] for row in severity_result["metrics"]["candidateScores"]},
            expected,
        )
        self.assertTrue(count_result["model"].startswith("SourceAwareDelta+"))
        self.assertTrue(severity_result["model"].startswith("SourceAwareDelta+"))

    def test_adaptive_features_anchor_predictions_to_previous_level(self) -> None:
        index = pd.date_range("2024-01-07", periods=12, freq="W")
        series = pd.DataFrame(
            {
                "crime_count": [100.0] * 8 + [1000.0, 1010.0, 1020.0, 1030.0],
                "active_jurisdictions": [1.0] * 8 + [2.0] * 4,
                "active_data_sources": [1.0] * 8 + [2.0] * 4,
            },
            index=index,
        )

        features = build_adaptive_feature_frame(series, "crime_count")

        self.assertEqual(features.iloc[9]["level_lag_1"], 1000.0)
        self.assertEqual(features.iloc[9]["delta_lag_1"], 900.0)
        self.assertEqual(features.iloc[9]["active_jurisdictions_lag_1"], 2.0)

    def test_weighted_aggregate_rows_preserve_event_totals(self) -> None:
        frame = pd.DataFrame(
            {
                "incident_at": pd.to_datetime(["2024-01-01", "2024-01-02"]),
                "crime_type": ["Larceny", "Larceny"],
                "neighborhood": ["A", "A"],
                "jurisdiction": ["City A", "City A"],
                "data_source": ["Source A", "Source A"],
                "event_count": [100, 250],
                "css": [0.2, 0.2],
                "css_fbi_bc": [0.1, 0.1],
                "css_ons_bc": [0.15, 0.15],
                "css_weighted": [20.0, 50.0],
                "css_fbi_bc_weighted": [10.0, 25.0],
                "css_ons_bc_weighted": [15.0, 37.5],
            }
        )

        weekly = build_time_series(frame, "W")

        self.assertEqual(float(weekly.iloc[0]["crime_count"]), 350.0)
        self.assertEqual(float(weekly.iloc[0]["severity_fbi_bc"]), 35.0)

    def test_non_finite_optional_values_are_json_safe(self) -> None:
        cleaned = make_json_safe(
            {"latitude": np.nan, "score": np.inf, "values": [1.0, -np.inf]}
        )

        self.assertEqual(
            cleaned,
            {"latitude": None, "score": None, "values": [1.0, None]},
        )

    def test_documented_paper_harm_values_are_preserved(self) -> None:
        self.assertEqual(IMPRISONMENT_DAYS["aggravated_assault"], 1460)
        self.assertEqual(IMPRISONMENT_DAYS["larceny"], 45)
        self.assertEqual(IMPRISONMENT_DAYS["traffic"], 180)
        self.assertEqual(IMPRISONMENT_DAYS["intimidation"], 120)
        self.assertEqual(IMPRISONMENT_DAYS["administrative"], 1)
        self.assertEqual(IMPRISONMENT_DAYS["unmatched"], 90)
        self.assertGreaterEqual(min(ONS_MULTIPLIERS.values()), 0.5)
        self.assertLessEqual(max(ONS_MULTIPLIERS.values()), 5.5)
        self.assertEqual(label_nibrs("Hit and Run"), "traffic")
        self.assertEqual(label_nibrs("Motor vehicle accident"), "administrative")

    def test_every_nibrs_harm_label_has_a_complete_severity_mapping(self) -> None:
        self.assertEqual(set(IMPRISONMENT_DAYS), set(ONS_MULTIPLIERS))
        frame = pd.DataFrame(
            {
                "crime_type": [
                    "Simple Assault",
                    "Aggravated Assault",
                    "Hit and Run",
                    "Motor vehicle accident",
                    "Unknown offence",
                ]
            }
        )

        enriched = add_severity_columns(frame)

        self.assertFalse(enriched["harm_days"].isna().any())
        self.assertFalse(enriched["ons_multiplier"].isna().any())
        self.assertFalse(enriched["css_fbi_bc"].isna().any())
        self.assertFalse(enriched["css_ons_bc"].isna().any())

    def test_rolling_validation_folds_are_chronological_and_pretest(self) -> None:
        period_count = 900
        development_end = int(period_count * 0.85)
        folds = rolling_validation_splits(period_count, development_end)

        self.assertEqual(len(folds), 3)
        self.assertTrue(all(0 < train_end < validation_end <= development_end for train_end, validation_end in folds))
        self.assertEqual(folds[-1][1], development_end)

    def test_all_configured_candidates_are_available_for_each_fresh_run(self) -> None:
        candidates = build_model_candidates(period_count=120)
        names = [model_name(candidate) for candidate in candidates]

        self.assertEqual(len(candidates), 6)
        self.assertEqual(len(names), len(set(names)))
        self.assertTrue(any("StandardScaler" in name for name in names))
        self.assertTrue(any("RobustScaler" in name for name in names))
        self.assertTrue(any("CatBoost" in name for name in names))

    def test_scaler_is_fitted_only_from_the_supplied_training_fold(self) -> None:
        candidate = build_model_candidates(period_count=120)[0]
        training = pd.DataFrame(
            {
                "feature_a": np.arange(10, dtype=float),
                "feature_b": np.arange(10, dtype=float) * 10,
            }
        )
        target = pd.Series(np.arange(10, dtype=float))

        candidate.fit(training, target)

        scaler = candidate.named_steps["scaler"]
        np.testing.assert_allclose(scaler.mean_, training.mean().to_numpy())


if __name__ == "__main__":
    unittest.main()
