using System;
using System.IO;
using System.Text;

namespace Vintagestory.API.Datastructures;

public class StringArrayAttribute : ArrayAttribute<string>, IAttribute
{
    public StringArrayAttribute()
    {

    }

    public StringArrayAttribute(string[] value)
    {
        this.value = value;
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            stream.Write(value[i]);
        }
            
    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        int quantity = reader.ReadInt32();
        value = new string[quantity];
        for (int i = 0; i < quantity; i++)
        {
            value[i] = reader.ReadString();
        }
    }

    public int GetAttributeId()
    {
        return 10;
    }

    public override string ToJsonToken()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('[');

        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0) sb.Append(", ");

            sb.Append("\"" + value[i] + "\"");
        }
        sb.Append(']');

        return sb.ToString();
    }

    public IAttribute Clone()
    {
        return new StringArrayAttribute((string[])value.Clone());
    }
}
