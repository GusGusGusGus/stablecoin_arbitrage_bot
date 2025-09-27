using System;
using System.Threading.Tasks;
using ArbitrageRunner.Infrastructure;
using ArbitrageRunner.Models;
using ArbitrageRunner.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        if (!string.IsNullOrWhiteSpace(appConfig.PriceFeeds.UniswapV3.Endpoint))
        {
            builder.Services.AddHttpClient(PriceFeedService.UniswapHttpClientName, client =>
            {
                client.BaseAddress = new Uri(appConfig.PriceFeeds.UniswapV3.Endpoint);
            });
        }

        if (!string.IsNullOrWhiteSpace(appConfig.PriceFeeds.Balancer.Endpoint))
        {
            builder.Services.AddHttpClient(PriceFeedService.BalancerHttpClientName, client =>
            {
                client.BaseAddress = new Uri(appConfig.PriceFeeds.Balancer.Endpoint);
            });
        }

        builder.Services.AddSingleton<EthereumClientFactory>();
        builder.Services.AddSingleton<SnapshotStore>();
        builder.Services.AddHttpClient();
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