using Analysis_Web.Services;
using Analysis_Web.ViewModels;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Mvc;

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

        // GET: /CrimeReport
        public async Task<IActionResult> CrimeReport(
            string? search, CrimeType? crimeType, int? year,
            string? neighborhood, string? sortBy, bool ascending = false,
            int page = 1, int pageSize = DefaultPageSize)
        {
            pageSize = Math.Clamp(pageSize, 5, 100);
            page = Math.Max(1, page);

            var (reports, total) = await _service.GetPagedAsync(
                page, pageSize, search, crimeType, year, neighborhood, sortBy, ascending);

            var vm = new CrimeReportIndexViewModel
            {
                Reports = reports.Select(MapToVm),
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                Search = search,
                FilterCrimeType = crimeType,
                FilterYear = year,
                FilterNeighborhood = neighborhood,
                SortBy = sortBy,
                Ascending = ascending,
            };

            return View("~/Views/CrimeData/CrimeReport.cshtml", vm);
        }

        // GET: /CrimeReport/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var report = await _service.GetByIdAsync(id);
            if (report == null)
            {
                TempData["Error"] = $"Crime report #{id} was not found.";
                return RedirectToAction(nameof(Index));
            }
            return View(MapToVm(report));
        }

        // GET: /CrimeReport/Create
        public IActionResult Create()
            => View(new CrimeReportViewModel { DateOfReport = DateTime.Today });

        // POST: /CrimeReport/Create
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CrimeReportViewModel vm)
        {
            if (await _service.FileNumberExistsAsync(vm.FileNumber))
                ModelState.AddModelError(nameof(vm.FileNumber), "This file number already exists.");

            if (!ModelState.IsValid)
                return View(vm);

            try
            {
                var report = MapFromVm(vm);
                var created = await _service.CreateAsync(report);
                TempData["Success"] = $"Crime report {created.FileNumber} created successfully.";
                return RedirectToAction(nameof(Details), new { id = created.CrimeReportId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating crime report");
                ModelState.AddModelError("", "An error occurred while saving. Please try again.");
                return View(vm);
            }
        }

        // GET: /CrimeReport/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var report = await _service.GetByIdAsync(id);
            if (report == null)
            {
                TempData["Error"] = $"Crime report #{id} was not found.";
                return RedirectToAction(nameof(Index));
            }
            return View(MapToVm(report));
        }

        // POST: /CrimeReport/Edit/5
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CrimeReportViewModel vm)
        {
            if (id != vm.CrimeReportId)
                return BadRequest();

            if (await _service.FileNumberExistsAsync(vm.FileNumber, excludeId: id))
                ModelState.AddModelError(nameof(vm.FileNumber), "This file number already exists.");

            if (!ModelState.IsValid)
                return View(vm);

            try
            {
                var report = MapFromVm(vm);
                var updated = await _service.UpdateAsync(id, report);
                if (updated == null)
                {
                    TempData["Error"] = "Report not found — it may have been deleted.";
                    return RedirectToAction(nameof(Index));
                }
                TempData["Success"] = $"Crime report {updated.FileNumber} updated successfully.";
                return RedirectToAction(nameof(Details), new { id = updated.CrimeReportId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating crime report {Id}", id);
                ModelState.AddModelError("", "An error occurred while saving. Please try again.");
                return View(vm);
            }
        }

        // GET: /CrimeReport/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var report = await _service.GetByIdAsync(id);
            if (report == null)
            {
                TempData["Error"] = $"Crime report #{id} was not found.";
                return RedirectToAction(nameof(Index));
            }
            return View(MapToVm(report));
        }

        // POST: /CrimeReport/Delete/5
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var deleted = await _service.DeleteAsync(id);
            TempData[deleted ? "Success" : "Error"] = deleted
                ? "Crime report deleted successfully."
                : $"Crime report #{id} was not found.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /CrimeReport/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var stats = await _service.GetStatsAsync();
            return View(stats);
        }

        // POST: /CrimeReport/BulkDelete  (AJAX)
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

        // ── Helpers ──────────────────────────────────────────────────────────────

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
    }
}