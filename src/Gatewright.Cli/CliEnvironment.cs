// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Cli;

/// <summary>Everything the CLI touches outside its own process, replaceable in tests.</summary>
public sealed class CliEnvironment
{
    /// <summary>Gets or sets the standard output.</summary>
    public TextWriter Out { get; set; } = Console.Out;

    /// <summary>Gets or sets the error output.</summary>
    public TextWriter Error { get; set; } = Console.Error;

    /// <summary>Gets or sets the standard input, used by <c>-</c> arguments.</summary>
    public TextReader In { get; set; } = Console.In;

    /// <summary>Gets or sets the directory holding <c>config.json</c> (server and token). Defaults to <c>~/.gatewright</c>.</summary>
    public string ConfigDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gatewright");

    /// <summary>Gets or sets how environment variables are read (<c>GATEWRIGHT_SERVER</c>, <c>GATEWRIGHT_TOKEN</c>).</summary>
    public Func<string, string?> GetEnvironmentVariable { get; set; } = Environment.GetEnvironmentVariable;

    /// <summary>Gets or sets how the HTTP client toward the Admin API is created. Tests inject an in-memory server.</summary>
    public Func<HttpClient> CreateHttpClient { get; set; } = () => new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
}
