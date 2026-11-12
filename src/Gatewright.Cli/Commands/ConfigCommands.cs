// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.CommandLine;
using System.Text.Json;
using Gatewright.Abstractions;
using Gatewright.Core.Configuration;
using Gatewright.Core.Policies;
using Gatewright.Core.Validation;
using Gatewright.Policies;

namespace Gatewright.Cli.Commands;

internal static class ConfigCommands
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Command Create(CliEnvironment environment)
    {
        var config = new Command("config", "Export, import and validate whole configurations (GitOps).");
        config.Subcommands.Add(CreateExport(environment));
        config.Subcommands.Add(CreateImport(environment));
        config.Subcommands.Add(CreateValidate(environment));
        return config;
    }

    private static Command CreateExport(CliEnvironment environment)
    {
        var env = new Option<string?>("--env") { Description = "Export the active publication of this environment. Default: the draft." };
        var output = new Option<string?>("--output", "-o") { Description = "Write to this file instead of the standard output." };
        var command = new Command("export", "Export the configuration as a declarative JSON document.") { env, output };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var query = parse.GetValue(env) is { } value ? "?environment=" + Uri.EscapeDataString(value) : "";
            var document = await CliApp.CreateClient(environment).SendAsync(HttpMethod.Get, "admin/v1/export" + query, cancellationToken: ct).ConfigureAwait(false);
            var pretty = JsonSerializer.Serialize(JsonElement.Parse(document), Indented);
            if (parse.GetValue(output) is { } path)
            {
                await File.WriteAllTextAsync(path, pretty + Environment.NewLine, ct).ConfigureAwait(false);
                await environment.Out.WriteLineAsync($"Exported to {path}.").ConfigureAwait(false);
            }
            else
            {
                await environment.Out.WriteLineAsync(pretty).ConfigureAwait(false);
            }

            return 0;
        }));
        return command;
    }

    private static Command CreateImport(CliEnvironment environment)
    {
        var file = new Argument<string>("file") { Description = "A document produced by 'config export' ('-' for the standard input)." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Validate on the server without changing the draft." };
        var command = new Command("import", "Replace the draft configuration with a declarative document.") { file, dryRun };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var document = await CliApp.ReadInputAsync(environment, parse.GetValue(file)!, ct).ConfigureAwait(false);
            var response = await CliApp.CreateClient(environment).SendAsync(
                HttpMethod.Post,
                "admin/v1/import" + (parse.GetValue(dryRun) ? "?dryRun=true" : ""),
                document,
                cancellationToken: ct).ConfigureAwait(false);
            var result = JsonElement.Parse(response);
            await environment.Out.WriteLineAsync(
                result.GetProperty("applied").GetBoolean()
                    ? $"Imported. Configuration hash {result.GetProperty("hash").GetString()}."
                    : $"Valid (dry run). Configuration hash {result.GetProperty("hash").GetString()}.").ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static Command CreateValidate(CliEnvironment environment)
    {
        var file = new Argument<string>("file") { Description = "A configuration or a document produced by 'config export' ('-' for the standard input)." };
        var allowUnknown = new Option<bool>("--allow-unknown-policies") { Description = "Do not fail on policy types this CLI does not know (custom policies)." };
        var command = new Command("validate", "Validate a configuration offline, with the same rules as the control plane (structure, references, built-in policy schemas).") { file, allowUnknown };
        command.SetAction((parse, ct) => CliApp.GuardAsync(environment, async () =>
        {
            var text = await CliApp.ReadInputAsync(environment, parse.GetValue(file)!, ct).ConfigureAwait(false);
            GatewayConfig config;
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                config = GatewayConfigSerializer.Deserialize(root.TryGetProperty("config", out var inner) ? inner.GetRawText() : text);
            }
            catch (JsonException ex)
            {
                throw new CliException($"Not a valid configuration document: {ex.Message}");
            }

            var errors = Validate(config, parse.GetValue(allowUnknown));
            if (errors.Count == 0)
            {
                await environment.Out.WriteLineAsync($"Valid. {config.Apis.Count} API(s), {config.Products.Count} product(s), {config.Subscriptions.Count} subscription(s).").ConfigureAwait(false);
                return 0;
            }

            foreach (var error in errors)
            {
                await environment.Error.WriteLineAsync(error).ConfigureAwait(false);
            }

            await environment.Error.WriteLineAsync($"{errors.Count} error(s).").ConfigureAwait(false);
            return 1;
        }));
        return command;
    }

    internal static List<string> Validate(GatewayConfig config, bool allowUnknownPolicies)
    {
        var registry = new PolicyRegistry(BuiltInPolicies.Create());
        var errors = new GatewayConfigValidator(allowUnknownPolicies ? null : registry).Validate(config).Select(e => e.ToString()).ToList();

        void Check(string where, PolicyReference reference)
        {
            if (reference.Type is { } type && registry.TryGet(type, out _))
            {
                errors.AddRange(registry.Validate(type, reference.Config).Select(e => $"[policy.config] {where} policy '{type}' at '{e.JsonPath}': {e.Message}"));
            }
        }

        void CheckSet(string where, PolicySet set)
        {
            foreach (var phase in new[] { PolicyPhase.Inbound, PolicyPhase.Backend, PolicyPhase.Outbound, PolicyPhase.OnError })
            {
                foreach (var reference in set.Get(phase))
                {
                    Check($"{where}.{phase}", reference);
                }
            }
        }

        CheckSet("globalPolicies", config.GlobalPolicies);
        CheckSet("gatewayPolicies", config.GatewayPolicies);
        foreach (var (name, reference) in config.Policies)
        {
            Check($"policies[{name}]", reference);
        }

        foreach (var product in config.Products)
        {
            CheckSet($"products[{product.Name}]", product.Policies);
        }

        foreach (var api in config.Apis)
        {
            CheckSet($"apis[{api.Name}]", api.Policies);
            foreach (var operation in api.Operations)
            {
                CheckSet($"apis[{api.Name}].operations[{operation.Name}]", operation.Policies);
            }
        }

        return errors;
    }
}
