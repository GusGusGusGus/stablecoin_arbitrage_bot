using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Infrastructure;
using ArbitrageRunner.Models;
using ArbitrageRunner.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("config/appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "ARBOT_");

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var appConfig = builder.Configuration.GetSection("Arbitrage").Get<AppConfig>() ?? AppConfig.Default();
builder.Services.AddSingleton(appConfig);

builder.Services.AddHttpClient();

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
builder.Services.AddSingleton<PriceFeedService>();
builder.Services.AddSingleton<GasOracleService>();
builder.Services.AddSingleton<FlashLoanService>();
builder.Services.AddSingleton<OpportunityScanner>();
builder.Services.AddSingleton<ExecutionPlanner>();
builder.Services.AddSingleton<BacktestService>();
builder.Services.AddSingleton<RunControlService>();
builder.Services.AddSingleton<RunTelemetryService>();
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddScoped<DashboardRunService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

public sealed class DashboardRunService : IAsyncDisposable
{
    private readonly RunCoordinator _coordinator;
    private readonly RunControlService _runControl;
    private readonly ILogger<DashboardRunService> _logger;
    private readonly Dictionary<string, RunProcess> _processes = new();
    private readonly object _sync = new();

    public DashboardRunService(RunCoordinator coordinator, RunControlService runControl, ILogger<DashboardRunService> logger)
    {
        _coordinator = coordinator;
        _runControl = runControl;
        _logger = logger;
    }

    public bool IsProcessRunning(string processId)
    {
        lock (_sync)
        {
            return _processes.TryGetValue(processId, out var process) && !process.Task.IsCompleted;
        }
    }

    public Task StartRunAsync(RunProcessRequest request, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_processes.TryGetValue(request.ProcessId, out var existing) && !existing.Task.IsCompleted)
            {
                throw new InvalidOperationException($"Process {request.ProcessId} is already running");
            }

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var task = Task.Run(async () =>
            {
                var context = new RunCoordinator.RunExecutionContext
                {
                    ProcessId = request.ProcessId,
                    Dex = request.Dex,
                    BaseCoin = request.BaseCoin,
                    QuoteCoin = request.QuoteCoin,
                    Network = request.Network
                };

                try
                {
                    switch (request.Mode)
                    {
                        case RunnerMode.OnDemand:
                            var onDemand = new RunCoordinator.OnDemandRunRequest
                            {
                                OpportunityPayload = request.Payload,
                                ExecuteOnOptimism = request.ExecuteOnOptimism
                            };
                            await _coordinator.RunOnDemandAsync(onDemand, context, linkedCts.Token);
                            break;
                        case RunnerMode.Loop:
                            await _coordinator.RunLoopAsync(linkedCts.Token, context);
                            break;
                        case RunnerMode.Backtest:
                            await _coordinator.RunBacktestAsync(linkedCts.Token, context);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(request.Mode), request.Mode, null);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Process {ProcessId} cancelled", request.ProcessId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Process {ProcessId} terminated unexpectedly", request.ProcessId);
                }
                finally
                {
                    _runControl.EndRun(request.ProcessId);

                    lock (_sync)
                    {
                        _processes.Remove(request.ProcessId);
                    }
                }
            }, linkedCts.Token);

            _processes[request.ProcessId] = new RunProcess(request.ProcessId, request.Mode, linkedCts, task);
            return Task.CompletedTask;
        }
    }

    public void CancelProcess(string processId)
    {
        RunProcess? process;
        lock (_sync)
        {
            _processes.TryGetValue(processId, out process);
        }

        if (process is null)
        {
            return;
        }

        process.Cancellation.Cancel();
        _runControl.CancelRun(processId);
    }

    public void CancelAll()
    {
        List<RunProcess> snapshot;
        lock (_sync)
        {
            snapshot = _processes.Values.ToList();
        }

        foreach (var process in snapshot)
        {
            process.Cancellation.Cancel();
        }

        _runControl.CancelAll();
    }

    public ValueTask DisposeAsync()
    {
        CancelAll();

        lock (_sync)
        {
            foreach (var process in _processes.Values)
            {
                process.Cancellation.Dispose();
            }

            _processes.Clear();
        }

        return ValueTask.CompletedTask;
    }

    private sealed record RunProcess(string ProcessId, RunnerMode Mode, CancellationTokenSource Cancellation, Task Task);
}

public sealed record RunProcessRequest
{
    public string ProcessId { get; init; } = Guid.NewGuid().ToString("N");
    public RunnerMode Mode { get; init; }
    public string? Payload { get; init; }
    public bool ExecuteOnOptimism { get; init; }
    public string Dex { get; init; } = "Uniswap";
    public string BaseCoin { get; init; } = "USDC";
    public string QuoteCoin { get; init; } = "USDT";
    public string Network { get; init; } = "Optimism";
}
