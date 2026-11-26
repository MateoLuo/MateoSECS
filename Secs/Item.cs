namespace MateoSECS.Secs
{
    public static class Item
    {
        /// <summary>空 LIST：L[0]。</summary>
        public static SecsItem L() => SecsList.Empty;

        public static SecsItem L(params SecsItem[] items) => new SecsList(items);

        public static SecsItem L(IEnumerable<SecsItem> items) => new SecsList(items);

        /// <summary>ASCII 字符串。</summary>
        public static SecsItem A(string value) => new SecsAscii(value);

        /// <summary>空 ASCII：A[0]。</summary>
        public static SecsItem A() => SecsAscii.Empty;

        /// <summary>Binary。</summary>
        public static SecsItem B(params byte[] value) => new SecsBinary(value);

        public static SecsItem B(ReadOnlyMemory<byte> value) => new SecsBinary(value);

        /// <summary>Boolean。</summary>
        public static SecsItem Boolean(params bool[] value) => new SecsBoolean(value);

        public static SecsItem U1(params byte[] value) => new SecsNum<byte>(SecsFormat.U1, value);

        public static SecsItem U2(params ushort[] value) => new SecsNum<ushort>(SecsFormat.U2, value);

        public static SecsItem U4(params uint[] value) => new SecsNum<uint>(SecsFormat.U4, value);

        public static SecsItem U8(params ulong[] value) => new SecsNum<ulong>(SecsFormat.U8, value);

        public static SecsItem I1(params sbyte[] value) => new SecsNum<sbyte>(SecsFormat.I1, value);

        public static SecsItem I2(params short[] value) => new SecsNum<short>(SecsFormat.I2, value);

        public static SecsItem I4(params int[] value) => new SecsNum<int>(SecsFormat.I4, value);

        public static SecsItem I8(params long[] value) => new SecsNum<long>(SecsFormat.I8, value);

        public static SecsItem F4(params float[] value) => new SecsNum<float>(SecsFormat.F4, value);

        public static SecsItem F8(params double[] value) => new SecsNum<double>(SecsFormat.F8, value);
    }
}
