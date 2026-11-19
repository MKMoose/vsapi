using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Vintagestory.Datastructures;

namespace Vintagestory.API.Datastructures;

public ref struct BinarySpanReader(ReadOnlySpan<byte> data) {
    readonly ReadOnlySpan<byte> data   = data;
             int                offset = 0;

    #region naming compatibiltiy with BinaryReader

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public byte ReadByte()     => ReadUInt8LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public UInt16 ReadUInt16() => ReadUInt16LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public UInt32 ReadUInt32() => ReadUInt32LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public UInt64 ReadUInt64() => ReadUInt64LE();

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public sbyte ReadSByte() => ReadInt8LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Int16 ReadInt16() => ReadInt16LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Int32 ReadInt32() => ReadInt32LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Int64 ReadInt64() => ReadInt64LE();

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Half   ReadHalf()   => ReadFloat16LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public float  ReadSingle() => ReadFloat32LE();
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public double ReadDouble() => ReadFloat64LE();

    #endregion


    #region Little Endian
    /// <remarks>
    ///   Equivalent to <see cref="System.IO.BinaryReader.ReadSByte"/>. <br/>
    ///   Identical to <see cref="ReadUInt8BE"/>. There are two methods for name parity with other operations.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadUInt8LE()
    {
        var v = this.data[this.offset];
        this.offset += sizeof(byte);
        return v;
    }
    /// <remarks>
    ///   Equivalent to <see cref="System.IO.BinaryReader.ReadByte"/>. <br/>
    ///   Identical to <see cref="ReadInt8BE"/>. There are two methods for name parity with other operations.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadInt8LE()
    {
        return Unsafe.BitCast<byte, sbyte>(this.ReadUInt8LE());
    }

    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadUInt16"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UInt16 ReadUInt16LE()
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt16);
        return v;
    }
    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadInt16"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Int16 ReadInt16LE()
    {
        return Unsafe.BitCast<UInt16, Int16>(this.ReadUInt16LE());
    }

    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadUInt32"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UInt32 ReadUInt32LE()
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt32);
        return v;
    }
    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadInt32"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Int32 ReadInt32LE()
    {
        return Unsafe.BitCast<UInt32, Int32>(this.ReadUInt32LE());
    }

    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadUInt64"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UInt64 ReadUInt64LE()
    {
        var v = BinaryPrimitives.ReadUInt64LittleEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt64);
        return v;
    }
    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadInt64"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Int64 ReadInt64LE()
    {
        return Unsafe.BitCast<UInt64, Int64>(this.ReadUInt64LE());
    }


    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadHalf"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Half ReadFloat16LE()
    {
        var v = BinaryPrimitives.ReadHalfLittleEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt16); // Rennorb: sizeof(Half) is unsafe. Internally this reads a u16 either way, so just advance by that and don't mess with marshalling the size.
        return v;
    }

    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadSingle"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat32LE()
    {
        var v = BinaryPrimitives.ReadSingleLittleEndian(this.data[this.offset..]);
        this.offset += sizeof(float);
        return v;
    }

    /// <remarks>Equivalent to <see cref="System.IO.BinaryReader.ReadDouble"/></remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadFloat64LE()
    {
        var v = BinaryPrimitives.ReadDoubleLittleEndian(this.data[this.offset..]);
        this.offset += sizeof(double);
        return v;
    }
    #endregion

    #region Big Endian
    /// <remarks>Identical to <see cref="ReadUInt8LE"/>. There are two methods for name parity with other operations. </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadUInt8BE()
    {
        return this.ReadUInt8LE();
    }
    /// <remarks>Identical to <see cref="ReadUInt8LE"/>. There are two methods for name parity with other operations. </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadInt8BE()
    {
        return this.ReadInt8LE();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UInt16 ReadUInt16BE()
    {
        var v = BinaryPrimitives.ReadUInt16BigEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt16);
        return v;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Int16 ReadInt16BE()
    {
        return Unsafe.BitCast<UInt16, Int16>(this.ReadUInt16BE());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UInt32 ReadUInt32BE()
    {
        var v = BinaryPrimitives.ReadUInt32BigEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt32);
        return v;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Int32 ReadInt32BE()
    {
        return Unsafe.BitCast<UInt32, Int32>(this.ReadUInt32BE());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UInt64 ReadUInt64BE()
    {
        var v = BinaryPrimitives.ReadUInt64BigEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt64);
        return v;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Int64 ReadInt64BE()
    {
        return Unsafe.BitCast<UInt64, Int64>(this.ReadUInt64BE());
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Half ReadFloat16BE()
    {
        var v = BinaryPrimitives.ReadHalfBigEndian(this.data[this.offset..]);
        this.offset += sizeof(UInt16); // Rennorb: sizeof(Half) is unsafe. Internally this reads a u16 either way, so just advance by that and don't mess with marshalling the size.
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat32BE()
    {
        var v = BinaryPrimitives.ReadSingleBigEndian(this.data[this.offset..]);
        this.offset += sizeof(float);
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadFloat64BE()
    {
        var v = BinaryPrimitives.ReadDoubleBigEndian(this.data[this.offset..]);
        this.offset += sizeof(double);
        return v;
    }
    #endregion

    /// <remarks>
    ///   Equivalent to <see cref="System.IO.BinaryReader.ReadBoolean"/>. <br/>
    ///   This reads a whole byte to read one bool. If you have multiple bools you want to transport consider packing them into a large enough integer type. <br/>
    ///   A byte already has enough space to transport 8 bools by packing them into its bits. <br/>
    ///   This exists for convenience and parity with <see cref="System.IO.BinaryReader"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBoolean()
    {
        return this.ReadInt8LE() != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var v = this.data.Slice(this.offset, count);
        this.offset += count;
        return v;
    }


    /// <param name="byteLengthLimit">
    ///   Since this method allocates memory form potentially untrusted data you can restrict the byte size of said allocation.
    ///   Should the data contain a length for the string larger than this parameters value a FormatException is thrown.
    /// </param>
    /// <remarks> Equivalent to <see cref="System.IO.BinaryReader.ReadString"/>.</remarks>
    public string ReadString(int byteLengthLimit = int.MaxValue)
    {
        // decode the length. BinaryWriter writes this as high bit extended (same as protobuf):
        int byteLength = 0;

        int i = 0;
        for (; i < 5; i++)
        {
            byte b = this.data[this.offset++];
            byteLength |= (b & 0x7F) << (i * 7);

            if ((b & 0b1000_0000) == 0) break;
        }

        if (i == 6 || byteLength < 0 || byteLength > byteLengthLimit) throw new FormatException("Malformed string length.");

        if (byteLength == 0) return string.Empty;

        var str = Encoding.UTF8.GetString(this.data.Slice(this.offset, byteLength));
        this.offset += byteLength;

        return str;
    }

    public readonly int CurrentOffset => this.offset;
    public readonly ReadOnlySpan<byte> RemainingBytes => this.data[this.offset..];

    public void SkipBytes(int count) => this.offset += count;
}


public static class BinarySpanReaderExtensions
{
    public static string[] ReadStringArray(ref this BinarySpanReader reader)
    {
        int length = reader.ReadInt32();
        string[] values = new string[length];

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadString();
        }

        return values;
    }

    /// <remarks>
    ///   The buffer must have been marked as exposed when the memory stream was constructed. <br/>
    ///   The buffer must not be modified while the returned reader is is use. <br/>
    ///   Takes the complete buffer of the memory stream, regardless of current position.
    /// </remarks>
    public static BinarySpanReader UnsafeAsBinarySpanReader(this MemoryStream stream) => new(stream.GetBuffer().AsSpan(0, (int)stream.Length));

    #region backwards compat patching utils


    public delegate void BinarySpanReaderAction(ref BinarySpanReader reader);

    [Obsolete("Don't use this.")]
    public static void UnsafeActAsBinarySpanReader(this BinaryReader reader, BinarySpanReaderAction action)
    {
        UnsafeActAsBinarySpanReader(reader, 0, (ref spanReader, _) => action(ref spanReader));
    }

    public delegate void BinarySpanReaderAction<T1>(ref BinarySpanReader reader, T1 arg1);

    [Obsolete("Don't use this.")]
    public static void UnsafeActAsBinarySpanReader<T1>(this BinaryReader reader, T1 arg1, BinarySpanReaderAction<T1> action)
    {
        UnsafeActAsBinarySpanReader(reader, arg1, (ref spanReader, arg1) => { action(ref spanReader, arg1); return 0; } );
    }

    public delegate void BinarySpanReaderAction<T1, T2>(ref BinarySpanReader reader, T1 arg1, T2 arg2);

    [Obsolete("Don't use this.")]
    public static void UnsafeActAsBinarySpanReader<T1, T2>(this BinaryReader reader, T1 arg1, T2 arg2, BinarySpanReaderAction<T1, T2> action)
    {
        UnsafeActAsBinarySpanReader(reader, (arg1, arg2), (ref spanReader, args) => { action(ref spanReader, args.arg1, args.arg2); return 0; } );
    }

    public delegate TR BinarySpanReaderFunc<TR>(ref BinarySpanReader reader);

    [Obsolete("Don't use this.")]
    public static TR UnsafeActAsBinarySpanReader<TR>(this BinaryReader reader, BinarySpanReaderFunc<TR> action)
    {
        return UnsafeActAsBinarySpanReader(reader, 0, (ref spanReader, _) => action(ref spanReader));
    }

    public delegate TR BinarySpanReaderFunc<T1, TR>(ref BinarySpanReader reader, T1 arg1);

    [Obsolete("Don't use this.")]
    public static TR UnsafeActAsBinarySpanReader<T1, TR>(this BinaryReader reader, T1 arg1, BinarySpanReaderFunc<T1, TR> action)
    {
        var baseStream = reader.BaseStream;

        bool needsRewinding = false;
        MemoryStream ms;
        if (baseStream is MemoryStream _ms) ms = _ms; // The 99% case.
        else
        {
            if (baseStream.CanSeek)
            {
                try { ms = new MemoryStream((int)baseStream.Length); }
                catch { ms = new MemoryStream(); }
            }
            else
            {
                throw new NotSupportedException("This no longer supports streams that cannot seek.");
            }

            baseStream.CopyTo(ms);
            needsRewinding = true;
        }

        var spanReader = new BinarySpanReader(ms.GetBuffer().AsSpan((int)ms.Position, (int)ms.Length));
        var result = action(ref spanReader, arg1);

        if (needsRewinding)
        {
            var remaining = (int)ms.Length - spanReader.CurrentOffset;
            if (remaining > 0)
            {
                reader.BaseStream.Seek(-remaining, SeekOrigin.Current);
            }
        }

        return result;
    }



    public delegate void BinaryReaderAction(BinaryReader reader);

    [Obsolete("Don't use this.")]
    public static void ActAsBinaryReader(ref this BinarySpanReader spanReader, BinaryReaderAction action)
    {
        ActAsBinaryReader(ref spanReader, 0, (reader, _) => action(reader));
    }

    public delegate void BinaryReaderAction<T1>(BinaryReader reader, T1 arg1);

    [Obsolete("Don't use this.")]
    public static void ActAsBinaryReader<T1>(ref this BinarySpanReader spanReader, T1 arg1, BinaryReaderAction<T1> action)
    {
        ActAsBinaryReader(ref spanReader, arg1, (reader, arg1) => { action(reader, arg1); return 0; } );
    }

    public delegate void BinaryReaderAction<T1, T2>(BinaryReader reader, T1 arg1, T2 arg2);

    [Obsolete("Don't use this.")]
    public static void ActAsBinaryReader<T1, T2>(ref this BinarySpanReader spanReader, T1 arg1, T2 arg2, BinaryReaderAction<T1, T2> action)
    {
        ActAsBinaryReader(ref spanReader, (arg1, arg2), (reader, args) => { action(reader, args.arg1, args.arg2); return 0; } );
    }

    public delegate TR BinaryReaderFunc<TR>(BinaryReader reader);

    [Obsolete("Don't use this.")]
    public static TR ActAsBinaryReader<TR>(ref this BinarySpanReader spanReader, BinaryReaderFunc<TR> action)
    {
        return ActAsBinaryReader(ref spanReader, 0, (reader, _) => action(reader));
    }

    public delegate TR BinaryReaderFunc<T1, TR>(BinaryReader reader, T1 arg1);

    [Obsolete("Don't use this.")]
    public static TR ActAsBinaryReader<T1, TR>(ref this BinarySpanReader spanReader, T1 arg1, BinaryReaderFunc<T1, TR> action)
    {
        using var pooledMem = new ArrayPoolReturner<byte>(spanReader.RemainingBytes);

        var stream = new MemoryStream(pooledMem.BorrowedArray, 0, pooledMem.RequestedLength);
        var reader = new BinaryReader(stream);

        var result = action(reader, arg1);

        spanReader.SkipBytes((int)stream.Position);

        return result;
    }

    #endregion
}
