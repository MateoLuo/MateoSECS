using System.Text;

namespace MateoSECS.Secs
{
    public sealed class SecsMsg
    {
        /// <summary> Stream（0..127） </summary>
        public byte S { get; }
        /// <summary> Funcation（0..255） </summary>
        public byte F { get; }
        /// <summary> Is Reply </summary>
        public bool Wbit { get; }
        /// <summary> SecsItem root </summary>
        public SecsItem? SecsItem { get; }
        public string? Name { get; init; }
        #region 属性
        public bool IsPrimary => (F & 0x01) == 1; 
        public bool IsSecondary => !IsPrimary; 
        public bool IsAbort => F == 0;
        public bool IsHeaderOnly => SecsItem is null;
        #endregion
        public SecsMsg(byte s, byte f, bool wbit = false, SecsItem? item = null, string? name = null)
        {
            if (s > 0x7F)
                throw new ArgumentOutOfRangeException(nameof(s), s, "Stream between 0..127。");
            if (wbit && (f & 0x01) == 0)
                throw new ArgumentException("F is even number", nameof(wbit));

            S = s;
            F = f;
            Wbit = wbit;
            SecsItem = item;
            Name = name;
        }
        /// <summary>
        /// 输出SML文本
        /// </summary>
        /// <returns></returns>
        public string ToSml()
        {
            var sb = new StringBuilder(256);
            sb.Append(Name is null ? $"S{S}F{F}" : $"{Name}: S{S}F{F}");
            if (Wbit)
                sb.Append(" W");

            if (SecsItem is null)
            {
                sb.Append('.');
                return sb.ToString();
            }

            sb.AppendLine();
            SecsItem.AppendSml(sb, 0);
            sb.Append('.');
            return sb.ToString();
        }
        /// <summary>
        /// 创建一般的SecsMsg回复
        /// </summary>
        /// <param name="item">Secs item body</param>
        /// <param name="name">reply name</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public SecsMsg CreateReply(SecsItem? item = null, string? name = null)
        {
            if (!IsPrimary)
                throw new InvalidOperationException($"S{S}F{F} not Primary Message, cannot generate reply.");
            if (F == 0xFF)
                throw new InvalidOperationException("Function overflow, cannot generate reply.");

            return new SecsMsg(S, (byte)(F + 1), false, item, name ?? (Name is null ? null : Name + ".Ack"));
        }
        public SecsMsg CreateAbortReply() => new(S, 0, false, null, $"S{S}F0 Abort");
    }
}
