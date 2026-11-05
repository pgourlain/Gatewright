// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.State.Redis;

/// <summary>Redis could not be used: the circuit is open, a call timed out or the connection failed. Callers apply their failure mode (fail-open or fail-closed).</summary>
public sealed class RedisUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RedisUnavailableException"/> class.</summary>
    public RedisUnavailableException()
        : base("Redis is unavailable.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisUnavailableException"/> class.</summary>
    /// <param name="message">The message.</param>
    public RedisUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisUnavailableException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public RedisUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
