// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Policies.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace Gatewright.Policies.Tests;

public sealed class HeadersPolicyValidationTests
{
    private static void Compile(string json) =>
        new HeadersPolicy().Compile(JsonElement.Parse(json), new PolicyCompilationContext(new ServiceCollection().BuildServiceProvider(), "api:test"));

    [Theory]
    [InlineData("""{"request":{"set":{"X-A\r\nX-B":"v"}}}""")]
    [InlineData("""{"request":{"set":{"X A":"v"}}}""")]
    [InlineData("""{"response":{"append":{"X:A":"v"}}}""")]
    [InlineData("""{"response":{"remove":["bad name"]}}""")]
    [InlineData("""{"request":{"set":{"X-A":"v\r\nSet-Cookie: x=1"}}}""")]
    public void Compile_InvalidHeaderNameOrValue_Throws(string json)
    {
        Should.Throw<ArgumentException>(() => Compile(json));
    }

    [Fact]
    public void Compile_ValidHeaders_Succeeds()
    {
        Should.NotThrow(() => Compile("""{"request":{"set":{"X-Tenant":"{claim:tid}"},"remove":["Cookie"]}}"""));
    }
}
