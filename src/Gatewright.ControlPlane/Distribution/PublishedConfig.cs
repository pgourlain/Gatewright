// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.ControlPlane.Distribution;

/// <summary>The active publication of an environment as seen by the distribution.</summary>
/// <param name="Number">The publication number (configuration version).</param>
/// <param name="Hash">The configuration content hash.</param>
/// <param name="ConfigJson">The configuration as JSON.</param>
public sealed record PublishedConfig(long Number, string Hash, string ConfigJson);
