// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Hosting;

/// <summary>Limits applied by Kestrel to every incoming request, bound from <c>Gateway:Limits</c>. Defaults match Kestrel's but are explicit and tunable.</summary>
public sealed class RequestLimitsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Gateway:Limits";

    /// <summary>Gets or sets the maximum request body size in bytes. Larger bodies are answered with 413. <see langword="null"/> disables the limit (not recommended).</summary>
    public long? MaxRequestBodySize { get; set; } = 30_000_000;

    /// <summary>Gets or sets the maximum total size in bytes of the request headers.</summary>
    public int MaxRequestHeadersTotalSize { get; set; } = 32_768;

    /// <summary>Gets or sets the maximum number of request headers.</summary>
    public int MaxRequestHeaderCount { get; set; } = 100;

    /// <summary>Gets or sets the maximum size in bytes of the request line (method, target, version).</summary>
    public int MaxRequestLineSize { get; set; } = 8_192;

    /// <summary>Gets or sets how long a client may take to send the request headers.</summary>
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the minimum rate in bytes per second a client must sustain when sending the body (slow-loris protection). 0 disables the check.</summary>
    public double MinRequestBodyDataRate { get; set; } = 240;
}
