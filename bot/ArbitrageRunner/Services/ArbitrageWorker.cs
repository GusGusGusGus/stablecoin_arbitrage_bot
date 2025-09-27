using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
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
        var delay = TimeSpan.FromSeconds(_config.Risk.LoopBackoffSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var opportunity = await _scanner.DetectAsync(cancellationToken);
                if (opportunity is null)
                {
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var validated = await _planner.ValidateAsync(opportunity, cancellationToken);
                if (validated is null)
                {
                    await Task.Delay(delay, cancellationToken);
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
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task RunOnDemandAsync(CancellationToken cancellationToken)
    {
        ArbitrageOpportunity? opportunity = null;

        if (!string.IsNullOrWhiteSpace(_options.OpportunityPayload))
        {
            opportunity = ParseOpportunityPayload(_options.OpportunityPayload!, _options.ExecuteOnOptimism);
        }
        else
        {
            opportunity = await _scanner.DetectAsync(cancellationToken);
            if (opportunity is null)
            {
                _logger.LogWarning("No live opportunity detected for on-demand execution");
                return;
            }
        }

        var validated = await _planner.ValidateAsync(opportunity, cancellationToken);
        if (validated is null)
        {
            _logger.LogWarning("On-demand opportunity {OpportunityId} failed validation", opportunity.OpportunityId);
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

    private static ArbitrageOpportunity ParseOpportunityPayload(string payload, bool executeOnOptimism)
    {
        var dto = JsonSerializer.Deserialize<OpportunityPayloadDto>(payload);
        if (dto is null)
        {
            throw new InvalidOperationException("Unable to parse opportunity payload");
        }

        return new ArbitrageOpportunity
        {
            OpportunityId = dto.OpportunityId ?? Guid.NewGuid().ToString("N"),
            BorrowAsset = dto.BorrowAsset,
            BorrowAmount = BigInteger.Parse(dto.BorrowAmount ?? "0"),
            MinimumProfit = BigInteger.Parse(dto.MinimumProfit ?? "0"),
            RouteTargets = dto.RouteTargets ?? Array.Empty<string>(),
            Calldata = DecodeCalldata(dto.Calldata ?? Array.Empty<string>()),
            EstimatedProfitUsd = dto.EstimatedProfitUsd,
            EstimatedGasUsd = dto.EstimatedGasUsd,
            EstimatedL1DataUsd = dto.EstimatedL1DataUsd,
            EstimatedFlashLoanFeeUsd = dto.EstimatedFlashLoanFeeUsd,
            EstimatedGasUnits = dto.EstimatedGasUnits,
            FlashLoanFeeBps = dto.FlashLoanFeeBps,
            ExecuteOnOptimism = dto.ExecuteOnOptimism ?? executeOnOptimism,
            BaseFeeUpperBoundWei = string.IsNullOrWhiteSpace(dto.BaseFeeUpperBoundWei) ? BigInteger.Zero : BigInteger.Parse(dto.BaseFeeUpperBoundWei),
            Deadline = dto.Deadline
        };
    }

    private static byte[][] DecodeCalldata(IReadOnlyList<string> encoded)
    {
        var result = new byte[encoded.Count][];
        for (var i = 0; i < encoded.Count; i++)
        {
            result[i] = Convert.FromBase64String(encoded[i]);
        }

        return result;
    }

    private sealed record OpportunityPayloadDto
    {
        public string? OpportunityId { get; init; }
        public string BorrowAsset { get; init; } = string.Empty;
        public string? BorrowAmount { get; init; }
        public string? MinimumProfit { get; init; }
        public string[]? RouteTargets { get; init; }
        public string[]? Calldata { get; init; }
        public decimal EstimatedProfitUsd { get; init; }
        public decimal EstimatedGasUsd { get; init; }
        public decimal EstimatedL1DataUsd { get; init; }
        public decimal EstimatedFlashLoanFeeUsd { get; init; }
        public uint EstimatedGasUnits { get; init; }
        public uint FlashLoanFeeBps { get; init; } = 9;
        public bool? ExecuteOnOptimism { get; init; }
        public string? BaseFeeUpperBoundWei { get; init; }
        public ulong Deadline { get; init; }
    }
}