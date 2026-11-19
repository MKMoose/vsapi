using System;
using System.IO;

namespace Vintagestory.API.Datastructures;

public class ByteArrayAttribute : ArrayAttribute<byte>, IAttribute
{
    private const int AttributeID = 8;

    public ByteArrayAttribute()
    {

    }

    public ByteArrayAttribute(byte[] value)
    {
        this.value = value;
    }

    public ByteArrayAttribute(FastMemoryStream ms)
    {
        this.value = ms.ToArray();
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write((ushort)value.Length);
        stream.Write(value);
    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        int length = reader.ReadInt16();
        value = reader.ReadBytes(length).ToArray();
    }

    public int GetAttributeId()
    {
        return AttributeID;
    }

    public IAttribute Clone()
    {
        return new ByteArrayAttribute((byte[])value.Clone());
    }
}
