using System;
using Vintagestory.API.Util;

namespace Vintagestory.API.Datastructures;

public readonly ref struct SerializedData(ReadOnlySpan<byte> rawData)
{
    public readonly ReadOnlySpan<byte> RawData = rawData;

    public readonly BinarySpanReader AsBinaryReader() => new(this.RawData);

    public readonly T Deserialize<T>() => SerializerUtil.Deserialize<T>(this);
}
