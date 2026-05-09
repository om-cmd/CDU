using System;
using System.Collections.Generic;
using System.Text;

namespace Core_Layer.ViewModels
{

    public class DashboardSummaryDto
    {
        public int TotalReports { get; set; }
        public int UniqueNeighborhoods { get; set; }
        public int UniqueCrimeTypes { get; set; }
        public int YearFrom { get; set; }
        public int YearTo { get; set; }
        public string TopCrimeType { get; set; } = "";
        public string HighestCrimeNeighborhood { get; set; } = "";
        public double YearOverYearChange { get; set; }
        public int CurrentYearCount { get; set; }
        public int PreviousYearCount { get; set; }
    }

    public class CrimeByYearDto
    {
        public int Year { get; set; }
        public int Count { get; set; }
        public double? ChangePercent { get; set; }
    }

    public class CrimeByMonthDto
    {
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public int Count { get; set; }
    }

    public class CrimeByTypeDto
    {
        public string CrimeType { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class CrimeByNeighborhoodDto
    {
        public string Neighborhood { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class CrimeByHourDto
    {
        public int Hour { get; set; }
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class CrimeByDayDto
    {
        public int DayOfWeek { get; set; }
        public string DayName { get; set; } = "";
        public int Count { get; set; }
    }

    public class HotspotDto
    {
        public string Neighborhood { get; set; } = "";
        public string CrimeType { get; set; } = "";
        public int Count { get; set; }
        public double RiskScore { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class SeasonDto
    {
        public string Season { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class SeasonalTrendDto
    {
        public List<SeasonDto> Seasons { get; set; } = new();
        public string PeakSeason { get; set; } = "";
        public string LowSeason { get; set; } = "";
    }
}
