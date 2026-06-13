from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.routers.crime import router as crime_router

app = FastAPI(
    title="Crime Severity Analysis API",
    version="1.0.0",
    description="Severity-aware crime analytics and forecasting service for the CDU .NET crime analysis project.",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost", "http://localhost:5000", "https://localhost:5001"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(crime_router, prefix="/api/v1/crime", tags=["crime-analysis"])


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}
