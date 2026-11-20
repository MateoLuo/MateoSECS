using System.Buffers;
using System.Text;

namespace MateoSECS.Secs
{
    public sealed class SecsAscii : SecsItem
    {
        #region 构造函数
        private static readonly Encoding Ascii = Encoding.ASCII; 
        public static readonly SecsAscii Empty = new(string.Empty); 
        public static bool StrictAscii { get; set; } = false;
        public SecsAscii(string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (StrictAscii)
            {
                foreach (char c in value)
                {
                    if (c > 0x7F)
                        throw new ArgumentException($"Character '{c}' (0x{(int)c:X4}) is outside the 7-bit ASCII range.", nameof(value));
                }
            }

            _bytes = Ascii.GetBytes(value);

            if (_bytes.Length > MaxItemDataLength)
                throw new ArgumentOutOfRangeException(nameof(value), _bytes.Length, $"The ASCII length must not exceed {{MaxItemDataLength}} bytes.");

            Value = value;
        }
        internal SecsAscii(ReadOnlySpan<byte> raw)
        {
            _bytes = raw.ToArray();
            Value = Ascii.GetString(_bytes);
        }
        #endregion
        #region 实现
        public string Value { get; }
        private readonly byte[] _bytes;


        public override SecsFormat Format => SecsFormat.ASCII;

        public override int Count => _bytes.Length;

        public override int EncodedLength => GetHeaderLength(_bytes.Length) + _bytes.Length;

        public override void AppendSml(StringBuilder sb, int indentLevel)
        {
            ArgumentNullException.ThrowIfNull(sb);

            AppendIndent(sb, indentLevel);
            sb.Append("<A[").Append(_bytes.Length).Append("] \"");

            foreach (char c in Value)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20 || c == 0x7F)
                            sb.Append("\\x").Append(((int)c).ToString("X2"));
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.AppendLine("\">");
        }
        public override string GetString() => Value;
        public override ReadOnlyMemory<byte> GetBytes() => _bytes;

        public override void EncodeTo(IBufferWriter<byte> writer)
        {
            WriteItemHeader(writer, SecsFormat.ASCII, Count);
            if (_bytes.Length > 0)
                writer.Write(_bytes);
        }
        #endregion
        public static implicit operator string(SecsAscii item) => item.Value;
    }
}
