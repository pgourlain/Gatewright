// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Gatewright.Policies.Resilience;

/// <summary>Holds circuit breakers per cluster so that their state survives snapshot changes and is shared by all operations of an API.</summary>
public sealed class CircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, Gatewright.Core.Resilience.CircuitBreaker> _breakers = new(StringComparer.Ordinal);

    internal Gatewright.Core.Resilience.CircuitBreaker GetOrCreate(string key, int threshold, TimeSpan breakDuration, TimeProvider time) =>
        _breakers.GetOrAdd($"{key}|{threshold}|{breakDuration.TotalSeconds}", _ => new Gatewright.Core.Resilience.CircuitBreaker(threshold, breakDuration, time));
}
