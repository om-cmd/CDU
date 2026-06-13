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
        public DbSet<PasswordResetOtp> PasswordResetOtps { get; set; }
        public DbSet<EmailMessage> EmailMessages { get; set; }
        public DbSet<EmailMessageRecipient> EmailMessageRecipients { get; set; }
        public DbSet<UserNotification> UserNotifications { get; set; }
        public DbSet<UserNotificationRecipient> UserNotificationRecipients { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RoleConfig());
            modelBuilder.ApplyConfiguration(new UserRoleConfig());
            modelBuilder.ApplyConfiguration(new PermissionConfig());
            modelBuilder.ApplyConfiguration(new RolePermissionConfig());

            modelBuilder.Entity<PasswordResetOtp>()
                .HasIndex(x => new { x.Email, x.ExpiresAtUtc });

            modelBuilder.Entity<EmailMessageRecipient>()
                .HasOne(x => x.EmailMessage)
                .WithMany(x => x.Recipients)
                .HasForeignKey(x => x.EmailMessageId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserNotificationRecipient>()
                .HasOne(x => x.Notification)
                .WithMany(x => x.Recipients)
                .HasForeignKey(x => x.UserNotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        }

    }
}
