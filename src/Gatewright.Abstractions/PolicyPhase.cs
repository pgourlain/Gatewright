// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>Phases of the request pipeline in which a policy can run.</summary>
[Flags]
public enum PolicyPhase
{
    /// <summary>No phase.</summary>
    None = 0,

    /// <summary>Before the request is forwarded to the backend.</summary>
    Inbound = 1,

    /// <summary>Around the forwarding of the request to the backend.</summary>
    Backend = 2,

    /// <summary>After the backend response is received, before it is returned to the client.</summary>
    Outbound = 4,

    /// <summary>When a policy failed with an exception or a timeout.</summary>
    OnError = 8,
}
