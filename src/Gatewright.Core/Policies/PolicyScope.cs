// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Policies;

/// <summary>The scope a policy is declared in, from the broadest to the narrowest.</summary>
public enum PolicyScope
{
    /// <summary>Applies to every API.</summary>
    Global = 0,

    /// <summary>Applies to every API of the gateway.</summary>
    Gateway = 1,

    /// <summary>Applies to the APIs of a product.</summary>
    Product = 2,

    /// <summary>Applies to one API.</summary>
    Api = 3,

    /// <summary>Applies to one operation.</summary>
    Operation = 4,
}
