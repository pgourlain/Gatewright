// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.RateLimit;

/// <summary>The outcome of one rate-limit check.</summary>
/// <param name="Allowed">Whether the request may proceed.</param>
/// <param name="Limit">The configured limit.</param>
/// <param name="Remaining">Requests left in the current budget.</param>
/// <param name="ResetSeconds">Seconds until the budget is fully restored (token bucket) or the window ends (fixed window).</param>
/// <param name="RetryAfterSeconds">Seconds to wait before retrying; 0 when allowed.</param>
/// <param name="StoreUnavailable">The shared store failed and the configured failure mode decided the outcome.</param>
internal readonly record struct RateLimitDecision(bool Allowed, int Limit, int Remaining, int ResetSeconds, int RetryAfterSeconds, bool StoreUnavailable = false);
