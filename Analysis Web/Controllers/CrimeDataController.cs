using Domain_Layer.DbModels.Enum;
using Analysis_Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Analysis_Web.Controllers
{
    public class CrimeDataController : Controller
    {
        private readonly ICrimeReportInterface _service;

        public CrimeDataController(ICrimeReportInterface service)
        {
            _service = service;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> LoadData(int draw, int start, int length,
            string? searchValue, string? sortColumn, string? sortDir)
        {
            length = length > 0 ? length : 25;

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
                latitude = r.Latitude?.ToString("F4") ?? "—",
                longitude = r.Longitude?.ToString("F4") ?? "—",
            });

            return Json(new
            {
                draw,
                recordsTotal = totalUnfiltered,
                recordsFiltered = filteredCount,
                data = paged
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
            sb.AppendLine("File Number,Date of Report,Crime Date Time,Crime Type,Reporting Area,Neighborhood,Location,Latitude,Longitude");

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