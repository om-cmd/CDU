using Analysis_Web.Services;
using Business_Layer;
using Core_Layer.Models;
using Core_Layer.Services;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace Analysis_Web.Controllers;

[Authorize]
public class AnalysisController : Controller
{
    private const int MaxRowsForPython = 250000;
    private readonly ICrimeReportInterface _crimeReports;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        ICrimeReportInterface crimeReports,
        IUnitOfWork unitOfWork,
        IHttpClientFactory httpClientFactory,
        ILogger<AnalysisController> logger)
    {
        _crimeReports = crimeReports;
        _unitOfWork = unitOfWork;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Prediction()
    {
        return RedirectToAction(nameof(CrimeDashboard));
    }

    [HttpGet]
    public async Task<IActionResult> CrimeDashboard()
    {
        return View(await BuildDashboardStatsAsync());
    }

    [HttpGet]
    public async Task<IActionResult> HarmDashboard()
    {
        return View(await BuildDashboardStatsAsync());
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

        if (payload.Records.Count == 0)
        {
            return BadRequest(new
            {
                message = "No CrimeReports records were found for the selected filters. Import the CSV or create manual crime reports first.",
                source = "CrimeReports table",
                totalAvailableRows = totalCount,
                rowsSentToPython = 0
            });
        }

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

    private async Task<DashboardStatsModel> BuildDashboardStatsAsync()
    {
        var model = new DashboardStatsModel
        {
            TotalRecords = await _unitOfWork.CrimeReports.CountAsync(),
            TotalWithCoords = await _unitOfWork.CrimeReports.CountAsync(r => r.Latitude.HasValue && r.Longitude.HasValue)
        };

        model.CrimeTypeCounts = await _unitOfWork.CrimeReports
            .GroupBy(r => r.CrimeType)
            .Select(g => new { CrimeType = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToDictionaryAsync(k => k.CrimeType, v => v.Count);

        model.NeighborhoodCounts = await _unitOfWork.CrimeReports
            .Where(r => r.Neighborhood != null)
            .GroupBy(r => r.Neighborhood!)
            .Select(g => new { Neighborhood = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToDictionaryAsync(k => k.Neighborhood, v => v.Count);

        model.YearlyCounts = await _unitOfWork.CrimeReports
            .Where(r => r.DateOfReport.HasValue)
            .GroupBy(r => r.DateOfReport!.Value.Year)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderBy(x => x.Year)
            .ToDictionaryAsync(k => k.Year, v => v.Count);

        model.MonthlyCounts = await _unitOfWork.CrimeReports
            .Where(r => r.DateOfReport.HasValue)
            .GroupBy(r => new { r.DateOfReport!.Value.Year, r.DateOfReport!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToDictionaryAsync(k => $"{k.Year}-{k.Month:00}", v => v.Count);

        model.HourlyCounts = await _unitOfWork.CrimeReports
            .Where(r => r.CrimeDateTime.HasValue)
            .GroupBy(r => r.CrimeDateTime!.Value.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToDictionaryAsync(k => k.Hour, v => v.Count);

        var mapPointsRaw = await _unitOfWork.CrimeReports
            .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
            .OrderByDescending(r => r.DateOfReport)
            .Select(r => new { r.Latitude, r.Longitude, r.CrimeType, r.FileNumber, r.Neighborhood, r.DateOfReport })
            .Take(5000)
            .ToListAsync();

        model.MapPoints = mapPointsRaw.Select(r => new MapPointModel
        {
            Lat = (double)r.Latitude!.Value,
            Lng = (double)r.Longitude!.Value,
            CrimeType = r.CrimeType.ToString(),
            FileNumber = r.FileNumber ?? "",
            Neighborhood = r.Neighborhood ?? "Unknown",
            DateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
            CssWeight = CrimeDataService.GetCssWeight(r.CrimeType.ToString())
        }).ToList();

        var recentRaw = await _unitOfWork.CrimeReports
            .OrderByDescending(r => r.DateOfReport)
            .Take(8)
            .Select(r => new { r.FileNumber, r.CrimeType, r.Neighborhood, r.Location, r.DateOfReport, r.CrimeDateTime })
            .ToListAsync();

        model.RecentIncidents = recentRaw.Select(r =>
        {
            var weight = CrimeDataService.GetCssWeight(r.CrimeType.ToString());
            return new RecentIncidentModel
            {
                FileNumber = r.FileNumber ?? "",
                CrimeType = r.CrimeType.ToString(),
                Neighborhood = r.Neighborhood ?? "Unknown",
                Location = r.Location ?? "",
                DateOfReport = r.DateOfReport?.ToString("dd MMM") ?? "",
                CrimeDateTime = r.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                CssWeight = weight,
                SeverityLevel = GetSeverityLevel(weight)
            };
        }).ToList();

        await AddHarmStatsAsync(model);
        return model;
    }

    private async Task AddHarmStatsAsync(DashboardStatsModel model)
    {
        var harmData = await _unitOfWork.CrimeReports
            .Select(r => new { r.CrimeType, r.DateOfReport, r.CrimeDateTime, r.Neighborhood })
            .ToListAsync();

        var weighted = harmData.Select(r => new
        {
            CrimeType = r.CrimeType.ToString(),
            Weight = CrimeDataService.GetCssWeight(r.CrimeType.ToString()),
            Month = r.DateOfReport.HasValue ? $"{r.DateOfReport.Value.Year}-{r.DateOfReport.Value.Month:00}" : null,
            HourBlock = GetHourBlock(r.CrimeDateTime),
            Neighborhood = r.Neighborhood ?? "Unknown"
        }).ToList();

        model.TotalHarm = weighted.Sum(x => x.Weight);
        model.HarmByType = weighted
            .GroupBy(x => x.CrimeType)
            .Select(g => new HarmByTypeModel
            {
                CrimeType = g.Key,
                Count = g.Count(),
                TotalHarm = g.Sum(x => x.Weight),
                CssWeight = g.First().Weight
            })
            .OrderByDescending(x => x.TotalHarm)
            .ToList();

        model.MonthlyHarm = weighted
            .Where(x => x.Month != null)
            .GroupBy(x => x.Month!)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Sum(x => (long)x.Weight));

        model.HighHarm = weighted.Where(x => x.Weight >= 300).Sum(x => x.Weight);
        model.MediumHarm = weighted.Where(x => x.Weight >= 60 && x.Weight < 300).Sum(x => x.Weight);
        model.LowHarm = weighted.Where(x => x.Weight < 60).Sum(x => x.Weight);

        for (var block = 0; block < 4; block++)
        {
            var blockSum = weighted.Where(x => x.HourBlock == block).Sum(x => x.Weight);
            model.HourlyHarmPct[block] = model.TotalHarm > 0 ? (double)blockSum / model.TotalHarm * 100 : 0;
        }

        model.HarmByNeighborhood = weighted
            .GroupBy(x => x.Neighborhood)
            .Select(g => new HarmByNeighborhoodModel
            {
                Name = g.Key,
                Count = g.Count(),
                TotalHarm = g.Sum(x => x.Weight)
            })
            .OrderByDescending(x => x.TotalHarm)
            .ToList();
    }

    private static int GetHourBlock(DateTime? crimeDateTime)
    {
        if (!crimeDateTime.HasValue) return 0;
        var hour = crimeDateTime.Value.Hour;
        if (hour < 6) return 0;
        if (hour < 12) return 1;
        if (hour < 18) return 2;
        return 3;
    }

    private static string GetSeverityLevel(int cssWeight)
    {
        if (cssWeight >= 300) return "High";
        if (cssWeight >= 60) return "Medium";
        return "Low";
    }
}
