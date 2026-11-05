// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json;
using Gatewright.Cli;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gatewright.IntegrationTests;

public sealed class CliEndToEndTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-cli-e2e-").FullName;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly WebApplicationFactory<Program> _factory;

    public CliEndToEndTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Gateway:Mode", "ControlPlane");
            b.UseSetting("ControlPlane:ConnectionString", $"Data Source={Path.Combine(_directory, "cp.db")}");
            b.UseSetting("ControlPlane:Auth:DevelopmentBypass", "true");
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private CliEnvironment Environment() => new()
    {
        Out = _out,
        Error = _error,
        ConfigDirectory = Path.Combine(_directory, "cfg"),
        GetEnvironmentVariable = key => key == "GATEWRIGHT_SERVER" ? "http://control-plane.test" : null,
        CreateHttpClient = () => _factory.CreateClient(),
    };

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string OpenApi = """
        openapi: 3.0.3
        info: { title: Orders, version: "1" }
        paths:
          /orders:
            get: { operationId: list, x-gateway: { rateLimit: { limit: 10 } } }
        """;

    [Fact]
    public async Task ImportPublishExportRollback_FullWorkflowFromTheCommandLine()
    {
        var env = Environment();
        var spec = Write("orders.yaml", OpenApi);

        (await CliApp.RunAsync(["api", "import", spec, "--destination", "http://orders:8080", "--dry-run"], env)).ShouldBe(0, _error.ToString());
        _out.ToString().ShouldContain("[dry run] API 'orders' created");

        (await CliApp.RunAsync(["api", "import", spec, "--destination", "http://orders:8080"], env)).ShouldBe(0, _error.ToString());
        _out.ToString().ShouldContain("added    GET /orders (list)");

        (await CliApp.RunAsync(["publish", "--env", "prod"], env)).ShouldBe(0, _error.ToString());
        _out.ToString().ShouldContain("Published revision 1 to prod as configuration version 1");

        var exported = Path.Combine(_directory, "export.json");
        (await CliApp.RunAsync(["config", "export", "--env", "prod", "-o", exported], env)).ShouldBe(0, _error.ToString());
        JsonDocument.Parse(File.ReadAllText(exported)).RootElement.GetProperty("config").GetProperty("apis")[0].GetProperty("name").GetString().ShouldBe("orders");
        (await CliApp.RunAsync(["config", "validate", exported], env)).ShouldBe(0, _error.ToString());

        await CliApp.RunAsync(["api", "import", spec, "--destination", "http://orders:9090"], env);
        (await CliApp.RunAsync(["publish", "--env", "prod"], env)).ShouldBe(0, _error.ToString());
        (await CliApp.RunAsync(["rollback", "--env", "prod", "--revision", "1"], env)).ShouldBe(0, _error.ToString());
        _out.ToString().ShouldContain("Rolled prod back to revision 1");

        (await CliApp.RunAsync(["config", "import", exported, "--dry-run"], env)).ShouldBe(0, _error.ToString());
        _out.ToString().ShouldContain("Valid (dry run)");
    }

    [Fact]
    public async Task ServerErrors_AreReadable()
    {
        var env = Environment();

        (await CliApp.RunAsync(["api", "import", Write("bad.json", """{"swagger":"2.0"}"""), "--destination", "http://b"], env)).ShouldBe(1);
        _error.ToString().ShouldContain("Unsupported OpenAPI version");
        (await CliApp.RunAsync(["publish", "--env", "nowhere"], env)).ShouldBe(1);
        _error.ToString().ShouldContain(((int)HttpStatusCode.NotFound).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
