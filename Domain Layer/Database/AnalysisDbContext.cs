using Domain_Layer.DbModels;
using Microsoft.EntityFrameworkCore;

namespace Domain_Layer.Database
{
    public class AnalysisDbContext : DbContext
    {
        public AnalysisDbContext(
            DbContextOptions<AnalysisDbContext> options)
            : base(options)
        {
        }

        protected AnalysisDbContext()
        {
        }

        public DbSet<ApplicationUser> ApplicationUsers { get; set; }

        public DbSet<CrimeReport> CrimeReports { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            //modelBuilder.Entity<CrimeReport>()
            //  .HasIndex(r => r.FileNumber)
            //  .IsUnique(false)          
            //  .HasDatabaseName("IX_CrimeReport_FileNumber");

            //modelBuilder.Entity<CrimeReport>()
            //    .HasIndex(r => r.ReportYear)
            //    .HasDatabaseName("IX_CrimeReport_ReportYear");

            //modelBuilder.Entity<CrimeReport>()
            //    .HasIndex(r => r.CrimeType)
            //    .HasDatabaseName("IX_CrimeReport_CrimeType");
        }

    }
}