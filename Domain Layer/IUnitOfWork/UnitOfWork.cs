using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Business_Layer
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly AnalysisDbContext _context;

        public AnalysisDbContext Context
        {
            get;
            private set;
        }

        public ApplicationUser? CurrentUser
        {
            get;
            private set;
        }

        public DbSet<ApplicationUser> Users
            => _context.ApplicationUsers;

        public DbSet<CrimeReport> CrimeReports
            => _context.CrimeReports;

        public UnitOfWork(AnalysisDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            Context = context;

            if (httpContextAccessor.HttpContext != null)
            {
                var session = httpContextAccessor.HttpContext.Session;
                string? userId = null;

                if (session.TryGetValue("UserId", out byte[] userIdBytes))
                {
                    userId = Encoding.UTF8.GetString(userIdBytes);
                }

                if (!string.IsNullOrEmpty(userId))
                {
                    CurrentUser = context.ApplicationUsers
                        .FirstOrDefault(x => x.UserAccountId == Convert.ToInt32(userId));
                }
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