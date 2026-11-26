namespace MateoSECS.Core;

public sealed class SystemByteGenerator
{
    public uint Current => unchecked((uint)Volatile.Read(ref _current));
    private uint _current;

    public SystemByteGenerator(uint seed)
    {
        _current = seed;
    }
    public SystemByteGenerator()
        : this((uint)Random.Shared.Next(1, int.MaxValue))
    {

    }
    public uint Next()
    {
        while (true)
        {
            uint value = unchecked((uint)Interlocked.Increment(ref _current));
            if (value != 0)
                return value;
        }
    }

    public void Reset(uint value) => Interlocked.Exchange(ref _current, unchecked(value));
    public override string ToString() => $"SystemByteGenerator[Current=0x{Current:X8}]";
}
