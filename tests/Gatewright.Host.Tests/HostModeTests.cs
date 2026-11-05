// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gatewright.Host.Tests;

public sealed class HostModeTests
{
    private static WebApplicationFactory<Program> Create(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static string[] Components(WebApplicationFactory<Program> factory) =>
        [.. factory.Services.GetServices<GatewayComponent>().Select(c => c.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Start_AllMode_EnablesControlAndDataPlane()
    {
        await using var factory = Create(("Gateway:Mode", "All"), ("Gateway:ConfigFile", "dev.json"));

        using var client = factory.CreateClient();
        Components(factory).ShouldBe(
            ["control-plane", "data-plane", "distribution:in-process", "state-store:memory"]);
        (await client.GetStringAsync("/")).ShouldBe("Gatewright");
    }

    [Fact]
    public async Task Start_ControlPlaneMode_EnablesOnlyControlPlane()
    {
        var database = Path.Combine(Path.GetTempPath(), $"gatewright-host-{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = Create(("Gateway:Mode", "ControlPlane"), ("ControlPlane:ConnectionString", $"Data Source={database}"));

            using var client = factory.CreateClient();
            Components(factory).ShouldBe(["admin-api", "control-plane", "distribution:grpc-server"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    [Fact]
    public async Task Start_DataPlaneMode_EnablesOnlyDataPlane()
    {
        await using var factory = Create(
            ("Gateway:Mode", "DataPlane"),
            ("Distribution:Endpoint", "http://localhost:1"),
            ("Distribution:Token", "unused"));

        using var client = factory.CreateClient();
        Components(factory).ShouldBe(["data-plane", "distribution:grpc-client", "state-store:memory"]);
    }

    [Fact]
    public async Task Start_DataPlaneModeWithoutControlPlaneAddress_FailsExplicitly()
    {
        await using var factory = Create(("Gateway:Mode", "DataPlane"));

        var ex = Should.Throw<Exception>(() => factory.CreateClient());

        Flatten(ex).ShouldContain("Distribution:Endpoint");
    }

    [Fact]
    public async Task Start_UnknownMode_FailsExplicitly()
    {
        await using var factory = Create(("Gateway:Mode", "Banana"));

        var ex = Should.Throw<Exception>(() => factory.CreateClient());

        Flatten(ex).ShouldContain("Gateway:Mode");
    }

    [Fact]
    public async Task Start_AllModeWithoutConfigFile_FailsExplicitly()
    {
        await using var factory = Create(("Gateway:Mode", "All"));

        var ex = Should.Throw<Exception>(() => factory.CreateClient());

        Flatten(ex).ShouldContain("Gateway:ConfigFile");
    }

    [Theory]
    [InlineData(GatewayMode.All, "x.json", true)]
    [InlineData(GatewayMode.All, null, false)]
    [InlineData(GatewayMode.DataPlane, null, true)]
    [InlineData((GatewayMode)42, null, false)]
    public void Validate_Options_MatchesExpectation(GatewayMode mode, string? file, bool valid)
    {
        var result = new GatewayOptionsValidator().Validate(null, new GatewayOptions { Mode = mode, ConfigFile = file });

        result.Succeeded.ShouldBe(valid);
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            parts.Add(e.Message + (e is OptionsValidationException v ? string.Join(' ', v.Failures) : ""));
        }

        return string.Join(" | ", parts);
    }

    [Fact]
    public async Task Readyz_TurnsToFailingAsSoonAsShutdownStarts()
    {
        var directory = Directory.CreateTempSubdirectory("gatewright-ready-").FullName;
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """{"version":1}""");
            await using var factory = Create(("Gateway:Mode", "All"), ("Gateway:ConfigFile", config), ("Gateway:ConfigPollInterval", "00:00:00.100"));
            using var client = factory.CreateClient();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((await client.GetAsync("/readyz")).StatusCode != System.Net.HttpStatusCode.OK && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            (await client.GetAsync("/readyz")).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

            factory.Services.GetRequiredService<Gatewright.DataPlane.ReadinessGate>().BeginDrain();

            (await client.GetAsync("/readyz")).StatusCode.ShouldBe(System.Net.HttpStatusCode.ServiceUnavailable);
            (await client.GetAsync("/healthz")).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
