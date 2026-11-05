// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Policies.Cache;

internal sealed class CacheEntry
{
    public int Status { get; set; }

    public Dictionary<string, string> Headers { get; set; } = [];

    public byte[] Body { get; set; } = [];
}
