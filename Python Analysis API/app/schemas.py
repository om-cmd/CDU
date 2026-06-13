from __future__ import annotations

from datetime import datetime
from typing import List, Optional

from pydantic import BaseModel, Field


class CrimeReportRecord(BaseModel):
    crimeReportId: Optional[int] = None
    fileNumber: Optional[str] = None
    dateOfReport: Optional[datetime] = None
    crimeDateTime: Optional[datetime] = None
    crimeDateTimeRaw: Optional[str] = None
    crimeType: Optional[str] = None
    reportingArea: Optional[str] = None
    neighborhood: Optional[str] = None
    location: Optional[str] = None
    latitude: Optional[float] = None
    longitude: Optional[float] = None


class CrimeAnalysisRequest(BaseModel):
    frequency: str = Field(default="D", pattern="^(D|W)$")
    forecastPeriods: int = Field(default=30, ge=1, le=90)
    records: List[CrimeReportRecord]

    @property
    def forecast_periods(self) -> int:
        return self.forecastPeriods
