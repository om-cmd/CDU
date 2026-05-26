using Domain_Layer.DbModels;
using Microsoft.EntityFrameworkCore;

namespace Business_Layer
{
    public interface IUnitOfWork : IDisposable
    {
        DbSet<ApplicationUser> Users { get; }

        DbSet<CrimeReport> CrimeReports { get; }

        ApplicationUser? CurrentUser { get; }

        Task<int> SaveChangesAsync();
    }
}