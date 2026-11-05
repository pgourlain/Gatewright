// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>Describes how the pipeline continues after a policy ran.</summary>
public enum PolicyResultKind
{
    /// <summary>The pipeline continues with the next policy.</summary>
    Continue = 0,

    /// <summary>The request is rejected with an error and the pipeline stops.</summary>
    Reject = 1,

    /// <summary>The policy produced the response itself and the pipeline stops.</summary>
    Respond = 2,
}
