using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace MateoSECS.Secs
{
    /// <summary>
    /// 缺乏构造这个类的能力
    /// </summary>
    public sealed class SecsList : SecsItem, IReadOnlyList<SecsItem>
    {
        public static readonly SecsList Empty = new SecsList(Array.Empty<SecsItem>());
        #region 构造函数
        public SecsList(params SecsItem[] items) :
            this((IEnumerable<SecsItem>)(items ?? Array.Empty<SecsItem>()))
        { }
        public SecsList(IEnumerable<SecsItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            _items = items as SecsItem[] ?? items.ToArray();
            if (_items.Length > MaxItemDataLength)
                throw new ArgumentOutOfRangeException(nameof(items), _items.Length, $"The number of LIST sub-items must not be exceeded {MaxItemDataLength}");
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i] is null)
                    throw new ArgumentNullException($"The {i}th sub-item of LIST is null.", nameof(items));
            }
        }
        #endregion
        #region 实现
        private readonly SecsItem[] _items;
        public override IReadOnlyList<SecsItem> Items => _items;
        public override SecsItem this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_items.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(index),
                        index,
                        $"index out of range, current item count {_items.Length}。");
                }
                return _items[index];
            }
        }

        public override SecsFormat Format => SecsFormat.List;

        public override int Count => _items.Length;

        private int _encodedLength = -1;
        public override int EncodedLength
        {
            get
            {
                if (_encodedLength >= 0)
                    return _encodedLength;
                //if item not empty
                int total = GetHeaderLength(Count);
                foreach (var item in _items)
                {
                    total += item.EncodedLength;
                }
                return total;
            }
        }


        public override void AppendSml(StringBuilder sb, int indentLevel)
        {
            ArgumentNullException.ThrowIfNull(sb);
            AppendIndent(sb, indentLevel);
            if (Count == 0)
            {
                sb.AppendLine("<L[0]>");
                return;
            }
            sb.Append("<L[").Append(Count).AppendLine("]");
            foreach (var item in _items)
                item.AppendSml(sb, indentLevel + 1);
            AppendIndent(sb, indentLevel);
            sb.AppendLine(">");
        }

        public override void EncodeTo(IBufferWriter<byte> writer)
        {
            WriteItemHeader(writer, SecsFormat.List, Count);
            foreach (var item in _items)
                item.EncodeTo(writer);

        }

        public IEnumerator<SecsItem> GetEnumerator() => ((IEnumerable<SecsItem>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
        #endregion
    }
}
