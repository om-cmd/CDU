using System;
using System.Collections.Generic;
using System.Text;

namespace Core_Layer.ViewModels
{
    public class ImportResultDto
    {
        public int JobId { get; set; }
        public bool Success { get; set; }
        public int TotalRows { get; set; }
        public int SuccessRows { get; set; }
        public int SkippedRows { get; set; }
        public int ErrorRows { get; set; }
        public double DurationSeconds { get; set; }
        public string? Message { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
