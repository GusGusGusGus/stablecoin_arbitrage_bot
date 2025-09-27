using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArbitrageRunner.Infrastructure;
using ArbitrageRunner.Models;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace ArbitrageRunner.Services;

public sealed class FlashLoanService
{
    private readonly EthereumClientFactory _clientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<FlashLoanService> _logger;

    public FlashLoanService(
        EthereumClientFactory clientFactory,
        AppConfig config,
        ILogger<FlashLoanService> logger)
    {
        _clientFactory = clientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(
        ArbitrageOpportunity opportunity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.Contract.ExecutorKey))
        {
            throw new InvalidOperationException("Executor key is not configured");
        }

        if (string.IsNullOrWhiteSpace(_config.Contract.ArbitrageAddress) ||
            _config.Contract.ArbitrageAddress == "0x0000000000000000000000000000000000000000")
        {
            throw new InvalidOperationException("Arbitrage contract address missing");
        }

        var account = new Account(_config.Contract.ExecutorKey);
        var web3 = opportunity.ExecuteOnOptimism
            ? _clientFactory.CreateOptimismClient(account)
            : _clientFactory.CreateMainnetClient(account);

        var handler = web3.Eth.GetContractTransactionHandler<ExecuteFlashArbitrageFunction>();
        var call = BuildTransactionMessage(opportunity, account.Address);

        var (feeEnabled, feeAmount, feePercent) = CalculatePlannedFee(opportunity.BorrowAmount);
        if (feeEnabled)
        {
            if (string.IsNullOrWhiteSpace(_config.Fees.RevenueAddress))
            {
                throw new InvalidOperationException("App fee revenue address must be configured when fees are enabled");
            }

            _logger.LogInformation(
                "App fee scheduled at {Percent:F2}% => {FeeAmount} units to {Recipient}",
                feePercent,
                feeAmount,
                _config.Fees.RevenueAddress);
        }

        try
        {
            await handler.EstimateGasAsync(_config.Contract.ArbitrageAddress, call);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gas estimation failed for opportunity payload; proceeding may revert");
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        _logger.LogInformation("Submitting arbitrage execution for opportunity {Opportunity}", opportunity.OpportunityId);
        var receipt = await handler.SendRequestAndWaitForReceiptAsync(
            _config.Contract.ArbitrageAddress,
            call,
            cancellationToken);

        _logger.LogInformation("Arbitrage transaction mined: {TxHash}", receipt.TransactionHash);
        return receipt.TransactionHash;
    }

    private (bool Enabled, BigInteger Amount, decimal Percentage) CalculatePlannedFee(BigInteger borrowAmount)
    {
        if (!_config.Fees.Enabled)
        {
            return (false, BigInteger.Zero, _config.Fees.Percentage);
        }

        if (_config.Fees.Percentage <= 0)
        {
            throw new InvalidOperationException("Fee percentage must be positive when fees are enabled");
        }

        var feeBps = (int)Math.Round(_config.Fees.Percentage * 100m, MidpointRounding.AwayFromZero);
        if (feeBps <= 0)
        {
            throw new InvalidOperationException("Fee basis points rounds to zero; adjust percentage");
        }

        var amount = (borrowAmount * feeBps) / 10_000;
        return (true, amount, _config.Fees.Percentage);
    }

    private ExecuteFlashArbitrageFunction BuildTransactionMessage(ArbitrageOpportunity opportunity, string payout)
    {
        var message = new ExecuteFlashArbitrageFunction
        {
            FromAddress = payout,
            Asset = opportunity.BorrowAsset,
            Amount = opportunity.BorrowAmount,
            MinProfit = opportunity.MinimumProfit,
            BaseFeeUpperBound = opportunity.BaseFeeUpperBoundWei == BigInteger.Zero
                ? BigInteger.Parse("100000000000")
                : opportunity.BaseFeeUpperBoundWei,
            Deadline = opportunity.Deadline,
            Payout = payout
        };

        for (var i = 0; i < opportunity.RouteTargets.Length; i++)
        {
            message.Trades.Add(new TradeInstruction
            {
                Target = opportunity.RouteTargets[i],
                Data = opportunity.Calldata[i]
            });
        }

        return message;
    }

    [Function("executeFlashArbitrage")]
    public sealed class ExecuteFlashArbitrageFunction : FunctionMessage
    {
        [Parameter("address", "asset", 1)]
        public string Asset { get; set; } = string.Empty;

        [Parameter("uint256", "amount", 2)]
        public BigInteger Amount { get; set; }

        [Parameter("tuple[]", "trades", 3)]
        public List<TradeInstruction> Trades { get; set; } = new();

        [Parameter("uint256", "minProfit", 4)]
        public BigInteger MinProfit { get; set; }

        [Parameter("uint256", "baseFeeUpperBound", 5)]
        public BigInteger BaseFeeUpperBound { get; set; }

        [Parameter("uint256", "deadline", 6)]
        public ulong Deadline { get; set; }

        [Parameter("address", "payout", 7)]
        public string Payout { get; set; } = string.Empty;
    }

    [Struct("TradeInstruction")]
    public sealed class TradeInstruction
    {
        [Parameter("address", "target", 1)]
        public string Target { get; set; } = string.Empty;

        [Parameter("bytes", "data", 2)]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}
