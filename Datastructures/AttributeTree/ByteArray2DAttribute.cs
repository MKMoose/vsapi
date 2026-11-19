using System;
using System.IO;
using System.Runtime.InteropServices;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Datastructures
{
    public class ByteArray2DAttribute : Array2DAttribute<byte>, IAttribute
    {
        public ByteArray2DAttribute()
        {

        }

        public ByteArray2DAttribute(byte[,] value)
        {
            this.value = value;
        }

        public void ToBytes(BinaryWriter stream)
        {
            stream.Write((ushort)value.GetLength(0));
            stream.Write((ushort)value.GetLength(1));
            byte[] buffer = new byte[value.Length];
            Buffer.BlockCopy(value, 0, buffer, 0, buffer.Length);
            stream.Write(buffer);
        }

        public void FromBytes(ref BinarySpanReader reader)
        {
            int length0 = reader.ReadUInt16();
            int length1 = reader.ReadUInt16();
            value = new byte[length0, length1];
            if (value.Length > 0)
            {
                ReadOnlySpan<byte> buffer = reader.ReadBytes(length0 * length1);
                buffer.CopyTo(MemoryMarshal.CreateSpan(ref value[0, 0], value.Length));
            }
        }

        public int GetAttributeId()
        {
            return 17;
        }

        public IAttribute Clone()
        {
            return new ByteArray2DAttribute((byte[,])value.Clone());
        }
    }
}
