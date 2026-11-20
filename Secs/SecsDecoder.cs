using System;
using System.Collections.Generic;
using System.Text;

namespace MateoSECS.Secs
{
    public class SecsDecodeException : Exception
    {
        public SecsDecodeException(string message) : base(message)
        {
        }

        public SecsDecodeException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>Byte offset in the message body where the error occurred (-1 means unknown).</summary>
        public int Offset { get; init; } = -1;

        /// <summary>Hex fragment around the error location, handy for pasting into logs during field troubleshooting.</summary>
        public string? HexDump { get; init; }

        public override string ToString()
        {
            var sb = new StringBuilder(base.ToString());
            if (Offset >= 0)
                sb.Append(" | Offset=").Append(Offset);
            if (!string.IsNullOrEmpty(HexDump))
                sb.Append(" | Near=").Append(HexDump);

            return sb.ToString();
        }
    }

    public static class SecsDecoder
    {
        public const int MaxNestingDepth = 32;

        /// <summary>Maximum number of bytes in the hex dump around the error site.</summary>
        private const int HexDumpWindow = 16;

        // ---------------- Public entry points ----------------

        /// <summary>
        /// Decode the message body; the entire buffer must be consumed exactly.
        /// </summary>
        /// <param name="body">Message body.</param>
        /// <returns></returns>
        /// <exception cref="SecsDecodeException">The message body is empty and no Item can be decoded.</exception>
        public static SecsItem Decode(this ReadOnlySpan<byte> body)
        {
            if (body.IsEmpty)
                throw new SecsDecodeException("Message body is empty; cannot decode Item.") { Offset = 0 };

            int offset = 0;
            SecsItem item = DecodeItem(body, ref offset, 0);

            if (offset != body.Length)
            {
                throw new SecsDecodeException($"Message body contains {body.Length - offset} trailing byte(s) of redundant data.")
                {
                    Offset = offset,
                    HexDump = Hex(body, offset),
                };
            }

            return item;
        }

        /// <summary>
        /// Decode a single Item and output the number of bytes actually consumed (the buffer may contain trailing data).
        /// </summary>
        /// <param name="body"></param>
        /// <param name="consumed"></param>
        /// <returns></returns>
        public static SecsItem Decode(this ReadOnlySpan<byte> body, out int consumed)
        {
            int offset = 0;
            SecsItem item = DecodeItem(body, ref offset, 0);
            consumed = offset;
            return item;
        }
        /// <summary>
        /// Decode a single Item starting at the supplied offset, and output the number of bytes actually consumed
        /// (the buffer may contain trailing data).
        /// </summary>
        /// <param name="body"></param>
        /// <param name="consumed"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static SecsItem Decode(this ReadOnlySpan<byte> body, out int consumed, int offset = 0)
        {
            SecsItem item = DecodeItem(body, ref offset, 0);
            consumed = offset;
            return item;
        }
        /// <summary>
        /// Non-throwing decode entry point. Recommended for receive loops:
        /// on failure, log it and reply S9F7 rather than letting the receive task crash.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> body, out SecsItem? item, out string? error)
        {
            try
            {
                item = Decode(body);
                error = null;
                return true;
            }
            catch (SecsDecodeException ex)
            {
                item = null;
                error = ex.ToString();
                return false;
            }
        }

        /// <summary>Decode the message body into a SecsItem; an empty body returns null (header-only messages are valid).</summary>
        public static SecsItem? DecodeBodyOrNull(ReadOnlySpan<byte> body)
            => body.IsEmpty ? null : Decode(body);

        // ---------------- Recursive implementation ----------------

        private static SecsItem DecodeItem(ReadOnlySpan<byte> buffer, ref int offset, int depth)
        {
            if (depth > MaxNestingDepth)
                throw new SecsDecodeException($"LIST nesting depth exceeds the limit of {MaxNestingDepth}.") { Offset = offset };

            if (offset >= buffer.Length)
                throw new SecsDecodeException("Insufficient data while reading the Item header.") { Offset = offset };

            int headerStart = offset;
            byte formatByte = buffer[offset];
            var format = (SecsFormat)(byte)(formatByte >> 2);
            int lengthBytes = formatByte & 0x03;

            if (lengthBytes == 0)
            {
                throw new SecsDecodeException($"Item length byte count is 0 (first byte 0x{formatByte:X2}), which is invalid.")
                {
                    Offset = headerStart,
                    HexDump = Hex(buffer, headerStart),
                };
            }

            if (!SecsItem.IsDefinedFormat((byte)format))
            {
                throw new SecsDecodeException($"Unknown SECS-II format code 0x{(byte)format:X2} (first byte 0x{formatByte:X2}).")
                {
                    Offset = headerStart,
                    HexDump = Hex(buffer, headerStart),
                };
            }

            if (offset + 1 + lengthBytes > buffer.Length)
            {
                throw new SecsDecodeException($"Insufficient data while reading the length field of {SecsItem.GetSMLToken(format)}.")
                {
                    Offset = headerStart,
                    HexDump = Hex(buffer, headerStart),
                };
            }

            int length = 0;
            for (int i = 0; i < lengthBytes; i++)
                length = (length << 8) | buffer[offset + 1 + i];

            offset += 1 + lengthBytes;

            if (format == SecsFormat.List)
                return DecodeList(buffer, ref offset, length, depth);

            if (length < 0 || offset + length > buffer.Length)
            {
                throw new SecsDecodeException(
                    $"{SecsItem.GetSMLToken(format)} declares length {length}, which exceeds the remaining {buffer.Length - offset} byte(s).")
                {
                    Offset = headerStart,
                    HexDump = Hex(buffer, headerStart),
                };
            }

            int dataStart = offset;
            ReadOnlySpan<byte> data = buffer.Slice(dataStart, length);
            offset += length;

            return CreateValueItem(format, data, dataStart, buffer);
        }

