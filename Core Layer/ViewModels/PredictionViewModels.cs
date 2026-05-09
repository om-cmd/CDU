using System;
using System.Collections.Generic;
using System.Text;

namespace Core_Layer.ViewModels
{
    public class PredictionRequestDto
    {
        public string Method { get; set; } = "LinearRegression";
        public string? Neighborhood { get; set; }
        public string? CrimeType { get; set; }
        public int ForecastYears { get; set; } = 3;
    }

    public class YearlyForecastDto
    {
        public int Year { get; set; }
        public double PredictedCount { get; set; }
        public double ConfidenceLow { get; set; }
        public double ConfidenceHigh { get; set; }
        public bool IsForecast { get; set; }
        public int? ActualCount { get; set; }
    }

    public class PredictionResultDto
    {
        public string Method { get; set; } = "";
        public string? Neighborhood { get; set; }
        public string? CrimeType { get; set; }
        public List<YearlyForecastDto> Forecasts { get; set; } = new();
        public double R2Score { get; set; }
        public string TrendDirection { get; set; } = "";
        public double AverageAnnualChange { get; set; }
        public string Interpretation { get; set; } = "";
    }
}
