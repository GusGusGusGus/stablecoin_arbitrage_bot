// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";

contract MockRouter {
    using SafeERC20 for IERC20;

    IERC20 public immutable token;

    constructor(IERC20 token_) {
        token = token_;
    }

    function swapExact(uint256 amountIn, uint256 amountOut) external returns (uint256) {
        token.safeTransferFrom(msg.sender, address(this), amountIn);
        token.safeTransfer(msg.sender, amountOut);
        return amountOut;
    }
}