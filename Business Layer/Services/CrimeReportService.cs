using Business_Layer;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.EntityFrameworkCore;

namespace Analysis_Web.Services
{
    public class CrimeReportService : ICrimeReportInterface
    {
        private readonly IUnitOfWork _context;

        public CrimeReportService(IUnitOfWork context)
        {
            _context = context;
        }

        public async Task<(IEnumerable<CrimeReport> Reports, int TotalCount)> GetPagedAsync(int page, int pageSize,string? search, CrimeType? crimeType, int? year, string? neighborhood,string? sortBy, bool ascending)
        {
            var q = _context.CrimeReports.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var sv = search.ToLower();
                q = q.Where(r =>
                    (r.FileNumber != null && r.FileNumber.ToLower().Contains(sv)) ||
                    (r.Neighborhood != null && r.Neighborhood.ToLower().Contains(sv)) ||
                    (r.Location != null && r.Location.ToLower().Contains(sv)) ||
                    (r.ReportingArea != null && r.ReportingArea.ToLower().Contains(sv)) ||
                    r.CrimeType.ToString().ToLower().Contains(sv));
            }

            if (crimeType.HasValue) q = q.Where(r => r.CrimeType == crimeType.Value);
            if (year.HasValue) q = q.Where(r => r.ReportYear == year.Value);
            if (!string.IsNullOrWhiteSpace(neighborhood))
                q = q.Where(r => r.Neighborhood != null && r.Neighborhood.ToLower().Contains(neighborhood.ToLower()));

            var total = await q.CountAsync();

            q = (sortBy?.ToLower(), ascending) switch
            {
                ("filenumber", true) => q.OrderBy(r => r.FileNumber),
                ("filenumber", false) => q.OrderByDescending(r => r.FileNumber),
                ("crimetype", true) => q.OrderBy(r => r.CrimeType),
                ("crimetype", false) => q.OrderByDescending(r => r.CrimeType),
                ("neighborhood", true) => q.OrderBy(r => r.Neighborhood),
                ("neighborhood", false) => q.OrderByDescending(r => r.Neighborhood),
                (_, true) => q.OrderBy(r => r.DateOfReport),
                (_, false) => q.OrderByDescending(r => r.DateOfReport),
            };

            var data = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (data, total);
        }

        public async Task<CrimeReport?> GetByIdAsync(int id)
            => await _context.CrimeReports.FindAsync(id);

        public async Task<CrimeReport> CreateAsync(CrimeReport report)
        {
            report.ImportedAt = DateTime.UtcNow;
            report.RowStamp = Guid.NewGuid();

            // Auto-populate derived date fields
            if (report.DateOfReport.HasValue)
            {
                report.ReportYear = report.DateOfReport.Value.Year;
                report.ReportMonth = report.DateOfReport.Value.Month;
                report.ReportDayOfWeek = (int)report.DateOfReport.Value.DayOfWeek;
            }
            if (report.CrimeDateTime.HasValue)
                report.CrimeHour = report.CrimeDateTime.Value.Hour;

            _context.CrimeReports.Add(report);
            await _context.SaveChangesAsync();
            return report;
        }

        public async Task<CrimeReport?> UpdateAsync(int id, CrimeReport report)
        {
            var existing = await _context.CrimeReports.FindAsync(id);
            if (existing == null) return null;

            existing.FileNumber = report.FileNumber;
            existing.DateOfReport = report.DateOfReport;
            existing.CrimeDateTime = report.CrimeDateTime;
            existing.CrimeDateTimeRaw = report.CrimeDateTimeRaw;
            existing.CrimeType = report.CrimeType;
            existing.ReportingArea = report.ReportingArea;
            existing.Neighborhood = report.Neighborhood;
            existing.Location = report.Location;
            existing.Latitude = report.Latitude;
            existing.Longitude = report.Longitude;

            // Re-derive date fields
            if (report.DateOfReport.HasValue)
            {
                existing.ReportYear = report.DateOfReport.Value.Year;
                existing.ReportMonth = report.DateOfReport.Value.Month;
                existing.ReportDayOfWeek = (int)report.DateOfReport.Value.DayOfWeek;
            }
            if (report.CrimeDateTime.HasValue)
                existing.CrimeHour = report.CrimeDateTime.Value.Hour;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var report = await _context.CrimeReports.FindAsync(id);
            if (report == null) return false;
            _context.CrimeReports.Remove(report);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
            => await _context.CrimeReports.AnyAsync(r => r.CrimeReportId == id);

        public async Task<bool> FileNumberExistsAsync(string fileNumber, int? excludeId = null)
        {
            var query = _context.CrimeReports.Where(r => r.FileNumber == fileNumber);
            if (excludeId.HasValue) query = query.Where(r => r.CrimeReportId != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<CrimeReportStats> GetStatsAsync()
        {
            var now = DateTime.UtcNow;
            var reports = await _context.CrimeReports.ToListAsync();

            return new CrimeReportStats
            {
                TotalReports = reports.Count,
                ReportsThisYear = reports.Count(r => r.ReportYear == now.Year),
                ReportsThisMonth = reports.Count(r => r.ReportYear == now.Year && r.ReportMonth == now.Month),
                ByType = reports.GroupBy(r => r.CrimeType.ToString())
                                           .ToDictionary(g => g.Key, g => g.Count()),
                ByNeighborhood = reports.Where(r => r.Neighborhood != null)
                                           .GroupBy(r => r.Neighborhood!)
                                           .OrderByDescending(g => g.Count())
                                           .Take(10)
                                           .ToDictionary(g => g.Key, g => g.Count()),
                ByHour = reports.Where(r => r.CrimeHour.HasValue)
                                           .GroupBy(r => r.CrimeHour!.Value)
                                           .ToDictionary(g => g.Key, g => g.Count())
            };
        }
        public async Task<int> BulkInsertAsync(IEnumerable<CrimeReport> reports)
        {
            var list = reports.ToList();
            if (list.Count == 0) return 0;

            await _context.CrimeReports.AddRangeAsync(list);
            await _context.SaveChangesAsync();
            return list.Count;
        }

        public async Task<HashSet<string>> GetAllFileNumbersAsync()
        {
            var fileNumbers = await _context.CrimeReports
                .AsNoTracking()
                .Select(r => r.FileNumber)
                .ToListAsync();

            return new HashSet<string>(fileNumbers, StringComparer.OrdinalIgnoreCase);
        }
        public async Task<Dictionary<string, int>> GetFileNumberToIdMapAsync()
        {
            var records = await _context.CrimeReports
                .Select(f => new { f.FileNumber, f.CrimeReportId })
                .ToListAsync();

            return records.ToDictionary(
                r => r.FileNumber,
                r => r.CrimeReportId
            );
        }

        public async Task<int> GetTotalCountAsync()
        {
            return await _context.CrimeReports.CountAsync();
        }
    }
}