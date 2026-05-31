using System;
using System.Security.Claims;
using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace Business_Layer
{
    public class UnitOfWork : IUnitOfWork
    {
        public AnalysisDbContext _db { get; }

        public AnalysisDbContext Db { get; }
        public IHttpContextAccessor HttpContextAccessor { get; }

        public ApplicationUser? CurrentUser { get; private set; }

        public string? UserAgent { get; private set; }

        public DbSet<ApplicationUser> Users => _db.ApplicationUsers;

        public DbSet<CrimeReport> CrimeReports => _db.CrimeReports;

        public UnitOfWork(
            AnalysisDbContext db,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            HttpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

            var httpContext = HttpContextAccessor.HttpContext;

            if (httpContext == null)
                return;

            var userAgent = httpContext.Request.Headers["User-Agent"];

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                UserAgent = userAgent.ToString();
            }

            string? ipAddress = null;

            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues forwardedIps))
            {
                ipAddress = forwardedIps.FirstOrDefault();
            }

            ipAddress ??= httpContext.Connection.RemoteIpAddress?.ToString();

            var email = httpContext.User.Claims
                .FirstOrDefault(x =>
                    x.Type == ClaimTypes.Actor
                )?.Value;

            if (!string.IsNullOrWhiteSpace(email))
            {
                CurrentUser = _db.ApplicationUsers
                    .FirstOrDefault(x => x.Email == email);
            }
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _db.SaveChangesAsync();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}