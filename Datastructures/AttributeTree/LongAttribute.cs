using System;
using System.IO;

namespace Vintagestory.API.Datastructures;

public class LongAttribute : ScalarAttribute<long>, IAttribute
{
    public LongAttribute()
    {

    }

    public LongAttribute(long value)
    {
        this.value = value;
    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        value = reader.ReadInt64();
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write(value);
    }

    public int GetAttributeId()
    {
        return 2;
    }

    public IAttribute Clone()
    {
        return new LongAttribute(value);
    }
}
