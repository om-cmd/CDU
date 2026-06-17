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
using Core_Layer.ViewModels;
using System.Security.Claims;

namespace Analysis_Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly IMemoryCache _cache;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IWebHostEnvironment _environment;

        public HomeController(IMemoryCache cache, IUnitOfWork unitOfWork, IWebHostEnvironment environment)
        {
            _cache = cache;
            _unitOfWork = unitOfWork;
            _environment = environment;
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

            var total = await query.CountAsync();
            var rawPoints = await query
                .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
                .OrderByDescending(r => r.DateOfReport)
                .Take(5000)
                .Select(r => new
                {
                    r.Latitude,
                    r.Longitude,
                    r.CrimeType,
                    r.FileNumber,
                    r.Neighborhood,
                    r.DateOfReport
                })
                .ToListAsync();
            var points = rawPoints.Select(r => new
            {
                lat = (double)r.Latitude!.Value,
                lng = (double)r.Longitude!.Value,
                crimeType = r.CrimeType.ToString(),
                fn = r.FileNumber ?? "",
                nbr = r.Neighborhood ?? "",
                dt = r.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
                cssWeight = CrimeDataService.GetCssWeight(r.CrimeType.ToString())
            });

            return Json(new { total, points });
        }

        public IActionResult Privacy() => View();

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _unitOfWork.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserAccountId == CurrentUserId() && !x.Deleted);

            if (user == null) return NotFound();
            return View(MapProfile(user));
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(UserProfileDto model, IFormFile? profileImage)
        {
            var user = await _unitOfWork.Users
                .FirstOrDefaultAsync(x => x.UserAccountId == CurrentUserId() && !x.Deleted);

            if (user == null) return NotFound();

            ModelState.Remove(nameof(UserProfileDto.UserType));
            ModelState.Remove(nameof(UserProfileDto.ImageUrl));
            ModelState.Remove(nameof(UserProfileDto.LastLoginAt));

            if (!ModelState.IsValid)
            {
                model.UserAccountId = user.UserAccountId;
                model.Email = user.Email;
                model.UserName = user.UserName;
                model.UserType = user.UserType.ToString();
                model.ImageUrl = user.ImageUrl;
                model.EmailConfirmedStatus = user.EmailConfirmedStatus;
                model.LastLoginAt = user.LastLoginAt;
                return View(model);
            }

            user.FullName = model.FullName.Trim();
            user.Department = NullIfWhiteSpace(model.Department);
            user.Contact = NullIfWhiteSpace(model.Contact);
            user.DateOfBirth = model.DateOfBirth;
            user.Address = NullIfWhiteSpace(model.Address);
            user.City = NullIfWhiteSpace(model.City);
            user.State = NullIfWhiteSpace(model.State);
            user.Country = NullIfWhiteSpace(model.Country);
            user.PostalCode = NullIfWhiteSpace(model.PostalCode);
            user.Gender = model.Gender;
            user.UpdatedAt = DateTime.UtcNow;
            user.DateModified = DateTime.UtcNow;

            if (profileImage is { Length: > 0 })
            {
                var imageResult = await SaveProfileImageAsync(profileImage);
                if (!imageResult.Success)
                {
                    ModelState.AddModelError(nameof(profileImage), imageResult.Message);
                    var invalidModel = MapProfile(user);
                    return View(invalidModel);
                }

                user.ImageUrl = imageResult.Path;
            }

            await _unitOfWork.SaveChangesAsync();
            TempData["Success"] = "Profile updated successfully.";
            return RedirectToAction(nameof(Profile));
        }

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
                model.TotalRecords = await _unitOfWork.CrimeReports.AsNoTracking().CountAsync();
                model.TotalWithCoords = await _unitOfWork.CrimeReports
                    .AsNoTracking()
                    .CountAsync(r => r.Latitude.HasValue && r.Longitude.HasValue);

                // 2. Crime type counts
                model.CrimeTypeCounts = await _unitOfWork.CrimeReports
                    .AsNoTracking()
                    .GroupBy(r => r.CrimeType)
                    .Select(g => new { CrimeType = g.Key.ToString(), Count = g.Count() })
                    .ToDictionaryAsync(k => k.CrimeType, v => v.Count);

                // 3. Neighborhood counts
                model.NeighborhoodCounts = await _unitOfWork.CrimeReports
                    .AsNoTracking()
                    .Where(r => r.Neighborhood != null)
                    .GroupBy(r => r.Neighborhood!)
                    .Select(g => new { Neighborhood = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.Neighborhood, v => v.Count);

                // 4. Yearly counts
                model.YearlyCounts = await _unitOfWork.CrimeReports
                    .AsNoTracking()
                    .Where(r => r.DateOfReport.HasValue)
                    .GroupBy(r => r.DateOfReport!.Value.Year)
                    .Select(g => new { Year = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.Year, v => v.Count);

                // 5. Hourly counts
                model.HourlyCounts = await _unitOfWork.CrimeReports
                    .AsNoTracking()
                    .Where(r => r.CrimeDateTime.HasValue)
                    .GroupBy(r => r.CrimeDateTime!.Value.Hour)
                    .Select(g => new { Hour = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.Hour, v => v.Count);

                // 6. Map points (only needed for the heatmap, limit to 5000 for performance)
                var mapPointsRaw = await _unitOfWork.CrimeReports
                    .AsNoTracking()
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
                    .AsNoTracking()
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
                .AsNoTracking()
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

        private int CurrentUserId()
            => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        private static UserProfileDto MapProfile(Domain_Layer.DbModels.ApplicationUser user) => new()
        {
            UserAccountId = user.UserAccountId,
            FullName = user.FullName,
            Email = user.Email,
            UserName = user.UserName,
            Department = user.Department,
            Contact = user.Contact,
            DateOfBirth = user.DateOfBirth,
            Address = user.Address,
            City = user.City,
            State = user.State,
            Country = user.Country,
            PostalCode = user.PostalCode,
            Gender = user.Gender,
            ImageUrl = user.ImageUrl,
            UserType = user.UserType.ToString(),
            EmailConfirmedStatus = user.EmailConfirmedStatus,
            LastLoginAt = user.LastLoginAt
        };

        private async Task<(bool Success, string Message, string? Path)> SaveProfileImageAsync(IFormFile file)
        {
            const long maxBytes = 2 * 1024 * 1024;
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.FileName);

            if (file.Length > maxBytes)
                return (false, "Profile picture must be 2 MB or smaller.", null);

            if (!allowedExtensions.Contains(extension) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return (false, "Upload a JPG, PNG, or WEBP image.", null);

            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "profile");
            Directory.CreateDirectory(uploadRoot);

            var fileName = $"{CurrentUserId()}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var physicalPath = Path.Combine(uploadRoot, fileName);

            await using var stream = System.IO.File.Create(physicalPath);
            await file.CopyToAsync(stream);

            return (true, "Uploaded.", $"/uploads/profile/{fileName}");
        }

        private static string? NullIfWhiteSpace(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        #endregion
    }
}
