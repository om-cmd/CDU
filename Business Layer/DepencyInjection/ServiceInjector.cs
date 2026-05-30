using Analysis_Web.Services;
using Business_Layer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Business_Layer.DependencyInjection;

public static class ServiceInjector
{
    public static IServiceCollection AddService(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ICrimeReportInterface, CrimeReportService>();

        return services;
    }
}