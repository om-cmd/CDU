using Domain_Layer.DbModels.Enum;
using System.ComponentModel.DataAnnotations;

namespace Analysis_Web.ViewModels
{
    public class CrimeReportViewModel
    {
        public int CrimeReportId { get; set; }

        [Required(ErrorMessage = "File number is required.")]
        [MaxLength(50, ErrorMessage = "File number cannot exceed 50 characters.")]
        [Display(Name = "File Number")]
        public string FileNumber { get; set; } = string.Empty;

        [Display(Name = "Date of Report")]
        [DataType(DataType.Date)]
        public DateTime? DateOfReport { get; set; }

        [Display(Name = "Crime Date & Time")]
        [DataType(DataType.DateTime)]
        public DateTime? CrimeDateTime { get; set; }

        [MaxLength(100)]
        [Display(Name = "Raw Crime Date/Time")]
        public string? CrimeDateTimeRaw { get; set; }

        [Required]
        [Display(Name = "Crime Type")]
        public CrimeType CrimeType { get; set; }

        [MaxLength(20)]
        [Display(Name = "Reporting Area")]
        public string? ReportingArea { get; set; }

        [MaxLength(100)]
        [Display(Name = "Neighbourhood")]
        public string? Neighborhood { get; set; }

        [MaxLength(255)]
        [Display(Name = "Location")]
        public string? Location { get; set; }

        [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
        [Display(Name = "Latitude")]
        public decimal? Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
        [Display(Name = "Longitude")]
        public decimal? Longitude { get; set; }

        [Display(Name = "Imported At")]
        public DateTime ImportedAt { get; set; }

        [Display(Name = "Row Stamp")]
        public Guid RowStamp { get; set; }
    }

    public class CrimeReportIndexViewModel
    {
        public IEnumerable<CrimeReportViewModel> Reports { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public string? Search { get; set; }
        public CrimeType? FilterCrimeType { get; set; }
        public int? FilterYear { get; set; }
        public string? FilterNeighborhood { get; set; }
        public string? SortBy { get; set; }
        public bool Ascending { get; set; } = false;
        public IEnumerable<string> AvailableNeighborhoods { get; set; } = [];
        public IEnumerable<int> AvailableYears { get; set; } = [];
    }
}