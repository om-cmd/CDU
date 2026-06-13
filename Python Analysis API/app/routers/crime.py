from __future__ import annotations

from fastapi import APIRouter, File, Form, HTTPException, UploadFile

from app.schemas import CrimeAnalysisRequest
from app.services.analysis_service import analyze_dataframe, parse_csv_upload, request_to_dataframe

router = APIRouter()


@router.post("/analyze")
def analyze_json(request: CrimeAnalysisRequest):
    if not request.records:
        raise HTTPException(status_code=400, detail="At least one crime record is required.")

    frame = request_to_dataframe(request)
    try:
        return analyze_dataframe(
            frame,
            frequency=request.frequency,
            forecast_periods=request.forecast_periods,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@router.post("/analyze-csv")
async def analyze_csv(
    file: UploadFile = File(...),
    frequency: str = Form("D"),
    forecast_periods: int = Form(30),
):
    if not file.filename.lower().endswith(".csv"):
        raise HTTPException(status_code=400, detail="Only CSV files are supported.")

    try:
        frame = await parse_csv_upload(file)
        return analyze_dataframe(frame, frequency=frequency, forecast_periods=forecast_periods)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
