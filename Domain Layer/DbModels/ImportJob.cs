using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Domain_Layer.DbModels
{
    [Table("ImportJobs")]
    public class ImportJob
    {
        [Key]
        public int ImportJobId { get; set; }

        [Required, MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Status { get; set; } = "Pending";

        public int TotalRows { get; set; }
        public int ProcessedRows { get; set; }
        public int SuccessRows { get; set; }
        public int SkippedRows { get; set; }
        public int ErrorRows { get; set; }

        [MaxLength(4000)]
        public string? ErrorLog { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public double? DurationSeconds { get; set; }

        public int? UserAccountId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid RowStamp { get; set; } = Guid.NewGuid();
    }
}
