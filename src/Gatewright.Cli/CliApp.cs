// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.CommandLine;
using Gatewright.Cli.Commands;

namespace Gatewright.Cli;

/// <summary>The <c>gatewright</c> command line application.</summary>
public static class CliApp
{
    /// <summary>Runs the CLI.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="environment">The environment (output, settings location, HTTP).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code: 0 success, 1 failure, 2 invalid usage.</returns>
    public static async Task<int> RunAsync(string[] args, CliEnvironment environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        var root = new RootCommand("Gatewright: manage your API gateway from the command line.");
        root.Subcommands.Add(LoginCommand.Create(environment));
        root.Subcommands.Add(ApiCommands.Create(environment));
        root.Subcommands.Add(ConfigCommands.Create(environment));
        root.Subcommands.Add(PublishCommands.CreatePublish(environment));
        root.Subcommands.Add(PublishCommands.CreateRollback(environment));
        root.Subcommands.Add(PublishCommands.CreateNodes(environment));

        var configuration = new InvocationConfiguration { Output = environment.Out, Error = environment.Error };
        return await root.Parse(args).InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> GuardAsync(CliEnvironment environment, Func<Task<int>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (CliException ex)
        {
            await environment.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await environment.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    internal static AdminClient CreateClient(CliEnvironment environment) =>
        new(environment.CreateHttpClient(), CliSettings.Load(environment));

    internal static async Task<string> ReadInputAsync(CliEnvironment environment, string source, CancellationToken cancellationToken)
    {
        if (source == "-")
        {
            return await environment.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            using var http = environment.CreateHttpClient();
            return await http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(source))
        {
            throw new CliException($"File not found: {source}");
        }

        return await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
    }
}
