// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using OpenTelemetry;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
        .UseOtlpExporter();
}

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var orders = new[]
{
    new Order(1, "Widget", 3),
    new Order(2, "Gadget", 1),
};

app.MapGet("/", () => Results.Ok(orders));
app.MapGet("/{id:int}", (int id) =>
    orders.FirstOrDefault(o => o.Id == id) is { } order ? Results.Ok(order) : Results.NotFound());
app.MapCancellationDemo();

app.Run();

internal sealed record Order(int Id, string Product, int Quantity);
