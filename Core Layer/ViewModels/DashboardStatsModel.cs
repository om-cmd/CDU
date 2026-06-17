namespace Core_Layer.Models
{
    public class DashboardStatsModel
    {
        public int TotalRecords { get; set; }
        public int TotalWithCoords { get; set; }
        public string LatestReportDate { get; set; } = "Not available";
        public string TopNeighborhoodName { get; set; } = "Not available";
        public int TopNeighborhoodCount { get; set; }
        public int HighHarmIncidentCount { get; set; }
        public int MediumHarmIncidentCount { get; set; }
        public int LowHarmIncidentCount { get; set; }
        public Dictionary<string, int> CrimeTypeCounts { get; set; } = new();
        public Dictionary<string, int> NeighborhoodCounts { get; set; } = new();
        public Dictionary<int, int> YearlyCounts { get; set; } = new();
        public Dictionary<string, int> MonthlyCounts { get; set; } = new();
        public Dictionary<int, int> HourlyCounts { get; set; } = new();

        public long TotalHarm { get; set; }

        public List<HarmByTypeModel> HarmByType { get; set; } = new();

        public List<HarmByNeighborhoodModel> HarmByNeighborhood { get; set; } = new();

        public Dictionary<int, long> YearlyHarm { get; set; } = new();
        public Dictionary<string, long> MonthlyHarm { get; set; } = new();

        public double[] HourlyHarmPct { get; set; } = new double[4];

        public long HighHarm { get; set; }   
        public long MediumHarm { get; set; }  
        public long LowHarm { get; set; }   

        public double HighHarmPct => TotalHarm > 0 ? (double)HighHarm / TotalHarm * 100 : 0;
        public double MediumHarmPct => TotalHarm > 0 ? (double)MediumHarm / TotalHarm * 100 : 0;
        public double LowHarmPct => TotalHarm > 0 ? (double)LowHarm / TotalHarm * 100 : 0;

        public List<MapPointModel> MapPoints { get; set; } = new();
        public List<RecentIncidentModel> RecentIncidents { get; set; } = new();
    }


    public class HarmByTypeModel
    {
        public string CrimeType { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalHarm { get; set; }
        public int CssWeight { get; set; }
    }

    public class HarmByNeighborhoodModel
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalHarm { get; set; }
    }

    public class MapPointModel
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string CrimeType { get; set; } = string.Empty;
        public string FileNumber { get; set; } = string.Empty;
        public string Neighborhood { get; set; } = string.Empty;
        public string DateOfReport { get; set; } = string.Empty;
        public int CssWeight { get; set; }  
    }

    public class RecentIncidentModel
    {
        public string FileNumber { get; set; } = string.Empty;
        public string CrimeType { get; set; } = string.Empty;
        public string Neighborhood { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string DateOfReport { get; set; } = string.Empty;
        public string CrimeDateTime { get; set; } = string.Empty;
        public int CssWeight { get; set; }
        public string SeverityLevel { get; set; } = string.Empty;
    }
}
