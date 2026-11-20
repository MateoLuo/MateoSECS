using MateoSECS.Secs;
using System;
using System.Buffers;

namespace Mateo.SECS;

/// <summary>
/// SECS-II encoder: SecsItem / SecsMessage -> byte stream.
/// Encoding is pure recursive writing, stateless, and thread-safe.
/// </summary>
public static class SecsEncoder
{
    /// <summary>Encode a single Item (including the Item Header) into a byte array.</summary>
    public static byte[] Encode(this SecsItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Length is known up front, so allocate once and avoid reallocation mid-encode.
        var buffer = new byte[item.EncodedLength];
        var writer = new FixedBufferWriter(buffer);
        item.EncodeTo(writer);

        if (writer.WrittenCount != buffer.Length)
            throw new InvalidOperationException($"Encoded length mismatch: expected {buffer.Length}, actual {writer.WrittenCount}.");

        return buffer;
    }

    /// <summary>Encode a single Item into the specified writer.</summary>
    public static void EncodeTo(this SecsItem item, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(writer);
        item.EncodeTo(writer);
    }

    /// <summary>Encode the SECS-II message body (header-only messages return an empty array).</summary>
    public static byte[] EncodeBody(SecsMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.SecsItem is null ? Array.Empty<byte>() : message.SecsItem.Encode();
    }

    /// <summary>Compute the encoded length of an Item without allocating memory (for flow control and length validation).</summary>
    public static int GetEncodedLength(SecsItem? item) => item?.EncodedLength ?? 0;

    /// <summary>
    /// Lightweight IBufferWriter backed by a fixed array, avoiding the copy overhead of ArrayBufferWriter.
    /// </summary>
    private sealed class FixedBufferWriter : IBufferWriter<byte>
    {
        private readonly byte[] _buffer;

        public FixedBufferWriter(byte[] buffer) => _buffer = buffer;

        public int WrittenCount { get; private set; }

        public void Advance(int count)
        {
            if (count < 0 || WrittenCount + count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Encode write out of bounds.");

            WrittenCount += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(WrittenCount);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(WrittenCount);
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint > _buffer.Length - WrittenCount)
                throw new InvalidOperationException($"Buffer too small: {_buffer.Length - WrittenCount} remaining, {sizeHint} required.");
        }
    }
}