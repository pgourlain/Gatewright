// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>Describes why the pipeline entered the <see cref="PolicyPhase.OnError"/> phase.</summary>
public sealed class PolicyFailure
{
    /// <summary>Initializes a new instance of the <see cref="PolicyFailure"/> class.</summary>
    /// <param name="policyType">The type of the policy that failed.</param>
    /// <param name="phase">The phase in which it failed.</param>
    /// <param name="isTimeout">Whether the policy exceeded its timeout.</param>
    /// <param name="exception">The exception thrown, if any.</param>
    public PolicyFailure(string policyType, PolicyPhase phase, bool isTimeout, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(policyType);
        PolicyType = policyType;
        Phase = phase;
        IsTimeout = isTimeout;
        Exception = exception;
    }

    /// <summary>Gets the type of the policy that failed.</summary>
    public string PolicyType { get; }

    /// <summary>Gets the phase in which the policy failed.</summary>
    public PolicyPhase Phase { get; }

    /// <summary>Gets a value indicating whether the policy exceeded its timeout.</summary>
    public bool IsTimeout { get; }

    /// <summary>Gets the exception thrown by the policy, if any.</summary>
    public Exception? Exception { get; }
}
