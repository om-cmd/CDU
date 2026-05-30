using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Core_Layer.HelperMethod;

namespace Infrastructure.Data
{
    public static class DbSeeder
    {
        public static async Task SeedSuperAdminAsync(AnalysisDbContext context)
        {
            // Ensure the database is created
            await context.Database.EnsureCreatedAsync();

            // Check if a SuperAdmin already exists (by email or role)
            bool superAdminExists = context.ApplicationUsers
                .Any(u => u.Email == "superadmin@crime.com" || u.UserType == UserType.SuperAdmin);

            if (!superAdminExists)
            {
                var superAdmin = new ApplicationUser
                {
                    FullName = "System Super Admin",
                    UserName = "superadmin",
                    Email = "superadmin@crime.com",
                    Password = StaticMethods.HashPassword("Admin@123").Item2,
                    UserType = UserType.SuperAdmin,
                    IsActive = true,
                    EmailConfirmedStatus = true,
                    IsValidated = true,
                    CreatedAt = DateTime.UtcNow
                };

                context.ApplicationUsers.Add(superAdmin);
                await context.SaveChangesAsync();
            }
        }
    }
}