using Analysis_Web.Services;
using Analysis_Web.ViewModels;
using Core_Layer.HelperMethod;
using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;

namespace Analysis_Web.Controllers
{
    public class CrimeReportController : Controller
    {
        private readonly ICrimeReportInterface _service;
        private readonly ICommunicationService _communicationService;
        private readonly ILogger<CrimeReportController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly AnalysisDbContext _db;
        private const int DefaultPageSize = 20;

        public CrimeReportController(
            ICrimeReportInterface service,
            ICommunicationService communicationService,
            ILogger<CrimeReportController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            AnalysisDbContext db)
        {
            _service = service;
            _communicationService = communicationService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _db = db;
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

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportCsv(
            IFormFile file,
            bool skipDuplicates = true,
            string? defaultJurisdiction = null,
            string? defaultDataSource = null,
            CancellationToken cancellationToken = default)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only .csv files are accepted." });

            if (file.Length > 50 * 1024 * 1024)
                return BadRequest(new { message = "File exceeds the 50 MB limit." });

            var inserted = 0;
            var skipped = 0;
            var errors = 0;
            var total = 0;

            try
            {
                List<string> lines;
                using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
                {
                    var content = await reader.ReadToEndAsync(cancellationToken);
                    lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                if (lines.Count < 2)
                    return BadRequest(new { message = "CSV has no data rows." });

                total = lines.Count - 1;
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
                int iJurisdiction = Get("Jurisdiction");
                int iDataSource = Get("Data Source");

                var existingFileNumbers = skipDuplicates
                    ? await _service.GetAllFileNumbersAsync()
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var toInsert = new List<CrimeReport>();

                for (int ln = 1; ln < lines.Count; ln++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                            Jurisdiction = Truncate(
                                FirstNonBlank(Cell(iJurisdiction), defaultJurisdiction, "Unspecified"),
                                100),
                            DataSource = Truncate(
                                FirstNonBlank(Cell(iDataSource), defaultDataSource, $"CSV:{file.FileName}"),
                                120),
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

                if (errors > 0)
                {
                    return UnprocessableEntity(new
                    {
                        message = $"CSV validation failed for {errors} row(s). Nothing was imported.",
                        inserted = 0,
                        skipped,
                        errors,
                        total,
                        rolledBack = true
                    });
                }

                await using var transaction =
                    await _db.Database.BeginTransactionAsync(cancellationToken);

                const int batchSize = 500;
                try
                {
                    for (int i = 0; i < toInsert.Count; i += batchSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var batch = toInsert.Skip(i).Take(batchSize).ToList();
                        inserted += await _service.BulkInsertAsync(batch, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (inserted > 0 || skipped > 0)
                    {
                        var message =
                            $"{CurrentDisplayName()} imported CSV data. Inserted: {inserted}, " +
                            $"skipped: {skipped}, total rows: {total}.";
                        await _communicationService.CreateAuditNotificationAsync(
                            "Crime reports CSV imported",
                            message,
                            CurrentUserId(),
                            CurrentEmail());
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await transaction.CommitAsync(CancellationToken.None);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }

                _logger.LogInformation(
                    "ChangeType=BulkImport Entity=CrimeReport Actor={ActorEmail} Inserted={Inserted} Skipped={Skipped} Total={Total}",
                    CurrentEmail(),
                    inserted,
                    skipped,
                    total);

                return Ok(new
                {
                    inserted,
                    skipped,
                    errors = 0,
                    total,
                    rolledBack = false
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "CSV import cancelled and rolled back for actor {ActorEmail}",
                    CurrentEmail());
                return StatusCode(499, new
                {
                    message = "CSV import cancelled. All changes were rolled back.",
                    inserted = 0,
                    skipped,
                    errors,
                    total,
                    rolledBack = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSV import failed and was rolled back");
                return StatusCode(500, new
                {
                    message = "Import failed. All changes were rolled back.",
                    inserted = 0,
                    skipped,
                    errors,
                    total,
                    rolledBack = true
                });
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
            Jurisdiction = r.Jurisdiction,
            DataSource = r.DataSource,
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

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportJsonApi(
            string apiUrl,
            int? fromYear = null,
            int maxRecords = 25000,
            string? jurisdiction = null,
            CancellationToken cancellationToken = default)
        {
            var currentYear = DateTime.UtcNow.Year;
            if (fromYear.HasValue && (fromYear < 2001 || fromYear > currentYear))
                return BadRequest(new { message = $"From year must be between 2001 and {currentYear}." });

            if (!Uri.TryCreate(apiUrl?.Trim(), UriKind.Absolute, out var sourceUri))
                return BadRequest(new { message = "Enter a valid absolute JSON API URL." });

            if (!await IsSafePublicApiUriAsync(sourceUri, cancellationToken))
            {
                return BadRequest(new
                {
                    message = "The API URL must use HTTPS and resolve to a public internet address."
                });
            }

            maxRecords = Math.Clamp(maxRecords, 1, 100000);
            var configuredPageSize = _configuration.GetValue("CrimeJsonImport:PageSize", 1000);
            var pageSize = Math.Clamp(configuredPageSize, 100, 5000);
            var client = _httpClientFactory.CreateClient("CrimeJsonImport");
            var existingFileNumbers = await _service.GetAllFileNumbersAsync();

            var fetched = 0;
            var inserted = 0;
            var skipped = 0;
            var errors = 0;
            var offset = 0;
            var toInsert = new List<CrimeReport>();

            try
            {
                while (fetched < maxRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var take = Math.Min(pageSize, maxRecords - fetched);
                    var pageUri = BuildPagedApiUri(sourceUri, take, offset, fromYear);

                    using var response = await client.GetAsync(
                        pageUri,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "JSON API {Host} returned {StatusCode}",
                            sourceUri.Host,
                            (int)response.StatusCode);
                        throw new HttpRequestException(
                            $"The JSON API returned HTTP {(int)response.StatusCode}.");
                    }

                    var sourceRows = await response.Content
                        .ReadFromJsonAsync<List<ChicagoCrimeRecord>>(cancellationToken: cancellationToken)
                        ?? new List<ChicagoCrimeRecord>();

                    if (sourceRows.Count == 0)
                        break;

                    fetched += sourceRows.Count;
                    offset += sourceRows.Count;

                    foreach (var source in sourceRows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var fileNumber = !string.IsNullOrWhiteSpace(source.CaseNumber)
                                ? source.CaseNumber.Trim()
                                : !string.IsNullOrWhiteSpace(source.Id)
                                    ? $"API-{source.Id.Trim()}"
                                    : string.Empty;

                            if (string.IsNullOrWhiteSpace(fileNumber))
                            {
                                errors++;
                                continue;
                            }

                            if (existingFileNumbers.Contains(fileNumber))
                            {
                                skipped++;
                                continue;
                            }

                            var crimeDate = ParseChicagoDate(source.Date);
                            var resolvedJurisdiction = FirstNonBlank(
                                jurisdiction,
                                string.Equals(sourceUri.Host, "data.cityofchicago.org", StringComparison.OrdinalIgnoreCase)
                                    ? "Chicago, IL, USA"
                                    : sourceUri.Host);
                            toInsert.Add(new CrimeReport
                            {
                                FileNumber = Truncate(fileNumber, 50)!,
                                DateOfReport = crimeDate,
                                CrimeDateTime = crimeDate,
                                CrimeDateTimeRaw = Truncate(source.Date, 100),
                                CrimeType = ParseCrimeType(source.PrimaryType ?? string.Empty),
                                ReportingArea = Truncate(BuildReportingArea(source), 20),
                                Neighborhood = Truncate(
                                    string.IsNullOrWhiteSpace(source.CommunityArea)
                                        ? null
                                        : $"Community Area {source.CommunityArea.Trim()}",
                                    100),
                                Location = Truncate(BuildChicagoLocation(source), 255),
                                Jurisdiction = Truncate(resolvedJurisdiction, 100),
                                DataSource = Truncate(sourceUri.Host, 120),
                                Latitude = ParseChicagoDecimal(source.Latitude),
                                Longitude = ParseChicagoDecimal(source.Longitude),
                                ReportYear = crimeDate?.Year,
                                ReportMonth = crimeDate?.Month,
                                ReportDayOfWeek = (int?)crimeDate?.DayOfWeek,
                                CrimeHour = crimeDate?.Hour,
                                ImportedAt = DateTime.UtcNow
                            });
                            existingFileNumbers.Add(fileNumber);
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            _logger.LogDebug(ex, "Could not map JSON API record {SourceId}", source.Id);
                        }
                    }

                    if (sourceRows.Count < take)
                        break;
                }

                if (errors > 0)
                {
                    return UnprocessableEntity(new
                    {
                        message =
                            $"JSON validation failed for {errors} record(s). Nothing was imported. " +
                            "Check that the API uses the supported crime-data field names.",
                        fetched,
                        inserted = 0,
                        skipped,
                        errors,
                        rolledBack = true
                    });
                }

                await using var transaction =
                    await _db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    const int batchSize = 500;
                    for (int i = 0; i < toInsert.Count; i += batchSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var batch = toInsert.Skip(i).Take(batchSize).ToList();
                        inserted += await _service.BulkInsertAsync(batch, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var message =
                        $"{CurrentDisplayName()} imported JSON API data from {sourceUri.Host}. " +
                        $"Fetched: {fetched}, inserted: {inserted}, skipped: {skipped}.";
                    await _communicationService.CreateAuditNotificationAsync(
                        "Crime JSON API imported",
                        message,
                        CurrentUserId(),
                        CurrentEmail());

                    cancellationToken.ThrowIfCancellationRequested();
                    await transaction.CommitAsync(CancellationToken.None);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }

                _logger.LogInformation(
                    "ChangeType=JsonApiImport Entity=CrimeReport Actor={ActorEmail} ApiHost={ApiHost} Fetched={Fetched} Inserted={Inserted} Skipped={Skipped}",
                    CurrentEmail(),
                    sourceUri.Host,
                    fetched,
                    inserted,
                    skipped);

                return Ok(new
                {
                    fetched,
                    inserted,
                    skipped,
                    errors = 0,
                    fromYear,
                    source = sourceUri.Host,
                    rolledBack = false
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "JSON API import from {ApiHost} was cancelled and rolled back",
                    sourceUri.Host);
                return StatusCode(499, new
                {
                    message = "JSON import cancelled. All changes were rolled back.",
                    fetched,
                    inserted = 0,
                    skipped,
                    errors,
                    rolledBack = true
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "JSON API import from {ApiHost} failed before commit",
                    sourceUri.Host);
                return StatusCode(502, new
                {
                    message = "The JSON API could not complete the request. Nothing was imported.",
                    fetched,
                    inserted = 0,
                    skipped,
                    errors,
                    rolledBack = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "JSON API import from {ApiHost} failed and was rolled back",
                    sourceUri.Host);
                return StatusCode(500, new
                {
                    message = "JSON import failed. All changes were rolled back.",
                    fetched,
                    inserted = 0,
                    skipped,
                    errors,
                    rolledBack = true
                });
            }
        }

        private static Uri BuildPagedApiUri(
            Uri sourceUri,
            int limit,
            int offset,
            int? fromYear)
        {
            var parsed = QueryHelpers.ParseQuery(sourceUri.Query);
            var query = new List<KeyValuePair<string, string?>>();
            var whereParts = new List<string>();
            var hasOrder = false;

            foreach (var pair in parsed)
            {
                if (string.Equals(pair.Key, "$limit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "$offset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(pair.Key, "$where", StringComparison.OrdinalIgnoreCase))
                {
                    whereParts.AddRange(pair.Value.Where(value => !string.IsNullOrWhiteSpace(value))!);
                    continue;
                }

                if (string.Equals(pair.Key, "$order", StringComparison.OrdinalIgnoreCase))
                    hasOrder = true;

                foreach (var value in pair.Value)
                    query.Add(new KeyValuePair<string, string?>(pair.Key, value));
            }

            if (fromYear.HasValue)
                whereParts.Add($"year >= {fromYear.Value}");

            if (whereParts.Count > 0)
            {
                query.Add(new KeyValuePair<string, string?>(
                    "$where",
                    string.Join(" AND ", whereParts.Select(part => $"({part})"))));
            }

            if (!hasOrder)
            {
                query.Add(new KeyValuePair<string, string?>(
                    "$order",
                    "date DESC,id DESC"));
            }

            query.Add(new KeyValuePair<string, string?>("$limit", limit.ToString(CultureInfo.InvariantCulture)));
            query.Add(new KeyValuePair<string, string?>("$offset", offset.ToString(CultureInfo.InvariantCulture)));

            var builder = new UriBuilder(sourceUri)
            {
                Query = QueryString.Create(query).Value?.TrimStart('?') ?? string.Empty
            };
            return builder.Uri;
        }

        private static async Task<bool> IsSafePublicApiUriAsync(
            Uri uri,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
                return addresses.Length > 0 && addresses.All(IsPublicAddress);
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any) ||
                address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal ||
                address.IsIPv6Multicast)
            {
                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = address.GetAddressBytes();
                return (bytes[0] & 0xFE) != 0xFC;
            }

            var octets = address.GetAddressBytes();
            return octets[0] != 0 &&
                   octets[0] != 10 &&
                   octets[0] != 127 &&
                   !(octets[0] == 100 && octets[1] >= 64 && octets[1] <= 127) &&
                   !(octets[0] == 169 && octets[1] == 254) &&
                   !(octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) &&
                   !(octets[0] == 192 && octets[1] == 168) &&
                   !(octets[0] == 198 && (octets[1] == 18 || octets[1] == 19)) &&
                   octets[0] < 224;
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
            AddChange(changes, "Jurisdiction", before.Jurisdiction, after.Jurisdiction);
            AddChange(changes, "Data Source", before.DataSource, after.DataSource);
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
            Jurisdiction = vm.Jurisdiction,
            DataSource = vm.DataSource,
            Latitude = vm.Latitude,
            Longitude = vm.Longitude,
        };

        private static DateTime? ParseChicagoDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed)
                ? parsed
                : null;
        }

        private static decimal? ParseChicagoDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;
        }

        private static string? BuildReportingArea(ChicagoCrimeRecord source)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(source.District))
                parts.Add($"D{source.District.Trim()}");
            if (!string.IsNullOrWhiteSpace(source.Beat))
                parts.Add($"B{source.Beat.Trim()}");
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        private static string? BuildChicagoLocation(ChicagoCrimeRecord source)
        {
            var parts = new[] { source.Block, source.LocationDescription, source.Description }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var location = string.Join(" | ", parts);
            return string.IsNullOrWhiteSpace(location) ? null : location;
        }

        private static string? Truncate(string? value, int maxLength)
            => string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

        private static string FirstNonBlank(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
               ?? "Unspecified";

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
                "theft" => CrimeType.LarcenyMisc,
                "battery" => CrimeType.AggravatedAssault,
                "assault" => CrimeType.SimpleAssault,
                "criminaldamage" => CrimeType.MalDestProperty,
                "criminaltrespass" => CrimeType.Trespassing,
                "motorvehicletheft" => CrimeType.AutoTheft,
                "robbery" => CrimeType.StreetRobbery,
                "burglary" => CrimeType.Housebreak,
                "narcotics" => CrimeType.Drugs,
                "othernarcoticviolation" => CrimeType.Drugs,
                "weaponsviolation" => CrimeType.WeaponViolations,
                "deceptivepractice" => CrimeType.FlimFlam,
                "publicpeaceviolation" => CrimeType.Disorderly,
                "liquorlawviolation" => CrimeType.LiquorPossessionSale,
                "concealedcarrylicenseviolation" => CrimeType.WeaponViolations,
                "sexoffense" => CrimeType.SexOffenderViolation,
                "crimsexualassault" => CrimeType.SexOffenderViolation,
                "humantrafficking" => CrimeType.Kidnapping,
                _ => CrimeType.Unknown
            };
        }

        private sealed class ChicagoCrimeRecord
        {
            [JsonPropertyName("id")]
            public string? Id { get; init; }

            [JsonPropertyName("case_number")]
            public string? CaseNumber { get; init; }

            [JsonPropertyName("date")]
            public string? Date { get; init; }

            [JsonPropertyName("block")]
            public string? Block { get; init; }

            [JsonPropertyName("primary_type")]
            public string? PrimaryType { get; init; }

            [JsonPropertyName("description")]
            public string? Description { get; init; }

            [JsonPropertyName("location_description")]
            public string? LocationDescription { get; init; }

            [JsonPropertyName("beat")]
            public string? Beat { get; init; }

            [JsonPropertyName("district")]
            public string? District { get; init; }

            [JsonPropertyName("community_area")]
            public string? CommunityArea { get; init; }

            [JsonPropertyName("latitude")]
            public string? Latitude { get; init; }

            [JsonPropertyName("longitude")]
            public string? Longitude { get; init; }
        }
    }
}
