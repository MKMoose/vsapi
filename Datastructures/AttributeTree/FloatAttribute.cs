using System;
using System.IO;
using Vintagestory.API.Config;

namespace Vintagestory.API.Datastructures;

public class FloatAttribute : ScalarAttribute<float>, IAttribute
{
    public FloatAttribute()
    {

    }

    public FloatAttribute(float value)
    {
        this.value = value;
    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        value = reader.ReadSingle();
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write(value);
    }

    public int GetAttributeId()
    {
        return 4;
    }

    public override string ToJsonToken()
    {
        return value.ToString(GlobalConstants.DefaultCultureInfo);
    }

    public override string ToString()
    {
        return value.ToString(GlobalConstants.DefaultCultureInfo);
    }

    public IAttribute Clone()
    {
        return new FloatAttribute(value);
    }
}
