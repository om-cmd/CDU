from __future__ import annotations

import math
import re

import numpy as np
import pandas as pd

PUNCTUATION = re.compile(r"[^a-z0-9]+")

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
    ("hit and run|accident", "traffic"),
    ("warrant|admin|medical|missing|elder|civil|suspicious", "administrative"),
]

IMPRISONMENT_DAYS: dict[str, float] = {
    "homicide": 5475,
    "kidnapping": 1825,
    "robbery": 1095,
    "aggravated_assault": 730,
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
    "traffic": 7,
    "administrative": 1,
    "unmatched": 90,
}

ONS_MULTIPLIERS: dict[str, float] = {
    "homicide": 1.00,
    "kidnapping": 0.95,
    "robbery": 0.85,
    "aggravated_assault": 0.82,
    "arson": 0.78,
    "sex_offense": 0.76,
    "burglary": 0.58,
    "weapon_law_violation": 0.55,
    "motor_vehicle_theft": 0.45,
    "intimidation": 0.36,
    "fraud": 0.32,
    "drug_liquor": 0.25,
    "larceny": 0.22,
    "public_order": 0.12,
    "traffic": 0.08,
    "administrative": 0.02,
    "unmatched": 0.30,
}


def normalize_offense(value: object) -> str:
    text = "" if value is None else str(value).lower()
    return PUNCTUATION.sub(" ", text).strip()


def label_nibrs(value: object) -> str:
    normalized = normalize_offense(value)
    for pattern, label in NIBRS_RULES:
        if re.search(pattern, normalized):
            return label
    return "unmatched"


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
    result["css"] = result["css_log"]
    return result
