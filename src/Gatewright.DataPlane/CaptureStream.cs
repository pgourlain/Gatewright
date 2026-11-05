// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.DataPlane;

/// <summary>Write-through stream that keeps a bounded copy of what is written.</summary>
internal sealed class CaptureStream : Stream
{
    private readonly Stream _inner;
    private readonly int _max;
    private MemoryStream? _copy = new();

    public CaptureStream(Stream inner, int maxBytes)
    {
        _inner = inner;
        _max = maxBytes;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public bool Overflowed => _copy is null;

    public byte[]? GetCopy() => _copy?.ToArray();

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Record(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Record(buffer);
        _inner.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Record(buffer.Span);
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Record(ReadOnlySpan<byte> data)
    {
        if (_copy is null)
        {
            return;
        }

        if (_copy.Length + data.Length > _max)
        {
            _copy = null;
            return;
        }

        _copy.Write(data);
    }
}
