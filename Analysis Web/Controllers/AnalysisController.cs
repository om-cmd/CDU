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
    private const int MapPointLimit = 2500;
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
            TotalRecords = await _unitOfWork.CrimeReports.AsNoTracking().CountAsync(),
            TotalWithCoords = await _unitOfWork.CrimeReports.AsNoTracking().CountAsync(r => r.Latitude.HasValue && r.Longitude.HasValue)
        };

        var latestDate = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.DateOfReport.HasValue)
            .MaxAsync(r => r.DateOfReport);

        model.LatestReportDate = latestDate?.ToString("yyyy-MM-dd") ?? "Not available";

        model.CrimeTypeCounts = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .GroupBy(r => r.CrimeType)
            .Select(g => new { CrimeType = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToDictionaryAsync(k => k.CrimeType, v => v.Count);

        var neighborhoodCounts = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.Neighborhood != null)
            .GroupBy(r => r.Neighborhood!)
            .Select(g => new { Neighborhood = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(25)
            .ToListAsync();

        model.NeighborhoodCounts = neighborhoodCounts.ToDictionary(k => k.Neighborhood, v => v.Count);
        var topNeighborhood = neighborhoodCounts.FirstOrDefault();
        if (topNeighborhood != null)
        {
            model.TopNeighborhoodName = topNeighborhood.Neighborhood;
            model.TopNeighborhoodCount = topNeighborhood.Count;
        }

        model.YearlyCounts = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.ReportYear.HasValue)
            .GroupBy(r => r.ReportYear!.Value)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderBy(x => x.Year)
            .ToDictionaryAsync(k => k.Year, v => v.Count);

        model.MonthlyCounts = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.ReportYear.HasValue && r.ReportMonth.HasValue)
            .GroupBy(r => new { Year = r.ReportYear!.Value, Month = r.ReportMonth!.Value })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToDictionaryAsync(k => $"{k.Year}-{k.Month:00}", v => v.Count);

        model.HourlyCounts = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.CrimeHour.HasValue)
            .GroupBy(r => r.CrimeHour!.Value)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToDictionaryAsync(k => k.Hour, v => v.Count);

        var mapPointsRaw = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
            .OrderByDescending(r => r.DateOfReport)
            .Select(r => new { r.Latitude, r.Longitude, r.CrimeType, r.FileNumber, r.Neighborhood, r.DateOfReport })
            .Take(MapPointLimit)
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
            .AsNoTracking()
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
        var harmByTypeRaw = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .GroupBy(r => r.CrimeType)
            .Select(g => new { CrimeType = g.Key, Count = g.Count() })
            .ToListAsync();

        model.HarmByType = harmByTypeRaw
            .Select(x =>
            {
                var crimeType = x.CrimeType.ToString();
                var weight = CrimeDataService.GetCssWeight(crimeType);
                return new HarmByTypeModel
                {
                    CrimeType = crimeType,
                    Count = x.Count,
                    TotalHarm = (long)x.Count * weight,
                    CssWeight = weight
                };
            })
            .OrderByDescending(x => x.TotalHarm)
            .ToList();
        model.TotalHarm = model.HarmByType.Sum(x => x.TotalHarm);

        var monthlyHarmRaw = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.ReportYear.HasValue && r.ReportMonth.HasValue)
            .GroupBy(r => new { Year = r.ReportYear!.Value, Month = r.ReportMonth!.Value, r.CrimeType })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.CrimeType, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync();

        model.MonthlyHarm = monthlyHarmRaw
            .GroupBy(x => $"{x.Year}-{x.Month:00}")
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => (long)x.Count * CrimeDataService.GetCssWeight(x.CrimeType.ToString())));

        var severityCounts = model.HarmByType
            .Select(x => new { x.Count, Total = x.TotalHarm, x.CssWeight })
            .ToList();

        model.HighHarmIncidentCount = severityCounts.Where(x => x.CssWeight >= 300).Sum(x => x.Count);
        model.MediumHarmIncidentCount = severityCounts.Where(x => x.CssWeight >= 60 && x.CssWeight < 300).Sum(x => x.Count);
        model.LowHarmIncidentCount = severityCounts.Where(x => x.CssWeight < 60).Sum(x => x.Count);
        model.HighHarm = severityCounts.Where(x => x.CssWeight >= 300).Sum(x => x.Total);
        model.MediumHarm = severityCounts.Where(x => x.CssWeight >= 60 && x.CssWeight < 300).Sum(x => x.Total);
        model.LowHarm = severityCounts.Where(x => x.CssWeight < 60).Sum(x => x.Total);

        var hourlyHarmRaw = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.CrimeHour.HasValue)
            .GroupBy(r => new { r.CrimeHour, r.CrimeType })
            .Select(g => new { Hour = g.Key.CrimeHour!.Value, g.Key.CrimeType, Count = g.Count() })
            .ToListAsync();

        for (var block = 0; block < 4; block++)
        {
            var blockSum = hourlyHarmRaw
                .Where(x => GetHourBlock(x.Hour) == block)
                .Sum(x => (long)x.Count * CrimeDataService.GetCssWeight(x.CrimeType.ToString()));
            model.HourlyHarmPct[block] = model.TotalHarm > 0 ? (double)blockSum / model.TotalHarm * 100 : 0;
        }

        var harmByNeighborhoodRaw = await _unitOfWork.CrimeReports
            .AsNoTracking()
            .Where(r => r.Neighborhood != null)
            .GroupBy(r => new { r.Neighborhood, r.CrimeType })
            .Select(g => new { Neighborhood = g.Key.Neighborhood!, g.Key.CrimeType, Count = g.Count() })
            .ToListAsync();

        model.HarmByNeighborhood = harmByNeighborhoodRaw
            .GroupBy(x => x.Neighborhood)
            .Select(g =>
            {
                return new HarmByNeighborhoodModel
                {
                    Name = g.Key,
                    Count = g.Sum(x => x.Count),
                    TotalHarm = g.Sum(x => (long)x.Count * CrimeDataService.GetCssWeight(x.CrimeType.ToString()))
                };
            })
            .OrderByDescending(x => x.TotalHarm)
            .Take(25)
            .ToList();
    }

    private static int GetHourBlock(int hour)
    {
        if (hour < 6) return 0;
        if (hour < 12) return 1;
        if (hour < 18) return 2;
        return 3;
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
