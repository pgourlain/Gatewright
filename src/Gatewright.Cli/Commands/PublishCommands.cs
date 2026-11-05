// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.CommandLine;
using System.Globalization;
using System.Text.Json;

namespace Gatewright.Cli.Commands;

internal static class PublishCommands
{
    public static Command CreatePublish(CliEnvironment environment)
    {
        var env = new Option<string>("--env") { Description = "Environment to publish to (dev, staging, prod…).", Required = true };
        var revision = new Option<long?>("--revision") { Description = "Revision number to publish. Defaults to the latest draft." };
        var command = new Command("publish", "Validate and publish a revision to an environment.") { env, revision };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var body = JsonSerializer.Serialize(new { revision = parse.GetValue(revision) ?? 0 });
            var response = await CliApp.CreateClient(environment).SendAsync(
                HttpMethod.Post,
                $"admin/v1/environments/{Uri.EscapeDataString(parse.GetValue(env)!)}/publications",
                body,
                cancellationToken: ct).ConfigureAwait(false);
            var publication = JsonDocument.Parse(response).RootElement;
            await environment.Out.WriteLineAsync(
                $"Published revision {publication.GetProperty("revision").GetInt64()} to {parse.GetValue(env)} as configuration version {publication.GetProperty("number").GetInt64()}.").ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    public static Command CreateRollback(CliEnvironment environment)
    {
        var env = new Option<string>("--env") { Description = "Environment to roll back.", Required = true };
        var revision = new Option<long>("--revision") { Description = "A revision previously published to this environment.", Required = true };
        var command = new Command("rollback", "Restore a previously published revision in an environment.") { env, revision };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var body = JsonSerializer.Serialize(new { revision = parse.GetValue(revision) });
            var response = await CliApp.CreateClient(environment).SendAsync(
                HttpMethod.Post,
                $"admin/v1/environments/{Uri.EscapeDataString(parse.GetValue(env)!)}/rollback",
                body,
                cancellationToken: ct).ConfigureAwait(false);
            var publication = JsonDocument.Parse(response).RootElement;
            await environment.Out.WriteLineAsync(
                $"Rolled {parse.GetValue(env)} back to revision {publication.GetProperty("revision").GetInt64()} (configuration version {publication.GetProperty("number").GetInt64()}).").ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    public static Command CreateNodes(CliEnvironment environment)
    {
        var env = new Option<string?>("--env") { Description = "Only nodes of this environment." };
        var command = new Command("nodes", "List data plane nodes and the configuration version each applied.") { env };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var query = parse.GetValue(env) is { } value ? "?environment=" + Uri.EscapeDataString(value) : "";
            var response = await CliApp.CreateClient(environment).SendAsync(HttpMethod.Get, "admin/v1/nodes" + query, cancellationToken: ct).ConfigureAwait(false);
            var nodes = JsonDocument.Parse(response).RootElement;
            foreach (var node in nodes.EnumerateArray())
            {
                var applied = node.GetProperty("appliedVersion").GetInt64();
                var latest = node.GetProperty("latestVersion").GetInt64();
                await environment.Out.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{node.GetProperty("nodeId").GetString(),-30} {node.GetProperty("environment").GetString(),-10} v{applied}/{latest} {(node.GetProperty("inSync").GetBoolean() ? "in sync" : "BEHIND")} {(node.GetProperty("connected").GetBoolean() ? "connected" : "disconnected")}")).ConfigureAwait(false);
            }

            return 0;
        }));
        return command;
    }
}
