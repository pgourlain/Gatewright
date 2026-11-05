// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.CommandLine;
using System.Text.Json;

namespace Gatewright.Cli.Commands;

internal static class ApiCommands
{
    public static Command Create(CliEnvironment environment)
    {
        var api = new Command("api", "Manage APIs.");
        api.Subcommands.Add(CreateImport(environment));
        return api;
    }

    private static Command CreateImport(CliEnvironment environment)
    {
        var source = new Argument<string>("openapi") { Description = "OpenAPI 3.0/3.1 document: a file, an http(s) URL, or '-' for the standard input." };
        var name = new Option<string?>("--name") { Description = "API name (default: x-gateway.name or the document title)." };
        var basePath = new Option<string?>("--base-path") { Description = "Base path (default: x-gateway.basePath or the first server path)." };
        var service = new Option<string?>("--service") { Description = "Kubernetes service backend: namespace/name:port." };
        var destination = new Option<string[]>("--destination") { Description = "Static backend destination URL; repeat for several.", AllowMultipleArgumentsPerToken = true };
        var product = new Option<string[]>("--product") { Description = "Product the API belongs to; repeat for several.", AllowMultipleArgumentsPerToken = true };
        var keepMissing = new Option<bool>("--keep-missing") { Description = "Keep operations that the document no longer declares." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Show what would change without changing anything." };
        var json = new Option<bool>("--json") { Description = "Print the raw JSON report." };
        var command = new Command("import", "Create or update an API from an OpenAPI document (draft revision; publish with 'gatewright publish').")
        {
            source, name, basePath, service, destination, product, keepMissing, dryRun, json,
        };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var document = await CliApp.ReadInputAsync(environment, parse.GetValue(source)!, ct).ConfigureAwait(false);
            var query = new List<string>();
            void Add(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    query.Add($"{key}={Uri.EscapeDataString(value)}");
                }
            }

            Add("name", parse.GetValue(name));
            Add("basePath", parse.GetValue(basePath));
            Add("service", parse.GetValue(service));
            foreach (var value in parse.GetValue(destination) ?? [])
            {
                Add("destination", value);
            }

            foreach (var value in parse.GetValue(product) ?? [])
            {
                Add("product", value);
            }

            if (parse.GetValue(keepMissing))
            {
                query.Add("removeMissing=false");
            }

            if (parse.GetValue(dryRun))
            {
                query.Add("dryRun=true");
            }

            var response = await CliApp.CreateClient(environment).SendAsync(
                HttpMethod.Post,
                "admin/v1/apis/import" + (query.Count > 0 ? "?" + string.Join('&', query) : ""),
                document,
                "application/yaml",
                ct).ConfigureAwait(false);

            if (parse.GetValue(json))
            {
                await environment.Out.WriteLineAsync(response).ConfigureAwait(false);
                return 0;
            }

            await PrintReportAsync(environment.Out, JsonDocument.Parse(response).RootElement).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static async Task PrintReportAsync(TextWriter output, JsonElement report)
    {
        var applied = report.GetProperty("applied").GetBoolean();
        var created = report.GetProperty("created").GetBoolean();
        await output.WriteLineAsync($"{(applied ? "" : "[dry run] ")}API '{report.GetProperty("api").GetString()}' {(created ? "created" : "updated")}.").ConfigureAwait(false);
        foreach (var (label, key) in new[] { ("changed", "apiChanges"), ("added", "added"), ("updated", "updated"), ("removed", "removed") })
        {
            foreach (var item in report.GetProperty(key).EnumerateArray())
            {
                await output.WriteLineAsync($"  {label,-8} {item.GetString()}").ConfigureAwait(false);
            }
        }

        await output.WriteLineAsync($"  {report.GetProperty("unchanged").GetInt32()} operation(s) unchanged.").ConfigureAwait(false);
        foreach (var warning in report.GetProperty("warnings").EnumerateArray())
        {
            await output.WriteLineAsync($"  warning: {warning.GetString()}").ConfigureAwait(false);
        }
    }
}
