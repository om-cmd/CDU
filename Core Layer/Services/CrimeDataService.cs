using Core_Layer.HelperMethod;
using Core_Layer.Models;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace Core_Layer.Services
{
    public class CrimeDataService
    {
        private readonly IMemoryCache _cache;
        private readonly string _csvPath;
        private const string CacheKey = "CrimeRecords_All";

        public CrimeDataService(IMemoryCache cache, string csvPath)
        {
            _cache = cache;
            _csvPath = csvPath;
        }

        public List<CrimeReport> GetAllRecords()
        {
            if (_cache.TryGetValue(CacheKey, out List<CrimeReport>? cached) && cached != null)
                return cached;

            var records = ParseCsv();
            _cache.Set(CacheKey, records, TimeSpan.FromHours(12));
            return records;
        }

        public DashboardStatsModel GetDashboardStats()
        {
            var records = GetAllRecords();

            var stats = new DashboardStatsModel
            {
                TotalRecords = records.Count,
                TotalWithCoords = records.Count(r => r.Latitude.HasValue && r.Longitude.HasValue),

                CrimeTypeCounts = records
                    .GroupBy(r => r.CrimeType.ToString())
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToDictionary(g => g.Key, g => g.Count()),

                NeighborhoodCounts = records
                    .Where(r => !string.IsNullOrWhiteSpace(r.Neighborhood))
                    .GroupBy(r => r.Neighborhood!)
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count()),

                YearlyCounts = records
                    .Where(r => r.ReportYear.HasValue)
                    .GroupBy(r => r.ReportYear!.Value)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Count()),

                HourlyCounts = records
                    .Where(r => r.CrimeHour.HasValue)
                    .GroupBy(r => r.CrimeHour!.Value)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Count()),

                // Send max 5000 points to map (sampled for performance)
                MapPoints = records
                    .Where(r => r.Latitude.HasValue && r.Longitude.HasValue)
                    .OrderByDescending(r => r.DateOfReport)
                    .Take(5000)
                    .Select(r => new MapPointModel
                    {
                        Lat = (double)r.Latitude!.Value,
                        Lng = (double)r.Longitude!.Value,
                        CrimeType = r.CrimeType.ToString(),
                        FileNumber = r.FileNumber,
                        Neighborhood = r.Neighborhood ?? "",
                        DateOfReport = r.DateOfReport?.ToString("yyyy-MM-dd") ?? ""
                    }).ToList(),

                RecentIncidents = records
                    .Where(r => r.DateOfReport.HasValue)
                    .OrderByDescending(r => r.DateOfReport)
                    .Take(10)
                    .Select(r => new RecentIncidentModel
                    {
                        FileNumber = r.FileNumber,
                        CrimeType = r.CrimeType.ToString(),
                        Neighborhood = r.Neighborhood ?? "—",
                        Location = r.Location ?? "—",
                        DateOfReport = r.DateOfReport?.ToString("dd MMM yyyy") ?? "—",
                        CrimeDateTime = r.CrimeDateTime?.ToString("dd MMM yyyy HH:mm") ?? "—"
                    }).ToList()
            };

            return stats;
        }

        public List<CrimeReport> FilterRecords(
            string? fileNumber, string? crimeType, string? neighborhood,
            DateTime? dateFrom, DateTime? dateTo,
            DateTime? crimeDateFrom, DateTime? crimeDateTo)
        {
            var records = GetAllRecords().AsEnumerable();

            if (!string.IsNullOrWhiteSpace(fileNumber))
                records = records.Where(r => r.FileNumber.Contains(fileNumber, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(crimeType) && crimeType != "All")
                records = records.Where(r => r.CrimeType.ToString() == crimeType);

            if (!string.IsNullOrWhiteSpace(neighborhood) && neighborhood != "All")
                records = records.Where(r => r.Neighborhood == neighborhood);

            if (dateFrom.HasValue)
                records = records.Where(r => r.DateOfReport >= dateFrom);

            if (dateTo.HasValue)
                records = records.Where(r => r.DateOfReport <= dateTo);

            if (crimeDateFrom.HasValue)
                records = records.Where(r => r.CrimeDateTime >= crimeDateFrom);

            if (crimeDateTo.HasValue)
                records = records.Where(r => r.CrimeDateTime <= crimeDateTo);

            return records.ToList();
        }

        // ── Parsing ──────────────────────────────────────────────────────────────

        private List<CrimeReport> ParseCsv()
        {
            var records = new List<CrimeReport>();
            if (!File.Exists(_csvPath)) return records;

            var lines = File.ReadAllLines(_csvPath, Encoding.UTF8);
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

                var reportDate = CsvParseHelper.ParseReportDate(Cell(iRpt));
                var crimeDate = CsvParseHelper.ParseCrimeDateTime(Cell(iCdt));

                records.Add(new CrimeReport
                {
                    FileNumber = Cell(iFile),
                    DateOfReport = reportDate,
                    CrimeDateTime = crimeDate,
                    CrimeDateTimeRaw = Cell(iCdt),
                    CrimeType = ParseCrimeType(Cell(iCrm)),
                    ReportingArea = Cell(iArea),
                    Neighborhood = Cell(iNbr),
                    Location = Cell(iLoc),
                    Latitude = CsvParseHelper.ParseDecimal(Cell(iLat)),
                    Longitude = CsvParseHelper.ParseDecimal(Cell(iLon)),
                    ReportYear = reportDate?.Year,
                    ReportMonth = reportDate?.Month,
                    ReportDayOfWeek = (int?)reportDate?.DayOfWeek,
                    CrimeHour = crimeDate?.Hour,
                });
            }

            return records;
        }

        public static CrimeType ParseCrimeType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return CrimeType.Unknown;
            var c = raw.Trim().Replace(" ", "").Replace("-", "").Replace(".", "")
                       .Replace("/", "").Replace("&", "").Replace("(", "")
                       .Replace(")", "").Replace(",", "").ToLower();
            return c switch
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
                "recstolenpropert" => CrimeType.RecStolenProperty,
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
    }
}