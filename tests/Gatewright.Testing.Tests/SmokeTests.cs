// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Testing.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Smoke_Always_Passes()
    {
        true.ShouldBeTrue();
    }
}
