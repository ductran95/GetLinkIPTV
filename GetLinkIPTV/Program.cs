using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace GetLinkIPTV
{
    class Program
    {
        public static IServiceCollection ServiceCollection { get; set; }
        public static IServiceProvider ServiceProvider { get; set; }
        
        static async Task Main(string[] args)
        {
            ServiceProvider = BuildServiceProvider();
            
            var worker = ServiceProvider.GetService<Worker>();
            await worker.StartAsync();
        }
        
        static IConfiguration GetConfiguration() =>
            new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

        static IServiceProvider BuildServiceProvider()
        {
            var config = GetConfiguration();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
            
            ServiceCollection = new ServiceCollection();

            ServiceCollection.AddLogging(config =>
            {
                config.ClearProviders();
                config.AddSerilog(Log.Logger);
            });
            
            ServiceCollection.AddSingleton<Worker>();

            return ServiceCollection.BuildServiceProvider();
        }
    }
}