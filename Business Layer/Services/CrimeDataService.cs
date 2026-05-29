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
        /// <summary>
        /// lisitng itso that can separate category 
        /// </summary>
        public static readonly IReadOnlyDictionary<string, int> CssWeights =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Homicide",            3650 },
            { "Kidnapping",          1825 },
            { "AggravatedAssault",    730 },
            { "StreetRobbery",        547 },
            { "CommercialRobbery",    547 },
            { "Arson",                547 },
            { "WeaponViolations",     365 },
            { "SexOffenderViolation", 365 },
            { "Drugs",                270 },
            { "Housebreak",           180 },
            { "CommercialBreak",      180 },
            { "ExtortionBlackmail",   180 },
            { "AutoTheft",            150 },
            { "SimpleAssault",        120 },
            { "DomesticDispute",      120 },
            { "Stalking",              90 },
            { "Threats",               90 },
            { "HitAndRun",             90 },
            { "OUI",                   90 },
            { "ViolationOfHO",         90 },
            { "ViolationOfRO",         90 },
            { "Embezzlement",          90 },
            { "LarcenyFromMV",         60 },
            { "LarcenyFromResidence",  60 },
            { "LarcenyFromBuilding",   60 },
            { "LarcenyFromPerson",     60 },
            { "Harassment",            60 },
            { "WarrantArrest",         60 },
            { "LarcenyOfBicycle",      45 },
            { "LarcenyMisc",           45 },
            { "MalDestProperty",       45 },
            { "FlimFlam",              45 },
            { "Shoplifting",           30 },
            { "Forgery",               30 },
            { "Counterfeiting",        30 },
            { "RecStolenProperty",     30 },
            { "IndecentExposure",      30 },
            { "PeepingSpying",         30 },
            { "Prostitution",          30 },
            { "MissingPerson",         30 },
            { "Trespassing",           30 },
            { "Disorderly",            20 },
            { "Accident",              20 },
            { "LiquorPossessionSale",  20 },
            { "DrinkingInPublic",      15 },
            { "AnnoyingAccosting",     15 },
            { "PhoneCalls",            15 },
            { "Gambling",              15 },
            { "NoiseComplaint",        10 },
            { "SuspiciousPackage",     10 },
            { "CivilDispute",          10 },
            { "TaxiViolation",         10 },
            { "ElderAssistance",       10 },
            { "Encampment",            10 },
            { "LarcenyOfPlate",        10 },
            { "LarcenyOfServices",     10 },
            { "AdminError",             5 },
            { "Medical",                5 },
            { "Hoarding",               5 },
            { "Unknown",                5 },
        };

        public static int GetCssWeight(string crimeType) =>
            CssWeights.TryGetValue(crimeType, out var w) ? w : 5;

        public static string GetSeverityLevel(int cssWeight) => cssWeight switch
        {
            >= 300 => "High",
            >= 60 => "Medium",
            _ => "Low"
        };

        // ── Public API ───────────────────────────────────────────────────────

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

            // ── Frequency dictionaries ───────────────────────────────────────
            var crimeTypeCounts = records
                .GroupBy(r => r.CrimeType.ToString())
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            var neighborhoodCounts = records
                .Where(r => !string.IsNullOrWhiteSpace(r.Neighborhood))
                .GroupBy(r => r.Neighborhood!)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());

            var yearlyCounts = records
                .Where(r => r.ReportYear.HasValue)
                .GroupBy(r => r.ReportYear!.Value)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count());

            var hourlyCounts = records
                .Where(r => r.CrimeHour.HasValue)
                .GroupBy(r => r.CrimeHour!.Value)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Count());

            // ── Harm calculations ────────────────────────────────────────────
            // Attach CSS weight to every record once
            var weighted = records
                .Select(r => new
                {
                    Record = r,
                    Weight = GetCssWeight(r.CrimeType.ToString())
                })
                .ToList();

            long totalHarm = weighted.Sum(x => (long)x.Weight);
            long highHarm = weighted.Where(x => x.Weight >= 300).Sum(x => (long)x.Weight);
            long medHarm = weighted.Where(x => x.Weight >= 60 && x.Weight < 300).Sum(x => (long)x.Weight);
            long lowHarm = totalHarm - highHarm - medHarm;

            // Harm by crime type (top 5)
            var harmByType = weighted
                .GroupBy(x => x.Record.CrimeType.ToString())
                .Select(g => new HarmByTypeModel
                {
                    CrimeType = g.Key,
                    Count = g.Count(),
                    TotalHarm = g.Sum(x => (long)x.Weight),
                    CssWeight = GetCssWeight(g.Key)
                })
                .OrderByDescending(h => h.TotalHarm)
                .Take(5)
                .ToList();

            // Harm by neighbourhood (top 5)
            var harmByNeighborhood = weighted
                .Where(x => !string.IsNullOrWhiteSpace(x.Record.Neighborhood))
                .GroupBy(x => x.Record.Neighborhood!)
                .Select(g => new HarmByNeighborhoodModel
                {
                    Name = g.Key,
                    Count = g.Count(),
                    TotalHarm = g.Sum(x => (long)x.Weight)
                })
                .OrderByDescending(h => h.TotalHarm)
                .Take(5)
                .ToList();

            // Yearly harm
            var yearlyHarm = weighted
                .Where(x => x.Record.ReportYear.HasValue)
                .GroupBy(x => x.Record.ReportYear!.Value)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.Sum(x => (long)x.Weight));

            // Hourly harm → 4 time-block percentages
            long totalHourlyHarm = weighted
                .Where(x => x.Record.CrimeHour.HasValue)
                .Sum(x => (long)x.Weight);

            double BlockPct(int from, int to) => totalHourlyHarm == 0 ? 0 :
                weighted
                    .Where(x => x.Record.CrimeHour.HasValue
                             && x.Record.CrimeHour >= from
                             && x.Record.CrimeHour < to)
                    .Sum(x => (long)x.Weight)
                    / (double)totalHourlyHarm * 100;

            var hourlyHarmPct = new double[]
            {
                Math.Round(BlockPct(0,  6),  1),
                Math.Round(BlockPct(6,  12), 1),
                Math.Round(BlockPct(12, 18), 1),
                Math.Round(BlockPct(18, 24), 1)
            };

            // ── Map points (up to 5,000 most recent, with CSS weight) ─────────
            var mapPoints = weighted
                .Where(x => x.Record.Latitude.HasValue && x.Record.Longitude.HasValue)
                .OrderByDescending(x => x.Record.DateOfReport)
                .Take(5000)
                .Select(x => new MapPointModel
                {
                    Lat = (double)x.Record.Latitude!.Value,
                    Lng = (double)x.Record.Longitude!.Value,
                    CrimeType = x.Record.CrimeType.ToString(),
                    FileNumber = x.Record.FileNumber,
                    Neighborhood = x.Record.Neighborhood ?? "",
                    DateOfReport = x.Record.DateOfReport?.ToString("yyyy-MM-dd") ?? "",
                    CssWeight = x.Weight
                })
                .ToList();

            // ── Recent incidents (top 10, with severity label) ────────────────
            var recentIncidents = weighted
                .Where(x => x.Record.DateOfReport.HasValue)
                .OrderByDescending(x => x.Record.DateOfReport)
                .Take(10)
                .Select(x => new RecentIncidentModel
                {
                    FileNumber = x.Record.FileNumber,
                    CrimeType = x.Record.CrimeType.ToString(),
                    Neighborhood = x.Record.Neighborhood ?? "—",
                    Location = x.Record.Location ?? "—",
                    DateOfReport = x.Record.DateOfReport?.ToString("dd MMM yyyy") ?? "—",
                    CrimeDateTime = x.Record.CrimeDateTime?.ToString("dd MMM yyyy HH:mm") ?? "—",
                    CssWeight = x.Weight,
                    SeverityLevel = GetSeverityLevel(x.Weight)
                })
                .ToList();

            return new DashboardStatsModel
            {
                TotalRecords = records.Count,
                TotalWithCoords = records.Count(r => r.Latitude.HasValue && r.Longitude.HasValue),
                CrimeTypeCounts = crimeTypeCounts,
                NeighborhoodCounts = neighborhoodCounts,
                YearlyCounts = yearlyCounts,
                HourlyCounts = hourlyCounts,
                TotalHarm = totalHarm,
                HighHarm = highHarm,
                MediumHarm = medHarm,
                LowHarm = lowHarm,
                HarmByType = harmByType,
                HarmByNeighborhood = harmByNeighborhood,
                YearlyHarm = yearlyHarm,
                HourlyHarmPct = hourlyHarmPct,
                MapPoints = mapPoints,
                RecentIncidents = recentIncidents
            };
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

        // ── CSV Parsing ───────────────────────────────────────────────────────

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