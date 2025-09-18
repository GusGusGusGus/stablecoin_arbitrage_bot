# Solidity Stablecoin Arbitrage Bot

This repository contains:
- A Solidity smart contract that executes flash-loan powered arbitrage across Uniswap, Balancer, and other EVM-compatible venues while enforcing strict safety checks.
- A Hardhat test-suite and deployment scripts for auditing and validating the contract.
- A .NET console orchestrator that discovers opportunities, performs profitability checks including gas / congestion analysis, and triggers on-chain execution in loop, on-demand, or back-test modes.

> ?? Flash-loan arbitrage is risky. Thoroughly audit, simulate, and run on testnets before considering deployment on mainnet.

## Repository Layout

```
contracts/                 Solidity contracts
interfaces/                External protocol interfaces (Aave, Uniswap, Balancer)
scripts/                   Hardhat deployment & maintenance scripts
test/                      Hardhat test cases
bot/ArbitrageRunner/       .NET 8 orchestrator source
config/                    Shared configuration templates (RPCs, addresses, heuristics)
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
3. Compile & test the contracts:
   ```bash
   npx hardhat compile
   npx hardhat test
   ```
4. Build and run the .NET arbitrage loop:
   ```bash
   dotnet build bot/ArbitrageRunner/ArbitrageRunner.csproj
   dotnet run --project bot/ArbitrageRunner/ArbitrageRunner.csproj --mode loop
   ```

## Modes of Operation

- **Loop**: continuously scans exchanges, applies congestion thresholds, and triggers optimistically routed bundles.
- **On-demand**: single-shot run for a given opportunity ID, pair, or calldata payload.
- **Backtesting**: replays historical price snapshots (Uniswap TWAPs, Balancer spot balances) saved locally.

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
- Maintain up-to-date protocol ABIs and addresses.

## Status

Initial scaffolding with core contract, tests, and orchestrator skeleton.
Future work includes production-grade monitoring, strategist UI, and robust simulation coverage.