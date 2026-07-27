using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;

namespace Analysis_Web.Services
{
    public interface ICrimeReportInterface
    {
        Task<(IEnumerable<CrimeReport> Reports, int TotalCount)> GetPagedAsync(
            int page, int pageSize, string? search, CrimeType? crimeType,
            int? year, string? neighborhood, string? sortBy, bool ascending);

        Task<CrimeReport?> GetByIdAsync(int id);
        Task<CrimeReport> CreateAsync(CrimeReport report);
        Task<CrimeReport?> UpdateAsync(int id, CrimeReport report);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<bool> FileNumberExistsAsync(string fileNumber, int? excludeId = null);
        Task<CrimeReportStats> GetStatsAsync();
        Task<int> BulkInsertAsync(
            IEnumerable<CrimeReport> reports,
            CancellationToken cancellationToken = default);
        Task<HashSet<string>> GetAllFileNumbersAsync();
        Task<Dictionary<string, int>> GetFileNumberToIdMapAsync();
        Task<int> GetTotalCountAsync();   
    }

    public class CrimeReportStats
    {
        public int TotalReports { get; set; }
        public int ReportsThisYear { get; set; }
        public int ReportsThisMonth { get; set; }
        public Dictionary<string, int> ByType { get; set; } = new();
        public Dictionary<string, int> ByNeighborhood { get; set; } = new();
        public Dictionary<int, int> ByHour { get; set; } = new();
    }
}
