using Domain_Layer.DbModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Business_Layer
{
    public interface IUnitOfWork : IDisposable
    {
        DbSet<ApplicationUser> Users { get; }

        DbSet<CrimeReport> CrimeReports { get; }

        ApplicationUser? CurrentUser { get; }
        IHttpContextAccessor HttpContextAccessor { get; }

        Task<int> SaveChangesAsync();
    }
}