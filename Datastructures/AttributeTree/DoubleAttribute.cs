using System;
using System.IO;
using Vintagestory.API.Config;


namespace Vintagestory.API.Datastructures;

public class DoubleAttribute : ScalarAttribute<double>, IAttribute
{
    public DoubleAttribute()
    {

    }

    public DoubleAttribute(double value)
    {
        this.value = value;
    }

    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
        => reader.UnsafeActAsBinarySpanReader(FromBytes);

    public void FromBytes(ref BinarySpanReader reader)
    {
        value = reader.ReadDouble();
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write(value);
    }

    public int GetAttributeId()
    {
        return 3;
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
        return new DoubleAttribute(value);
    }
}