        private static SecsItem DecodeList(ReadOnlySpan<byte> buffer, ref int offset, int itemCount, int depth)
        {
            // The LIST length field is the number of child items, not the byte count.
            // Each child item is at least 2 bytes (1 format byte + 1 length byte); use that
            // lower bound to reject malformed messages claiming millions of children
            // before allocating the List capacity.
            int remaining = buffer.Length - offset;
            if (itemCount < 0 || (long)itemCount * 2 > remaining)
            {
                throw new SecsDecodeException($"LIST declares {itemCount} child item(s), which is inconsistent with the remaining {remaining} byte(s).")
                {
                    Offset = offset,
                    HexDump = Hex(buffer, offset),
                };
            }

            if (itemCount == 0)
                return SecsList.Empty;

            var items = new List<SecsItem>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                try
                {
                    items.Add(DecodeItem(buffer, ref offset, depth + 1));
                }
                catch (SecsDecodeException ex)
                {
                    throw new SecsDecodeException($"Failed to decode child item #{i} of LIST: {ex.Message}", ex)
                    {
                        Offset = ex.Offset,
                        HexDump = ex.HexDump,
                    };
                }
            }

            return new SecsList(items);
        }

        /// <summary>Construct the concrete value-type Item based on the format code.</summary>
        private static SecsItem CreateValueItem(
            SecsFormat format,
            ReadOnlySpan<byte> data,
            int dataStart,
            ReadOnlySpan<byte> whole)
        {
            switch (format)
            {
                case SecsFormat.ASCII:
                    return data.IsEmpty ? SecsAscii.Empty : new SecsAscii(data);

                case SecsFormat.JIS8:
                    return new SecsJis8(data);

                case SecsFormat.Binary:
                    return data.IsEmpty ? SecsBinary.Empty : new SecsBinary(data.ToArray());

                case SecsFormat.Boolean:
                    return new SecsBoolean(data);

                case SecsFormat.U1:
                case SecsFormat.U2:
                case SecsFormat.U4:
                case SecsFormat.U8:
                case SecsFormat.I1:
                case SecsFormat.I2:
                case SecsFormat.I4:
                case SecsFormat.I8:
                case SecsFormat.F4:
                case SecsFormat.F8:
                    {
                        int elementSize = SecsItem.GetElementSize(format);
                        if (data.Length % elementSize != 0)
                        {
                            throw new SecsDecodeException(
                                $"{SecsItem.GetSMLToken(format)} data length {data.Length} is not a multiple of the element width {elementSize}.")
                            {
                                Offset = dataStart,
                                HexDump = Hex(whole, dataStart),
                            };
                        }

                        try
                        {
                            return SecsNum.FromBigEndian(format, data);
                        }
                        catch (Exception ex) when (ex is not SecsDecodeException)
                        {
                            throw new SecsDecodeException($"{SecsItem.GetSMLToken(format)} numeric parsing failed: {ex.Message}", ex)
                            {
                                Offset = dataStart,
                                HexDump = Hex(whole, dataStart),
                            };
                        }
                    }

                case SecsFormat.List:
                    // LIST is handled by DecodeList and should never reach here.
                    throw new SecsDecodeException("Internal error: LIST should not go through the value-type branch.") { Offset = dataStart };

                default:
                    throw new SecsDecodeException($"Unsupported format code 0x{(byte)format:X2}.")
                    {
                        Offset = dataStart,
                        HexDump = Hex(whole, dataStart),
                    };
            }
        }

        // ---------------- Diagnostic helpers ----------------

        /// <summary>Extract a hex fragment around the error location.</summary>
        private static string Hex(ReadOnlySpan<byte> buffer, int offset)
        {
            if (buffer.IsEmpty)
                return string.Empty;

            int start = Math.Max(0, offset);
            if (start >= buffer.Length)
                start = buffer.Length - 1;

            int length = Math.Min(HexDumpWindow, buffer.Length - start);
            ReadOnlySpan<byte> window = buffer.Slice(start, length);

            var sb = new StringBuilder(length * 3 + 8);
            for (int i = 0; i < window.Length; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(window[i].ToString("X2"));
            }

            if (start + length < buffer.Length)
                sb.Append(" ...");

            return sb.ToString();
        }

        /// <summary>Hex string of the whole message body, for "raw message retention" in logs.</summary>
        public static string ToHexString(this ReadOnlySpan<byte> buffer, bool withSpace = true)
        {
            if (buffer.IsEmpty)
                return string.Empty;

            var sb = new StringBuilder(buffer.Length * (withSpace ? 3 : 2));
            for (int i = 0; i < buffer.Length; i++)
            {
                if (withSpace && i > 0)
                    sb.Append(' ');
                sb.Append(buffer[i].ToString("X2"));
            }

            return sb.ToString();
        }

        public static SecsMsg ToSecsItem(this string sml)
        {
            throw new NotImplementedException();
        }
    }
}