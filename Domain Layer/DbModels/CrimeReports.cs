using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Domain_Layer.DbModels
{
    [Table("CrimeReports")]
    public class CrimeReport
    {
        [Key]
        public int CrimeReportId { get; set; }

        [Required, MaxLength(50)]
        public string FileNumber { get; set; } = string.Empty;

        public DateTime? DateOfReport { get; set; }

        public DateTime? CrimeDateTime { get; set; }

        [MaxLength(100)]
        public string? CrimeDateTimeRaw { get; set; }

        [MaxLength(150)]
        public string? CrimeType { get; set; }

        [MaxLength(20)]
        public string? ReportingArea { get; set; }

        [MaxLength(100)]
        public string? Neighborhood { get; set; }

        [MaxLength(255)]
        public string? Location { get; set; }

        [Column(TypeName = "decimal(10,7)")]
        public decimal? Latitude { get; set; }

        [Column(TypeName = "decimal(10,7)")]
        public decimal? Longitude { get; set; }

        public int? ReportYear { get; set; }
        public int? ReportMonth { get; set; }
        public int? ReportDayOfWeek { get; set; }
        public int? CrimeHour { get; set; }

        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid RowStamp { get; set; } = Guid.NewGuid();
    }
}
