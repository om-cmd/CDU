using Domain_Layer.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Data;

public class DatabaseContextFactory:IDesignTimeDbContextFactory<AnalysisDbContext>
{
    public AnalysisDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnalysisDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=AnalysisDb;User Id=sa;Password=Str0ngPass@12345;Trusted_Connection=False;MultipleActiveResultSets=true;TrustServerCertificate=True;Encrypt=False"
        );

        return new AnalysisDbContext(optionsBuilder.Options);
    }
}