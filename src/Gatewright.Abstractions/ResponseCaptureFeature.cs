// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions;

/// <summary>
/// An HTTP feature a policy sets (<c>httpContext.Features.Set</c>) to receive a copy of the backend response, for example to fill a cache.
/// The gateway buffers at most <see cref="MaxBodyBytes"/>; larger responses are streamed untouched and the callback is not invoked.
/// </summary>
public sealed class ResponseCaptureFeature
{
    /// <summary>Initializes a new instance of the <see cref="ResponseCaptureFeature"/> class.</summary>
    /// <param name="maxBodyBytes">The largest body to buffer.</param>
    /// <param name="captured">Called once the response completed successfully and fits in the buffer.</param>
    public ResponseCaptureFeature(int maxBodyBytes, Func<ResponseCapture, ValueTask> captured)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBodyBytes);
        ArgumentNullException.ThrowIfNull(captured);
        MaxBodyBytes = maxBodyBytes;
        Captured = captured;
    }

    /// <summary>Gets the largest body to buffer.</summary>
    public int MaxBodyBytes { get; }

    /// <summary>Gets the callback receiving the captured response.</summary>
    public Func<ResponseCapture, ValueTask> Captured { get; }
}
