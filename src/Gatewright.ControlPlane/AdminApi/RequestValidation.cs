// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Gatewright.ControlPlane.AdminApi;

/// <summary>Shape validation of Admin API requests. Cross-object consistency is checked when a configuration is published or validated.</summary>
internal static partial class RequestValidation
{
    public static bool IsValidName(string? name) => name is not null && NameRegex().IsMatch(name);

    public static IResult Problem(IDictionary<string, string[]> errors) => Results.ValidationProblem(errors);

    public static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult Conflict(string message) => Results.Problem(message, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    public static IResult NotFound(string what, string name) =>
        Results.Problem($"{what} '{name}' does not exist.", statusCode: StatusCodes.Status404NotFound, title: "Not found");

    public static string? CheckName(string field, string? value, Dictionary<string, string[]> errors)
    {
        if (!IsValidName(value))
        {
            errors[field] = ["Must start with a letter or digit and contain only letters, digits, '.', '_' or '-' (max 128 characters)."];
        }

        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex NameRegex();
}
