// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gatewright.Core.Policies;

/// <summary>Telemetry sources emitted by the policy pipeline.</summary>
public static class PolicyTelemetry
{
    /// <summary>The name of the <see cref="ActivitySource"/> and of the <see cref="Meter"/>.</summary>
    public const string Name = "Gatewright.Policies";

    internal static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    internal static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("gatewright.policy.duration", unit: "ms", description: "Duration of policy invocations.");
}
