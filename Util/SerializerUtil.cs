using ProtoBuf;
using System;
using System.IO;
using Vintagestory.API.Datastructures;
using Vintagestory.Datastructures;

namespace Vintagestory.API.Util;

public static class SerializerUtil
{
    public delegate void ByteWriteDelegate(BinaryWriter writer);
    public delegate void ByteReadDelegate(BinaryReader reader);

    public static readonly byte[] SerializedOne;
    public static readonly byte[] SerializedZero;

    static SerializerUtil()
    {
        SerializedOne = Serialize(1);
        SerializedZero = Serialize(0);
    }

    /// <summary>
    /// Uses ProtoBuf.NET to serialize T into bytes
    /// </summary>
    public static byte[] Serialize<T>(T data)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, data);
        return ms.ToArray();
    }

    #region Serialize

    /// <summary>
    /// For performance, version using a reusable FastMemoryStream provided as a parameter.
    /// <br/>(Caution: do not re-use the same stream in a nested way e.g. serializing individual elements within a larger object, as every call to this resets the stream)
    /// </summary>
    public static byte[] Serialize<T>(T data, FastMemoryStream ms)
    {
        ms.Reset();
        Serializer.Serialize(ms, data);
        return ms.ToArray();
    }

    #endregion

    #region Deserialize

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into T
    /// </summary>
    public static T Deserialize<T>(byte[] data)
    {
        var ms = new MemoryStream(data);
        return Serializer.Deserialize<T>(ms);
    }

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into T
    /// </summary>
    public static T Deserialize<T>(SerializedData data)
    {
        return Deserialize<T>(data.RawData);
    }

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into T
    /// </summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        using var pooledMem = new ArrayPoolReturner<byte>(data);
        var ms = new MemoryStream(pooledMem.BorrowedArray, 0, pooledMem.RequestedLength);
        return Serializer.Deserialize<T>(ms);
    }

    #endregion

    #region Deserialize with default

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into T. Returns the default value if data is null
    /// </summary>
    public static T Deserialize<T>(byte[]? data, T defaultValue)
    {
        if (data == null) return defaultValue;

        var ms = new MemoryStream(data);
        return Serializer.Deserialize<T>(ms);
    }

    #endregion

    #region DeserializeInto

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into existing object T
    /// </summary>
    public static T DeserializeInto<T>(T instance, byte[] data)
    {
        var ms = new MemoryStream(data);
        return Serializer.Merge(ms, instance);
    }

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into existing object T
    /// </summary>
    public static void DeserializeInto<T>(ref T instance, SerializedData data) where T : struct
    {
        DeserializeInto(ref instance, data.RawData);
    }

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into existing object T
    /// </summary>
    public static void DeserializeInto<T>(T instance, SerializedData data) where T : class
    {
        DeserializeInto(instance, data.RawData);
    }

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into existing object T
    /// </summary>
    public static T DeserializeInto<T>(ref T instance, ReadOnlySpan<byte> data) where T : struct
    {
        using var pooledMem = new ArrayPoolReturner<byte>(data);
        var ms = new MemoryStream(pooledMem.BorrowedArray, 0, pooledMem.RequestedLength);
        return Serializer.Merge(ms, instance);
    }

    /// <summary>
    /// Uses ProtoBuf.Net to deserialize bytes into existing object T
    /// </summary>
    public static T DeserializeInto<T>(T instance, ReadOnlySpan<byte> data) where T : class
    {
        using var pooledMem = new ArrayPoolReturner<byte>(data);
        var ms = new MemoryStream(pooledMem.BorrowedArray, 0, pooledMem.RequestedLength);
        return Serializer.Merge(ms, instance);
    }

    #endregion


    public static byte[] ToBytes(ByteWriteDelegate toWrite)
    {
        var ms = new MemoryStream();
        using(var writer = new BinaryWriter(ms))
        {
            toWrite(writer);
        }

        return ms.ToArray();
    }

    [Obsolete]
    public static void FromBytes(byte[] data, ByteReadDelegate toRead)
    {
        var ms = new MemoryStream(data);
        var reader = new BinaryReader(ms);
        toRead(reader);
    }
}
