namespace Core_Layer.Models
{
    public class DashboardStatsModel
    {
        public int TotalRecords { get; set; }
        public int TotalWithCoords { get; set; }
        public Dictionary<string, int> CrimeTypeCounts { get; set; } = new();
        public Dictionary<string, int> NeighborhoodCounts { get; set; } = new();
        public Dictionary<int, int> YearlyCounts { get; set; } = new();
        public Dictionary<int, int> HourlyCounts { get; set; } = new();
        public List<MapPointModel> MapPoints { get; set; } = new();
        public List<RecentIncidentModel> RecentIncidents { get; set; } = new();
    }

    public class MapPointModel
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string CrimeType { get; set; } = string.Empty;
        public string FileNumber { get; set; } = string.Empty;
        public string Neighborhood { get; set; } = string.Empty;
        public string DateOfReport { get; set; } = string.Empty;
    }

    public class RecentIncidentModel
    {
        public string FileNumber { get; set; } = string.Empty;
        public string CrimeType { get; set; } = string.Empty;
        public string Neighborhood { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string DateOfReport { get; set; } = string.Empty;
        public string CrimeDateTime { get; set; } = string.Empty;
    }
}