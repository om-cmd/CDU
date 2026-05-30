using System.Security.Claims;
using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Business_Layer
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly AnalysisDbContext _context;

        public AnalysisDbContext Context { get; }

        public ApplicationUser? CurrentUser { get; private set; }
        public string UserAgent { get; set; } 

        public IHttpContextAccessor HttpContextAccessor { get; }

        public DbSet<ApplicationUser> Users => _context.ApplicationUsers;

        public DbSet<CrimeReport> CrimeReports => _context.CrimeReports;

        public UnitOfWork(
            AnalysisDbContext context,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            Context = context;
            HttpContextAccessor = httpContextAccessor;

            var httpContext = httpContextAccessor.HttpContext;

            if (httpContext == null)
                return;
var userAgent = httpContextAccessor.HttpContext.Request.Headers["User-Agent"];
if(!string.IsNullOrEmpty(userAgent))
    UserAgent = userAgent[0].ToString();
string? ipAddress = null;

if (httpContextAccessor.HttpContext?.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues ipAddresses) == true)
{
    ipAddress = ipAddresses.FirstOrDefault();
}
ipAddress ??= httpContextAccessor.HttpContext?
    .Connection
    .RemoteIpAddress?
    .ToString();

string username = httpContextAccessor.HttpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Actor)?.Value;
if (!string.IsNullOrWhiteSpace(username))
{
    CurrentUser = _context.ApplicationUsers.FirstOrDefault(x => x.UserName == username);
}
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}