import { expect } from "chai";
import { ethers } from "hardhat";

describe("StablecoinArbitrage", () => {
  const DECIMALS = 1_000_000n; // 6 decimals
  const flashAmount = 1_000n * DECIMALS; // 1,000 USDC
  const profitTarget = 30n * DECIMALS; // 30 USDC

  async function deployFixture() {
    const [deployer, executor, payout, revenue] = await ethers.getSigners();

    const MockPool = await ethers.getContractFactory("MockPool");
    const pool = await MockPool.deploy();

    const MockERC20 = await ethers.getContractFactory("MockERC20");
    const usdc = await MockERC20.deploy("MockUSDC", "mUSDC", 6);

    const StablecoinArbitrage = await ethers.getContractFactory("StablecoinArbitrage");
    const arbitrage = await StablecoinArbitrage.deploy(await pool.getAddress(), deployer.address, deployer.address);

    const MockRouter = await ethers.getContractFactory("MockRouter");
    const router = await MockRouter.deploy(await usdc.getAddress());

    const premiumBps = await pool.premiumBps();
    const premium = (flashAmount * premiumBps) / 10_000n;

    // Fund pool liquidity
    await usdc.mint(deployer.address, 20_000n * DECIMALS);
    await usdc.connect(deployer).approve(await pool.getAddress(), flashAmount);
    await pool.fund(await usdc.getAddress(), flashAmount);

    // Provide router with profit + repayment liquidity
    await usdc.mint(await router.getAddress(), flashAmount + profitTarget + premium);

    // Role and configuration setup
    await arbitrage.grantRole(await arbitrage.EXECUTOR_ROLE(), executor.address);
    await arbitrage.setApprovedAsset(await usdc.getAddress(), true);
    await arbitrage.setApprovedTarget(await router.getAddress(), true);

    const selector = router.interface.getFunction("swapExact")!.selector;
    await arbitrage.setAllowedSelector(selector, true);
    await arbitrage.approveSpender(await usdc.getAddress(), await router.getAddress(), flashAmount);

    return { pool, arbitrage, usdc, router, deployer, executor, payout, premium, revenue };
  }

  it("executes a profitable flash loan arbitrage plan", async () => {
    const { arbitrage, executor, payout, usdc, router, premium } = await deployFixture();

    const encodedSwap = router.interface.encodeFunctionData("swapExact", [flashAmount, flashAmount + profitTarget + premium]);
    const trades = [{ target: await router.getAddress(), data: encodedSwap }];
    const deadline = (await ethers.provider.getBlock("latest"))!.timestamp + 3600;

    await expect(
      arbitrage
        .connect(executor)
        .executeFlashArbitrage(
          await usdc.getAddress(),
          flashAmount,
          trades,
          profitTarget,
          1_000_000_000n,
          deadline,
          payout.address
        )
    ).to.emit(arbitrage, "FlashArbitrageExecuted");

    const payoutBalance = await usdc.balanceOf(payout.address);
    expect(payoutBalance).to.equal(profitTarget);

    const contractResidual = await usdc.balanceOf(await arbitrage.getAddress());
    expect(contractResidual).to.equal(0n);
  });

  it("reverts when minimum profit target is not achieved", async () => {
    const { arbitrage, executor, payout, usdc, router, premium } = await deployFixture();

    const encodedSwap = router.interface.encodeFunctionData("swapExact", [flashAmount, flashAmount + premium]);
    const trades = [{ target: await router.getAddress(), data: encodedSwap }];
    const deadline = (await ethers.provider.getBlock("latest"))!.timestamp + 3600;

    await expect(
      arbitrage
        .connect(executor)
        .executeFlashArbitrage(
          await usdc.getAddress(),
          flashAmount,
          trades,
          profitTarget,
          1_000_000_000n,
          deadline,
          payout.address
        )
    ).to.be.revertedWithCustomError(arbitrage, "InsufficientProfit");
  });

  it("reverts when borrow amount exceeds the per-asset cap", async () => {
    const { arbitrage, executor, payout, usdc, router, premium } = await deployFixture();

    const cap = flashAmount - 1n;
    await arbitrage.setMaxBorrow(await usdc.getAddress(), cap);

    const encodedSwap = router.interface.encodeFunctionData("swapExact", [
      flashAmount,
      flashAmount + profitTarget + premium,
    ]);
    const trades = [{ target: await router.getAddress(), data: encodedSwap }];
    const deadline = (await ethers.provider.getBlock("latest"))!.timestamp + 3600;

    await expect(
      arbitrage
        .connect(executor)
        .executeFlashArbitrage(
          await usdc.getAddress(),
          flashAmount,
          trades,
          profitTarget,
          1_000_000_000n,
          deadline,
          payout.address
        )
    )
      .to.be.revertedWithCustomError(arbitrage, "BorrowAmountTooHigh")
      .withArgs(flashAmount, cap);
  });
  it("collects the app fee when enabled", async () => {
    const { arbitrage, executor, payout, usdc, router, premium, revenue } = await deployFixture();

    const feeBps = 500n;
    const appFee = (flashAmount * feeBps) / 10_000n;

    await arbitrage.setFeeSettings(true, Number(feeBps), revenue.address);

    // Ensure router holds enough liquidity to cover premium, profit and fee.
    await usdc.mint(await router.getAddress(), appFee);

    const encodedSwap = router.interface.encodeFunctionData("swapExact", [
      flashAmount,
      flashAmount + profitTarget + premium + appFee,
    ]);
    const trades = [{ target: await router.getAddress(), data: encodedSwap }];
    const deadline = (await ethers.provider.getBlock("latest"))!.timestamp + 3600;

    await expect(
      arbitrage
        .connect(executor)
        .executeFlashArbitrage(
          await usdc.getAddress(),
          flashAmount,
          trades,
          profitTarget,
          1_000_000_000n,
          deadline,
          payout.address
        )
    ).to.emit(arbitrage, "FlashArbitrageExecuted");

    expect(await usdc.balanceOf(revenue.address)).to.equal(appFee);
    expect(await usdc.balanceOf(payout.address)).to.equal(profitTarget);
  });
});
