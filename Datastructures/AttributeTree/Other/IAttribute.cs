using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Vintagestory.API.Common;

namespace Vintagestory.API.Datastructures;

/// <summary>
/// An attribute from an attribute tree
/// </summary>
public interface IAttribute
{
    void ToBytes(BinaryWriter stream);

    [Obsolete("Use the BinarySpanReader overload.", true)]
    void FromBytes(BinaryReader stream)
    {
        var thisT = this.GetType();
        Trace.WriteLine($"You are calling obsolete overload {thisT}.{nameof(this.FromBytes)}(BinaryReader). If you are the developer make sure to move to the BinarySpanReader overload as soon as possible.");

        var newFromBytes = thisT.GetMethod(nameof(this.FromBytes), BindingFlags.Public | BindingFlags.Instance, [ typeof(BinarySpanReader) ]);
        if (newFromBytes != null && newFromBytes.DeclaringType != typeof(IAttribute))
        {
            stream.UnsafeActAsBinarySpanReader(FromBytes);
        }
    }

    /// <remarks>Do not call this default implementation when overriding this method, it only exists for backwards compat.</remarks>
    void FromBytes(ref BinarySpanReader reader)
    {
        var thisT = this.GetType();
        var oldFromBytes = thisT.GetMethod(nameof(this.FromBytes), BindingFlags.Public | BindingFlags.Instance, [ typeof(BinaryReader) ])!;
        if (oldFromBytes != null && oldFromBytes.DeclaringType != typeof(IAttribute))
        {
            Trace.WriteLine($"Calling obsolete overload {thisT}.{nameof(this.FromBytes)}(BinaryReader). If you are the developer make sure to move to the BinarySpanReader overload as soon as possible.");
            #pragma warning disable CS0618 // Type or member is obsolete
            reader.ActAsBinaryReader(oldFromBytes.CreateDelegate<BinarySpanReaderExtensions.BinaryReaderAction>(this));
            #pragma warning restore CS0618
        }
    }

    int GetAttributeId();

    Type GetType();
    object? GetValue();
    string ToJsonToken();

    bool Equals(IWorldAccessor worldForResolve, [NotNullWhen(true)] IAttribute? attr);
    IAttribute Clone();
}
