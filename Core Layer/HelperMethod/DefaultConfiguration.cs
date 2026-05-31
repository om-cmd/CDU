using Microsoft.Extensions.Configuration;

namespace Core_Layer
{
    public static class DefaultConfiguration
    {
        public static IConfiguration? StaticConfiguration { get; private set; }

        public static string? ConnectionString { get; private set; }

        public static void SetStaticConfiguration(this IConfiguration configuration)
        {
            StaticConfiguration = configuration;
        }

        public static void SetConnectionString(string connectionString)
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                ConnectionString = connectionString;
            }
        }
    }
}