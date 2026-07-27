from __future__ import annotations

import math
import re

import numpy as np
import pandas as pd

PUNCTUATION = re.compile(r"[^a-z0-9]+")
CAMEL_CASE_BOUNDARY = re.compile(r"(?<=[a-z0-9])(?=[A-Z])")

NIBRS_RULES: list[tuple[str, str]] = [
    ("homicide|murder", "homicide"),
    ("kidnapping", "kidnapping"),
    ("aggravated assault", "aggravated_assault"),
    ("simple assault|domestic dispute", "simple_assault"),
    ("street robbery|commercial robbery|robbery", "robbery"),
    ("housebreak|commercial break|burglary", "burglary"),
    ("auto theft|motor vehicle theft", "motor_vehicle_theft"),
    ("larceny|shoplifting|stolen property|theft|plate", "larceny"),
    ("forgery|counterfeit|fraud|flim flam|embezzlement|extortion", "fraud"),
    ("arson", "arson"),
    ("weapon", "weapon_law_violation"),
    ("drugs|liquor|oui|drinking", "drug_liquor"),
    ("sex|indecent|prostitution|peeping|stalking", "sex_offense"),
    ("threat|harassment|annoying", "intimidation"),
    ("trespassing|disorderly|noise|gambling|encampment|hoarding", "public_order"),
    ("hit and run", "traffic"),
    ("accident|warrant|admin|medical|missing|elder|civil|suspicious", "administrative"),
]

IMPRISONMENT_DAYS: dict[str, float] = {
    "homicide": 5475,
    "kidnapping": 1825,
    "robbery": 1095,
    "aggravated_assault": 1460,
    "simple_assault": 1460,
    "arson": 730,
    "sex_offense": 600,
    "burglary": 365,
    "weapon_law_violation": 240,
    "motor_vehicle_theft": 180,
    "intimidation": 120,
    "fraud": 90,
    "drug_liquor": 60,
    "larceny": 45,
    "public_order": 14,
    "traffic": 180,
    "administrative": 1,
    "unmatched": 90,
}

ONS_MULTIPLIERS: dict[str, float] = {
    "homicide": 5.50,
    "kidnapping": 5.225,
    "robbery": 4.675,
    "aggravated_assault": 4.51,
    "simple_assault": 4.51,
    "arson": 4.29,
    "sex_offense": 4.18,
    "burglary": 3.19,
    "weapon_law_violation": 3.025,
    "motor_vehicle_theft": 2.475,
    "intimidation": 1.98,
    "fraud": 1.76,
    "drug_liquor": 1.375,
    "larceny": 1.21,
    "public_order": 0.66,
    "traffic": 0.50,
    "administrative": 0.50,
    "unmatched": 1.65,
}


def normalize_offense(value: object) -> str:
    # .NET sends enum names such as ``AggravatedAssault`` and ``HitAndRun``.
    # Split those names before lower-casing so the same NIBRS rules also work
    # for human-readable CSV labels.
    text = "" if value is None else CAMEL_CASE_BOUNDARY.sub(" ", str(value)).lower()
    return PUNCTUATION.sub(" ", text).strip()


def label_nibrs(value: object) -> str:
    normalized = normalize_offense(value)
    for pattern, label in NIBRS_RULES:
        if re.search(pattern, normalized):
            return label
    return "unmatched"


def minmax_boxcox(
    values: pd.Series,
    power: float,
    lower_bound: float | None = None,
    upper_bound: float | None = None,
) -> pd.Series:
    numeric = values.astype(float).clip(lower=1e-9)
    transformed = (np.power(numeric, power) - 1.0) / power
    minimum_source = float(numeric.min()) if lower_bound is None else lower_bound
    maximum_source = float(numeric.max()) if upper_bound is None else upper_bound
    minimum = float((np.power(minimum_source, power) - 1.0) / power)
    maximum = float((np.power(maximum_source, power) - 1.0) / power)
    if maximum <= minimum:
        return pd.Series(np.zeros(len(values)), index=values.index)
    return (transformed - minimum) / (maximum - minimum)


def add_severity_columns(frame: pd.DataFrame) -> pd.DataFrame:
    result = frame.copy()
    result["nibrs_label"] = result["crime_type"].map(label_nibrs)
    result["harm_days"] = result["nibrs_label"].map(IMPRISONMENT_DAYS).fillna(IMPRISONMENT_DAYS["unmatched"])
    result["ons_multiplier"] = result["nibrs_label"].map(ONS_MULTIPLIERS).fillna(ONS_MULTIPLIERS["unmatched"])
    result["raw_severity"] = result["harm_days"] * result["ons_multiplier"]

    max_raw = float(result["raw_severity"].max() or 1.0)
    result["css_raw"] = result["raw_severity"] / max_raw
    result["css_log"] = np.log1p(result["raw_severity"]) / math.log1p(max_raw)
    result["css_sqrt"] = np.sqrt(result["raw_severity"]) / math.sqrt(max_raw)
    # The paper's fitted powers are fixed here so the same transformation can
    # be reproduced without learning anything from the held-out target period.
    fbi_values = list(IMPRISONMENT_DAYS.values())
    ons_values = [
        IMPRISONMENT_DAYS[label] * ONS_MULTIPLIERS[label]
        for label in IMPRISONMENT_DAYS
    ]
    result["css_fbi_bc"] = minmax_boxcox(
        result["harm_days"],
        0.3229,
        lower_bound=min(fbi_values),
        upper_bound=max(fbi_values),
    )
    result["css_ons_bc"] = minmax_boxcox(
        result["raw_severity"],
        0.3306,
        lower_bound=min(ons_values),
        upper_bound=max(ons_values),
    )
    result["css"] = result["css_log"]
    return result
