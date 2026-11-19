using System;
using System.IO;

namespace Vintagestory.API.Datastructures;

public class StringAttribute : ScalarAttribute<string>, IAttribute
{
    private const int AttributeID = 5;

    public StringAttribute()
    {
        this.value = "";
    }

    public StringAttribute(string value)
    {
        this.value = value;
    }

    public void ToBytes(BinaryWriter stream)
    {
        value ??= "";
        stream.Write(value);
    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        value = reader.ReadString();
    }

    public int GetAttributeId()
    {
        return AttributeID;
    }

    public override string ToJsonToken()
    {
        return "\"" + value + "\"";
    }
    public IAttribute Clone()
    {
        return new StringAttribute(value);
    }

    /// <summary>
    /// Equivalent output as if a StringAttribute had been created within a TreeAttribute, and then that TreeAttribute was immediately serialized to the writer
    /// </summary>
    internal static void DirectWrite(BinaryWriter writer, string key, string value)
    {
        writer.Write((byte)AttributeID);
        writer.Write(key);
        writer.Write(value);
    }
}
