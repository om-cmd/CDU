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
        }
    }
}