using Domain_Layer.DbModels.Enum;
using Analysis_Web.Services;
using Domain_Layer.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Analysis_Web.Controllers
{
    public class CrimeDataController : Controller
    {
        private readonly ICrimeReportInterface _service;
        private readonly AnalysisDbContext _db;

        public CrimeDataController(ICrimeReportInterface service, AnalysisDbContext db)
        {
            _service = service;
            _db = db;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> LoadData(int draw, int start, int length,string? searchValue, string? sortColumn, string? sortDir)
        {
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            length = Math.Clamp(length > 0 ? length : 25, 1, 500);

            var (reports, filteredCount) = await _service.GetPagedAsync(
                page: (start / length) + 1,
                pageSize: length,
                search: searchValue,
                crimeType: null,
                year: null,
                neighborhood: null,
                sortBy: sortColumn,
                ascending: string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)
            );

            var totalUnfiltered = await _service.GetTotalCountAsync();
            var connection = _db.Database.GetDbConnection();

            var paged = reports.Select(r => new
            {
                crimeReportId = r.CrimeReportId,
                fileNumber = r.FileNumber ?? "—",
                dateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                crimeDateTime = r.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                crimeType = r.CrimeType.ToString(),
                reportingArea = r.ReportingArea ?? "—",
                neighborhood = r.Neighborhood ?? "—",
                location = r.Location ?? "—",
                jurisdiction = r.Jurisdiction ?? "Unspecified",
                dataSource = r.DataSource ?? "Unspecified",
                latitude = r.Latitude?.ToString("F4") ?? "—",
                longitude = r.Longitude?.ToString("F4") ?? "—",
            });

            return Json(new
            {
                draw,
                recordsTotal = totalUnfiltered,
                recordsFiltered = filteredCount,
                source = new
                {
                    table = "CrimeReports",
                    database = connection.Database,
                    server = connection.DataSource,
                    countedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    rowCount = totalUnfiltered
                },
                data = paged
            });
        }

        [HttpGet]
        public async Task<IActionResult> SourceInfo()
        {
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            var connection = _db.Database.GetDbConnection();
            return Json(new
            {
                table = "CrimeReports",
                database = connection.Database,
                server = connection.DataSource,
                rowCount = await _db.CrimeReports.CountAsync(),
                checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

       
        [HttpGet]
        public async Task<IActionResult> ExportCsv(string? searchValue)
        {
            var (reports, _) = await _service.GetPagedAsync(
                page: 1, pageSize: int.MaxValue,
                search: searchValue,
                crimeType: null, year: null, neighborhood: null,
                sortBy: null, ascending: false);

            var sb = new StringBuilder();
            sb.AppendLine("File Number,Date of Report,Crime Date Time,Crime Type,Reporting Area,Neighborhood,Location,Jurisdiction,Data Source,Latitude,Longitude");

            foreach (var r in reports)
            {
                sb.AppendLine(string.Join(",",
                    CsvQuote(r.FileNumber),
                    CsvQuote(r.DateOfReport?.ToString("yyyy-MM-dd HH:mm")),
                    CsvQuote(r.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm")),
                    CsvQuote(r.CrimeType.ToString()),
                    CsvQuote(r.ReportingArea),
                    CsvQuote(r.Neighborhood),
                    CsvQuote(r.Location),
                    CsvQuote(r.Jurisdiction),
                    CsvQuote(r.DataSource),
                    r.Latitude?.ToString("F7") ?? "",
                    r.Longitude?.ToString("F7") ?? ""
                ));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"CrimeReports_Export_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private static string CsvQuote(string? val)
        {
            if (val == null) return "";
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }
    }
}
