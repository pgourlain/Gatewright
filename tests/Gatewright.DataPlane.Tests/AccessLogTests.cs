// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.DataPlane.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gatewright.DataPlane.Tests;

public sealed class AccessLogTests
{
    private static AccessLogService Create(params string[] extraMasked) => new(
        Options.Create(new TelemetryOptions { AccessLog = new AccessLogOptions { MaskedQueryParameters = [.. extraMasked] } }),
        NullLoggerFactory.Instance);

    [Theory]
    [InlineData("?api_key=abc&page=2", "?api_key=***&page=2")]
    [InlineData("?Token=abc", "?Token=***")]
    [InlineData("?access_token=a&code=b&x=1", "?access_token=***&code=***&x=1")]
    [InlineData("?page=2", "?page=2")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskQuery_SensitiveNames_AreMasked(string? query, string expected)
    {
        Create().MaskQuery(query).ShouldBe(expected);
    }

    [Fact]
    public void MaskQuery_ConfiguredName_IsMasked()
    {
        Create("tenant").MaskQuery("?tenant=acme&a=b").ShouldBe("?tenant=***&a=b");
    }

    [Fact]
    public void MaskQuery_EncodedSensitiveName_IsMasked()
    {
        Create().MaskQuery("?api%5Fkey=abc").ShouldBe("?api%5Fkey=***");
    }
}
