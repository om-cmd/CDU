using System.Security.Claims;
using Core_Layer.HelperMethod;
using Domain_Layer.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Business_Layer.Middleware;

public class AuthorizePermission : TypeFilterAttribute
{
    public AuthorizePermission(string? claimValue = null, string? module = null)
        : base(typeof(ClaimRequirementFilter))
    {
        Arguments = new object[]
        {
            new Claim("Permission", claimValue ?? ""),
            new Claim("Module", module ?? "ADMIN")
        };
    }

    public class ClaimRequirementFilter : IAsyncActionFilter
    {
        private readonly Claim _claim;
        private readonly Claim _module;

        public ClaimRequirementFilter(Claim claim, Claim module)
        {
            _claim = claim;
            _module = module;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            try
            {
                var db = context.HttpContext.RequestServices
                    .GetRequiredService<AnalysisDbContext>();

                var claims = context.HttpContext.User.Claims.ToList();

                var token = claims.FirstOrDefault(x => x.Type == "Token")?.Value;

                if (string.IsNullOrWhiteSpace(token))
                {
                    token = context.HttpContext.Request.Headers.Authorization
                        .ToString()
                        .Replace("Bearer ", string.Empty);
                }

                var data = StaticMethods.ParseToken(token);

                if (data.Status != "200" || data.Data == null)
                {
                    context.Result = new ObjectResult(new
                    {
                        Status = data.Status,
                        Message = data.Message
                    })
                    {
                        StatusCode = int.TryParse(data.Status, out var code) ? code : 401
                    };

                    return;
                }

                if (string.IsNullOrWhiteSpace(data.Data.EmailAddress))
                {
                    context.Result = new UnauthorizedObjectResult(new
                    {
                        Status = "401",
                        Message = "Invalid user data"
                    });

                    return;
                }

                if (data.Data.UserType == "SuperAdmin")
                {
                    await next();
                    return;
                }

                if (string.IsNullOrWhiteSpace(_claim.Value))
                {
                    await next();
                    return;
                }

                var actionUser = db.ApplicationUsers
                    .FirstOrDefault(x => x.Email.ToLower() == data.Data.EmailAddress.ToLower());

                if (actionUser == null)
                {
                    context.Result = new UnauthorizedObjectResult(new
                    {
                        Status = "401",
                        Message = "User not found"
                    });

                    return;
                }

                var userIdClaim = context.HttpContext.User
                    .FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!long.TryParse(userIdClaim, out var userId))
                {
                    context.Result = new RedirectToActionResult("Login", "Authentication", null);
                    return;
                }

                var slug = _claim.Value.Split('&').Last();

                var hasPermission =
                    (from ur in db.UserRoles
                     join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
                     join p in db.Permissions on rp.PermissionId equals p.Id
                     where ur.UserAccountId == userId && p.Slug == slug
                     select p).Any();

                if (hasPermission)
                {
                    await next();
                    return;
                }

                context.Result = new RedirectToActionResult(
                    "AccessDenied",
                    "Authentication",
                    null);
            }
            catch
            {
                context.Result = new RedirectToActionResult(
                    "Login",
                    "Authentication",
                    null);
            }
        }
    }
}