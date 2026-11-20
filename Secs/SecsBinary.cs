using System.Buffers;
using System.Globalization;
using System.Text;

namespace MateoSECS.Secs
{
    public sealed class SecsBinary : SecsItem
    {
        /// <summary>Shared empty Binary: &lt;B[0]&gt;.</summary>
        public static readonly SecsBinary Empty = new(ReadOnlyMemory<byte>.Empty);

        /// <summary>Commonly used ACK = 0x00.</summary>
        public static SecsBinary Ack => new(new byte[] { 0x00 });

        private readonly ReadOnlyMemory<byte> _data;

        public SecsBinary(params byte[] data)
            : this((ReadOnlyMemory<byte>)(data ?? Array.Empty<byte>()))
        {
        }

        public SecsBinary(ReadOnlyMemory<byte> data)
        {
            if (data.Length > MaxItemDataLength)
                throw new ArgumentOutOfRangeException(nameof(data), data.Length, $"Binary length must not exceed {MaxItemDataLength} bytes.");

            _data = data;
        }

        public override SecsFormat Format => SecsFormat.Binary;

        public override int Count => _data.Length;

        public override int EncodedLength => GetHeaderLength(_data.Length) + _data.Length;

        public override ReadOnlyMemory<byte> GetBytes() => _data;

        public override T GetValue<T>()
        {
            if (_data.Length == 0)
                throw new InvalidOperationException("Binary item is empty; cannot get value.");

            return (T)Convert.ChangeType(_data.Span[0], typeof(T), CultureInfo.InvariantCulture);
        }

        public override T[] GetValues<T>()
        {
            var result = new T[_data.Length];
            ReadOnlySpan<byte> span = _data.Span;
            for (int i = 0; i < span.Length; i++)
                result[i] = (T)Convert.ChangeType(span[i], typeof(T), CultureInfo.InvariantCulture);

            return result;
        }

        public override void EncodeTo(IBufferWriter<byte> writer)
        {
            WriteItemHeader(writer, SecsFormat.Binary, _data.Length);
            if (_data.Length > 0)
                writer.Write(_data.Span);
        }

        public override void AppendSml(StringBuilder sb, int indentLevel)
        {
            ArgumentNullException.ThrowIfNull(sb);

            AppendIndent(sb, indentLevel);
            sb.Append("<B[").Append(_data.Length).Append(']');

            ReadOnlySpan<byte> span = _data.Span;
            for (int i = 0; i < span.Length; i++)
                sb.Append(" 0x").Append(span[i].ToString("X2"));

            sb.AppendLine(">");
        }
    }

}