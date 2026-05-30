using Data;
using Data.Configuration;
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

        public DbSet<ApplicationUser> ApplicationUsers { get; set; }

        public DbSet<CrimeReport> CrimeReports { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<Permission> Permissions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RoleConfig());
            modelBuilder.ApplyConfiguration(new UserRoleConfig());
            modelBuilder.ApplyConfiguration(new PermissionConfig());
            modelBuilder.ApplyConfiguration(new RolePermissionConfig());
        }
    }
}