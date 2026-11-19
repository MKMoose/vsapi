using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Config;

namespace Vintagestory.API.MathTools;

[ProtoContract]
[JsonObject(MemberSerialization.OptIn)]
public struct FastBlockPos : IEquatable<BlockPos>, IEquatable<FastBlockPos>
{
    [ProtoMember(1)] [JsonProperty] public int X;

    [ProtoMember(2)]
    [JsonProperty]
    public int InternalY
    {
        get => Y + dimension * BlockPos.DimensionBoundary;
        set
        {
            Y = value % BlockPos.DimensionBoundary;
            dimension = value / BlockPos.DimensionBoundary;
        }
    }

    [ProtoMember(3)] [JsonProperty] public int Z;

    public int Y;
    public int dimension;

    public FastBlockPos()
    {
    }

    public FastBlockPos(int x, int y, int z)
    {
        X = x;
        Y = y % BlockPos.DimensionBoundary;
        Z = z;
        dimension = y / BlockPos.DimensionBoundary;
    }

    public FastBlockPos(int x, int y, int z, int dimension)
    {
        X = x;
        Y = y;
        Z = z;
        this.dimension = dimension;
    }

    public FastBlockPos(BlockPos pos)
    {
        X = pos.X;
        Y = pos.Y;
        Z = pos.Z;
        dimension = pos.dimension;
    }

    #region IEquatable

    public bool Equals(FastBlockPos other)
    {
        return X == other.X && Z == other.Z && Y == other.Y && dimension == other.dimension;
    }

    public bool Equals(BlockPos? other)
    {
        return other != null && X == other.X && Y == other.Y && Z == other.Z && dimension == other.dimension;
    }

    /// <summary>
    /// same as in FastBlockPos so they can be safely compared
    /// </summary>
    /// <returns></returns>
    public override int GetHashCode()
    {
        return ((17 * 23 + X) * 23 + Y) * 23 + Z + dimension * 269023;
    }

    public override bool Equals(object? obj)
    {
        return obj is FastBlockPos other && Equals(other);
    }

    #endregion



    /// <summary>
    /// Offsets the position into the direction of given block face
    /// </summary>
    /// <param name="facing"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos Offset(BlockFacing facing)
    {
        var faceNormals = facing.Normali;
        X += faceNormals.X;
        Y += faceNormals.Y;
        Z += faceNormals.Z;
        return this;
    }

    #region Copy offset

