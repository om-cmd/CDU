using Microsoft.Extensions.Configuration;

namespace Core_Layer.HelperMethod;

public static class DefaultConfiguration
{
    public static IConfiguration staticConfiguration { get; private set; }

    public static void SetStaticConfiguration(this IConfiguration configuration)
    {
        staticConfiguration = configuration;
    }
    public static string ConnectionString { get; private set; }

    public static void setConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            ConnectionString = connectionString;
        }
    }
}