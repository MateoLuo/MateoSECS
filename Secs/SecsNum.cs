using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MateoSECS.Secs
{
    public sealed class SecsNum<TValue> : SecsItem where TValue : unmanaged, IConvertible
    {
        public SecsNum(SecsFormat format, params TValue[] values)
        {
            ValidateFormat(format);

            _format = format;
            _values = values ?? Array.Empty<TValue>();
            _elementSize = GetElementSize(format);
            int byteLength = checked(_values.Length * _elementSize);
            if (byteLength > MaxItemDataLength)
                throw new ArgumentOutOfRangeException(nameof(values), byteLength, $"data {MaxItemDataLength} 字节。");

        }
        private static void ValidateFormat(SecsFormat format)
        {
            Type expected = format switch
            {
                SecsFormat.U1 => typeof(byte),
                SecsFormat.U2 => typeof(ushort),
                SecsFormat.U4 => typeof(uint),
                SecsFormat.U8 => typeof(ulong),
                SecsFormat.I1 => typeof(sbyte),
                SecsFormat.I2 => typeof(short),
                SecsFormat.I4 => typeof(int),
                SecsFormat.I8 => typeof(long),
                SecsFormat.F4 => typeof(float),
                SecsFormat.F8 => typeof(double),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "This format code is not a numeric type"),
            };

            if (expected != typeof(TValue))
                throw new ArgumentException($"Format {format} need {expected.Name}，Actually is {typeof(TValue).Name}。", nameof(format));
        }
        #region 实现
        private readonly SecsFormat _format;
        /// <summary> 单个数据字节大小 </summary>
        private readonly int _elementSize;
        private readonly TValue[] _values;
        public TValue FirstValue => _values.Length > 0 ? _values[0] : throw new InvalidOperationException($"{GetSMLToken(_format)} items is empty,Unable get value");
        public override SecsFormat Format => _format;
        /// <summary> 数据区字节数 </summary>
        public int DataLength => _values.Length * _elementSize;

        public override int Count => _values.Length;

        public override int EncodedLength => GetHeaderLength(DataLength) + DataLength;

        public override void AppendSml(StringBuilder sb, int indentLevel)
        {
            ArgumentNullException.ThrowIfNull(sb);

            AppendIndent(sb, indentLevel);
            sb.Append('<').Append(GetSMLToken(_format)).Append('[').Append(_values.Length).Append(']');

            foreach (TValue v in _values)
            {
                sb.Append(' ');
                sb.Append(v switch
                {
                    float f => f.ToString("R", CultureInfo.InvariantCulture),
                    double d => d.ToString("R", CultureInfo.InvariantCulture),
                    _ => v.ToString(CultureInfo.InvariantCulture),
                });
            }

            sb.AppendLine(">");
        }

        public override void EncodeTo(IBufferWriter<byte> writer)
        {
            int byteLength = DataLength;
            WriteItemHeader(writer, _format, byteLength);

            if (byteLength == 0)
                return;

            Span<byte> span = writer.GetSpan(byteLength);
            WriteBigEndian(span);
            writer.Advance(byteLength);
        }
        public override T GetValue<T>() => ConvertTo<T>(FirstValue);
        public override T[] GetValues<T>()
        {
            if (typeof(T) == typeof(TValue))
                return (T[])(object)_values.Clone();

            var result = new T[_values.Length];
            for (int i = 0; i < _values.Length; i++)
                result[i] = ConvertTo<T>(_values[i]);

            return result;
        }

        private T ConvertTo<T>(TValue value) where T : struct, IConvertible
        {
            if (typeof(T) == typeof(TValue))
                return (T)(object)value;
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        #endregion
        #region 编码
        private void WriteBigEndian(Span<byte> dest)
        {
            switch (_values)
            {
                case byte[] a:
                    a.AsSpan().CopyTo(dest);
                    break;

                case sbyte[] a:
                    for (int i = 0; i < a.Length; i++)
                        dest[i] = unchecked((byte)a[i]);
                    break;

                case short[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteInt16BigEndian(dest[(i * 2)..], a[i]);
                    break;

                case ushort[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteUInt16BigEndian(dest[(i * 2)..], a[i]);
                    break;

                case int[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteInt32BigEndian(dest[(i * 4)..], a[i]);
                    break;

                case uint[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteUInt32BigEndian(dest[(i * 4)..], a[i]);
                    break;

                case long[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteInt64BigEndian(dest[(i * 8)..], a[i]);
                    break;

                case ulong[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteUInt64BigEndian(dest[(i * 8)..], a[i]);
                    break;

                case float[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteSingleBigEndian(dest[(i * 4)..], a[i]);
                    break;

                case double[] a:
                    for (int i = 0; i < a.Length; i++)
                        BinaryPrimitives.WriteDoubleBigEndian(dest[(i * 8)..], a[i]);
                    break;

                default:
                    throw new NotSupportedException($"不支持的数值类型 {typeof(TValue).Name}。");
            }
        }

        #endregion
    }

    public static class SecsNum
    {
        /// <summary>从大端字节流构造数值项。</summary>
        public static SecsItem FromBigEndian(SecsFormat format, ReadOnlySpan<byte> data)
        {
            int elementSize = SecsItem.GetElementSize(format);
            if (data.Length % elementSize != 0)
                throw new ArgumentException($"{format} 数据长度 {data.Length} 不是元素宽度 {elementSize} 的整数倍。", nameof(data));

            int count = data.Length / elementSize;

            switch (format)
            {
                case SecsFormat.U1:
                    return new SecsNum<byte>(format, data.ToArray());

                case SecsFormat.I1:
                    {
                        var values = new sbyte[count];
                        for (int i = 0; i < count; i++)
                            values[i] = unchecked((sbyte)data[i]);
                        return new SecsNum<sbyte>(format, values);
                    }

                case SecsFormat.U2:
                    {
                        var values = new ushort[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(i * 2)..]);
                        return new SecsNum<ushort>(format, values);
                    }

                case SecsFormat.I2:
                    {
                        var values = new short[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadInt16BigEndian(data[(i * 2)..]);
                        return new SecsNum<short>(format, values);
                    }

                case SecsFormat.U4:
                    {
                        var values = new uint[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadUInt32BigEndian(data[(i * 4)..]);
                        return new SecsNum<uint>(format, values);
                    }

                case SecsFormat.I4:
                    {
                        var values = new int[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadInt32BigEndian(data[(i * 4)..]);
                        return new SecsNum<int>(format, values);
                    }

                case SecsFormat.U8:
                    {
                        var values = new ulong[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadUInt64BigEndian(data[(i * 8)..]);
                        return new SecsNum<ulong>(format, values);
                    }

                case SecsFormat.I8:
                    {
                        var values = new long[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadInt64BigEndian(data[(i * 8)..]);
                        return new SecsNum<long>(format, values);
                    }

                case SecsFormat.F4:
                    {
                        var values = new float[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadSingleBigEndian(data[(i * 4)..]);
                        return new SecsNum<float>(format, values);
                    }

                case SecsFormat.F8:
                    {
                        var values = new double[count];
                        for (int i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadDoubleBigEndian(data[(i * 8)..]);
                        return new SecsNum<double>(format, values);
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "该格式码不是数值类型。");
            }
        }
    }
}
