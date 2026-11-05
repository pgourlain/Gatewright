// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Snapshots;

namespace Gatewright.Core.Security;

/// <summary>Receives the security data of each snapshot before it becomes active.</summary>
public interface ISecurityDataSink
{
    /// <summary>Replaces the current security data.</summary>
    /// <param name="data">The security data of the new snapshot.</param>
    void Apply(SecurityData data);
}
