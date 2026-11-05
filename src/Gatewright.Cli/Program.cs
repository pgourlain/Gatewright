// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Cli;

internal static class CliEntryPoint
{
    private static async Task<int> Main(string[] args) => await CliApp.RunAsync(args, new CliEnvironment());
}
