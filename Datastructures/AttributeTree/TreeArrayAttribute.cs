using System;
using System.IO;

namespace Vintagestory.API.Datastructures;

public class TreeArrayAttribute : ArrayAttribute<TreeAttribute>, IAttribute
{
    public TreeArrayAttribute()
    {

    }

    public TreeArrayAttribute(TreeAttribute[] value)
    {
        this.value = value;
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            value[i].ToBytes(stream);
        }

    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        int quantity = reader.ReadInt32();
        value = new TreeAttribute[quantity];
        for (int i = 0; i < quantity; i++)
        {
            value[i] = new TreeAttribute();
            value[i].FromBytes(ref reader);
        }
    }

    public int GetAttributeId()
    {
        return 14;
    }

    public IAttribute Clone()
    {
        var newlist = new TreeAttribute[value.Length];
        for (int i = 0; i < newlist.Length; i++) newlist[i] = (TreeAttribute)value[i].Clone();

        return new TreeArrayAttribute(newlist);
    }

}
