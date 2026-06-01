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
                var password = StaticMethods.HashPassword("Admin@123").Item2;
                var superAdmin = new ApplicationUser
                {
                    FullName = "System Super Admin",
                    UserName = "superadmin",
                    Email = "superadmin@crime.com",
                    Password = password,
                    UserType = UserType.SuperAdmin,
                    IsActive = true,
                    EmailConfirmedStatus = true,
                    IsValidated = true,
                    CreatedAt = DateTime.UtcNow
                };

                context.ApplicationUsers.Add(superAdmin);
                await context.SaveChangesAsync();
            }

            if (!context.Roles.Any())
            {
                var role = new Role()
                {
                    CreatedBy = "System",
                    CreatedOn = DateTime.UtcNow,
                    RoleName = "SuperAdmin",
                    Status = "Active",
                    RoleDescription = "All Roles Are Granted",
                    ModifiedBy = "System",
                    ModifiedOn = DateTime.UtcNow,
                };
                    context.Roles.Add(role);
                    await context.SaveChangesAsync();
            }

            if (!context.UserRoles.Any())
            {
                var role =context.Roles.FirstOrDefault(x => x.RoleName.ToLower() == "superadmin");
                var user =  context.ApplicationUsers
                    .FirstOrDefault(u => u.Email == "superadmin@crime.com" || u.UserType == UserType.SuperAdmin);
                var userRole = new UserRole()
                {
                    RoleId = role.RoleId,
                    CreatedBy = "System",
                    CreatedDate = DateTime.UtcNow,
                    UserAccountId = user.UserAccountId,
                };
                context.UserRoles.Add(userRole);
                await context.SaveChangesAsync();
            }
            if (!context.RolePermissions.Any())
            {
                var roleId = context.Roles.FirstOrDefault(x => x.RoleName.ToLower() =="superadmin");
                var permission = context.Permissions.ToList();
                foreach (var per in permission)
                {
                    if (!context.RolePermissions.Any(x => x.RoleId == roleId.RoleId && x.PermissionId == per.Id))
                    {
                        var rolePermission = new RolePermission()
                        {
                            RoleId = roleId.RoleId,
                            PermissionId = per.Id,
                            CreatedBy = "System",
                            CreatedDate = DateTime.UtcNow,
                        };
                        context.RolePermissions.Add(rolePermission);
                        context.SaveChanges();
                    }
                }
            }
        }
    }
}