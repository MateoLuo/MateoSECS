using System.Buffers;
using System.Globalization;
using System.Text;

namespace MateoSECS.Secs
{
    public sealed class SecsBoolean : SecsItem
    {
        public static readonly SecsBoolean True = new(true);
        public static readonly SecsBoolean False = new(false);

        private readonly bool[] _values;

        public SecsBoolean(params bool[] values)
        {
            _values = values ?? Array.Empty<bool>();

            if (_values.Length > MaxItemDataLength)
                throw new ArgumentOutOfRangeException(nameof(values), _values.Length, "Too many Boolean elements.");
        }

        internal SecsBoolean(ReadOnlySpan<byte> raw)
        {
            _values = new bool[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                _values[i] = raw[i] != 0x00;
        }

        public ReadOnlyMemory<bool> Values => _values;

        public override SecsFormat Format => SecsFormat.Boolean;

        public override int Count => _values.Length;

        public override int EncodedLength => GetHeaderLength(_values.Length) + _values.Length;

        public override T GetValue<T>()
        {
            if (_values.Length == 0)
                throw new InvalidOperationException("Boolean item is empty; cannot get value.");

            return (T)Convert.ChangeType(_values[0], typeof(T), CultureInfo.InvariantCulture);
        }

        public override T[] GetValues<T>()
        {
            var result = new T[_values.Length];
            for (int i = 0; i < _values.Length; i++)
                result[i] = (T)Convert.ChangeType(_values[i], typeof(T), CultureInfo.InvariantCulture);

            return result;
        }

        public override void EncodeTo(IBufferWriter<byte> writer)
        {
            WriteItemHeader(writer, SecsFormat.Boolean, _values.Length);

            if (_values.Length == 0)
                return;

            Span<byte> span = writer.GetSpan(_values.Length);
            for (int i = 0; i < _values.Length; i++)
                span[i] = _values[i] ? (byte)0x01 : (byte)0x00;

            writer.Advance(_values.Length);
        }

        public override void AppendSml(StringBuilder sb, int indentLevel)
        {
            AppendIndent(sb, indentLevel);
            sb.Append("<BOOLEAN[").Append(_values.Length).Append(']');

            foreach (bool v in _values)
                sb.Append(v ? " True" : " False");

            sb.AppendLine(">");
        }
    }
}