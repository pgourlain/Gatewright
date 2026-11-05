// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var orders = new[]
{
    new Order(1, "Widget", 3),
    new Order(2, "Gadget", 1),
};

app.MapGet("/", () => Results.Ok(orders));
app.MapGet("/{id:int}", (int id) =>
    orders.FirstOrDefault(o => o.Id == id) is { } order ? Results.Ok(order) : Results.NotFound());

app.Run();

internal sealed record Order(int Id, string Product, int Quantity);
