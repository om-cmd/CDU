using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Data
{
    public static class DbSeeder
    {
        public static async Task SeedRolesAndAdminAsync(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            if (!await roleManager.RoleExistsAsync("SuperAdmin"))
                await roleManager.CreateAsync(new IdentityRole("SuperAdmin"));

            if (!await roleManager.RoleExistsAsync("Admin"))
                await roleManager.CreateAsync(new IdentityRole("Admin"));

            if (!await roleManager.RoleExistsAsync("User"))
                await roleManager.CreateAsync(new IdentityRole("User"));

            var superAdmin = await userManager.FindByEmailAsync("superadmin@crime.com");

            if (superAdmin == null)
            {
                var user = new ApplicationUser
                {
                    UserName = "superadmin",
                    Email = "superadmin@crime.com",
                    Password = "Admin@123",
                    FullName = "System Super Admin",
                    EmailConfirmed = true,
                    UserType = UserType.SuperAdmin,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(user, "Admin@123");

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "SuperAdmin");
                }
            }
        }
    }
}