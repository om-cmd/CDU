using Core_Layer.HelperMethod;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Analysis_Web.Services;            // ← add this using
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Analysis_Web.Controllers
{
    public class CrimeDataController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly ICrimeReportInterface _service;   // ← inject service

        public CrimeDataController(IWebHostEnvironment env, ICrimeReportInterface service)
        {
            _env = env;
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
            var records = GetParsedRecords();

          
            var dbLookup = await _service.GetFileNumberToIdMapAsync();

            // 3. Global search
            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                var sv = searchValue.ToLower();
                records = records.Where(r =>
                    (r.FileNumber?.ToLower().Contains(sv) ?? false) ||
                    (r.Neighborhood?.ToLower().Contains(sv) ?? false) ||
                    (r.Location?.ToLower().Contains(sv) ?? false) ||
                    (r.ReportingArea?.ToLower().Contains(sv) ?? false) ||
                    r.CrimeType.ToString().ToLower().Contains(sv)
                ).ToList();
            }

            int totalRecords = records.Count;

            // 4. Sorting
            records = (sortColumn, sortDir?.ToLower()) switch
            {
                ("FileNumber", "asc") => records.OrderBy(r => r.FileNumber).ToList(),
                ("FileNumber", _) => records.OrderByDescending(r => r.FileNumber).ToList(),
                ("DateOfReport", "asc") => records.OrderBy(r => r.DateOfReport).ToList(),
                ("DateOfReport", _) => records.OrderByDescending(r => r.DateOfReport).ToList(),
                ("CrimeType", "asc") => records.OrderBy(r => r.CrimeType.ToString()).ToList(),
                ("CrimeType", _) => records.OrderByDescending(r => r.CrimeType.ToString()).ToList(),
                ("Neighborhood", "asc") => records.OrderBy(r => r.Neighborhood).ToList(),
                ("Neighborhood", _) => records.OrderByDescending(r => r.Neighborhood).ToList(),
                _ => records.OrderByDescending(r => r.DateOfReport).ToList()
            };

            // 5. Page + project — include DB id (0 if not saved to DB yet)
            var paged = records.Skip(start).Take(length).Select(r =>
            {
                dbLookup.TryGetValue(r.FileNumber ?? "", out int dbId);
                return new
                {
                    crimeReportId = dbId,          // 0 = CSV only, >0 = in DB
                    fileNumber = r.FileNumber,
                    dateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                    crimeDateTime = r.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                    crimeType = r.CrimeType.ToString(),
                    reportingArea = r.ReportingArea ?? "—",
                    neighborhood = r.Neighborhood ?? "—",
                    location = r.Location ?? "—",
                    latitude = r.Latitude?.ToString("F4") ?? "—",
                    longitude = r.Longitude?.ToString("F4") ?? "—",
                };
            });

            return Json(new
            {
                draw,
                recordsTotal = totalRecords,
                recordsFiltered = totalRecords,
                data = paged
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET /CrimeData/ExportCsv
        // ─────────────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult ExportCsv(string? searchValue)
        {
            var records = GetParsedRecords();

            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                var sv = searchValue.ToLower();
                records = records.Where(r =>
                    (r.FileNumber?.ToLower().Contains(sv) ?? false) ||
                    (r.Neighborhood?.ToLower().Contains(sv) ?? false) ||
                    (r.Location?.ToLower().Contains(sv) ?? false) ||
                    (r.ReportingArea?.ToLower().Contains(sv) ?? false) ||
                    r.CrimeType.ToString().ToLower().Contains(sv)
                ).ToList();
            }

            var sb = new StringBuilder();
            sb.AppendLine("File Number,Date of Report,Crime Date Time,Crime Type,Reporting Area,Neighborhood,Location,Latitude,Longitude");

            foreach (var r in records)
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

        // ─────────────────────────────────────────────────────────────────────
        // CSV parsing — same logic as before, kept in one place
        // ─────────────────────────────────────────────────────────────────────
        private List<CrimeReport> GetParsedRecords()
        {
            var csvPath = Path.Combine(_env.WebRootPath, "CSV", "Crime_Reports_20260508.csv");
            var records = new List<CrimeReport>();

            if (!System.IO.File.Exists(csvPath)) return records;

            var lines = System.IO.File.ReadAllLines(csvPath, Encoding.UTF8);
            if (lines.Length < 2) return records;

            var headers = CsvParseHelper.SplitLine(lines[0]);
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                idx[headers[i].Trim()] = i;

            int Get(string name) => idx.TryGetValue(name, out var v) ? v : -1;

            int iFile = Get("File Number");
            int iRpt = Get("Date of Report");
            int iCdt = Get("Crime Date Time");
            int iCrm = Get("Crime");
            int iArea = Get("Reporting Area");
            int iNbr = Get("Neighborhood");
            int iLoc = Get("Location");
            int iLat = Get("Reporting Area Lat");
            int iLon = Get("Reporting Area Lon");

            for (int ln = 1; ln < lines.Length; ln++)
            {
                var line = lines[ln];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = CsvParseHelper.SplitLine(line);
                string Cell(int i) => i >= 0 && i < cols.Length ? cols[i].Trim() : "";

                var crimeRaw = Cell(iCrm);
                var dateRpt = CsvParseHelper.ParseReportDate(Cell(iRpt));

                records.Add(new CrimeReport
                {
                    FileNumber = Cell(iFile),
                    DateOfReport = dateRpt,
                    CrimeDateTime = CsvParseHelper.ParseCrimeDateTime(Cell(iCdt)),
                    CrimeDateTimeRaw = Cell(iCdt),
                    CrimeType = CrimeReportController.ParseCrimeType(crimeRaw),
                    ReportingArea = Cell(iArea),
                    Neighborhood = Cell(iNbr),
                    Location = Cell(iLoc),
                    Latitude = CsvParseHelper.ParseDecimal(Cell(iLat)),
                    Longitude = CsvParseHelper.ParseDecimal(Cell(iLon)),
                    ReportYear = dateRpt?.Year,
                    ReportMonth = dateRpt?.Month,
                    ReportDayOfWeek = (int?)dateRpt?.DayOfWeek,
                    CrimeHour = CsvParseHelper.ParseCrimeDateTime(Cell(iCdt))?.Hour,
                });
            }

            return records;
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