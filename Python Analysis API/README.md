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
