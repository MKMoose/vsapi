using System;
using System.IO;

namespace Vintagestory.API.Datastructures;

public class IntAttribute : ScalarAttribute<int>, IAttribute
{
    public IntAttribute()
    {

    }
    public IntAttribute(int value)
    {
        this.value = value;
    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        value = reader.ReadInt32();
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write(value);
    }

    public int GetAttributeId()
    {
        return 1;
    }

    public IAttribute Clone()
    {
        return new IntAttribute(value);
    }
}
