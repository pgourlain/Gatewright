// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Policies;

/// <summary>Options of the <see cref="PolicyPipeline"/>.</summary>
public sealed class PolicyPipelineOptions
{
    /// <summary>Gets or sets the timeout applied to policies that do not define their own. <see cref="TimeSpan.Zero"/> disables it.</summary>
    public TimeSpan DefaultPolicyTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
