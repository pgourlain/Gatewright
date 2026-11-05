// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Security;

/// <summary>Receives key revocations announced between two snapshots.</summary>
public interface IRevocationSink
{
    /// <summary>Stops accepting a key immediately.</summary>
    /// <param name="keyHash">The key hash.</param>
    void Revoke(string keyHash);
}
