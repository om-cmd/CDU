using Analysis_Web.Services;
using Analysis_Web.ViewModels;
using Core_Layer.HelperMethod;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;

namespace Analysis_Web.Controllers
{
    public class CrimeReportController : Controller
    {
        private readonly ICrimeReportInterface _service;
        private readonly ICommunicationService _communicationService;
        private readonly ILogger<CrimeReportController> _logger;
        private const int DefaultPageSize = 20;

        public CrimeReportController(
            ICrimeReportInterface service,
            ICommunicationService communicationService,
            ILogger<CrimeReportController> logger)
        {
            _service = service;
            _communicationService = communicationService;
            _logger = logger;
        }

        public IActionResult CrimeReport()
            => View("~/Views/CrimeData/CrimeReport.cshtml");

        [HttpGet]
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
                await NotifyCrimeReportChangedAsync(
                    "Crime report created",
                    $"{CurrentDisplayName()} created report {created.FileNumber} ({created.CrimeType}) in {created.Neighborhood ?? "Unknown area"}.",
                    created.CrimeReportId);
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

            if (await _service.FileNumberExistsAsync(vm.FileNumber, excludeId: id))
                return BadRequest(new { errors = new { FileNumber = new[] { "This file number already exists on another record." } } });

            try
            {
                var before = await _service.GetByIdAsync(id);
                var updated = await _service.UpdateAsync(id, MapFromVm(vm));
                if (updated == null)
                    return NotFound(new { message = "Report not found — it may have been deleted." });

                var changes = DescribeChanges(before, updated);
                await NotifyCrimeReportChangedAsync(
                    "Crime report updated",
                    $"{CurrentDisplayName()} updated report {updated.FileNumber}. Changes: {changes}.",
                    updated.CrimeReportId);
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
            var existing = await _service.GetByIdAsync(id);
            var deleted = await _service.DeleteAsync(id);
            if (!deleted)
                return NotFound(new { message = $"Crime report #{id} was not found." });

            await NotifyCrimeReportChangedAsync(
                "Crime report deleted",
                $"{CurrentDisplayName()} deleted report {existing?.FileNumber ?? id.ToString()} ({existing?.CrimeType.ToString() ?? "Unknown"}).",
                id);
            return Ok(new { message = "Deleted successfully." });
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

                if (inserted > 0 || skipped > 0 || errors > 0)
                {
                    var message = $"{CurrentDisplayName()} imported CSV data. Inserted: {inserted}, skipped: {skipped}, errors: {errors}, total rows: {lines.Count - 1}.";
                    _logger.LogInformation(
                        "ChangeType=BulkImport Entity=CrimeReport Actor={ActorEmail} Inserted={Inserted} Skipped={Skipped} Errors={Errors} Total={Total}",
                        CurrentEmail(),
                        inserted,
                        skipped,
                        errors,
                        lines.Count - 1);

                    await _communicationService.CreateAuditNotificationAsync("Crime reports CSV imported", message, CurrentUserId(), CurrentEmail());
                }

                return Ok(new
                {
                    inserted,
                    skipped,
                    errors,
                    total = lines.Count - 1
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSV import failed");
                return StatusCode(500, new { message = "Import failed: " + ex.Message });
            }
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

        private async Task NotifyCrimeReportChangedAsync(string title, string message, int reportId)
        {
            _logger.LogInformation(
                "ChangeType=EntityChange Entity=CrimeReport EntityId={EntityId} Actor={ActorEmail} Title={Title} Message={Message}",
                reportId,
                CurrentEmail(),
                title,
                message);

            await _communicationService.CreateAuditNotificationAsync(title, message, CurrentUserId(), CurrentEmail());
        }

        private int CurrentUserId()
            => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        private string CurrentEmail()
            => User.FindFirstValue(ClaimTypes.Actor) ?? "system";

        private string CurrentDisplayName()
            => User.FindFirstValue(ClaimTypes.Name) ?? CurrentEmail();

        private static string DescribeChanges(CrimeReport? before, CrimeReport after)
        {
            if (before == null) return "record values updated";

            var changes = new List<string>();
            AddChange(changes, "File #", before.FileNumber, after.FileNumber);
            AddChange(changes, "Crime Type", before.CrimeType.ToString(), after.CrimeType.ToString());
            AddChange(changes, "Neighborhood", before.Neighborhood, after.Neighborhood);
            AddChange(changes, "Location", before.Location, after.Location);
            AddChange(changes, "Report Date", before.DateOfReport?.ToString("yyyy-MM-dd HH:mm"), after.DateOfReport?.ToString("yyyy-MM-dd HH:mm"));
            AddChange(changes, "Crime Date", before.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm"), after.CrimeDateTime?.ToString("yyyy-MM-dd HH:mm"));

            return changes.Count == 0 ? "no visible field changes" : string.Join("; ", changes.Take(6));
        }

        private static void AddChange(List<string> changes, string label, string? before, string? after)
        {
            before ??= "";
            after ??= "";
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add($"{label}: '{before}' to '{after}'");
        }

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
