from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np
import pandas as pd
from sklearn.preprocessing import LabelEncoder


MISSING_REJECT_THRESHOLD = 0.60
CORE_COLUMNS = ["incident_at", "crime_type", "neighborhood"]
CATEGORICAL_COLUMNS = ["crime_type", "neighborhood", "reporting_area"]


@dataclass
class PreprocessResult:
    frame: pd.DataFrame
    report: dict[str, Any] = field(default_factory=dict)


def preprocess_for_analysis(frame: pd.DataFrame) -> PreprocessResult:
    original_rows = int(len(frame))
    working = frame.copy()
    working = _standardize_missing_values(working)

    missing_ratios = {
        column: float(working[column].isna().mean())
        for column in working.columns
    }

    rejected_columns = [
        column
        for column, ratio in missing_ratios.items()
        if ratio > MISSING_REJECT_THRESHOLD and column not in CORE_COLUMNS
    ]
    if rejected_columns:
        working = working.drop(columns=rejected_columns)

    failed_core_columns = [
        column
        for column in CORE_COLUMNS
        if column in missing_ratios and missing_ratios[column] > MISSING_REJECT_THRESHOLD
    ]
    if failed_core_columns:
        raise ValueError(
            "Dataset rejected because required column(s) have more than 60% missing values: "
            + ", ".join(failed_core_columns)
        )

    working, row_quality = _remove_bad_rows(working)
    working, anomaly_report = _remove_anomalies(working)
    working, encoding_report = _label_encode_categories(working)

    report = {
        "inputRows": original_rows,
        "usableRows": int(len(working)),
        "droppedRows": int(original_rows - len(working)),
        "missingRejectThreshold": MISSING_REJECT_THRESHOLD,
        "missingRatios": {key: round(value, 4) for key, value in missing_ratios.items()},
        "rejectedColumns": rejected_columns,
        "rowQuality": row_quality,
        "anomalies": anomaly_report,
        "encoding": encoding_report,
        "scaling": {
            "appliedToModelFeatures": True,
            "method": "StandardScaler inside the forecasting pipeline",
        },
    }

    if working.empty:
        raise ValueError("Dataset rejected because no usable records remained after preprocessing.")

    return PreprocessResult(frame=working, report=report)


def _standardize_missing_values(frame: pd.DataFrame) -> pd.DataFrame:
    result = frame.copy()
    object_columns = result.select_dtypes(include=["object"]).columns
    for column in object_columns:
        result[column] = result[column].replace(r"^\s*$", np.nan, regex=True)
        result[column] = result[column].replace(
            ["null", "NULL", "None", "none", "N/A", "n/a", "NaN", "nan"],
            np.nan,
        )
        result[column] = result[column].map(lambda value: value.strip() if isinstance(value, str) else value)
    return result


def _remove_bad_rows(frame: pd.DataFrame) -> tuple[pd.DataFrame, dict[str, int]]:
    result = frame.copy()
    before = len(result)

    row_missing_ratio = result.isna().mean(axis=1)
    result = result.loc[row_missing_ratio <= MISSING_REJECT_THRESHOLD].copy()
    dropped_missing_rows = before - len(result)

    before = len(result)
    result = result.dropna(subset=[column for column in CORE_COLUMNS if column in result.columns])
    dropped_core_rows = before - len(result)

    before = len(result)
    duplicate_subset = [column for column in ["file_number", "incident_at", "crime_type", "location"] if column in result.columns]
    if duplicate_subset:
        result = result.drop_duplicates(subset=duplicate_subset)
    else:
        result = result.drop_duplicates()
    dropped_duplicates = before - len(result)

    return result, {
        "droppedRowsWithMoreThan60PercentMissing": int(dropped_missing_rows),
        "droppedRowsMissingRequiredFields": int(dropped_core_rows),
        "droppedDuplicateRows": int(dropped_duplicates),
    }


def _remove_anomalies(frame: pd.DataFrame) -> tuple[pd.DataFrame, dict[str, Any]]:
    result = frame.copy()
    anomaly_report: dict[str, Any] = {
        "negativeAgeRowsRemoved": 0,
        "invalidLatitudeRowsRemoved": 0,
        "invalidLongitudeRowsRemoved": 0,
        "futureIncidentDateRowsRemoved": 0,
        "checkedColumns": [],
    }

    for column in list(result.columns):
        lowered = column.lower()
        if "age" in lowered:
            numeric = pd.to_numeric(result[column], errors="coerce")
            mask = numeric.notna() & (numeric < 0)
            anomaly_report["negativeAgeRowsRemoved"] += int(mask.sum())
            anomaly_report["checkedColumns"].append(column)
            result = result.loc[~mask].copy()

    if "latitude" in result.columns:
        latitude = pd.to_numeric(result["latitude"], errors="coerce")
        mask = latitude.notna() & ~latitude.between(-90, 90)
        anomaly_report["invalidLatitudeRowsRemoved"] = int(mask.sum())
        result = result.loc[~mask].copy()

    if "longitude" in result.columns:
        longitude = pd.to_numeric(result["longitude"], errors="coerce")
        mask = longitude.notna() & ~longitude.between(-180, 180)
        anomaly_report["invalidLongitudeRowsRemoved"] = int(mask.sum())
        result = result.loc[~mask].copy()

    if "incident_at" in result.columns:
        mask = result["incident_at"] > pd.Timestamp.utcnow().tz_localize(None)
        anomaly_report["futureIncidentDateRowsRemoved"] = int(mask.sum())
        result = result.loc[~mask].copy()

    return result, anomaly_report


def _label_encode_categories(frame: pd.DataFrame) -> tuple[pd.DataFrame, dict[str, Any]]:
    result = frame.copy()
    encoded_columns: list[dict[str, Any]] = []

    for column in CATEGORICAL_COLUMNS:
        if column not in result.columns:
            continue

        values = result[column].fillna("Unknown").astype(str)
        encoder = LabelEncoder()
        encoded_name = f"{column}_code"
        result[encoded_name] = encoder.fit_transform(values)
        encoded_columns.append(
            {
                "sourceColumn": column,
                "encodedColumn": encoded_name,
                "classes": int(len(encoder.classes_)),
            }
        )

    return result, {
        "method": "LabelEncoder",
        "columns": encoded_columns,
    }
