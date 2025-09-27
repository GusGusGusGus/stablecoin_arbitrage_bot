using System;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;

namespace ArbitrageRunner.Services;

public sealed class RunCoordinator
{
    private readonly OpportunityScanner _scanner;
    private readonly ExecutionPlanner _planner;
    private readonly FlashLoanService _flashLoan;
    private readonly BacktestService _backtest;
    private readonly RunControlService _runControl;
    private readonly RunTelemetryService _telemetry;
    private readonly AppConfig _config;
    private readonly ILogger<RunCoordinator> _logger;

    public RunCoordinator(
        OpportunityScanner scanner,
        ExecutionPlanner planner,
        FlashLoanService flashLoan,
        BacktestService backtest,
        RunControlService runControl,
        RunTelemetryService telemetry,
        AppConfig config,
        ILogger<RunCoordinator> logger)
    {
        _scanner = scanner;
        _planner = planner;
        _flashLoan = flashLoan;
        _backtest = backtest;
        _runControl = runControl;
        _telemetry = telemetry;
        _config = config;
        _logger = logger;
    }

    public async Task RunLoopAsync(CancellationToken cancellationToken, RunExecutionContext? context = null)
    {
        var executionContext = context ?? RunExecutionContext.System;
        var delay = TimeSpan.FromSeconds(_config.Risk.LoopBackoffSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            RunTelemetryEntry? runEntry = null;

            try
            {
                using var runScope = new RunScope(_runControl, executionContext.ProcessId, cancellationToken);
                var runToken = runScope.Token;

                var opportunity = await _scanner.DetectAsync(runToken);
                if (opportunity is null)
                {
                    runScope.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var validated = await _planner.ValidateAsync(opportunity, runToken);
                if (validated is null)
                {
                    runScope.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                runEntry = _telemetry.RecordRunScheduled(RunnerMode.Loop, validated, executionContext.ToTelemetryMetadata());
                _telemetry.MarkRunExecuting(runEntry.RunId);

                var txHash = await _flashLoan.ExecuteAsync(validated, runToken);
                _telemetry.MarkRunSucceeded(runEntry.RunId, txHash);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (runEntry is not null)
                {
                    _telemetry.MarkRunCancelled(runEntry.RunId);
                }

                _logger.LogInformation("Current run cancelled by operator");
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (runEntry is not null)
                {
                    _telemetry.MarkRunFailed(runEntry.RunId, ex.Message);
                }

                _logger.LogError(ex, "Loop execution error");
                await Task.Delay(delay, cancellationToken);
            }
            finally
            {
                runEntry = null;
            }
        }
    }

    public async Task RunOnDemandAsync(OnDemandRunRequest request, RunExecutionContext? context, CancellationToken cancellationToken)
    {
        var executionContext = context ?? RunExecutionContext.System;

        using var runScope = new RunScope(_runControl, executionContext.ProcessId, cancellationToken);
        var runToken = runScope.Token;

        ArbitrageOpportunity? opportunity = request.Opportunity;

        if (opportunity is null)
        {
            if (!string.IsNullOrWhiteSpace(request.OpportunityPayload))
            {
                opportunity = ParseOpportunityPayload(request.OpportunityPayload!, request.ExecuteOnOptimism);
            }
            else
            {
                opportunity = await _scanner.DetectAsync(runToken);
                if (opportunity is null)
                {
                    _logger.LogWarning("No live opportunity detected for on-demand execution");
                    return;
                }
            }
        }

        var validated = await _planner.ValidateAsync(opportunity, runToken);
        if (validated is null)
        {
            _logger.LogWarning("On-demand opportunity {OpportunityId} failed validation", opportunity.OpportunityId);
            return;
        }

        var runEntry = _telemetry.RecordRunScheduled(RunnerMode.OnDemand, validated, executionContext.ToTelemetryMetadata());
        _telemetry.MarkRunExecuting(runEntry.RunId);

        try
        {
            var txHash = await _flashLoan.ExecuteAsync(validated, runToken);
            _telemetry.MarkRunSucceeded(runEntry.RunId, txHash);
        }
        catch (OperationCanceledException)
        {
            _telemetry.MarkRunCancelled(runEntry.RunId);
            throw;
        }
        catch (Exception ex)
        {
            _telemetry.MarkRunFailed(runEntry.RunId, ex.Message);
            throw;
        }
    }

    public async Task RunBacktestAsync(CancellationToken cancellationToken, RunExecutionContext? context = null)
    {
        var executionContext = context ?? RunExecutionContext.System;

        using var runScope = new RunScope(_runControl, executionContext.ProcessId, cancellationToken);
        var runToken = runScope.Token;

        var snapshots = await _backtest.LoadRecentAsync(limit: null, runToken);
        foreach (var snapshot in snapshots)
        {
            runToken.ThrowIfCancellationRequested();

            var validated = await _planner.ValidateAsync(snapshot, runToken);
            if (validated is null)
            {
                _logger.LogInformation("Skipping snapshot {OpportunityId}", snapshot.OpportunityId);
                continue;
            }

            _logger.LogInformation("Backtest would execute opportunity {OpportunityId}", snapshot.OpportunityId);
        }
    }

    public ArbitrageOpportunity ParseOpportunityPayload(string payload, bool executeOnOptimism)
    {
        return ArbitrageWorker.ParseOpportunityPayload(payload, executeOnOptimism);
    }

    public sealed record OnDemandRunRequest
    {
        public ArbitrageOpportunity? Opportunity { get; init; }
        public string? OpportunityPayload { get; init; }
        public bool ExecuteOnOptimism { get; init; }
    }

    private sealed class RunScope : IDisposable
    {
        private readonly RunControlService _control;
        private readonly string _key;
        private bool _disposed;

        public RunScope(RunControlService control, string key, CancellationToken parentToken)
        {
            _control = control;
            _key = key;
            Token = control.BeginRun(key, parentToken);
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _control.EndRun(_key);
            _disposed = true;
        }
    }

    public sealed record RunExecutionContext
    {
        public string ProcessId { get; init; } = RunControlService.DefaultKey;
        public string Dex { get; init; } = "Auto";
        public string BaseCoin { get; init; } = "USDC";
        public string QuoteCoin { get; init; } = string.Empty;
        public string Network { get; init; } = "Mainnet";

        public RunTelemetryMetadata ToTelemetryMetadata() => new()
        {
            ProcessId = ProcessId,
            Dex = Dex,
            BaseCoin = BaseCoin,
            QuoteCoin = QuoteCoin,
            Network = Network
        };

        public static RunExecutionContext System { get; } = new()
        {
            ProcessId = RunControlService.DefaultKey,
            Dex = "Auto",
            BaseCoin = "USDC",
            QuoteCoin = string.Empty,
            Network = "Mainnet"
        };
    }
}
