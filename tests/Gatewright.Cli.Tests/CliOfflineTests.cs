// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT


namespace Gatewright.Cli.Tests;

public sealed class CliOfflineTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-cli-").FullName;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private CliEnvironment Environment() => new()
    {
        Out = _out,
        Error = _error,
        ConfigDirectory = Path.Combine(_directory, "cfg"),
        GetEnvironmentVariable = _ => null,
    };

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Validate_ValidConfig_ExitsZero()
    {
        var file = Write("ok.json", """{"apis":[{"name":"a","basePath":"/a","backend":{"destinations":["http://b:80"]},"policies":{"inbound":[{"type":"rateLimit","config":{"limit":5}}]}}]}""");

        var exit = await CliApp.RunAsync(["config", "validate", file], Environment());

        exit.ShouldBe(0);
        _out.ToString().ShouldContain("Valid. 1 API(s)");
    }

    [Fact]
    public async Task Validate_ReportsStructureAndPolicySchemaErrors()
    {
        var file = Write("bad.json", """{"apis":[{"name":"a","basePath":"nope","backend":{},"policies":{"inbound":[{"type":"rateLimit","config":{"limit":0}},{"type":"mystery"}]}}]}""");

        var exit = await CliApp.RunAsync(["config", "validate", file], Environment());

        exit.ShouldBe(1);
        var errors = _error.ToString();
        errors.ShouldContain("api.base-path-invalid");
        errors.ShouldContain("policy.unknown-type");
        errors.ShouldContain("policy 'rateLimit'");
    }

    [Fact]
    public async Task Validate_AllowUnknownPolicies_AcceptsCustomTypes()
    {
        var file = Write("custom.json", """{"apis":[{"name":"a","basePath":"/a","backend":{"destinations":["http://b:80"]},"policies":{"inbound":[{"type":"acme.fraud"}]}}]}""");

        (await CliApp.RunAsync(["config", "validate", file, "--allow-unknown-policies"], Environment())).ShouldBe(0);
    }

    [Fact]
    public async Task Validate_AcceptsAnExportDocument()
    {
        var file = Write("export.json", """{"format":"gatewright/v1","config":{"apis":[]}}""");

        (await CliApp.RunAsync(["config", "validate", file], Environment())).ShouldBe(0);
    }

    [Fact]
    public async Task Validate_MissingFileOrBrokenJson_ExitsOneWithAMessage()
    {
        (await CliApp.RunAsync(["config", "validate", "nope.json"], Environment())).ShouldBe(1);
        _error.ToString().ShouldContain("File not found");
        (await CliApp.RunAsync(["config", "validate", Write("broken.json", "{")], Environment())).ShouldBe(1);
    }

    [Fact]
    public async Task Login_StoresSettingsAndEnvironmentVariablesWin()
    {
        var env = Environment();

        (await CliApp.RunAsync(["login", "--server", "https://gw.example.com/", "--token", "abc", "--no-verify"], env)).ShouldBe(0);

        var stored = CliSettings.Load(env);
        stored.Server.ShouldBe("https://gw.example.com");
        stored.Token.ShouldBe("abc");
        env.GetEnvironmentVariable = key => key == "GATEWRIGHT_TOKEN" ? "from-env" : null;
        CliSettings.Load(env).Token.ShouldBe("from-env");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Path.Combine(env.ConfigDirectory, "config.json")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task Commands_WithoutServer_ExplainHowToLogIn()
    {
        (await CliApp.RunAsync(["publish", "--env", "dev"], Environment())).ShouldBe(1);

        _error.ToString().ShouldContain("gatewright login");
    }

    [Fact]
    public async Task UnknownCommand_IsAUsageError()
    {
        (await CliApp.RunAsync(["frobnicate"], Environment())).ShouldNotBe(0);
    }
}
