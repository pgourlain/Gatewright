// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.ControlPlane.Domain;

namespace Gatewright.ControlPlane.Tests.Domain;

public sealed class SubscriptionEntityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void AddKey_PrimaryAndSecondary_AreBothActive()
    {
        var subscription = new SubscriptionEntity();

        subscription.AddKey("h1", "primary", Now);
        subscription.AddKey("h2", "secondary", Now);

        subscription.ActiveKeys.Select(k => k.Label).ShouldBe(["primary", "secondary"]);
    }

    [Fact]
    public void AddKey_ThirdActiveKey_Throws()
    {
        var subscription = new SubscriptionEntity();
        subscription.AddKey("h1", "primary", Now);
        subscription.AddKey("h2", "secondary", Now);

        Should.Throw<InvalidOperationException>(() => subscription.AddKey("h3", "tertiary", Now));
    }

    [Fact]
    public void AddKey_SameLabelWhileActive_Throws()
    {
        var subscription = new SubscriptionEntity();
        subscription.AddKey("h1", "primary", Now);

        Should.Throw<InvalidOperationException>(() => subscription.AddKey("h2", "primary", Now));
    }

    [Fact]
    public void RevokeKey_ActiveKey_IsRevokedAndLabelReusable()
    {
        var subscription = new SubscriptionEntity();
        subscription.AddKey("h1", "primary", Now);

        subscription.RevokeKey("primary", Now.AddDays(1)).ShouldBeTrue();
        subscription.ActiveKeys.ShouldBeEmpty();
        subscription.AddKey("h2", "primary", Now.AddDays(2));

        subscription.Keys.Count.ShouldBe(2);
        subscription.Keys[0].RevokedAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void RevokeKey_UnknownLabel_ReturnsFalse()
    {
        new SubscriptionEntity().RevokeKey("primary", Now).ShouldBeFalse();
    }
}
