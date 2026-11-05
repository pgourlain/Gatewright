// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Abstractions.Tests;

public sealed class PolicyResultTests
{
    [Fact]
    public void Continue_Accessed_ReturnsSharedContinueResult()
    {
        PolicyResult.Continue.Kind.ShouldBe(PolicyResultKind.Continue);
        PolicyResult.Continue.ShouldBeSameAs(PolicyResult.Continue);
        PolicyResult.Continue.StatusCode.ShouldBe(0);
    }

    [Fact]
    public void Reject_ValidArguments_CarriesStatusCodeAndMessage()
    {
        var result = PolicyResult.Reject(429, "rate_limited", "Too many requests");

        result.Kind.ShouldBe(PolicyResultKind.Reject);
        result.StatusCode.ShouldBe(429);
        result.Code.ShouldBe("rate_limited");
        result.Message.ShouldBe("Too many requests");
        result.Response.ShouldBeNull();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(399)]
    [InlineData(600)]
    public void Reject_StatusOutsideErrorRange_Throws(int status)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PolicyResult.Reject(status, "code", "message"));
    }

    [Fact]
    public void Reject_BlankCode_Throws()
    {
        Should.Throw<ArgumentException>(() => PolicyResult.Reject(400, " ", "message"));
    }

    [Fact]
    public void Respond_ValidResponse_ExposesResponseAndStatus()
    {
        var response = new PolicyResponse(
            204,
            new Dictionary<string, string> { ["x-test"] = "1" });

        var result = PolicyResult.Respond(response);

        result.Kind.ShouldBe(PolicyResultKind.Respond);
        result.StatusCode.ShouldBe(204);
        result.Response.ShouldBeSameAs(response);
        result.Code.ShouldBeNull();
    }

    [Fact]
    public void Respond_NullResponse_Throws()
    {
        Should.Throw<ArgumentNullException>(() => PolicyResult.Respond(null!));
    }

    [Fact]
    public void PolicyResponse_InvalidStatus_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PolicyResponse(42));
    }

    [Fact]
    public void PolicyResponse_NoHeaders_UsesEmptyHeaders()
    {
        new PolicyResponse(200).Headers.ShouldBeEmpty();
    }
}
