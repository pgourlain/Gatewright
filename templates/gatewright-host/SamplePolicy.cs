using System.ComponentModel;
using Gatewright.Abstractions;
using Gatewright.Sdk;

namespace Acme.Gateway;

/// <summary>Configuration of the <c>acme.requireHeader</c> policy; its JSON Schema is generated from this type.</summary>
public sealed record SamplePolicyConfig
{
    [Description("Name of the request header that must be present.")]
    public required string Header { get; init; }
}

/// <summary>Rejects requests that do not carry a given header. Replace it with your own logic.</summary>
[GatewayPolicy("acme.requireHeader", Phases = PolicyPhase.Inbound)]
public sealed class SamplePolicy : GatewayPolicy<SamplePolicyConfig>
{
    protected override IPolicyInstance Create(SamplePolicyConfig config, PolicyCompilationContext context)
    {
        // Runs once per configuration snapshot: do the expensive work here, keep the per-request code cheap.
        var header = config.Header;
        return PolicyInstance.From(request =>
            request.HttpContext.Request.Headers.ContainsKey(header)
                ? PolicyResult.Continue
                : PolicyResult.Reject(400, "missing_header", $"The '{header}' header is required."));
    }
}
