using Analysis_Web.Services;
using Analysis_Web.ViewModels;
using Core_Layer.HelperMethod;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Analysis_Web.Controllers
{
    public class CrimeReportController : Controller
    {
        private readonly ICrimeReportInterface _service;
        private readonly ILogger<CrimeReportController> _logger;
        private const int DefaultPageSize = 20;

        public CrimeReportController(ICrimeReportInterface service, ILogger<CrimeReportController> logger)
        {
            _service = service;
            _logger = logger;
        }

        public async Task<IActionResult> CrimeReport(
            string? search, CrimeType? crimeType, int? year,
            string? neighborhood, string? sortBy, bool ascending = false,
            int page = 1, int pageSize = DefaultPageSize)
        {
            pageSize = Math.Clamp(pageSize, 5, 100);
            page = Math.Max(1, page);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var (reports, total) = await _service.GetPagedAsync(
                    page, pageSize, search, crimeType, year, neighborhood, sortBy, ascending);

                return Ok(new
                {
                    reports = reports.Select(MapToVm),
                    totalCount = total,
                    page,
                    pageSize
                });
            }

            var vm = new CrimeReportIndexViewModel
            {
                TotalCount = 0,
                Page = 1,
                PageSize = pageSize,
                Search = search,
                SortBy = sortBy,
                Ascending = ascending,
            };
            return View("~/Views/CrimeData/CrimeReport.cshtml", vm);
        }

    
        public IActionResult Create()
            => View("~/Views/CrimeData/CrimeReport.cshtml");

    
        public async Task<IActionResult> Details(int id)
        {
            var report = await _service.GetByIdAsync(id);
            if (report == null)
                return NotFound(new { message = $"Crime report #{id} not found." });

            return Ok(MapToVm(report));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CrimeReportViewModel vm)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { errors = ModelStateErrors() });

            if (string.IsNullOrWhiteSpace(vm.FileNumber))
                return BadRequest(new { errors = new { FileNumber = new[] { "File number is required." } } });

            if (await _service.FileNumberExistsAsync(vm.FileNumber))
                return BadRequest(new { errors = new { FileNumber = new[] { "This file number already exists." } } });

            try
            {
                var created = await _service.CreateAsync(MapFromVm(vm));
                return Ok(MapToVm(created));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating crime report");
                return StatusCode(500, new { message = "An error occurred while saving. Please try again." });
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [FromBody] CrimeReportViewModel vm)
        {
            if (id != vm.CrimeReportId)
                return BadRequest(new { message = "ID mismatch." });

            if (!ModelState.IsValid)
                return BadRequest(new { errors = ModelStateErrors() });

            if (string.IsNullOrWhiteSpace(vm.FileNumber))
                return BadRequest(new { errors = new { FileNumber = new[] { "File number is required." } } });

            // Duplicate check: same file number on a DIFFERENT record
            if (await _service.FileNumberExistsAsync(vm.FileNumber, excludeId: id))
                return BadRequest(new { errors = new { FileNumber = new[] { "This file number already exists on another record." } } });

            try
            {
                var updated = await _service.UpdateAsync(id, MapFromVm(vm));
                if (updated == null)
                    return NotFound(new { message = "Report not found — it may have been deleted." });

                return Ok(MapToVm(updated));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating crime report {Id}", id);
                return StatusCode(500, new { message = "An error occurred while saving. Please try again." });
            }
        }

      
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var deleted = await _service.DeleteAsync(id);
            if (!deleted)
                return NotFound(new { message = $"Crime report #{id} was not found." });

            return Ok(new { message = "Deleted successfully." });
        }

        public async Task<IActionResult> Dashboard()
        {
            var stats = await _service.GetStatsAsync();
            return Ok(stats);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromBody] int[] ids)
        {
            if (ids == null || ids.Length == 0)
                return BadRequest(new { message = "No IDs provided." });

            int deleted = 0;
            foreach (var id in ids)
                if (await _service.DeleteAsync(id)) deleted++;

            return Ok(new { deleted, total = ids.Length });
        }

   
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportCsv(IFormFile file, bool skipDuplicates = true)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only .csv files are accepted." });

            if (file.Length > 50 * 1024 * 1024)
                return BadRequest(new { message = "File exceeds the 50 MB limit." });

            try
            {
                List<string> lines;
                using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
                {
                    var content = await reader.ReadToEndAsync();
                    lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                if (lines.Count < 2)
                    return BadRequest(new { message = "CSV has no data rows." });

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


                var existingFileNumbers = skipDuplicates
                    ? await _service.GetAllFileNumbersAsync()
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var toInsert = new List<CrimeReport>();
                int skipped = 0, errors = 0;

                for (int ln = 1; ln < lines.Count; ln++)
                {
                    var line = lines[ln].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var cols = CsvParseHelper.SplitLine(line);
                        string Cell(int i) => i >= 0 && i < cols.Length ? cols[i].Trim() : "";

                        var fileNumber = Cell(iFile);
                        if (string.IsNullOrWhiteSpace(fileNumber)) { errors++; continue; }

                        if (skipDuplicates && existingFileNumbers.Contains(fileNumber))
                        {
                            skipped++;
                            continue;
                        }

                        var crimeRaw = Cell(iCrm);
                        var dateRpt = CsvParseHelper.ParseReportDate(Cell(iRpt));

                        toInsert.Add(new CrimeReport
                        {
                            FileNumber = fileNumber,
                            DateOfReport = dateRpt,
                            CrimeDateTime = CsvParseHelper.ParseCrimeDateTime(Cell(iCdt)),
                            CrimeDateTimeRaw = Cell(iCdt),
                            CrimeType = ParseCrimeType(crimeRaw),
                            ReportingArea = Cell(iArea),
                            Neighborhood = Cell(iNbr),
                            Location = Cell(iLoc),
                            Latitude = CsvParseHelper.ParseDecimal(Cell(iLat)),
                            Longitude = CsvParseHelper.ParseDecimal(Cell(iLon)),
                            ReportYear = dateRpt?.Year,
                            ReportMonth = dateRpt?.Month,
                            ReportDayOfWeek = (int?)dateRpt?.DayOfWeek,
                            CrimeHour = CsvParseHelper.ParseCrimeDateTime(Cell(iCdt))?.Hour,
                            ImportedAt = DateTime.UtcNow,
                        });

                        if (skipDuplicates) existingFileNumbers.Add(fileNumber);
                    }
                    catch
                    {
                        errors++;
                    }
                }

                int inserted = 0;
                const int batchSize = 500;
                for (int i = 0; i < toInsert.Count; i += batchSize)
                {
                    var batch = toInsert.Skip(i).Take(batchSize).ToList();
                    inserted += await _service.BulkInsertAsync(batch);
                }

                return Ok(new { inserted, skipped, errors, total = lines.Count - 1 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSV import failed");
                return StatusCode(500, new { message = "Import failed: " + ex.Message });
            }
        }


        private Dictionary<string, string[]> ModelStateErrors()
            => ModelState
                .Where(kv => kv.Value?.Errors.Count > 0)
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

        private static CrimeReportViewModel MapToVm(CrimeReport r) => new()
        {
            CrimeReportId = r.CrimeReportId,
            FileNumber = r.FileNumber,
            DateOfReport = r.DateOfReport,
            CrimeDateTime = r.CrimeDateTime,
            CrimeDateTimeRaw = r.CrimeDateTimeRaw,
            CrimeType = r.CrimeType,
            ReportingArea = r.ReportingArea,
            Neighborhood = r.Neighborhood,
            Location = r.Location,
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            ImportedAt = r.ImportedAt,
            RowStamp = r.RowStamp,
        };

        private static CrimeReport MapFromVm(CrimeReportViewModel vm) => new()
        {
            CrimeReportId = vm.CrimeReportId,
            FileNumber = vm.FileNumber,
            DateOfReport = vm.DateOfReport,
            CrimeDateTime = vm.CrimeDateTime,
            CrimeDateTimeRaw = vm.CrimeDateTimeRaw,
            CrimeType = vm.CrimeType,
            ReportingArea = vm.ReportingArea,
            Neighborhood = vm.Neighborhood,
            Location = vm.Location,
            Latitude = vm.Latitude,
            Longitude = vm.Longitude,
        };

        internal static CrimeType ParseCrimeType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return CrimeType.Unknown;
            var cleaned = raw.Trim()
                .Replace(" ", "").Replace("-", "").Replace(".", "")
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
    }
}