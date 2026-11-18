namespace MateoSECS.Secs
{
    public enum SecsFormat : byte
    {
        List = 0b000000,    // 0
        Binary = 0b001000,  // 8
        Boolean = 0b001001, // 9
        ASCII = 0b010000,   // 16
        JIS8 = 0b010001,    // 17
        CHAR2 = 0b010010, // 18
        I8 = 0b011000,      // 24
        I1 = 0b011001,      // 25
        I2 = 0b011010,      // 26
        I4 = 0b011100,      // 28
        F8 = 0b100000,      // 32
        F4 = 0b100100,      // 36
        U8 = 0b101000,      // 40
        U1 = 0b101001,      // 41
        U2 = 0b101010,      // 42
        U4 = 0b101100,      // 44
    }
}
