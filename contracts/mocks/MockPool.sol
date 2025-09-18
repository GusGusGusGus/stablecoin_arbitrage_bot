// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IPool} from "../../interfaces/aave/IPool.sol";
import {IFlashLoanSimpleReceiver} from "../../interfaces/aave/IFlashLoanSimpleReceiver.sol";
import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";

contract MockPool is IPool {
    using SafeERC20 for IERC20;

    uint256 public premiumBps = 9; // ~0.09%

    function setPremiumBps(uint256 newPremiumBps) external {
        premiumBps = newPremiumBps;
    }

    function fund(address asset, uint256 amount) external {
        IERC20(asset).safeTransferFrom(msg.sender, address(this), amount);
    }

    function flashLoanSimple(
        address receiverAddress,
        address asset,
        uint256 amount,
        bytes calldata params,
        uint16 /* referralCode */
    ) external override {
        IERC20 token = IERC20(asset);
        uint256 balanceBefore = token.balanceOf(address(this));
        require(balanceBefore >= amount, "insufficient-liquidity");

        uint256 premium = (amount * premiumBps) / 10_000;
        token.safeTransfer(receiverAddress, amount);

        bool success = IFlashLoanSimpleReceiver(receiverAddress).executeOperation(
            asset,
            amount,
            premium,
            address(this),
            params
        );
        require(success, "callback-failed");

        uint256 balanceAfter = token.balanceOf(address(this));
        require(balanceAfter >= balanceBefore + premium, "flashloan-not-repaid");
    }
}