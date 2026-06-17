using Core_Layer.Models;
using Core_Layer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using Analysis_Web.Models;
using Microsoft.AspNetCore.Authorization;
using Business_Layer;
using Microsoft.EntityFrameworkCore;
using Domain_Layer.DbModels.Enum;

namespace Analysis_Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly IMemoryCache _cache;
        private readonly IUnitOfWork _unitOfWork;

        public HomeController(IMemoryCache cache, IUnitOfWork unitOfWork)
        {
            _cache = cache;
            _unitOfWork = unitOfWork;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var stats = await GetDashboardStatsFromDb();
            return View(stats);
        }

        [HttpGet]
        public async Task<IActionResult> FilterMapPoints(
            string? fileNumber,
            string? crimeType,
            string? neighborhood,
            string? dateFrom,
            string? dateTo,
            string? crimeDateFrom,
            string? crimeDateTo)
        {
            var query = _unitOfWork.CrimeReports.AsNoTracking();

            if (!string.IsNullOrEmpty(fileNumber))
                query = query.Where(r => r.FileNumber != null && r.FileNumber.Contains(fileNumber));

            if (!string.IsNullOrEmpty(crimeType) && crimeType != "All")
            {
                if (Enum.TryParse<CrimeType>(crimeType, true, out var crimeEnum))
                    query = query.Where(r => r.CrimeType == crimeEnum);
            }

            if (!string.IsNullOrEmpty(neighborhood) && neighborhood != "All")
                query = query.Where(r => r.Neighborhood == neighborhood);

            if (DateTime.TryParse(dateFrom, out var df))
                query = query.Where(r => r.DateOfReport >= df);
            if (DateTime.TryParse(dateTo, out var dt))
                query = query.Where(r => r.DateOfReport <= dt);
            if (DateTime.TryParse(crimeDateFrom, out var cdf))
                query = query.Where(r => r.CrimeDateTime >= cdf);
            if (DateTime.TryParse(crimeDateTo, out var cdt))
                query = query.Where(r => r.CrimeDateTime <= cdt);

            var filtered = await query.ToListAsync();

            var points = filtered
                .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
                .Take(5000)
                .Select(r => new
                {
                    lat = (double)r.Latitude.Value,
                    lng = (double)r.Longitude.Value,
                    crimeType = r.CrimeType.ToString(),
                    fn = r.FileNumber ?? "",
                    nbr = r.Neighborhood ?? "",
                    dt = r.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
                    cssWeight = CrimeDataService.GetCssWeight(r.CrimeType.ToString())
                });

            return Json(new { total = filtered.Count, points });
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() =>
            View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

        #region Private Methods

        private async Task<DashboardStatsModel> GetDashboardStatsFromDb()
        {
            try
            {
                var model = new DashboardStatsModel();

                // 1. Total counts
                model.TotalRecords = await _unitOfWork.CrimeReports.CountAsync();
                model.TotalWithCoords = await _unitOfWork.CrimeReports
                    .CountAsync(r => r.Latitude.HasValue && r.Longitude.HasValue);

                // 2. Crime type counts
                model.CrimeTypeCounts = await _unitOfWork.CrimeReports
                    .GroupBy(r => r.CrimeType)
                    .Select(g => new { CrimeType = g.Key.ToString(), Count = g.Count() })
                    .ToDictionaryAsync(k => k.CrimeType, v => v.Count);

                // 3. Neighborhood counts
                model.NeighborhoodCounts = await _unitOfWork.CrimeReports
                    .Where(r => r.Neighborhood != null)
                    .GroupBy(r => r.Neighborhood!)
                    .Select(g => new { Neighborhood = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.Neighborhood, v => v.Count);

                // 4. Yearly counts
                model.YearlyCounts = await _unitOfWork.CrimeReports
                    .Where(r => r.DateOfReport.HasValue)
                    .GroupBy(r => r.DateOfReport!.Value.Year)
                    .Select(g => new { Year = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.Year, v => v.Count);

                // 5. Hourly counts
                model.HourlyCounts = await _unitOfWork.CrimeReports
                    .Where(r => r.CrimeDateTime.HasValue)
                    .GroupBy(r => r.CrimeDateTime!.Value.Hour)
                    .Select(g => new { Hour = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.Hour, v => v.Count);

                // 6. Map points (only needed for the heatmap, limit to 5000 for performance)
                var mapPointsRaw = await _unitOfWork.CrimeReports
                    .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
                    .Select(r => new { r.Latitude, r.Longitude, r.CrimeType, r.FileNumber, r.Neighborhood, r.DateOfReport })
                    .Take(5000)
                    .ToListAsync();

                model.MapPoints = mapPointsRaw.Select(r => new MapPointModel
                {
                    Lat = (double)r.Latitude!.Value,
                    Lng = (double)r.Longitude!.Value,
                    CrimeType = r.CrimeType.ToString(),
                    FileNumber = r.FileNumber ?? "",
                    Neighborhood = r.Neighborhood ?? "",
                    DateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
                    CssWeight = CrimeDataService.GetCssWeight(r.CrimeType.ToString())
                }).ToList();

                // 7. Recent incidents (10 most recent by DateOfReport)
                var recentRaw = await _unitOfWork.CrimeReports
                    .OrderByDescending(r => r.DateOfReport)
                    .Take(10)
                    .Select(r => new { r.FileNumber, r.CrimeType, r.Neighborhood, r.Location, r.DateOfReport, r.CrimeDateTime })
                    .ToListAsync();

                model.RecentIncidents = recentRaw.Select(r => new RecentIncidentModel
                {
                    FileNumber = r.FileNumber ?? "",
                    CrimeType = r.CrimeType.ToString(),
                    Neighborhood = r.Neighborhood ?? "",
                    Location = r.Location ?? "",
                    DateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
                    CrimeDateTime = r.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    CssWeight = CrimeDataService.GetCssWeight(r.CrimeType.ToString()),
                    SeverityLevel = GetSeverityLevel(CrimeDataService.GetCssWeight(r.CrimeType.ToString()))
                }).ToList();

                // 8. Harm-based stats (requires weights, compute via aggregation)
                await ComputeHarmStatsOptimized(model);

                return model;
            }
            catch (Exception ex)
            {
                // Log error here
                // Return empty model (or throw)
                return new DashboardStatsModel();
            }
        }

        private async Task ComputeHarmStatsOptimized(DashboardStatsModel model)
        {
            // Get all necessary data for harm calculations (but only required fields)
            var harmData = await _unitOfWork.CrimeReports
                .Select(r => new
                {
                    r.CrimeType,
                    r.DateOfReport,
                    r.CrimeDateTime,
                    r.Neighborhood
                })
                .ToListAsync();

            var withWeight = harmData.Select(r => new
            {
                CrimeType = r.CrimeType.ToString(),
                Weight = CrimeDataService.GetCssWeight(r.CrimeType.ToString()),
                Year = r.DateOfReport?.Year,
                HourBlock = GetHourBlock(r.CrimeDateTime),
                Neighborhood = r.Neighborhood ?? "Unknown"
            }).ToList();

            model.TotalHarm = withWeight.Sum(x => x.Weight);

            model.HarmByType = withWeight
                .GroupBy(x => x.CrimeType)
                .Select(g => new HarmByTypeModel
                {
                    CrimeType = g.Key,
                    Count = g.Count(),
                    TotalHarm = g.Sum(x => x.Weight),
                    CssWeight = (int)g.First().Weight
                })
                .OrderByDescending(x => x.TotalHarm)
                .ToList();

            model.YearlyHarm = withWeight
              .Where(x => x.Year.HasValue)
              .GroupBy(x => x.Year!.Value)
              .ToDictionary(g => g.Key, g => g.Sum(x => (long)x.Weight));

            model.HighHarm = withWeight.Where(x => x.Weight >= 300).Sum(x => x.Weight);
            model.MediumHarm = withWeight.Where(x => x.Weight >= 60 && x.Weight < 300).Sum(x => x.Weight);
            model.LowHarm = withWeight.Where(x => x.Weight < 60).Sum(x => x.Weight);

            for (int block = 0; block < 4; block++)
            {
                var blockSum = withWeight.Where(x => x.HourBlock == block).Sum(x => x.Weight);
                model.HourlyHarmPct[block] = model.TotalHarm > 0 ? (double)blockSum / model.TotalHarm * 100 : 0;
            }

            model.HarmByNeighborhood = withWeight
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

        private int GetHourBlock(DateTime? crimeDateTime)
        {
            if (!crimeDateTime.HasValue) return 0;
            int hour = crimeDateTime.Value.Hour;
            if (hour < 6) return 0;
            if (hour < 12) return 1;
            if (hour < 18) return 2;
            return 3;
        }

        private string GetSeverityLevel(int cssWeight)
        {
            if (cssWeight >= 300) return "High";
            if (cssWeight >= 60) return "Medium";
            return "Low";
        }

        #endregion
    }
}