    /// <summary>
    /// Creates a copy of this blocks position with the x-position adjusted by -<paramref name="length"/>
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos WestCopy(int length = 1)
    {
        return new FastBlockPos(X - length, Y, Z, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position with the z-position adjusted by +<paramref name="length"/>
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos SouthCopy(int length = 1)
    {
        return new FastBlockPos(X, Y, Z + length, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position with the x-position adjusted by +<paramref name="length"/>
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos EastCopy(int length = 1)
    {
        return new FastBlockPos(X + length, Y, Z, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position with the z-position adjusted by -<paramref name="length"/>
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos NorthCopy(int length = 1)
    {
        return new FastBlockPos(X, Y, Z - length, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position with the y-position adjusted by -<paramref name="length"/>
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos DownCopy(int length = 1)
    {
        return new FastBlockPos(X, Y - length, Z, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position with the y-position adjusted by +<paramref name="length"/>
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos UpCopy(int length = 1)
    {
        return new FastBlockPos(X, Y + length, Z, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position and offsets it by given xyz
    /// </summary>
    /// <param name="dx"></param>
    /// <param name="dy"></param>
    /// <param name="dz"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos AddCopy(float dx, float dy, float dz)
    {
        return new FastBlockPos((int)(X + dx), (int)(Y + dy), (int)(Z + dz), dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position and offsets it by given xyz
    /// </summary>
    /// <param name="dx"></param>
    /// <param name="dy"></param>
    /// <param name="dz"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos AddCopy(int dx, int dy, int dz)
    {
        return new FastBlockPos(X + dx, Y + dy, Z + dz, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position and offsets it by given xyz
    /// </summary>
    /// <param name="xyz"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos AddCopy(int xyz)
    {
        return new FastBlockPos(X + xyz, Y + xyz, Z + xyz, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position and offsets it by given xyz
    /// </summary>
    /// <param name="vector"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos AddCopy(Vec3i vector)
    {
        return new FastBlockPos(X + vector.X, Y + vector.Y, Z + vector.Z, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position and offsets it in the direction of given block face
    /// </summary>
    /// <param name="facing"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos AddCopy(BlockFacing facing)
    {
        return new FastBlockPos(X + facing.Normali.X, Y + facing.Normali.Y, Z + facing.Normali.Z, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position and offsets it in the direction of given block face
    /// </summary>
    /// <param name="facing"></param>
    /// <param name="length"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos AddCopy(BlockFacing facing, int length)
    {
        return new FastBlockPos(X + facing.Normali.X * length, Y + facing.Normali.Y * length, Z + facing.Normali.Z * length, dimension);
    }

    /// <summary>
    /// Creates a copy of this block's position and offsets it in the direction of the given vector
    /// </summary>
    /// <param name="vector"></param>
    /// <returns></returns>
    public FastBlockPos AddCopy(FastVec3i vector)
    {
        return new FastBlockPos(X + vector.X, Y + vector.Y, Z + vector.Z, dimension);
    }

    /// <summary>
    /// Substract a position => you'll have the manhattan distance.
    /// <br/>If used within a non-zero dimension the resulting FastBlockPos will be dimensionless as it's a distance or offset between two positions
    /// </summary>
    /// <param name="pos"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos SubCopy(FastBlockPos pos)
    {
        return new FastBlockPos(X - pos.X, InternalY - pos.InternalY, Z - pos.Z, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos SubCopy(int x, int y, int z)
    {
        return new FastBlockPos(X - x, Y - y, Z - z, dimension);
    }

    /// <summary>
    /// Creates a copy of this blocks position and divides it by given factor
    /// </summary>
    /// <param name="factor"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FastBlockPos DivCopy(int factor)
    {
        return new FastBlockPos(X / factor, Y / factor, Z / factor, dimension);
    }

    #endregion

    #region Operators

    public static FastBlockPos operator +(FastBlockPos left, FastBlockPos right)
    {
        return new FastBlockPos(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.dimension);
    }

    public static FastBlockPos operator -(FastBlockPos left, FastBlockPos right)
    {
        return new FastBlockPos(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.dimension);
    }

    public static FastBlockPos operator +(FastBlockPos left, int right)
    {
        return new FastBlockPos(left.X + right, left.Y + right, left.Z + right, left.dimension);
    }

    public static FastBlockPos operator -(FastBlockPos left, int right)
    {
        return new FastBlockPos(left.X - right, left.Y - right, left.Z - right, left.dimension);
    }

    public static FastBlockPos operator *(FastBlockPos left, int right)
    {
        return new FastBlockPos(left.X * right, left.Y * right, left.Z * right, left.dimension);
    }

    public static FastBlockPos operator *(int left, FastBlockPos right)
    {
        return new FastBlockPos(left * right.X, left * right.Y, left * right.Z, right.dimension);
    }

    public static FastBlockPos operator /(FastBlockPos left, int right)
    {
        return new FastBlockPos(left.X / right, left.Y / right, left.Z / right, left.dimension);
    }

    public static bool operator ==(FastBlockPos left, FastBlockPos right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FastBlockPos left, FastBlockPos right)
    {
        return !(left == right);
    }

    #endregion

    public readonly override string ToString()
    {
        return X + ", " + Y + ", " + Z + (dimension > 0 ? " : " + dimension : "");
    }

    public int ToIndex3d()
    {
        const int chunksizeMask = GlobalConstants.ChunkSize - 1;
        int lx = X & chunksizeMask;
        int lz = Z & chunksizeMask;
        return ((Y & chunksizeMask) * GlobalConstants.ChunkSize + lz) * GlobalConstants.ChunkSize + lx;
    }

    public static FastBlockPos FromIndex3d(int index3d, int cx, int cy, int cz)
    {
        const int chunksize = GlobalConstants.ChunkSize;
        int x = index3d % chunksize + cx * chunksize;
        int y = index3d / chunksize / chunksize + cy * chunksize;
        int z = index3d / chunksize % chunksize + cz * chunksize;
        return new FastBlockPos(x, y, z);
    }
}

public sealed class FastBlockPosComparer : IEqualityComparer<BlockPos>, IAlternateEqualityComparer<FastBlockPos, BlockPos>
{
    #region IEqualityComparer<BlockPos> Members

    public bool Equals(BlockPos? x, BlockPos? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.X == y.X && x.Y == y.Y && x.Z == y.Z && x.dimension == y.dimension;
    }

    public int GetHashCode(BlockPos obj) => obj.GetHashCode();

    #endregion

    #region IAlternateEqualityComparer<FastBlockPos, BlockPos> Members

    public bool Equals(FastBlockPos alternate, BlockPos other)
    {
        return alternate.X == other.X && alternate.Y == other.Y && alternate.Z == other.Z;
    }

    public int GetHashCode(FastBlockPos alternate)
    {
        return ((17 * 23 + alternate.X) * 23 + alternate.Y) * 23 + alternate.Z + alternate.dimension * 269023;
    }

    /// <summary>
    /// Creates a BlockPos from a FastBlockPos for example when a new entry is added to a dictionary
    /// </summary>
    /// <param name="alternate"></param>
    /// <returns></returns>
    public BlockPos Create(FastBlockPos alternate)
    {
        return new BlockPos(alternate.X, alternate.Y, alternate.Z, alternate.dimension);
    }

    #endregion
}
