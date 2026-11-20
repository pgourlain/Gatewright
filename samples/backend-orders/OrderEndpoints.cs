// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System.Diagnostics;

internal static partial class OrderEndpoints
{
    public static void MapCancellationDemo(this WebApplication app)
    {
        app.MapGet("/slow", async Task<IResult> (CancellationToken cancellationToken) =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                return TypedResults.NoContent();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Activity.Current?.SetTag("orders.work.cancelled", true);
                LogCancelled(app.Logger);
                return TypedResults.Empty;
            }
        })
        .WithName("SimulateSlowOrderWork")
        .WithSummary("Demonstrate cooperative request cancellation")
        .WithDescription("Simulates 15 seconds of order work and stops when the HTTP client disconnects.")
        .Produces(StatusCodes.Status204NoContent);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order work cancelled because the request was aborted.")]
    private static partial void LogCancelled(ILogger logger);
}
