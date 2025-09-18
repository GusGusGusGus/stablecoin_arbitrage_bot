using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ArbitrageRunner.Infrastructure;
using ArbitrageRunner.Models;
using ArbitrageRunner.Services;

namespace ArbitrageRunner;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("config/appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "ARBOT_")
            .AddCommandLine(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddConsole();

        var runnerOptions = RunnerOptions.FromConfiguration(builder.Configuration);
        builder.Services.AddSingleton(runnerOptions);

        var appConfig = builder.Configuration.GetSection("Arbitrage").Get<AppConfig>() ?? AppConfig.Default();
        builder.Services.AddSingleton(appConfig);

        builder.Services.AddSingleton<EthereumClientFactory>();
        builder.Services.AddSingleton<PriceFeedService>();
        builder.Services.AddSingleton<GasOracleService>();
        builder.Services.AddSingleton<FlashLoanService>();
        builder.Services.AddSingleton<OpportunityScanner>();
        builder.Services.AddSingleton<ExecutionPlanner>();
        builder.Services.AddSingleton<BacktestService>();
        builder.Services.AddHostedService<ArbitrageWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}