// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Hosting;

namespace Gatewright.Core.Tests.Hosting;

public sealed class GatewayOptionsValidatorTests
{
    private static GatewayOptions Valid() => new() { Mode = GatewayMode.ControlPlane };

    [Theory]
    [InlineData("00:00:00.100")]
    [InlineData("00:00:30")]
    [InlineData("1.00:00:00")]
    public void Validate_PollIntervalInRange_Succeeds(string interval)
    {
        var options = Valid();
        options.ConfigPollInterval = TimeSpan.Parse(interval, System.Globalization.CultureInfo.InvariantCulture);

        new GatewayOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("30000")] // read as 30000 days, which crashed the host at startup with an unhelpful PeriodicTimer error
    [InlineData("1.00:00:01")]
    public void Validate_PollIntervalOutOfRange_FailsWithAFormatHint(string interval)
    {
        var options = Valid();
        options.ConfigPollInterval = TimeSpan.Parse(interval, System.Globalization.CultureInfo.InvariantCulture);

        var result = new GatewayOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("hh:mm:ss");
    }
}
