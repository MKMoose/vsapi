using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Vintagestory.Datastructures; // Refactor: move to API namespace

/// <summary>
/// Simplifies usage of array pools by providing a automatic guard to returning the rented buffer.
/// <code>
/// void myThing() {
///   using var memory = new ArrayPoolReturner<byte>(1024);
///   // ... do thing with memory
///   // No need to return it afterwards, that happens automatically.
/// </code>
/// </summary>
public ref struct ArrayPoolReturner<T> : IDisposable {
    readonly ArrayPool<T>? pool;
             T[]?          borrowedArray;
    readonly int           requestedLength;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArrayPoolReturner(int length) : this(ArrayPool<T>.Shared, length) { }

    /// <remarks> Copies the data from span into the pooled array. </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArrayPoolReturner(ReadOnlySpan<T> data) : this(ArrayPool<T>.Shared, data) { }

    /// <remarks> Copies the data from span into the pooled array. </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArrayPoolReturner(ArrayPool<T> pool, ReadOnlySpan<T> data) : this(pool, data.Length)
    {
        data.CopyTo(this.borrowedArray);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArrayPoolReturner(ArrayPool<T> pool, int length)
    {
        this.pool            = pool;
        this.borrowedArray   = this.pool.Rent(length);
        this.requestedLength = length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Span<T>(in ArrayPoolReturner<T> self) => self.borrowedArray == null ? Span<T>.Empty : self.borrowedArray.AsSpan(0, self.requestedLength);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Memory<T>(in ArrayPoolReturner<T> self) => self.borrowedArray == null ? Memory<T>.Empty : self.borrowedArray.AsMemory(0, self.requestedLength);

    public readonly T[] BorrowedArray
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.borrowedArray ?? Array.Empty<T>();
    }

    public readonly int RequestedLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.requestedLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (borrowedArray == null) return;

        pool!.Return(borrowedArray);

        borrowedArray = null;
    }
}
