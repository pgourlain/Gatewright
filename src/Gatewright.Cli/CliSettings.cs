// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace Gatewright.Cli;

/// <summary>The server and token the CLI talks to, stored in the user profile (file readable by the user only) or taken from the environment.</summary>
public sealed class CliSettings
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Gets or sets the Admin API base address, for example <c>https://gatewright.example.com</c>.</summary>
    public string? Server { get; set; }

    /// <summary>Gets or sets the bearer token (an Entra ID access token for the Admin API audience).</summary>
    public string? Token { get; set; }

    /// <summary>Loads the settings: environment variables win over the stored file.</summary>
    /// <param name="environment">The CLI environment.</param>
    /// <returns>The settings.</returns>
    public static CliSettings Load(CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var settings = new CliSettings();
        var path = Path.Combine(environment.ConfigDirectory, "config.json");
        if (File.Exists(path))
        {
            settings = JsonSerializer.Deserialize<CliSettings>(File.ReadAllText(path), Options) ?? new CliSettings();
        }

        settings.Server = environment.GetEnvironmentVariable("GATEWRIGHT_SERVER") ?? settings.Server;
        settings.Token = environment.GetEnvironmentVariable("GATEWRIGHT_TOKEN") ?? settings.Token;
        return settings;
    }

    /// <summary>Saves the settings with owner-only permissions on Unix.</summary>
    /// <param name="environment">The CLI environment.</param>
    public void Save(CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        Directory.CreateDirectory(environment.ConfigDirectory);
        var path = Path.Combine(environment.ConfigDirectory, "config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
