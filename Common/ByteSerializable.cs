using System;
using System.IO;
using System.Reflection;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common;

public interface IByteSerializable
{
    void ToBytes(BinaryWriter writer);

    /// <remarks> Obsolete: Do not implement this overload. </remarks>
    [Obsolete("Use the BinarySpanReader overload.", true)]
    void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        var thisT = this.GetType();
        Console.WriteLine($"You are calling obsolete overload {thisT}.{nameof(this.FromBytes)}(BinaryReader, IWorldAccessor). If you are the developer make sure to move to the BinarySpanReader overload as soon as possible.");

        var oldFromBytes = thisT.GetMethod(nameof(this.FromBytes), BindingFlags.Public | BindingFlags.Instance, [ typeof(BinarySpanReader), typeof(IWorldAccessor) ]);
        if (oldFromBytes.DeclaringType != typeof(IByteSerializable))
        {
            reader.UnsafeActAsBinarySpanReader(resolver, FromBytes);
        }
    }

    void FromBytes(ref BinarySpanReader reader, IWorldAccessor resolver)
    {
        var thisT = this.GetType();
        var oldFromBytes = thisT.GetMethod(nameof(this.FromBytes), BindingFlags.Public | BindingFlags.Instance, [ typeof(BinaryReader), typeof(IWorldAccessor) ]);
        if (oldFromBytes.DeclaringType != typeof(IByteSerializable))
        {
            Console.WriteLine($"Calling obsolete overload {thisT}.{nameof(this.FromBytes)}(BinaryReader, IWorldAccessor). If you are the developer make sure to move to the BinarySpanReader overload as soon as possible.");
            #pragma warning disable CS0618 // Type or member is obsolete
            reader.ActAsBinaryReader(resolver, oldFromBytes.CreateDelegate<BinarySpanReaderExtensions.BinaryReaderAction<IWorldAccessor>>(this));
            #pragma warning restore CS0618
        }
    }
}
