namespace ArbitrageRunner.Models;

public sealed record AppConfig
{
    public required NetworkConfig Networks { get; init; }
    public required ContractConfig Contract { get; init; }
    public required RiskConfig Risk { get; init; }
    public HistoricalDataConfig HistoricalData { get; init; } = new();

    public static AppConfig Default() => new()
    {
        Networks = new NetworkConfig(),
        Contract = new ContractConfig(),
        Risk = new RiskConfig(),
        HistoricalData = new HistoricalDataConfig()
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
    public decimal MinimumProfitUsd { get; init; } = 25m;
    public decimal MaxGasUsd { get; init; } = 5m;
    public decimal MaxSlippageBps { get; init; } = 15m;
    public uint MaxConcurrentFlashLoans { get; init; } = 1;
    public ulong BackoffSeconds { get; init; } = 4;
}

public sealed record HistoricalDataConfig
{
    public string DatabasePath { get; init; } = "data/backtests.sqlite";
    public bool AutoMigrate { get; init; } = true;
}