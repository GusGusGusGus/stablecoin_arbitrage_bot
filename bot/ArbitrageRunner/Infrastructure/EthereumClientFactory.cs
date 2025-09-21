using System;
using ArbitrageRunner.Models;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace ArbitrageRunner.Infrastructure;

public sealed class EthereumClientFactory
{
    private readonly AppConfig _config;

    public EthereumClientFactory(AppConfig config)
    {
        _config = config;
    }

    public Web3 CreateMainnetClient(Account? account = null)
    {
        return CreateClient(_config.Networks.MainnetRpc, account);
    }

    public Web3 CreateOptimismClient(Account? account = null)
    {
        return CreateClient(_config.Networks.OptimismRpc, account);
    }

    public Web3 CreateClient(string rpcUrl, Account? account = null)
    {
        if (string.IsNullOrWhiteSpace(rpcUrl))
        {
            throw new ArgumentException("RPC URL cannot be empty", nameof(rpcUrl));
        }

        return account is null
            ? new Web3(rpcUrl)
            : new Web3(account, rpcUrl);
    }
}
