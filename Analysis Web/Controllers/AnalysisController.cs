using Analysis_Web.Services;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace Analysis_Web.Controllers;

[Authorize]
public class AnalysisController : Controller
{
    private const int MaxRowsForPython = 250000;
    private readonly ICrimeReportInterface _crimeReports;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        ICrimeReportInterface crimeReports,
        IHttpClientFactory httpClientFactory,
        ILogger<AnalysisController> logger)
    {
        _crimeReports = crimeReports;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Prediction()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run([FromBody] DotNetCrimeAnalysisRequest request)
    {
        request.PageSize = Math.Clamp(request.PageSize <= 0 ? 100000 : request.PageSize, 100, MaxRowsForPython);
        request.ForecastPeriods = Math.Clamp(request.ForecastPeriods <= 0 ? 30 : request.ForecastPeriods, 1, 90);
        request.Frequency = string.Equals(request.Frequency, "W", StringComparison.OrdinalIgnoreCase) ? "W" : "D";

        var crimeType = TryParseCrimeType(request.CrimeType);
        var (reports, totalCount) = await _crimeReports.GetPagedAsync(
            page: 1,
            pageSize: request.PageSize,
            search: request.Search,
            crimeType: crimeType,
            year: request.Year,
            neighborhood: request.Neighborhood,
            sortBy: "dateOfReport",
            ascending: true);

        var payload = new PythonCrimeAnalysisRequest
        {
            Frequency = request.Frequency,
            ForecastPeriods = request.ForecastPeriods,
            Records = reports.Select(r => new PythonCrimeReportDto
            {
                CrimeReportId = r.CrimeReportId,
                FileNumber = r.FileNumber,
                DateOfReport = r.DateOfReport,
                CrimeDateTime = r.CrimeDateTime,
                CrimeDateTimeRaw = r.CrimeDateTimeRaw,
                CrimeType = r.CrimeType.ToString(),
                ReportingArea = r.ReportingArea,
                Neighborhood = r.Neighborhood,
                Location = r.Location,
                Latitude = r.Latitude.HasValue ? Convert.ToDouble(r.Latitude.Value) : null,
                Longitude = r.Longitude.HasValue ? Convert.ToDouble(r.Longitude.Value) : null
            }).ToList()
        };

        try
        {
            var client = _httpClientFactory.CreateClient("CrimeAnalysisPython");
            var response = await client.PostAsJsonAsync("/api/v1/crime/analyze", payload);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python analysis failed with {StatusCode}: {Body}", response.StatusCode, body);
                return StatusCode((int)response.StatusCode, new
                {
                    message = "Python analysis API failed.",
                    details = body
                });
            }

            return Content(
                $$"""{"source":"CrimeReports table","totalAvailableRows":{{totalCount}},"rowsSentToPython":{{payload.Records.Count}},"pythonResult":{{body}}}""",
                "application/json");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach Python analysis API.");
            return StatusCode(503, new
            {
                message = "Could not reach Python analysis API. Start the FastAPI project on http://localhost:8001 first."
            });
        }
    }

    private static CrimeType? TryParseCrimeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<CrimeType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }
}
