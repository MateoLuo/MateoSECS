using System.Buffers;
using System.Text;

namespace MateoSECS.Secs
{
    public sealed class SecsJis8 : SecsItem
    {
        private static readonly Encoding Jis8 = ResolveEncoding();

        private readonly byte[] _bytes;

        public SecsJis8(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _bytes = Jis8.GetBytes(value);
            Value = value;
        }

        internal SecsJis8(ReadOnlySpan<byte> raw)
        {
            _bytes = raw.ToArray();
            Value = Jis8.GetString(_bytes);
        }

        public string Value { get; }

        public override SecsFormat Format => SecsFormat.JIS8;

        public override int Count => _bytes.Length;

        public override int EncodedLength => GetHeaderLength(_bytes.Length) + _bytes.Length;

        public override string GetString() => Value;

        public override ReadOnlyMemory<byte> GetBytes() => _bytes;

        public override void EncodeTo(IBufferWriter<byte> writer)
        {
            WriteItemHeader(writer, SecsFormat.JIS8, _bytes.Length);
            if (_bytes.Length > 0)
                writer.Write(_bytes);
        }

        public override void AppendSml(StringBuilder sb, int indentLevel)
        {
            AppendIndent(sb, indentLevel);
            sb.Append("<J[").Append(_bytes.Length).Append("] \"").Append(Value).AppendLine("\">");
        }

        private static Encoding ResolveEncoding()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProviderShim.Instance);
                return Encoding.GetEncoding(932);
            }
            catch
            {
                return Encoding.Latin1;
            }
        }

        /// <summary>
        /// 避免强制依赖 System.Text.Encoding.CodePages 包：反射探测，取不到就用 Latin1。
        /// </summary>
        private static class CodePagesEncodingProviderShim
        {
            public static EncodingProvider Instance { get; } = Resolve();

            private static EncodingProvider Resolve()
            {
                Type? type = Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages");
                object? instance = type?.GetProperty("Instance")?.GetValue(null);
                return instance as EncodingProvider ?? new NullProvider();
            }

            private sealed class NullProvider : EncodingProvider
            {
                public override Encoding? GetEncoding(int codepage) => null;

                public override Encoding? GetEncoding(string name) => null;
            }
        }
    }
}
