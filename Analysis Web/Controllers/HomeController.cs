using Core_Layer.Models;
using Core_Layer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using Analysis_Web.Models;
using Microsoft.AspNetCore.Authorization;

namespace Analysis_Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly IMemoryCache _cache;

        public HomeController(IWebHostEnvironment env, IMemoryCache cache)
        {
            _env = env;
            _cache = cache;
        }
[Authorize]
        public IActionResult Index()
        {
            var csvPath = Path.Combine(_env.WebRootPath, "CSV", "Crime_Reports_20260508.csv");
            var svc = new CrimeDataService(_cache, csvPath);
            var stats = svc.GetDashboardStats();
            return View(stats);
        }

        [HttpGet]
        public IActionResult FilterMapPoints(
            string? fileNumber,
            string? crimeType,
            string? neighborhood,
            string? dateFrom,
            string? dateTo,
            string? crimeDateFrom,
            string? crimeDateTo)
        {
            var csvPath = Path.Combine(_env.WebRootPath, "CSV", "Crime_Reports_20260508.csv");
            var svc = new CrimeDataService(_cache, csvPath);

            DateTime? ParseDate(string? s) =>
                DateTime.TryParse(s, out var d) ? d : null;

            var filtered = svc.FilterRecords(
                fileNumber, crimeType, neighborhood,
                ParseDate(dateFrom), ParseDate(dateTo),
                ParseDate(crimeDateFrom), ParseDate(crimeDateTo));

            var points = filtered
                .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
                .Take(5000)
                .Select(r => new
                {
                    lat = (double)r.Latitude!.Value,
                    lng = (double)r.Longitude!.Value,
                    crimeType = r.CrimeType.ToString(),
                    fn = r.FileNumber,
                    nbr = r.Neighborhood ?? "",
                    dt = r.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
                    // FIX: include cssWeight so heatmap intensity is preserved after filtering
                    cssWeight = CrimeDataService.GetCssWeight(r.CrimeType.ToString())
                });

            return Json(new { total = filtered.Count, points });
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() =>
            View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}