// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.IdentityModel.Tokens;

namespace Gatewright.Policies.Jwt;

/// <summary>The signing keys of one issuer, swapped atomically by the background refresher and read without locking by requests.</summary>
public sealed class JwksHandle
{
    private IReadOnlyList<SecurityKey> _keys = [];
    private long _lastRefreshTicks;
    private long _lastRefreshRequestTicks;

    internal JwksHandle(Uri? jwksUri, Uri? metadataAddress, TimeSpan refreshInterval)
    {
        JwksUri = jwksUri;
        MetadataAddress = metadataAddress;
        RefreshInterval = refreshInterval;
    }

    /// <summary>Gets the JWKS address, when configured directly.</summary>
    public Uri? JwksUri { get; }

    /// <summary>Gets the OpenID Connect metadata address used to discover the JWKS address.</summary>
    public Uri? MetadataAddress { get; }

    /// <summary>Gets how often the keys are refreshed in the background.</summary>
    public TimeSpan RefreshInterval { get; }

    /// <summary>Gets the current keys; empty until the first successful download.</summary>
    public IReadOnlyList<SecurityKey> Keys => Volatile.Read(ref _keys);

    /// <summary>Gets a value indicating whether keys were downloaded at least once.</summary>
    public bool IsLoaded => Volatile.Read(ref _lastRefreshTicks) != 0;

    internal DateTimeOffset? LastRefresh =>
        Volatile.Read(ref _lastRefreshTicks) is var ticks and not 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    internal void SetKeys(IReadOnlyList<SecurityKey> keys, DateTimeOffset now)
    {
        Volatile.Write(ref _keys, keys);
        Volatile.Write(ref _lastRefreshTicks, now.UtcTicks);
    }

    /// <summary>Asks the background refresher to reload soon (used when a token carries an unknown <c>kid</c>). Never blocks.</summary>
    internal void RequestRefresh() => Volatile.Write(ref _lastRefreshRequestTicks, DateTimeOffset.UtcNow.UtcTicks);

    internal bool IsRefreshDue(DateTimeOffset now, TimeSpan minimumInterval)
    {
        var last = Volatile.Read(ref _lastRefreshTicks);
        if (last == 0)
        {
            return true;
        }

        var sinceLast = now - new DateTimeOffset(last, TimeSpan.Zero);
        if (sinceLast >= RefreshInterval)
        {
            return true;
        }

        var requested = Volatile.Read(ref _lastRefreshRequestTicks);
        return requested > last && sinceLast >= minimumInterval;
    }
}
