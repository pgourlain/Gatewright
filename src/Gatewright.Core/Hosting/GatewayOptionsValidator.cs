// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Options;

namespace Gatewright.Core.Hosting;

/// <summary>Validates <see cref="GatewayOptions"/> at startup.</summary>
public sealed class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    private static readonly TimeSpan MaxConfigPollInterval = TimeSpan.FromDays(1);

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!Enum.IsDefined(options.Mode))
        {
            failures.Add($"Gateway:Mode '{options.Mode}' is not valid. Expected All, ControlPlane or DataPlane.");
        }

        if (!Enum.IsDefined(options.ConfigSource))
        {
            failures.Add($"Gateway:ConfigSource '{options.ConfigSource}' is not valid. Expected File or Database.");
        }

        if (options.Mode == GatewayMode.All
            && options.ConfigSource == ConfigSourceKind.File
            && string.IsNullOrWhiteSpace(options.ConfigFile))
        {
            failures.Add("Gateway:ConfigFile is required when Gateway:Mode is All and Gateway:ConfigSource is File.");
        }

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            failures.Add("Gateway:Environment must not be empty.");
        }

        if (options.ConfigPollInterval <= TimeSpan.Zero || options.ConfigPollInterval > MaxConfigPollInterval)
        {
            // A bare number is read as days by TimeSpan ("30000" is 30000 days), which is the usual way to end up here.
            failures.Add($"Gateway:ConfigPollInterval must be greater than zero and at most {MaxConfigPollInterval} (got {options.ConfigPollInterval}). Use the hh:mm:ss format, for example 00:00:30 for 30 seconds.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
