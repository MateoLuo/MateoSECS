using System.Buffers;
using System.Text;

namespace MateoSECS.Secs
{
    public abstract class SecsItem
    {
        public const int MaxItemDataLength = 0xFFFFFF;
        public abstract SecsFormat Format { get; }
        public abstract int Count { get; }
        public abstract int EncodedLength { get; }
        public bool IsList => Format == SecsFormat.List;
        public bool IsEmpty => Count == 0;

        #region 访问器
        public virtual IReadOnlyList<SecsItem> Items => throw new InvalidOperationException("This item is not a list.");
        public virtual SecsItem this[int index] => Items[index];
        public virtual string GetString() => throw new InvalidOperationException("This item is not a string.");
        public virtual T GetValue<T>() where T : struct, IConvertible => throw new InvalidOperationException("This item is not a value type.");
        public virtual T[] GetValues<T>() where T : struct, IConvertible => throw new InvalidOperationException("This item is not an array of value types.");
        public SecsItem? TryGetItem(int index)
        {
            if (!IsList || index < 0 || index >= Count)
                return null;
            return Items[index];
        }
        #endregion
        #region 编码
        public abstract void EncodeTo(IBufferWriter<byte> writer);
        /// <summary>
        /// 写入头部
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="format"></param>
        /// <param name="length"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected static void WriteItemHeader(IBufferWriter<byte> writer, SecsFormat format, int length)
        {
            ArgumentNullException.ThrowIfNull(writer);
            //检查长度是否在有效范围内 长度不能超过3字节
            if (length is < 0 or > MaxItemDataLength)
                throw new ArgumentOutOfRangeException(nameof(length), length, $"Item length must be between 0 and {MaxItemDataLength}.");
            //首先计算长度所需的字节数
            int lengthBytes = length switch
            {
                <= 0xFF => 1,
                <= 0xFFFF => 2,
                _ => 3,
            };
            //根据长度字节数和格式生成头部字节
            Span<byte> span = writer.GetSpan(1 + lengthBytes);
            //写入头部字节
            span[0] = (byte)(((byte)format << 2) | lengthBytes);
            //根据长度字节数写入长度
            switch (lengthBytes)
            {
                case 1:
                    span[1] = (byte)length;
                    break;
                case 2:
                    span[1] = (byte)(length >> 8);
                    span[2] = (byte)length;
                    break;
                default:
                    span[1] = (byte)(length >> 16);
                    span[2] = (byte)(length >> 8);
                    span[3] = (byte)length;
                    break;
            }
            //通知writer已写入的字节数
            writer.Advance(1 + lengthBytes);
        }
        /// <summary>
        /// 获取头部所占用的字节
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        protected static int GetHeaderLength(int length) => length switch
        {
            <= 0xFF => 2,
            <= 0xFFFF => 3,
            _ => 4,
        };
        #endregion
        #region SML
        public abstract void AppendSml(StringBuilder sb, int indentLevel);
        /// <summary>返回本 Item 的 SML 文本。</summary>
        public string ToSml()
        {
            var sb = new StringBuilder(128);
            AppendSml(sb, 0);
            return sb.ToString();
        }
        /// <summary>SML 缩进（两空格一级）。</summary>
        protected static void AppendIndent(StringBuilder sb, int indentLevel) => sb.Append(' ', indentLevel * 2);

        /// <summary>SML 中的格式关键字</summary>
        public static string GetSmlToken(SecsFormat format) => format switch
        {
            SecsFormat.List => "L",
            SecsFormat.Binary => "B",
            SecsFormat.Boolean => "BOOLEAN",
            SecsFormat.ASCII => "A",
            SecsFormat.JIS8 => "J",
            SecsFormat.CHAR2 => "CHAR2",
            SecsFormat.I1 => "I1",
            SecsFormat.I2 => "I2",
            SecsFormat.I4 => "I4",
            SecsFormat.I8 => "I8",
            SecsFormat.F4 => "F4",
            SecsFormat.F8 => "F8",
            SecsFormat.U1 => "U1",
            SecsFormat.U2 => "U2",
            SecsFormat.U4 => "U4",
            SecsFormat.U8 => "U8",
            _ => $"0x{(byte)format:X2}",
        };
        /// <summary>单个元素的字节宽度（LIST / ASCII / Binary 为 1）。</summary>
        public static int GetElementSize(SecsFormat format) => format switch
        {
            SecsFormat.I1 or SecsFormat.U1 or SecsFormat.Binary or SecsFormat.Boolean or SecsFormat.ASCII or SecsFormat.JIS8 => 1,
            SecsFormat.I2 or SecsFormat.U2 or SecsFormat.CHAR2 => 2,
            SecsFormat.I4 or SecsFormat.U4 or SecsFormat.F4 => 4,
            SecsFormat.I8 or SecsFormat.U8 or SecsFormat.F8 => 8,
            SecsFormat.List => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown Secs Format"),
        };
        /// <summary>判断格式码是否合法。</summary>
        public static bool IsDefinedFormat(byte formatCode) => Enum.IsDefined(typeof(SecsFormat), (SecsFormat)formatCode);
        public override string ToString() => ToSml();
        #endregion
    }
}
