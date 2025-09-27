using System;
using System.Collections.Generic;

namespace ArbitrageRunner.Models;

public sealed record AppConfig
{
    public required NetworkConfig Networks { get; init; }
    public required ContractConfig Contract { get; init; }
    public required RiskConfig Risk { get; init; }
    public PriceFeedsConfig PriceFeeds { get; init; } = new();
    public HistoricalDataConfig HistoricalData { get; init; } = new();
    public FeeConfig Fees { get; init; } = new();

    public static AppConfig Default() => new()
    {
        Networks = new NetworkConfig(),
        Contract = new ContractConfig(),
        Risk = RiskConfig.Default(),
        PriceFeeds = new PriceFeedsConfig(),
        HistoricalData = new HistoricalDataConfig(),
        Fees = new FeeConfig()
    };
}

public sealed record NetworkConfig
{
    public string MainnetRpc { get; init; } = "https://mainnet.infura.io/v3/KEY";
    public string OptimismRpc { get; init; } = "https://mainnet.optimism.io";
    public string FlashbotsRelay { get; init; } = string.Empty;
}

public sealed record ContractConfig
{
    public string ArbitrageAddress { get; init; } = "0x0000000000000000000000000000000000000000";
    public string ExecutorKey { get; init; } = string.Empty;
    public string AavePool { get; init; } = "0x0000000000000000000000000000000000000000";
    public string[] WhitelistedAssets { get; init; } = Array.Empty<string>();
}

public sealed record RiskConfig
{
    public RiskProfile Mainnet { get; init; } = RiskProfile.Create(25m, 6m, 20m);
    public RiskProfile Optimism { get; init; } = RiskProfile.Create(20m, 3m, 15m);
    public decimal RelayerFeeUsd { get; init; } = 0.5m;
    public decimal MevProtectionFeeUsd { get; init; } = 0.0m;
    public decimal PriorityFeeMultiplier { get; init; } = 1.2m;
    public decimal BaseFeeSafetyMultiplier { get; init; } = 1.3m;
    public decimal SandwichBufferBps { get; init; } = 5m;
    public ulong LoopBackoffSeconds { get; init; } = 4;
    // Optional: per-asset absolute borrow caps in raw token units (wei for 18d, 6d for USDC, etc.)
    // Key: token address (any case); Value: numeric string representing max amount in token's smallest unit.
    public Dictionary<string, string> MaxBorrowByAsset { get; init; } = new();

    public static RiskConfig Default() => new();
}

public sealed record FeeConfig
{
    public bool Enabled { get; init; }
    public decimal Percentage { get; init; } = 5m;
    public string RevenueAddress { get; init; } = string.Empty;
}

public sealed record RiskProfile
{
    public decimal MinimumProfitUsd { get; init; }
    public decimal MaxExecutionUsd { get; init; }
    public decimal MaxSlippageBps { get; init; }

    public static RiskProfile Create(decimal minProfit, decimal maxExecution, decimal slippageBps) => new()
    {
        MinimumProfitUsd = minProfit,
        MaxExecutionUsd = maxExecution,
        MaxSlippageBps = slippageBps
    };
}

public sealed record PriceFeedsConfig
{
    public UniswapPriceFeedConfig UniswapV3 { get; init; } = new();
    public BalancerPriceFeedConfig Balancer { get; init; } = new();
    public Dictionary<string, decimal> ManualQuotes { get; init; } = new();
}

public sealed record UniswapPriceFeedConfig
{
    public string Endpoint { get; init; } = string.Empty;
    public Dictionary<string, string> Pools { get; init; } = new();
}

public sealed record BalancerPriceFeedConfig
{
    public string Endpoint { get; init; } = string.Empty;
    public Dictionary<string, string> Pools { get; init; } = new();
}

public sealed record HistoricalDataConfig
{
    public string DatabasePath { get; init; } = "data/backtests.sqlite";
    public bool AutoMigrate { get; init; } = true;
}
