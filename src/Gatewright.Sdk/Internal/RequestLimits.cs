// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Gatewright.Sdk.Internal;

/// <summary>Applies <see cref="RequestLimitsOptions"/> to Kestrel.</summary>
internal static class RequestLimits
{
    public static RequestLimitsOptions Read(IConfiguration configuration)
    {
        var options = new RequestLimitsOptions();
        configuration.GetSection(RequestLimitsOptions.SectionName).Bind(options);
        if (options.MaxRequestBodySize is < 0 || options.MaxRequestHeadersTotalSize <= 0 || options.MaxRequestHeaderCount <= 0
            || options.MaxRequestLineSize <= 0 || options.RequestHeadersTimeout <= TimeSpan.Zero || options.MinRequestBodyDataRate < 0)
        {
            throw new InvalidOperationException($"Invalid '{RequestLimitsOptions.SectionName}' configuration: sizes, counts and timeouts must be positive.");
        }

        return options;
    }

    public static void Apply(IConfiguration configuration, KestrelServerLimits limits)
    {
        var options = Read(configuration);
        limits.MaxRequestBodySize = options.MaxRequestBodySize;
        limits.MaxRequestHeadersTotalSize = options.MaxRequestHeadersTotalSize;
        limits.MaxRequestHeaderCount = options.MaxRequestHeaderCount;
        limits.MaxRequestLineSize = options.MaxRequestLineSize;
        limits.RequestHeadersTimeout = options.RequestHeadersTimeout;
        limits.MinRequestBodyDataRate = options.MinRequestBodyDataRate == 0
            ? null
            : new MinDataRate(options.MinRequestBodyDataRate, TimeSpan.FromSeconds(5));
    }
}
