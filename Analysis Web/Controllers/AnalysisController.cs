using Analysis_Web.Services;
using Business_Layer;
using Core_Layer.Models;
using Core_Layer.Services;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace Analysis_Web.Controllers;

[Authorize]
public class AnalysisController : Controller
{
    private const int MaxRowsForPython = 250000;
    private const int MapPointLimit = 2500;
    private static readonly SemaphoreSlim AnalysisRunGate = new(1, 1);
    private static readonly object AnalysisCacheLock = new();
    private static CachedAnalysisRun? _cachedAnalysisRun;
    private static ActiveAnalysisRun? _activeAnalysisRun;
    private readonly ICrimeReportInterface _crimeReports;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        ICrimeReportInterface crimeReports,
        IUnitOfWork unitOfWork,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger<AnalysisController> logger)
    {
        _crimeReports = crimeReports;
        _unitOfWork = unitOfWork;
        _httpClientFactory = httpClientFactory;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Prediction()
    {
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard([FromQuery] DashboardFilterModel filters)
    {
        return View(await BuildDashboardStatsAsync(filters));
    }

    [HttpGet]
    public async Task<IActionResult> CrimeDashboard([FromQuery] DashboardFilterModel filters)
    {
        return View(await BuildDashboardStatsAsync(filters));
    }

    [HttpGet]
    public async Task<IActionResult> HarmDashboard([FromQuery] DashboardFilterModel filters)
    {
        return View(await BuildDashboardStatsAsync(filters));
    }

    [HttpGet]
    public IActionResult ModelEvaluation()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run([FromBody] DotNetCrimeAnalysisRequest request)
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";

        request.PageSize = Math.Clamp(request.PageSize <= 0 ? 100000 : request.PageSize, 100, MaxRowsForPython);
        request.ForecastPeriods = Math.Clamp(request.ForecastPeriods <= 0 ? 30 : request.ForecastPeriods, 1, 90);
        request.Frequency = string.Equals(request.Frequency, "W", StringComparison.OrdinalIgnoreCase) ? "W" : "D";

        var crimeType = TryParseCrimeType(request.CrimeType);
        var payloadRecords = new List<PythonCrimeReportDto>();
        var payload = new PythonCrimeAnalysisRequest();
        int totalCount;
        DateTime? dataDateFrom;
        DateTime? dataDateTo;
        DateTime? latestImportedAtUtc;
        bool rowsTruncated;

        if (!AnalysisRunGate.Wait(0))
        {
            var activeRun = GetActiveAnalysisRun();
            Response.Headers["Retry-After"] = "5";
            return Conflict(new
            {
                code = "ANALYSIS_RUNNING",
                message = "A model evaluation is already running. This page will retry automatically; do not start another task.",
                retryable = true,
                retryAfterSeconds = 5,
                startedAtUtc = activeRun?.StartedAtUtc,
                stage = activeRun?.Stage ?? "Model evaluation in progress"
            });
        }

        var runStartedAtUtc = DateTime.UtcNow;
        SetActiveAnalysisRun(new ActiveAnalysisRun(runStartedAtUtc, "Reading current CrimeReports data"));
        using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            _applicationLifetime.ApplicationStopping);
        operationTimeout.CancelAfter(TimeSpan.FromMinutes(35));
        var operationToken = operationTimeout.Token;

        try
        {
        if (request.IncludeAllRows)
        {
            var query = ApplyAnalysisFilters(
                _unitOfWork.CrimeReports.AsNoTracking(),
                request,
                crimeType);
            var snapshot = await query
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    TotalCount = group.Count(),
                    DateFrom = group.Min(r => r.DateOfReport),
                    DateTo = group.Max(r => r.DateOfReport),
                    LatestImportedAtUtc = group.Max(r => (DateTime?)r.ImportedAt)
                })
                .SingleOrDefaultAsync(operationToken);

            totalCount = snapshot?.TotalCount ?? 0;
            dataDateFrom = snapshot?.DateFrom;
            dataDateTo = snapshot?.DateTo;
            latestImportedAtUtc = snapshot?.LatestImportedAtUtc;

            SetActiveAnalysisRun(new ActiveAnalysisRun(runStartedAtUtc, "Preparing weighted daily aggregates"));
            var aggregates = await query
                .Where(r => r.DateOfReport.HasValue)
                .GroupBy(r => new
                {
                    IncidentDate = r.DateOfReport!.Value.Date,
                    r.CrimeType,
                    Jurisdiction = r.Jurisdiction ?? "Unspecified",
                    DataSource = r.DataSource ?? "Unspecified"
                })
                .Select(group => new
                {
                    group.Key.IncidentDate,
                    group.Key.CrimeType,
                    group.Key.Jurisdiction,
                    group.Key.DataSource,
                    EventCount = group.Count()
                })
                .OrderBy(row => row.IncidentDate)
                .ToListAsync(operationToken);

            payloadRecords = aggregates.Select((row, index) => new PythonCrimeReportDto
            {
                FileNumber = $"AGG-{index + 1}",
                DateOfReport = row.IncidentDate,
                CrimeDateTime = row.IncidentDate,
                CrimeType = row.CrimeType.ToString(),
                Neighborhood = row.Jurisdiction,
                Jurisdiction = row.Jurisdiction,
                DataSource = row.DataSource,
                EventCount = row.EventCount
            }).ToList();
            rowsTruncated = false;
        }
        else
        {
            var (reports, matchingCount) = await _crimeReports.GetPagedAsync(
                page: 1,
                pageSize: request.PageSize,
                search: request.Search,
                crimeType: crimeType,
                year: request.Year,
                neighborhood: request.Neighborhood,
                sortBy: "dateOfReport",
                ascending: false,
                cancellationToken: operationToken);

            var reportList = reports.ToList();
            totalCount = matchingCount;
            dataDateFrom = reportList.Count == 0 ? null : reportList.Min(r => r.DateOfReport);
            dataDateTo = reportList.Count == 0 ? null : reportList.Max(r => r.DateOfReport);
            latestImportedAtUtc = reportList.Count == 0 ? null : reportList.Max(r => r.ImportedAt);
            rowsTruncated = totalCount > reportList.Count;
            payloadRecords = reportList.Select(r => new PythonCrimeReportDto
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
                Jurisdiction = r.Jurisdiction,
                DataSource = r.DataSource,
                EventCount = 1,
                Latitude = r.Latitude.HasValue ? Convert.ToDouble(r.Latitude.Value) : null,
                Longitude = r.Longitude.HasValue ? Convert.ToDouble(r.Longitude.Value) : null
            }).ToList();
        }

        payload = new PythonCrimeAnalysisRequest
        {
            Frequency = request.Frequency,
            ForecastPeriods = request.ForecastPeriods,
            Records = payloadRecords
        };

        if (payload.Records.Count == 0)
        {
            return BadRequest(new
            {
                message = "No CrimeReports records were found for the selected filters. Import the CSV or create manual crime reports first.",
                source = "CrimeReports table",
                dataScope = "All current CrimeReports records",
                totalAvailableRows = totalCount,
                rowsSentToPython = 0
            });
        }

        var cacheKey = string.Join("|",
            request.Frequency,
            request.ForecastPeriods,
            request.IncludeAllRows,
            request.Search?.Trim() ?? string.Empty,
            request.Neighborhood?.Trim() ?? string.Empty,
            request.CrimeType?.Trim() ?? string.Empty,
            request.Year?.ToString() ?? string.Empty,
            totalCount,
            payload.Records.Count,
            dataDateFrom?.Ticks ?? 0,
            dataDateTo?.Ticks ?? 0,
            latestImportedAtUtc?.Ticks ?? 0);

            if (!request.ForceRetrain &&
                TryGetCachedAnalysis(cacheKey, out var cachedResult, out _))
            {
                return BuildAnalysisResponse(cachedResult, modelRetrained: false, cachedResult: true);
            }

            SetActiveAnalysisRun(new ActiveAnalysisRun(runStartedAtUtc, "Training and evaluating candidate models"));
            var client = _httpClientFactory.CreateClient("CrimeAnalysisPython");
            using var analysisTimeout = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
            analysisTimeout.CancelAfter(TimeSpan.FromMinutes(30));
            var response = await client.PostAsJsonAsync(
                "/api/v1/crime/analyze",
                payload,
                analysisTimeout.Token);
            var body = await response.Content.ReadAsStringAsync(analysisTimeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python analysis failed with {StatusCode}: {Body}", response.StatusCode, body);
                return StatusCode((int)response.StatusCode, new
                {
                    message = "Python analysis API could not complete the evaluation.",
                    details = body
                });
            }

            using var pythonDocument = JsonDocument.Parse(body);
            var pythonResult = pythonDocument.RootElement.Clone();
            lock (AnalysisCacheLock)
            {
                _cachedAnalysisRun = new CachedAnalysisRun(cacheKey, pythonResult, DateTime.UtcNow);
            }

            return BuildAnalysisResponse(pythonResult, modelRetrained: true, cachedResult: false);
        }
        catch (OperationCanceledException) when (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogInformation("Crime model evaluation stopped because the application is shutting down.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The application is restarting. Reload the page after the server starts.",
                retryable = true
            });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Crime model evaluation exceeded the server-side analysis timeout.");
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                message = "Database preparation or model training exceeded the server safety limit. No database data was changed; check SQL Server and the Python service, then retry.",
                retryable = true
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach Python analysis API.");
            return StatusCode(503, new
            {
                message = "Could not reach Python analysis API. Start the FastAPI project on http://localhost:8001 first."
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Python analysis API returned invalid JSON.");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "The Python analysis service returned an invalid response. Check its console log and retry.",
                retryable = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crime model evaluation failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "The model evaluation could not be completed. The failure was recorded; it is safe to retry.",
                retryable = true
            });
        }
        finally
        {
            ClearActiveAnalysisRun();
            AnalysisRunGate.Release();
        }

        IActionResult BuildAnalysisResponse(
            JsonElement pythonResult,
            bool modelRetrained,
            bool cachedResult)
        {
            return Json(new
            {
                source = "CrimeReports table",
                dataScope = request.IncludeAllRows
                    ? "all current matching database events"
                    : "the selected recent database events",
                totalAvailableRows = totalCount,
                rowsSentToPython = totalCount,
                aggregateRowsSentToPython = payload.Records.Count,
                rowsTruncated,
                rowSelection = rowsTruncated
                    ? $"Most recent {payload.Records.Count:N0} rows by report date"
                    : request.IncludeAllRows
                        ? $"All matching database rows compressed to {payload.Records.Count:N0} daily jurisdiction/crime aggregates"
                        : "All matching database rows",
                latestImportedAtUtc,
                dataDateFrom,
                dataDateTo,
                modelRetrained,
                cachedResult,
                pythonResult
            });
        }
    }

    private static bool TryGetCachedAnalysis(
        string cacheKey,
        out JsonElement pythonResult,
        out DateTime completedAtUtc)
    {
        lock (AnalysisCacheLock)
        {
            if (_cachedAnalysisRun is not null &&
                string.Equals(_cachedAnalysisRun.CacheKey, cacheKey, StringComparison.Ordinal))
            {
                pythonResult = _cachedAnalysisRun.PythonResult;
                completedAtUtc = _cachedAnalysisRun.CompletedAtUtc;
                return true;
            }
        }

        pythonResult = default;
        completedAtUtc = default;
        return false;
    }

    private static ActiveAnalysisRun? GetActiveAnalysisRun()
    {
        lock (AnalysisCacheLock)
        {
            return _activeAnalysisRun;
        }
    }

    private static void SetActiveAnalysisRun(ActiveAnalysisRun run)
    {
        lock (AnalysisCacheLock)
        {
            _activeAnalysisRun = run;
        }
    }

    private static void ClearActiveAnalysisRun()
    {
        lock (AnalysisCacheLock)
        {
            _activeAnalysisRun = null;
        }
    }

    private sealed record CachedAnalysisRun(
        string CacheKey,
        JsonElement PythonResult,
        DateTime CompletedAtUtc);

    private sealed record ActiveAnalysisRun(
        DateTime StartedAtUtc,
        string Stage);

    private static CrimeType? TryParseCrimeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<CrimeType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

    private static IQueryable<CrimeReport> ApplyAnalysisFilters(
        IQueryable<CrimeReport> query,
        DotNetCrimeAnalysisRequest request,
        CrimeType? crimeType)
    {
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchText = request.Search.Trim();
            var pattern = $"%{searchText}%";
            var matchingCrimeTypes = Enum.GetValues<CrimeType>()
                .Where(value => value.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
            query = query.Where(report =>
                EF.Functions.Like(report.FileNumber, pattern) ||
                (report.Neighborhood != null && EF.Functions.Like(report.Neighborhood, pattern)) ||
                (report.Location != null && EF.Functions.Like(report.Location, pattern)) ||
                (report.ReportingArea != null && EF.Functions.Like(report.ReportingArea, pattern)) ||
                (report.Jurisdiction != null && EF.Functions.Like(report.Jurisdiction, pattern)) ||
                (report.DataSource != null && EF.Functions.Like(report.DataSource, pattern)) ||
                matchingCrimeTypes.Contains(report.CrimeType));
        }

        if (crimeType.HasValue)
            query = query.Where(report => report.CrimeType == crimeType.Value);
        if (request.Year.HasValue)
            query = query.Where(report => report.ReportYear == request.Year.Value);
        if (!string.IsNullOrWhiteSpace(request.Neighborhood))
        {
            var neighborhoodPattern = $"%{request.Neighborhood.Trim()}%";
            query = query.Where(report =>
                report.Neighborhood != null &&
                EF.Functions.Like(report.Neighborhood, neighborhoodPattern));
        }

        return query;
    }

    private async Task<DashboardStatsModel> BuildDashboardStatsAsync(DashboardFilterModel? filters = null)
    {
        filters ??= new DashboardFilterModel();
        var allReports = _unitOfWork.CrimeReports.AsNoTracking();
        var filteredReports = ApplyDashboardFilters(allReports, filters);
        var model = new DashboardStatsModel
        {
            Filters = filters,
            FilterOptions = await BuildFilterOptionsAsync(allReports),
            TotalRecords = await filteredReports.CountAsync(),
            TotalWithCoords = await filteredReports.CountAsync(r => r.Latitude.HasValue && r.Longitude.HasValue)
        };

        var latestDate = await filteredReports
            .Where(r => r.DateOfReport.HasValue)
            .MaxAsync(r => r.DateOfReport);

        model.LatestReportDate = latestDate?.ToString("yyyy-MM-dd") ?? "Not available";

        model.CrimeTypeCounts = await filteredReports
            .GroupBy(r => r.CrimeType)
            .Select(g => new { CrimeType = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToDictionaryAsync(k => k.CrimeType, v => v.Count);

        var locationRows = await filteredReports
            .Select(r => new { r.Neighborhood, r.Location, r.ReportingArea })
            .ToListAsync();

        var neighborhoodCounts = locationRows
            .Select(r => GetLocationLabel(r.Neighborhood, r.Location, r.ReportingArea))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name)
            .Select(g => new { Neighborhood = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(25)
            .ToList();

        model.NeighborhoodCounts = neighborhoodCounts.ToDictionary(k => k.Neighborhood, v => v.Count);
        var topNeighborhood = neighborhoodCounts.FirstOrDefault();
        if (topNeighborhood != null)
        {
            model.TopNeighborhoodName = topNeighborhood.Neighborhood;
            model.TopNeighborhoodCount = topNeighborhood.Count;
        }

        model.YearlyCounts = await filteredReports
            .Where(r => r.ReportYear.HasValue)
            .GroupBy(r => r.ReportYear!.Value)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderBy(x => x.Year)
            .ToDictionaryAsync(k => k.Year, v => v.Count);

        model.MonthlyCounts = await filteredReports
            .Where(r => r.ReportYear.HasValue && r.ReportMonth.HasValue)
            .GroupBy(r => new { Year = r.ReportYear!.Value, Month = r.ReportMonth!.Value })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToDictionaryAsync(k => $"{k.Year}-{k.Month:00}", v => v.Count);

        model.HourlyCounts = await filteredReports
            .Where(r => r.CrimeHour.HasValue)
            .GroupBy(r => r.CrimeHour!.Value)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToDictionaryAsync(k => k.Hour, v => v.Count);

        var mapPointsRaw = await filteredReports
            .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
            .OrderByDescending(r => r.DateOfReport)
            .Select(r => new { r.Latitude, r.Longitude, r.CrimeType, r.FileNumber, r.Neighborhood, r.Location, r.ReportingArea, r.DateOfReport })
            .Take(MapPointLimit)
            .ToListAsync();

        model.MapPoints = mapPointsRaw.Select(r => new MapPointModel
        {
            Lat = (double)r.Latitude!.Value,
            Lng = (double)r.Longitude!.Value,
            CrimeType = r.CrimeType.ToString(),
            FileNumber = r.FileNumber ?? "",
            Neighborhood = GetLocationLabel(r.Neighborhood, r.Location, r.ReportingArea),
            DateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
            CssWeight = CrimeDataService.GetCssWeight(r.CrimeType.ToString())
        }).ToList();

        var recentRaw = await filteredReports
            .OrderByDescending(r => r.DateOfReport)
            .Take(8)
            .Select(r => new { r.FileNumber, r.CrimeType, r.Neighborhood, r.Location, r.ReportingArea, r.DateOfReport, r.CrimeDateTime })
            .ToListAsync();

        model.RecentIncidents = recentRaw.Select(r =>
        {
            var weight = CrimeDataService.GetCssWeight(r.CrimeType.ToString());
            return new RecentIncidentModel
            {
                FileNumber = r.FileNumber ?? "",
                CrimeType = r.CrimeType.ToString(),
                Neighborhood = GetLocationLabel(r.Neighborhood, r.Location, r.ReportingArea),
                Location = r.Location ?? "",
                DateOfReport = r.DateOfReport?.ToString("dd MMM") ?? "",
                CrimeDateTime = r.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                CssWeight = weight,
                SeverityLevel = GetSeverityLevel(weight)
            };
        }).ToList();

        await AddHarmStatsAsync(model, filteredReports);
        return model;
    }

    private async Task AddHarmStatsAsync(DashboardStatsModel model, IQueryable<CrimeReport> filteredReports)
    {
        var harmByTypeRaw = await filteredReports
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

        var monthlyHarmRaw = await filteredReports
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

        var hourlyHarmRaw = await filteredReports
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

        var harmByNeighborhoodRaw = await filteredReports
            .Select(r => new { r.Neighborhood, r.Location, r.ReportingArea, r.CrimeType })
            .ToListAsync();

        model.HarmByNeighborhood = harmByNeighborhoodRaw
            .GroupBy(x => GetLocationLabel(x.Neighborhood, x.Location, x.ReportingArea))
            .Select(g =>
            {
                return new HarmByNeighborhoodModel
                {
                    Name = g.Key,
                    Count = g.Count(),
                    TotalHarm = g.Sum(x => (long)CrimeDataService.GetCssWeight(x.CrimeType.ToString()))
                };
            })
            .OrderByDescending(x => x.TotalHarm)
            .Take(25)
            .ToList();
    }

    private static IQueryable<CrimeReport> ApplyDashboardFilters(IQueryable<CrimeReport> query, DashboardFilterModel filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(r =>
                r.FileNumber.Contains(search)
                || (r.Location != null && r.Location.Contains(search))
                || (r.Neighborhood != null && r.Neighborhood.Contains(search))
                || (r.ReportingArea != null && r.ReportingArea.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(filters.CrimeType)
            && !string.Equals(filters.CrimeType, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<CrimeType>(filters.CrimeType, true, out var crimeType))
        {
            query = query.Where(r => r.CrimeType == crimeType);
        }

        if (!string.IsNullOrWhiteSpace(filters.Location)
            && !string.Equals(filters.Location, "All", StringComparison.OrdinalIgnoreCase))
        {
            var location = filters.Location.Trim();
            var reportingArea = location.StartsWith("Reporting Area ", StringComparison.OrdinalIgnoreCase)
                ? location["Reporting Area ".Length..].Trim()
                : location;
            query = query.Where(r =>
                r.Neighborhood == location
                || r.Location == location
                || r.ReportingArea == reportingArea);
        }

        if (filters.Year.HasValue)
            query = query.Where(r => r.ReportYear == filters.Year.Value);

        if (filters.DateFrom.HasValue)
            query = query.Where(r => r.DateOfReport >= filters.DateFrom.Value.Date);

        if (filters.DateTo.HasValue)
        {
            var to = filters.DateTo.Value.Date.AddDays(1);
            query = query.Where(r => r.DateOfReport < to);
        }

        if (filters.CrimeDateFrom.HasValue)
            query = query.Where(r => r.CrimeDateTime >= filters.CrimeDateFrom.Value.Date);

        if (filters.CrimeDateTo.HasValue)
        {
            var to = filters.CrimeDateTo.Value.Date.AddDays(1);
            query = query.Where(r => r.CrimeDateTime < to);
        }

        return query;
    }

    private static async Task<DashboardFilterOptions> BuildFilterOptionsAsync(IQueryable<CrimeReport> allReports)
    {
        var crimeTypes = await allReports
            .GroupBy(r => r.CrimeType)
            .Select(g => g.Key.ToString())
            .OrderBy(x => x)
            .ToListAsync();

        var years = await allReports
            .Where(r => r.ReportYear.HasValue)
            .GroupBy(r => r.ReportYear!.Value)
            .Select(g => g.Key)
            .OrderByDescending(x => x)
            .ToListAsync();

        var rawLocations = await allReports
            .Select(r => new { r.Neighborhood, r.Location, r.ReportingArea })
            .ToListAsync();

        var locations = rawLocations
            .Select(r => GetLocationLabel(r.Neighborhood, r.Location, r.ReportingArea))
            .Where(name => !string.IsNullOrWhiteSpace(name) && name != "Unspecified location")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .Take(250)
            .ToList();

        return new DashboardFilterOptions
        {
            CrimeTypes = crimeTypes,
            Locations = locations,
            Years = years
        };
    }

    private static string GetLocationLabel(string? neighborhood, string? location, string? reportingArea)
    {
        static bool HasValue(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value.Trim(), "n/a", StringComparison.OrdinalIgnoreCase);

        if (HasValue(neighborhood)) return neighborhood!.Trim();
        if (HasValue(location)) return location!.Trim();
        if (HasValue(reportingArea)) return $"Reporting Area {reportingArea!.Trim()}";
        return "Unspecified location";
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
