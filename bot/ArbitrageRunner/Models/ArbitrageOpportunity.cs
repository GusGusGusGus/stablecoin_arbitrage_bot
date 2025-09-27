using System.Numerics;

namespace ArbitrageRunner.Models;

public sealed record ArbitrageOpportunity
{
    public required string OpportunityId { get; init; }
    public required string BorrowAsset { get; init; }
    public required BigInteger BorrowAmount { get; init; }
    public required BigInteger MinimumProfit { get; init; }
    public required string[] RouteTargets { get; init; }
    public required byte[][] Calldata { get; init; }
    public decimal EstimatedProfitUsd { get; init; }
    public decimal EstimatedGasUsd { get; init; }
    public decimal EstimatedL1DataUsd { get; init; }
    public decimal EstimatedFlashLoanFeeUsd { get; init; }
    public uint EstimatedGasUnits { get; init; }
    public uint FlashLoanFeeBps { get; init; } = 9;
    public bool ExecuteOnOptimism { get; init; }
    public BigInteger BaseFeeUpperBoundWei { get; init; } = BigInteger.Zero;
    public ulong Deadline { get; init; }
}