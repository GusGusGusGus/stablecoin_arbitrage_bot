import { HardhatUserConfig } from "hardhat/config";
import "@nomicfoundation/hardhat-toolbox";
import * as dotenv from "dotenv";

dotenv.config({ path: process.env.HARDHAT_ENV ?? ".env" });

const {
  MAINNET_RPC_URL,
  OPTIMISM_RPC_URL,
  ARBITRAGE_OPERATOR_KEY,
  ETHERSCAN_API_KEY,
  OPTIMISMSCAN_API_KEY
} = process.env;

const accounts = ARBITRAGE_OPERATOR_KEY ? [ARBITRAGE_OPERATOR_KEY] : [];

const config: HardhatUserConfig = {
  solidity: {
    version: "0.8.24",
    settings: {
      optimizer: {
        enabled: true,
        runs: 200
      },
      viaIR: true
    }
  },
  networks: {
    hardhat: {
      forking: MAINNET_RPC_URL
        ? {
            url: MAINNET_RPC_URL,
            blockNumber: process.env.MAINNET_FORK_BLOCK
              ? parseInt(process.env.MAINNET_FORK_BLOCK, 10)
              : undefined
          }
        : undefined
    },
    mainnet: {
      url: MAINNET_RPC_URL || "https://mainnet.infura.io/v3/YOUR_KEY",
      accounts,
      chainId: 1
    },
    optimism: {
      url: OPTIMISM_RPC_URL || "https://mainnet.optimism.io",
      accounts,
      chainId: 10
    }
  },
  etherscan: {
    apiKey: {
      mainnet: ETHERSCAN_API_KEY || "",
      optimisticEthereum: OPTIMISMSCAN_API_KEY || ""
    }
  },
  paths: {
    sources: "contracts",
    tests: "test",
    cache: "cache",
    artifacts: "artifacts"
  }
};

export default config;