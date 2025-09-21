using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArbitrageRunner.Services;

public sealed class ArbitrageWorker : BackgroundService
{
    private readonly RunnerOptions _options;
    private readonly OpportunityScanner _scanner;
    private readonly ExecutionPlanner _planner;
    private readonly FlashLoanService _flashLoan;
    private readonly BacktestService _backtest;
    private readonly AppConfig _config;
    private readonly ILogger<ArbitrageWorker> _logger;

    public ArbitrageWorker(
        RunnerOptions options,
        OpportunityScanner scanner,
        ExecutionPlanner planner,
        FlashLoanService flashLoan,
        BacktestService backtest,
        AppConfig config,
        ILogger<ArbitrageWorker> logger)
    {
        _options = options;
        _scanner = scanner;
        _planner = planner;
        _flashLoan = flashLoan;
        _backtest = backtest;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Arbitrage worker started in {Mode} mode", _options.Mode);

        switch (_options.Mode)
        {
            case RunnerMode.Loop:
                await RunLoopAsync(stoppingToken);
                break;
            case RunnerMode.OnDemand:
                await RunOnDemandAsync(stoppingToken);
                break;
            case RunnerMode.Backtest:
                await RunBacktestAsync(stoppingToken);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var opportunity = await _scanner.DetectAsync(cancellationToken);
                if (opportunity is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.Risk.BackoffSeconds), cancellationToken);
                    continue;
                }

                var validated = await _planner.ValidateAsync(opportunity, cancellationToken);
                if (validated is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.Risk.BackoffSeconds), cancellationToken);
                    continue;
                }

                await _flashLoan.ExecuteAsync(validated, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loop execution error");
                await Task.Delay(TimeSpan.FromSeconds(_config.Risk.BackoffSeconds), cancellationToken);
            }
        }
    }

    private async Task RunOnDemandAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.OpportunityId))
        {
            _logger.LogWarning("No opportunity id supplied for on-demand mode");
            return;
        }

        var snapshot = (await _backtest.LoadByIdAsync(_options.OpportunityId, cancellationToken)).FirstOrDefault();
        if (snapshot is null)
        {
            _logger.LogWarning("Opportunity {OpportunityId} not found in snapshot store", _options.OpportunityId);
            return;
        }

        var validated = await _planner.ValidateAsync(snapshot, cancellationToken);
        if (validated is null)
        {
            _logger.LogWarning("Opportunity {OpportunityId} failed validation", snapshot.OpportunityId);
            return;
        }

        await _flashLoan.ExecuteAsync(validated, cancellationToken);
    }

    private async Task RunBacktestAsync(CancellationToken cancellationToken)
    {
        var snapshots = await _backtest.LoadRecentAsync(limit: null, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            var validated = await _planner.ValidateAsync(snapshot, cancellationToken);
            if (validated is null)
            {
                _logger.LogInformation("Skipping snapshot {OpportunityId}", snapshot.OpportunityId);
                continue;
            }

            _logger.LogInformation("Backtest would execute opportunity {OpportunityId}", snapshot.OpportunityId);
        }
    }
}