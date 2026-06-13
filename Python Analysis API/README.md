# Crime Analysis Python API

FastAPI service for severity-aware crime analysis and forecasting. It is designed to run from PyCharm and receive records either directly from the .NET Web API or from the provided Cambridge crime CSV.

## Setup in PyCharm

1. Open the `Python Analysis API` folder in PyCharm.
2. Create a virtual environment with Python 3.11+.
3. Install dependencies:

```powershell
pip install -r requirements.txt
```

4. Start the API:

```powershell
uvicorn app.main:app --host 0.0.0.0 --port 8001 --reload
```

5. Open Swagger:

```text
http://localhost:8001/docs
```

## Useful Endpoints

- `GET /health` checks the service.
- `POST /api/v1/crime/analyze` accepts JSON records from .NET.
- `POST /api/v1/crime/analyze-csv` accepts the CSV file directly.

The .NET Web API calls `http://localhost:8001/api/v1/crime/analyze` through `POST /api/crime-analysis/from-database`.

## Data Quality And Model Preparation

Before analysis, the API now applies a dedicated preprocessing stage:

- Rejects required analytical columns when more than 60% of their values are missing.
- Drops non-required columns with more than 60% missing values.
- Drops rows with more than 60% missing values.
- Drops rows missing required analysis fields after normalization.
- Removes duplicate records.
- Removes anomaly rows such as negative age values, invalid latitude/longitude, and future incident dates.
- Applies label encoding to crime type, neighborhood, and reporting area.
- Adds encoded-category aggregate features into the forecasting time series.
- Scales model features with `StandardScaler` before training.

The API response includes a `dataQuality` section showing rejected columns, dropped rows, anomaly counts, encoding information, and scaling details.
