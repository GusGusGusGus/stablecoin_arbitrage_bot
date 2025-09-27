using System;
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
    private readonly RunCoordinator _coordinator;
    private readonly ILogger<ArbitrageWorker> _logger;

    public ArbitrageWorker(
        RunnerOptions options,
        RunCoordinator coordinator,
        ILogger<ArbitrageWorker> logger)
    {
        _options = options;
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Arbitrage worker started in {Mode} mode", _options.Mode);

        switch (_options.Mode)
        {
            case RunnerMode.Loop:
                await _coordinator.RunLoopAsync(stoppingToken, RunCoordinator.RunExecutionContext.System);
                break;
            case RunnerMode.OnDemand:
                await _coordinator.RunOnDemandAsync(new RunCoordinator.OnDemandRunRequest
                {
                    OpportunityPayload = _options.OpportunityPayload,
                    ExecuteOnOptimism = _options.ExecuteOnOptimism
                }, RunCoordinator.RunExecutionContext.System, stoppingToken);
                break;
            case RunnerMode.Backtest:
                await _coordinator.RunBacktestAsync(stoppingToken, RunCoordinator.RunExecutionContext.System);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    internal static ArbitrageOpportunity ParseOpportunityPayload(string payload, bool executeOnOptimism)
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
