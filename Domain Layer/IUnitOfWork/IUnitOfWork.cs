using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Business_Layer
{
    public interface IUnitOfWork : IDisposable
    {
        AnalysisDbContext _db { get; }

        IHttpContextAccessor HttpContextAccessor { get; }

        ApplicationUser? CurrentUser { get; }

        DbSet<ApplicationUser> Users { get; }

        DbSet<CrimeReport> CrimeReports { get; }

        Task<int> SaveChangesAsync();
    }
}