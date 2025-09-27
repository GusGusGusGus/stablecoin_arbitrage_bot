import { ethers } from "hardhat";
import * as fs from "fs";
import * as path from "path";

type Config = {
  approvedAssets?: string[];
  approvedTargets?: string[];
  allowedSelectors?: string[];
};

type AppSettings = {
  Arbitrage?: {
    Contract?: {
      ArbitrageAddress?: string;
    };
  };
};

async function main() {
  const [deployer] = await ethers.getSigners();
  const poolAddress = process.env.AAVE_POOL_ADDRESS;
  const treasuryAddress = process.env.TREASURY_ADDRESS ?? deployer.address;

  if (!poolAddress) {
    throw new Error("AAVE_POOL_ADDRESS env var must be set");
  }

  console.info("\n==== Stablecoin Arbitrage Deployment ====");
  console.info("Network:", (await ethers.provider.getNetwork()).name);
  console.info("Deployer:", deployer.address);
  console.info("Treasury:", treasuryAddress);

  const Arbitrage = await ethers.getContractFactory("StablecoinArbitrage");
  const instance = await Arbitrage.deploy(poolAddress, deployer.address, treasuryAddress);
  await instance.waitForDeployment();
  const address = await instance.getAddress();

  console.info("Contract deployed at:", address);

  const configPath = process.env.ARB_CONFIG_PATH ?? path.join("config", "deployment.json");
  if (fs.existsSync(configPath)) {
    const raw = fs.readFileSync(configPath, "utf8");
    const config: Config = JSON.parse(raw);

    if (config.approvedAssets) {
      for (const asset of config.approvedAssets) {
        const tx = await instance.setApprovedAsset(asset, true);
        await tx.wait();
        console.info("Approved asset", asset);
      }
    }

    if (config.approvedTargets) {
      for (const target of config.approvedTargets) {
        const tx = await instance.setApprovedTarget(target, true);
        await tx.wait();
        console.info("Approved target", target);
      }
    }

    if (config.allowedSelectors) {
      for (const selector of config.allowedSelectors) {
        const tx = await instance.setAllowedSelector(selector as `0x${string}`, true);
        await tx.wait();
        console.info("Allowed selector", selector);
      }
    }
  } else {
    console.warn("No deployment config found at", configPath);
  }

  persistAddressArtifacts(address);

  console.info("Deployment complete.\n");
}

function persistAddressArtifacts(address: string) {
  const outputPath = process.env.ARB_DEPLOYMENT_OUTPUT ?? path.join("config", "deployment.latest.json");
  fs.writeFileSync(
    outputPath,
    JSON.stringify({ arbitrage: { address, deployedAt: new Date().toISOString() } }, null, 2),
    "utf8"
  );
  console.info("Wrote deployment artifact to", outputPath);

  const appSettingsPath = process.env.APPSETTINGS_PATH ?? path.join("config", "appsettings.json");
  if (!fs.existsSync(appSettingsPath)) {
    console.warn("Appsettings file not found, skipping automatic contract address injection");
    return;
  }

  try {
    const raw = fs.readFileSync(appSettingsPath, "utf8");
    const settings: AppSettings = JSON.parse(raw);
    settings.Arbitrage = settings.Arbitrage ?? {};
    settings.Arbitrage.Contract = settings.Arbitrage.Contract ?? {};
    settings.Arbitrage.Contract.ArbitrageAddress = address;
    fs.writeFileSync(appSettingsPath, JSON.stringify(settings, null, 2), "utf8");
    console.info("Updated", appSettingsPath, "with new arbitrage address");
  } catch (err) {
    console.warn("Failed to update appsettings with new address", err);
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});