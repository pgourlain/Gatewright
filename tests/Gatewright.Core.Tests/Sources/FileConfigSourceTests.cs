// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using Gatewright.Core.Configuration;
using Gatewright.Core.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatewright.Core.Tests.Sources;

public sealed class FileConfigSourceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("gatewright-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteConfig(long version, string? name = null)
    {
        var path = Path.Combine(_directory, name ?? "config.json");
        File.WriteAllText(path, GatewayConfigSerializer.Serialize(new GatewayConfig { Version = version }));
        return path;
    }

    private static FileConfigSource Source(string path) =>
        new(path, TimeSpan.FromMilliseconds(30), NullLogger<FileConfigSource>.Instance);

    private static async Task<GatewayConfig> NextAsync(IAsyncEnumerator<GatewayConfig> enumerator, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        var move = enumerator.MoveNextAsync().AsTask();
        (await Task.WhenAny(move, Task.Delay(Timeout.Infinite, cts.Token))).ShouldBe(move);
        (await move).ShouldBeTrue();
        return enumerator.Current;
    }

    [Fact]
    public async Task Watch_ExistingFile_YieldsConfigImmediately()
    {
        var path = WriteConfig(1);
        using var cts = new CancellationTokenSource();
        await using var enumerator = Source(path).WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        (await NextAsync(enumerator)).Version.ShouldBe(1);
    }

    [Fact]
    public async Task Watch_FileModified_YieldsNewConfigWithinFiveSeconds()
    {
        var path = WriteConfig(1);
        using var cts = new CancellationTokenSource();
        await using var enumerator = Source(path).WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextAsync(enumerator);

        WriteConfig(2);

        (await NextAsync(enumerator, TimeSpan.FromSeconds(5))).Version.ShouldBe(2);
    }

    [Fact]
    public async Task Watch_InvalidJson_IsSkippedAndNextValidVersionIsYielded()
    {
        var path = WriteConfig(1);
        using var cts = new CancellationTokenSource();
        await using var enumerator = Source(path).WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await NextAsync(enumerator);

        await File.WriteAllTextAsync(path, "{ not json");
        await Task.Delay(150);
        WriteConfig(3);

        (await NextAsync(enumerator)).Version.ShouldBe(3);
    }

    [Fact]
    public async Task Watch_FileCreatedLater_YieldsOnceItExists()
    {
        var path = Path.Combine(_directory, "late.json");
        using var cts = new CancellationTokenSource();
        await using var enumerator = Source(path).WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var pending = NextAsync(enumerator);

        await Task.Delay(100);
        WriteConfig(5, "late.json");

        (await pending).Version.ShouldBe(5);
    }

    [Fact]
    public async Task Watch_Cancelled_CompletesWithoutThrowing()
    {
        var path = WriteConfig(1);
        using var cts = new CancellationTokenSource();
        var seen = 0;

        var task = Task.Run(async () =>
        {
            await foreach (var _ in Source(path).WatchAsync(cts.Token))
            {
                Interlocked.Increment(ref seen);
            }
        });
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref seen) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await cts.CancelAsync();
        await task.WaitAsync(TimeSpan.FromSeconds(5));

        seen.ShouldBe(1);
    }

    [Fact]
    public void Constructor_NonPositiveInterval_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FileConfigSource("x", TimeSpan.Zero, NullLogger<FileConfigSource>.Instance));
    }
}
