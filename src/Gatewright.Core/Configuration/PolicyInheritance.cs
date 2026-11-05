// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Configuration;

/// <summary>How a policy declared in a scope combines with the policies inherited from parent scopes.</summary>
public enum PolicyInheritance
{
    /// <summary>The policy is added to the inherited chain (default).</summary>
    Inherit = 0,

    /// <summary>The policy replaces inherited policies of the same type in the same phase.</summary>
    Override = 1,

    /// <summary>Inherited policies of the same type are removed from the same phase; the policy itself is not added.</summary>
    Remove = 2,
}
