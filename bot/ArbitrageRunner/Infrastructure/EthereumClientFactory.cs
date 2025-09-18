using ArbitrageRunner.Models;
using Nethereum.Web3;

namespace ArbitrageRunner.Infrastructure;

public sealed class EthereumClientFactory
{
    private readonly AppConfig _config;

    public EthereumClientFactory(AppConfig config)
    {
        _config = config;
    }

    public Web3 CreateMainnetClient()
    {
        return CreateClient(_config.Networks.MainnetRpc);
    }

    public Web3 CreateOptimismClient()
    {
        return CreateClient(_config.Networks.OptimismRpc);
    }

    public Web3 CreateClient(string rpcUrl)
    {
        if (string.IsNullOrWhiteSpace(rpcUrl))
        {
            throw new ArgumentException("RPC URL cannot be empty", nameof(rpcUrl));
        }

        var web3 = new Web3(rpcUrl);
        return web3;
    }
}