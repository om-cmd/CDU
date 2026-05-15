using Core_Layer.HelperMethod;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Analysis_Web.Controllers
{
    public class CrimeDataController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public CrimeDataController(IWebHostEnvironment env)
        {
            _env = env;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult LoadData(int draw, int start, int length,
            string? searchValue, string? sortColumn, string? sortDir)
        {
            var records = GetParsedRecords();

            // Global search
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

            // Sorting
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

            var paged = records.Skip(start).Take(length).Select(r => new
            {
                r.FileNumber,
                DateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                CrimeDateTime = r.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                CrimeType = r.CrimeType.ToString(),
                ReportingArea = r.ReportingArea ?? "—",
                Neighborhood = r.Neighborhood ?? "—",
                Location = r.Location ?? "—",
                Latitude = r.Latitude?.ToString("F4") ?? "—",
                Longitude = r.Longitude?.ToString("F4") ?? "—",
            });

            return Json(new
            {
                draw,
                recordsTotal = totalRecords,
                recordsFiltered = totalRecords,
                data = paged
            });
        }

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

        // ── CSV parsing ──────────────────────────────────────────────────────────

        private List<CrimeReport> GetParsedRecords()
        {
            var csvPath = Path.Combine(_env.WebRootPath, "CSV", "Crime_Reports_20260508.csv");
            var records = new List<CrimeReport>();

            if (!System.IO.File.Exists(csvPath)) return records;

            var lines = System.IO.File.ReadAllLines(csvPath, Encoding.UTF8);
            if (lines.Length < 2) return records;

            // Map headers
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
                var crimeType = ParseCrimeType(crimeRaw);

                records.Add(new CrimeReport
                {
                    FileNumber = Cell(iFile),
                    DateOfReport = CsvParseHelper.ParseReportDate(Cell(iRpt)),
                    CrimeDateTime = CsvParseHelper.ParseCrimeDateTime(Cell(iCdt)),
                    CrimeDateTimeRaw = Cell(iCdt),
                    CrimeType = crimeType,
                    ReportingArea = Cell(iArea),
                    Neighborhood = Cell(iNbr),
                    Location = Cell(iLoc),
                    Latitude = CsvParseHelper.ParseDecimal(Cell(iLat)),
                    Longitude = CsvParseHelper.ParseDecimal(Cell(iLon)),
                    ReportYear = CsvParseHelper.ParseReportDate(Cell(iRpt))?.Year,
                    ReportMonth = CsvParseHelper.ParseReportDate(Cell(iRpt))?.Month,
                    ReportDayOfWeek = (int?)CsvParseHelper.ParseReportDate(Cell(iRpt))?.DayOfWeek,
                    CrimeHour = CsvParseHelper.ParseCrimeDateTime(Cell(iCdt))?.Hour,
                });
            }

            return records;
        }

        private static CrimeType ParseCrimeType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return CrimeType.Unknown;
            var cleaned = raw.Trim().Replace(" ", "").Replace("-", "").Replace(".", "")
                             .Replace("/", "").Replace("&", "").Replace("(", "")
                             .Replace(")", "").Replace(",", "").ToLower();
            return cleaned switch
            {
                "hitandrun" => CrimeType.HitAndRun,
                "larcenyfrommv" => CrimeType.LarcenyFromMV,
                "shoplifting" => CrimeType.Shoplifting,
                "larcenyofbicycle" => CrimeType.LarcenyOfBicycle,
                "forgery" => CrimeType.Forgery,
                "maldestproperty" => CrimeType.MalDestProperty,
                "warrantarrest" => CrimeType.WarrantArrest,
                "larcenyfromresidence" => CrimeType.LarcenyFromResidence,
                "simpleassault" => CrimeType.SimpleAssault,
                "larcenyfrombuildingm" => CrimeType.LarcenyFromBuilding,
                "larcenyfrombuildinge" => CrimeType.LarcenyFromBuilding,
                "larcenyfrombuilding" => CrimeType.LarcenyFromBuilding,
                "housebreak" => CrimeType.Housebreak,
                "accident" => CrimeType.Accident,
                "adminerror" => CrimeType.AdminError,
                "larcenyfromperson" => CrimeType.LarcenyFromPerson,
                "threats" => CrimeType.Threats,
                "aggravatedassault" => CrimeType.AggravatedAssault,
                "flimflam" => CrimeType.FlimFlam,
                "missingperson" => CrimeType.MissingPerson,
                "autotheft" => CrimeType.AutoTheft,
                "harassment" => CrimeType.Harassment,
                "streetrobbery" => CrimeType.StreetRobbery,
                "drugs" => CrimeType.Drugs,
                "suspiciouspackage" => CrimeType.SuspiciousPackage,
                "commercialbreak" => CrimeType.CommercialBreak,
                "trespassing" => CrimeType.Trespassing,
                "larcenymisc" => CrimeType.LarcenyMisc,
                "oui" => CrimeType.OUI,
                "phonecalls" => CrimeType.PhoneCalls,
                "disorderly" => CrimeType.Disorderly,
                "larcenyofplate" => CrimeType.LarcenyOfPlate,
                "indecentexposure" => CrimeType.IndecentExposure,
                "commercialrobbery" => CrimeType.CommercialRobbery,
                "taxiviolation" => CrimeType.TaxiViolation,
                "drinkinginpublic" => CrimeType.DrinkingInPublic,
                "recstolenpropertym" => CrimeType.RecStolenProperty,
                "recstolenpropertye" => CrimeType.RecStolenProperty,
                "recstolenpropert" => CrimeType.RecStolenProperty,
                "recstoledproperty" => CrimeType.RecStolenProperty,
                "violationofho" => CrimeType.ViolationOfHO,
                "larcenyofservices" => CrimeType.LarcenyOfServices,
                "counterfeiting" => CrimeType.Counterfeiting,
                "extortionblackmail" => CrimeType.ExtortionBlackmail,
                "weaponviolations" => CrimeType.WeaponViolations,
                "noisecomplaint" => CrimeType.NoiseComplaint,
                "medical" => CrimeType.Medical,
                "annoyingaccosting" => CrimeType.AnnoyingAccosting,
                "embezzlement" => CrimeType.Embezzlement,
                "arson" => CrimeType.Arson,
                "civildispute" => CrimeType.CivilDispute,
                "sexoffenderviolation" => CrimeType.SexOffenderViolation,
                "peepingspying" => CrimeType.PeepingSpying,
                "liquorpossessionsale" => CrimeType.LiquorPossessionSale,
                "prostitution" => CrimeType.Prostitution,
                "elderassistance19a" => CrimeType.ElderAssistance,
                "stalking" => CrimeType.Stalking,
                "encampment" => CrimeType.Encampment,
                "kidnapping" => CrimeType.Kidnapping,
                "homicide" => CrimeType.Homicide,
                "violationofro" => CrimeType.ViolationOfRO,
                "domesticdispute" => CrimeType.DomesticDispute,
                "gambling" => CrimeType.Gambling,
                "hoarding" => CrimeType.Hoarding,
                _ => CrimeType.Unknown
            };
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