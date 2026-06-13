using Analysis_Web.Services;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace Web_API.Controllers;

[ApiController]
[Route("api/crime-analysis")]
public class CrimeAnalysisController : ControllerBase
{
    private const int MaxRowsForPython = 250000;
    private readonly ICrimeReportInterface _crimeReports;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CrimeAnalysisController> _logger;

    public CrimeAnalysisController(
        ICrimeReportInterface crimeReports,
        IHttpClientFactory httpClientFactory,
        ILogger<CrimeAnalysisController> logger)
    {
        _crimeReports = crimeReports;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost("from-database")]
    public async Task<IActionResult> AnalyzeFromDatabase([FromBody] DotNetCrimeAnalysisRequest request)
    {
        request.PageSize = Math.Clamp(request.PageSize, 100, MaxRowsForPython);
        request.ForecastPeriods = Math.Clamp(request.ForecastPeriods, 1, 90);

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
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Python analysis API returned {StatusCode}: {Body}", response.StatusCode, body);
                return StatusCode((int)response.StatusCode, new
                {
                    message = "Python analysis API failed.",
                    details = body
                });
            }

            var result = await response.Content.ReadFromJsonAsync<object>();
            return Ok(new
            {
                source = "CrimeReports table",
                totalAvailableRows = totalCount,
                rowsSentToPython = payload.Records.Count,
                pythonResult = result
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach Python analysis API.");
            return StatusCode(503, new
            {
                message = "Could not reach Python analysis API. Start the FastAPI service in Python Analysis API first.",
                configuredBaseUrl = HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>()["PythonAnalysisApi:BaseUrl"]
            });
        }
    }

    [HttpPost("forward")]
    public async Task<IActionResult> ForwardRecords([FromBody] PythonCrimeAnalysisRequest payload)
    {
        if (payload.Records.Count == 0)
            return BadRequest(new { message = "At least one crime record is required." });

        payload.ForecastPeriods = Math.Clamp(payload.ForecastPeriods, 1, 90);
        payload.Records = payload.Records.Take(MaxRowsForPython).ToList();

        var client = _httpClientFactory.CreateClient("CrimeAnalysisPython");
        var response = await client.PostAsJsonAsync("/api/v1/crime/analyze", payload);
        var body = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
            Content = body
        };
    }

    private static CrimeType? TryParseCrimeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<CrimeType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }
}
