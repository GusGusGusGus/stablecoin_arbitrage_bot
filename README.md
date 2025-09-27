# Solidity Stablecoin Arbitrage Bot

This repository contains:
- A Solidity smart contract that executes flash-loan powered arbitrage across Uniswap, Balancer, and other EVM-compatible venues while enforcing strict safety checks.
- A Hardhat test-suite and deployment scripts for auditing and validating the contract.
- A .NET 8 console orchestrator that discovers opportunities, performs profitability checks including gas / congestion analysis, persists opportunities to SQLite for backtesting, and triggers on-chain execution in loop, on-demand, or replay modes.

> ?? Flash-loan arbitrage is risky. Thoroughly audit, simulate, and run on testnets before considering deployment on mainnet.

## Repository Layout

```
contracts/                 Solidity contracts
interfaces/                External protocol interfaces (Aave, Uniswap, Balancer)
scripts/                   Hardhat deployment & maintenance scripts
test/                      Hardhat test cases
bot/ArbitrageRunner/       .NET 8 orchestrator source
config/                    Shared configuration templates (RPCs, addresses, heuristics)
data/                      SQLite persistence for historical opportunities
```

## Prerequisites

- Node.js 18+
- pnpm / yarn / npm (your choice)
- Hardhat toolchain (`npm install` in this repo)
- .NET 8 SDK for the orchestrator (`dotnet --list-sdks`)

## Getting Started

1. Install JS dependencies:
   ```bash
   npm install
   ```
2. Copy `config/hardhat.example.env` to `.env` and populate with RPC URLs & private keys for testnets.
3. Copy `config/appsettings.example.json` to `config/appsettings.json` and update:
   - RPC endpoints per network.
   - Deployed `ArbitrageAddress`, Aave pool, and executor key (keep secrets out of source control).
   - Per-network risk profiles, fee multipliers, and price-feed pool IDs.
4. Compile & test the contracts:
   ```bash
   npx hardhat compile
   npx hardhat test
   ```
5. Build and run the .NET arbitrage loop:
   ```bash
   dotnet build bot/ArbitrageRunner/ArbitrageRunner.csproj
   dotnet run --project bot/ArbitrageRunner/ArbitrageRunner.csproj --mode loop
   ```

## Modes of Operation

- **Loop**: continuously scans exchanges, applies congestion thresholds from the gas oracle, and triggers bundles when profit clears risk buffers.
- **On-demand**: execute a single arbitrage either by supplying a JSON payload via `--opportunityPayload` (base64 calldata, targets, etc.) or by requesting an immediate live scan.
- **Backtest**: replays opportunities stored in the SQLite snapshot database for dry-run evaluation.

## Security Considerations

- Restrictive access control (role based, pausable, flash-loan guardian).
- Strict invariant checks on minimum profit, slippage caps, and gas premium safety margins.
- Built-in emergency stop & withdrawal for operators.
- Flash loan callback sanity-checks to avoid griefing and reentrancy.
- Requires external monitoring to ensure MEV protection (bundle submissions, Optimism RPC routing).

Before mainnet deployment:
- Commission independent audits.
- Use forked network simulations (Hardhat mainnet fork).
- Integrate with private transaction relayers (Flashbots, Eden, or Optimism equivalents).
- Maintain up-to-date protocol ABIs and price-feed mappings.

## Status

Core smart contract, Hardhat coverage, and .NET orchestrator with SQLite persistence, subgraph price feeds, gas oracle, and configurable execution planner.
Next steps include production-grade monitoring, strategist UI, and richer opportunity generation.