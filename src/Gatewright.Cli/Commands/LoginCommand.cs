// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.CommandLine;

namespace Gatewright.Cli.Commands;

internal static class LoginCommand
{
    public static Command Create(CliEnvironment environment)
    {
        var server = new Option<string>("--server") { Description = "Base address of the Gatewright control plane, for example https://gatewright.example.com.", Required = true };
        var token = new Option<string?>("--token") { Description = "Bearer token for the Admin API (for example from 'az account get-access-token --resource api://gatewright-admin'). Read from the GATEWRIGHT_TOKEN variable or the standard input when omitted." };
        var noVerify = new Option<bool>("--no-verify") { Description = "Save the settings without calling the server." };
        var command = new Command("login", "Store the control plane address and an access token for the next commands.") { server, token, noVerify };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var settings = new CliSettings { Server = parse.GetValue(server)!.TrimEnd('/'), Token = parse.GetValue(token) ?? environment.GetEnvironmentVariable("GATEWRIGHT_TOKEN") };
            if (string.IsNullOrWhiteSpace(settings.Token) && Console.IsInputRedirected)
            {
                settings.Token = (await environment.In.ReadToEndAsync(ct).ConfigureAwait(false)).Trim();
            }

            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                throw new CliException("No token provided. Pass --token, set GATEWRIGHT_TOKEN, or pipe it to the standard input.");
            }

            if (!parse.GetValue(noVerify))
            {
                var client = new AdminClient(environment.CreateHttpClient(), settings);
                await client.SendAsync(HttpMethod.Get, "admin/v1/environments", cancellationToken: ct).ConfigureAwait(false);
            }

            settings.Save(environment);
            await environment.Out.WriteLineAsync($"Logged in to {settings.Server}.").ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
