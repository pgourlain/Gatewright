// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Security;

/// <summary>The size and refill speed of a shared token bucket.</summary>
/// <param name="Capacity">The most tokens the bucket can hold.</param>
/// <param name="RefillPerSecond">How many tokens are added each second.</param>
public readonly record struct RateLimitBudget(int Capacity, double RefillPerSecond);

/// <summary>The outcome of a token reservation.</summary>
/// <param name="Granted">Tokens handed to the caller, between 0 and the number requested.</param>
/// <param name="RetryAfter">When fewer tokens than requested were granted, how long until more are available.</param>
public readonly record struct RateLimitReservation(int Granted, TimeSpan RetryAfter);
