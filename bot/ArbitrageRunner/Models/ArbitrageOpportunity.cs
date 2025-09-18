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
    public ulong Deadline { get; init; }
}