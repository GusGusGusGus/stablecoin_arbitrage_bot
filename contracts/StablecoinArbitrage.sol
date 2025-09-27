// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";

import {IFlashLoanSimpleReceiver} from "../interfaces/aave/IFlashLoanSimpleReceiver.sol";
import {IPool} from "../interfaces/aave/IPool.sol";

/**
 * @title StablecoinArbitrage
 * @notice Executes flash-loan assisted arbitrage across whitelisted DEX routers while enforcing
 *         strict access control, profitability guards, and base fee limits.
 */
contract StablecoinArbitrage is IFlashLoanSimpleReceiver, AccessControl, Pausable, ReentrancyGuard {
    using SafeERC20 for IERC20;

    bytes32 public constant EXECUTOR_ROLE = keccak256("EXECUTOR_ROLE");
    bytes32 public constant STRATEGIST_ROLE = keccak256("STRATEGIST_ROLE");
    bytes32 public constant GUARDIAN_ROLE = keccak256("GUARDIAN_ROLE");

    struct TradeInstruction {
        address target;
        bytes data;
    }

    struct FlashCallbackData {
        address initiator;
        address payout;
        address profitToken;
        uint256 minProfit;
        uint256 baseFeeUpperBound;
        uint256 deadline;
        TradeInstruction[] trades;
    }

    struct RuntimeContext {
        uint256 preBalance;
        address asset;
        bool active;
    }

    IPool public immutable aavePool;
    address public treasury;

    RuntimeContext private runtime;

    mapping(address => bool) public approvedAssets;
    mapping(address => bool) public approvedTargets;
    mapping(bytes4 => bool) public allowedSelectors;
    // Optional per-asset maximum borrow caps (0 means no cap)
    mapping(address => uint256) public maxBorrowByAsset;

    struct FeeSettings {
        bool enabled;
        uint256 feeBps;
        address recipient;
    }

    FeeSettings public feeSettings;

    event FlashArbitrageRequested(address indexed executor, address indexed asset, uint256 amount, uint256 minProfit);
    event FlashArbitrageExecuted(address indexed asset, uint256 grossProfit, uint256 premium, uint256 netProfit, address indexed payout);
    event TargetUpdated(address indexed target, bool allowed);
    event AssetUpdated(address indexed asset, bool allowed);
    event SelectorUpdated(bytes4 indexed selector, bool allowed);
    event TreasuryUpdated(address indexed newTreasury);
    event MaxBorrowUpdated(address indexed asset, uint256 cap);
    event FeeSettingsUpdated(bool enabled, uint256 feeBps, address indexed recipient);

    error InvalidTarget(address target);
    error InvalidSelector(bytes4 selector);
    error UnauthorizedCaller();
    error FlashLoanInFlight();
    error BaseFeeTooHigh(uint256 current, uint256 allowed);
    error DeadlineExpired(uint256 deadline, uint256 timestamp);
    error AssetNotApproved(address asset);
    error InsufficientProfit(uint256 expected, uint256 actual);
    error PoolMismatch(address sender);
    error BorrowAmountTooHigh(uint256 requested, uint256 maxAllowed);
    error InvalidFeeRecipient();
    error InvalidFeeBps(uint256 bps);

    constructor(IPool pool, address admin, address treasury_) {
        require(address(pool) != address(0), "pool-zero");
        require(admin != address(0), "admin-zero");
        require(treasury_ != address(0), "treasury-zero");

        aavePool = pool;
        treasury = treasury_;

        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(GUARDIAN_ROLE, admin);
        _grantRole(STRATEGIST_ROLE, admin);

        feeSettings = FeeSettings({enabled: false, feeBps: 500, recipient: treasury_});
    }

    receive() external payable {}

    function setApprovedAsset(address asset, bool allowed) external onlyRole(STRATEGIST_ROLE) {
        approvedAssets[asset] = allowed;
        emit AssetUpdated(asset, allowed);
    }

    function setApprovedTarget(address target, bool allowed) external onlyRole(STRATEGIST_ROLE) {
        approvedTargets[target] = allowed;
        emit TargetUpdated(target, allowed);
    }

    function setAllowedSelector(bytes4 selector, bool allowed) external onlyRole(STRATEGIST_ROLE) {
        allowedSelectors[selector] = allowed;
        emit SelectorUpdated(selector, allowed);
    }

    function setMaxBorrow(address asset, uint256 cap) external onlyRole(STRATEGIST_ROLE) {
        maxBorrowByAsset[asset] = cap;
        emit MaxBorrowUpdated(asset, cap);
    }

    function setTreasury(address newTreasury) external onlyRole(DEFAULT_ADMIN_ROLE) {
        require(newTreasury != address(0), "treasury-zero");
        treasury = newTreasury;
        emit TreasuryUpdated(newTreasury);
    }

    function setFeeSettings(bool enabled, uint256 feeBps, address recipient) external onlyRole(DEFAULT_ADMIN_ROLE) {
        if (enabled) {
            if (recipient == address(0)) revert InvalidFeeRecipient();
            if (feeBps == 0 || feeBps > 10_000) revert InvalidFeeBps(feeBps);
        }

        feeSettings = FeeSettings({enabled: enabled, feeBps: feeBps, recipient: recipient});
        emit FeeSettingsUpdated(enabled, feeBps, recipient);
    }

    function approveSpender(address token, address spender, uint256 amount) external onlyRole(STRATEGIST_ROLE) {
        IERC20(token).forceApprove(spender, amount);
    }

    function rescueTokens(address token, address recipient, uint256 amount) external onlyRole(DEFAULT_ADMIN_ROLE) {
        IERC20(token).safeTransfer(recipient, amount);
    }

    function pause() external onlyRole(GUARDIAN_ROLE) {
        _pause();
    }

    function unpause() external onlyRole(DEFAULT_ADMIN_ROLE) {
        _unpause();
    }

    function executeFlashArbitrage(
        address asset,
        uint256 amount,
        TradeInstruction[] calldata trades,
        uint256 minProfit,
        uint256 baseFeeUpperBound,
        uint256 deadline,
        address payout
    ) external onlyRole(EXECUTOR_ROLE) whenNotPaused nonReentrant {
        if (runtime.active) revert FlashLoanInFlight();
        if (!approvedAssets[asset]) revert AssetNotApproved(asset);
        if (block.basefee > baseFeeUpperBound) revert BaseFeeTooHigh(block.basefee, baseFeeUpperBound);
        if (block.timestamp > deadline) revert DeadlineExpired(deadline, block.timestamp);
        require(trades.length > 0, "trades-empty");
        require(payout != address(0), "payout-zero");
        require(amount > 0, "amount-zero");
        uint256 cap = maxBorrowByAsset[asset];
        if (cap != 0 && amount > cap) revert BorrowAmountTooHigh(amount, cap);

        runtime = RuntimeContext({preBalance: IERC20(asset).balanceOf(address(this)), asset: asset, active: true});

        TradeInstruction[] memory plan = new TradeInstruction[](trades.length);
        for (uint256 i = 0; i < trades.length; i++) {
            plan[i] = trades[i];
        }

        FlashCallbackData memory callbackData = FlashCallbackData({
            initiator: msg.sender,
            payout: payout,
            profitToken: asset,
            minProfit: minProfit,
            baseFeeUpperBound: baseFeeUpperBound,
            deadline: deadline,
            trades: plan
        });

        bytes memory params = abi.encode(callbackData);

        emit FlashArbitrageRequested(msg.sender, asset, amount, minProfit);

        aavePool.flashLoanSimple(address(this), asset, amount, params, 0);

        runtime.active = false;
    }

    function executeOperation(
        address asset,
        uint256 amount,
        uint256 premium,
        address initiator,
        bytes calldata params
    ) external override returns (bool) {
        if (msg.sender != address(aavePool)) revert PoolMismatch(msg.sender);
        if (!runtime.active || runtime.asset != asset) revert UnauthorizedCaller();
        if (initiator != address(this)) revert UnauthorizedCaller();

        FlashCallbackData memory callbackData = abi.decode(params, (FlashCallbackData));

        if (block.basefee > callbackData.baseFeeUpperBound) revert BaseFeeTooHigh(block.basefee, callbackData.baseFeeUpperBound);
        if (block.timestamp > callbackData.deadline) revert DeadlineExpired(callbackData.deadline, block.timestamp);

        uint256 startingBalance = IERC20(asset).balanceOf(address(this));

        for (uint256 i = 0; i < callbackData.trades.length; i++) {
            TradeInstruction memory trade = callbackData.trades[i];
            if (!approvedTargets[trade.target]) revert InvalidTarget(trade.target);
            if (trade.data.length < 4) revert InvalidSelector(0x00000000);
            bytes4 selector = bytes4(trade.data);
            if (!allowedSelectors[selector]) revert InvalidSelector(selector);

            (bool success, bytes memory returndata) = trade.target.call(trade.data);
            if (!success) {
                if (returndata.length > 0) {
                    assembly {
                        let returndata_size := mload(returndata)
                        revert(add(32, returndata), returndata_size)
                    }
                }
                revert("trade-failed");
            }
        }

        uint256 repayment = amount + premium;
        uint256 endingBalance = IERC20(asset).balanceOf(address(this));

        uint256 feeAmount = 0;
        if (feeSettings.enabled) {
            feeAmount = (amount * feeSettings.feeBps) / 10_000;
        }

        uint256 expectedPostRepayBalance = runtime.preBalance + callbackData.minProfit + repayment + feeAmount;
        if (endingBalance < expectedPostRepayBalance) {
            revert InsufficientProfit(expectedPostRepayBalance, endingBalance);
        }

        IERC20(asset).safeTransfer(address(aavePool), repayment);

        if (feeAmount > 0) {
            address recipient = feeSettings.recipient;
            if (recipient == address(0)) revert InvalidFeeRecipient();
            IERC20(asset).safeTransfer(recipient, feeAmount);
        }

        uint256 netRemainder = endingBalance - repayment - feeAmount;
        uint256 profit = netRemainder - runtime.preBalance;

        if (profit > 0) {
            IERC20(asset).safeTransfer(callbackData.payout, profit);
        }

        emit FlashArbitrageExecuted(asset, endingBalance - startingBalance, premium, profit, callbackData.payout);
        return true;
    }
}
