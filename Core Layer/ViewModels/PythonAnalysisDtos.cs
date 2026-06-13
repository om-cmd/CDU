namespace Core_Layer.ViewModels;

public class DotNetCrimeAnalysisRequest
{
    public string? Search { get; set; }
    public string? Neighborhood { get; set; }
    public string? CrimeType { get; set; }
    public int? Year { get; set; }
    public string Frequency { get; set; } = "D";
    public int ForecastPeriods { get; set; } = 30;
    public int PageSize { get; set; } = 100000;
}

public class PythonCrimeAnalysisRequest
{
    public string Frequency { get; set; } = "D";
    public int ForecastPeriods { get; set; } = 30;
    public List<PythonCrimeReportDto> Records { get; set; } = new();
}

public class PythonCrimeReportDto
{
    public int? CrimeReportId { get; set; }
    public string? FileNumber { get; set; }
    public DateTime? DateOfReport { get; set; }
    public DateTime? CrimeDateTime { get; set; }
    public string? CrimeDateTimeRaw { get; set; }
    public string? CrimeType { get; set; }
    public string? ReportingArea { get; set; }
    public string? Neighborhood { get; set; }
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
