using System;
using System.IO;

namespace Vintagestory.API.Datastructures;

/// <summary>
/// When serialized, produces identical byte output to a ByteArrayAttribute.  The difference is, for performance, this writes directly to the provided BinaryWriter stream
/// <br/>Intended to be deserialized to a ByteArrayAttribute
/// </summary>
public class StreamedByteArrayAttribute : ArrayAttribute<byte>, IAttribute
{
    private const int AttributeID = 8;
    private readonly FastMemoryStream ms;

    public StreamedByteArrayAttribute(FastMemoryStream ms)
    {
        this.ms = ms;
    }

    public void BeginDirectWrite(BinaryWriter stream, string key)
    {
        stream.Write((byte)AttributeID);
        stream.Write(key);
    }

    public void ToBytes(BinaryWriter stream)
    {
        stream.Write((ushort)ms.Position);
        if (stream.BaseStream is FastMemoryStream outputMS)
        {
            outputMS.Write(ms);   // Avoids calling .ToArray() and creating the intermediate byte[] if we are using a FastMemoryStream
            return;
        }

        stream.Write(ms.ToArray());
    }

    public int GetAttributeId()
    {
        return AttributeID;
    }


    [Obsolete("Use the BinarySpanReader overload.", true)]
    public void FromBytes(BinaryReader reader)
    {
        // Not implemented, because this is intended to be deserialized to a ByteArrayAttribute
        throw new NotImplementedException();
    }

    public void FromBytes(ref BinarySpanReader reader)
    {
        // Not implemented, because this is intended to be deserialized to a ByteArrayAttribute
        throw new NotImplementedException();
    }

    public IAttribute Clone()
    {
        throw new NotImplementedException();
    }
}
