using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

#nullable disable

namespace Vintagestory.API.Client
{
    /*
     * radfast thought cloud (February 2024):
     *   Reasons why this helps performance:
     *     1. MeshData used when tesselating chunks can be large in size, even non-full chunk tests with a lot of clutter and microblocks can produce 250k vertex chunks (the most complex bookshelves+books block models for example have around 300 vertices, a 32x32 wall of these would be 300k vertices).  Size in bytes is at least 34 x verticesCount, so on this example 10MB, and that's not counting xyzFaces and CustomInts etc
     *     2. Major heap memory spiking problem if such chunks are repeatedly tesselated, e.g. by player block breaking or placing actions, or grass growing etc.  Heap memory can grow by 1GB or more with 40-50 player actions
     *     3. New heap memory allocation process is also costly in CPU, can be e.g. 10ms for a large MeshData whose internal arrays are on the large object heap; the threshold MeshData size for some components being on the large object heap is around 7000 vertices
     *     4. Large MeshData objects from tesselating chunks have a short useful life in the engine; they are stored in TesselatedChunk objects, placed in the tessChunksQueue, then when they reach the head of the queue, they are uploaded to the GPU at the next opportunity, and references to them stored in MeshDataPool (whose functions are more like MeshDataIndexingPool); sometimes they are discarded before upload, e.g. if merging a new TesselatedChunk at the same coordinates
     *     5. The garbage collector is slow to collect them, for unknown reasons
     *   
     *   Technique used:
     *     6. MeshData.Dispose() is now called at all points where the MeshData is no longer required to be held in memory
     *     7. MeshData.Dispose() clears certain data fields, but retains the basic arrays (xyz, Uv, rgba, Flags, Indices) intact
     *     8. It then calls MeshDataRecycler.Recycle(). Recycle() adds the MeshData to a ConcurrentQueue of MeshData objects which are ready for recycling - "recycling" here means making available for re-use
     *     9. We do the actual recycling on the same "consumer thread" which attempts to fetch objects from the recycler, in order to avoid having to use locks everywhere
     *     10. Recycled and available MeshData objects are held in this class in one of three lists, smallSizes, mediumSizes and largeSizes
     *     11. Calling GetOrCreateMesh() on the "consumer thread" (ChunkTesselation thread) attempts first to retrieve a suitable existing MeshData object from the lists
     *     12. A "suitable" object here means the existing object has VerticesMax equal to or larger than the required VerticesMax  (but not much larger, we cap it at 25% larger to avoid wastefully using very large MeshData, and we don't allow jumping from the Small to the Medium pool in the search)
     *     13. If a suitable object is found, return that and the MeshData.CloneUsingRecycler() method will then repopulate the basic arrays without needing to do a new heap allocation
     *     14. If a suitable object is not found, we create a new MeshData object with the required characteristics; this can later be itself recycled
     *     
     *   Some fine details:
     *     15. We always create objects with a verticesCount (and therefore VerticesMax) rounded up to the nearest multiple of 4, because all or virtually all blocks in the game always have 4 verticesPerFace and therefore chunk meshdata has verticesCount as a multiple of 4; and because this ensures IndicesCount will be in the standard 6:4 ratio without rounding errors
     *     16. We use SortedList to hold the objects (sorted by a key derived from VerticesMax), this is to allow rapid binary search method when later looking for one of a suitable size
     *     17. SortedList prohibits duplicate keys (will throw an exception if Added) but there can be two MeshData of exactly the same size
     *     18. To solve this problem, we use float keys, with fraction values allowed, e.g. 87.0f, 87.125f, 87.25f are three keys for three MeshData objects all of size 87
     *     19. For VerticesMax sized in the millions (possible if spamming VertexEater objects), fractional values may reach the limits of precision of float; to mitigate this, the key is always VerticesMax/4 (because we know it must be a multiple of 4); and we test against equality due to float precision limits
     *     20. To access Array.BinarySearch on our SortedLists, we need to use reflection to access a private field in the library class :o
     *     
     *   Further implementation details
     *     21. Admission reserves actual basic-array bytes against each size-class budget, including pending returns.
     *     22. Once per second, including while idle, remove all entries unused for TTL milliseconds.
     *     23. Using and then recycling an element will refresh its timestamp
     *     24. A MeshData obtained from this system has its `Recyclable` field set to true; it will be set to false again when it is actually recycled; this prevents double attempts to recycle the same MeshData
     */


    /// <summary>
    /// This is a recycling system for MeshData objects, so that they can be re-used: helps performance by easing memory allocation pressure, at the cost of holding typically around 300-400MB of memory for these recycled objects
    /// </summary>
    public class MeshDataRecycler
    {
        public const int MinimumSizeForRecycling = 4096;    // Prevents the overheads of this for small objects, and prevents wasteful use of larger recycled objects for small-ish arrays (120 vertices, individual arrays at most 360 elements long)
        public const int TTL = 15000;                // 15 seconds in milliseconds
        private const int smallLimit = 368;          // Contains meshes up to around 50kb in size
        private const int mediumLimit = 3072;        // Contains meshes up to around 375kb in size; anything above this we call 'large'
        private const int Four = 4;                 // The divisor we use to calculate the size key from VerticesMax

        SortedList<float, MeshData> smallSizes = new SortedList<float, MeshData>();    // key is VerticesMax / 4; keys must be unique; fractional keys indicate equal VerticesMax up to 8 the same
        SortedList<float, MeshData> mediumSizes = new SortedList<float, MeshData>();
        SortedList<float, MeshData> largeSizes = new SortedList<float, MeshData>(); 

        private IClientWorldAccessor game;
        private volatile bool disposed;               // If set to true will fully disable further use of the recycling system (memory held will be cleared and non-recycled MeshData will always be created)
        private ConcurrentQueue<MeshData> forRecycling = new ConcurrentQueue<MeshData>();
        private FieldInfo keysAccessor;
        private readonly object recyclingLock = new object();

        // 40 MB for small meshes
        // 120 MB for medium meshes
        // 300 MB for large meshes
        private static readonly long[] byteBudgets = { 40*1024*1024, 120*1024*1024, 300*1024*1024 };
        private readonly long[] heldBytes = new long[3];
        private long nextExpiryCheck;

        private static int SizeClass(MeshData mesh) => mesh.VerticesMax / Four < smallLimit ? 0 : mesh.VerticesMax / Four < mediumLimit ? 1 : 2;

        private static long BasicDataBytes(MeshData mesh) => 4 * ((long)(mesh.xyz?.Length ?? 0) + (mesh.Uv?.Length ?? 0) + (mesh.Flags?.Length ?? 0) + (mesh.Indices?.Length ?? 0)) + (mesh.Rgba?.Length ?? 0);

        private void ReleaseReservation(MeshData mesh)
        {
            lock (recyclingLock)
            {
                heldBytes[SizeClass(mesh)] -= BasicDataBytes(mesh);
            }
        }

        private void Discard(MeshData mesh)
        {
            ReleaseReservation(mesh);
            mesh.DisposeBasicData();
        }

        public MeshDataRecycler(IClientWorldAccessor clientMain)
        {
            this.game = clientMain;

            // Annoyingly, the keys field in SortedList which we need to do a proper BinarySearch is private
            Type t = typeof(SortedList<float, MeshData>);
            keysAccessor = t.GetField("keys", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        // ===== Public methods =====

        /// <summary>
        /// Gets or creates a MeshData with basic data fields already allocated (may contain junk data) and capacity (VerticesMax) at least equal to minimumVertices; in MeshData created/recycled using this system, IndicesMax will be fixed equal to VerticesMax * 6 / 4
        /// </summary>
        /// <param name="minimumVertices"></param>
        /// <returns></returns>
        public MeshData GetOrCreateMesh(int minimumVertices)
        {
            minimumVertices = ((minimumVertices + Four - 1) / Four) * Four;    // Round up to next whole multiple of 4

            MeshData newMesh = disposed ? null : GetRecycled(minimumVertices);
            if (newMesh == null)
            {
                if (!disposed) minimumVertices = ((minimumVertices * 41 / 40 + Four - 1) / Four) * Four;    // Create one 2.5% larger than requested, this makes for better recyclability if vertices are being added (player building, lake ice forming, grass growing etc)
                newMesh = new MeshData(minimumVertices);
            }
            else
            {
                if (newMesh.IndicesMax != newMesh.VerticesMax * MeshData.StandardIndicesPerFace / MeshData.StandardVerticesPerFace)  // Check Indices is still in the standard 6:4 ratio
                {
                    newMesh.Indices = new int[newMesh.VerticesMax * MeshData.StandardIndicesPerFace / MeshData.StandardVerticesPerFace];
                    newMesh.IndicesMax = newMesh.Indices.Length;
                }
            }
            newMesh.Recyclable = true;
            return newMesh;
        }

        /// <summary>
        /// Call this periodically on the same thread which will call GetOrCreateMesh, this is required to ensure the Recycling system is up to date
        /// </summary>
        public void DoRecycling()
        {
            if (disposed)
            {
                // Lists belong to the consumer thread. Producers cannot enqueue after Dispose.
                while (forRecycling.TryDequeue(out MeshData pending)) Discard(pending);
                foreach (var mesh in smallSizes.Values) Discard(mesh);
                foreach (var mesh in mediumSizes.Values) Discard(mesh);
                foreach (var mesh in largeSizes.Values) Discard(mesh);
                smallSizes.Clear();
                mediumSizes.Clear();
                largeSizes.Clear();
                return;
            }

            long now = game.ElapsedMilliseconds;
            // Run even when idle, but avoid scanning every tesselation tick.
            if (now >= nextExpiryCheck)
            {
                RemoveExpired(smallSizes, now);
                RemoveExpired(mediumSizes, now);
                RemoveExpired(largeSizes, now);
                nextExpiryCheck = now + 1000;
            }

            while (forRecycling.TryDequeue(out MeshData recycled))
            {
                // TTL starts when returned, not when a delayed consumer eventually drains it.
                if (disposed || now - recycled.RecyclingTime >= TTL)
                {
                    Discard(recycled);
                    continue;
                }
                int entrySize = recycled.VerticesMax / Four;
                if (entrySize < smallLimit) TryAdd(smallSizes, entrySize, recycled);
                else if (entrySize < mediumLimit) TryAdd(mediumSizes, entrySize, recycled);
                else TryAdd(largeSizes, entrySize, recycled);
            }
        }

        /// <summary>
        /// Offer this MeshData to the recycling system: it will first be queued for recycling, and later processed to be either recycled or disposed of
        /// </summary>
        /// <param name="meshData"></param>
        public void Recycle(MeshData meshData)
        {
            long bytes = BasicDataBytes(meshData);
            int sizeClass = SizeClass(meshData);
            lock (recyclingLock)
            {
                if (!disposed && bytes > 0 && bytes <= byteBudgets[sizeClass] - heldBytes[sizeClass])
                {
                    heldBytes[sizeClass] += bytes;
                    meshData.RecyclingTime = game.ElapsedMilliseconds;
                    forRecycling.Enqueue(meshData);
                    return;
                }
            }
            meshData.DisposeBasicData();
        }

        /// <summary>
        /// Dispose of the MeshDataRecycler (normally on game exit, but can also be used to disable further use of it)
        /// </summary>
        public void Dispose()
        {
            lock (recyclingLock) this.disposed = true;
        }


        // ===== Private methods =====

        private void RemoveExpired(SortedList<float, MeshData> list, long now)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MeshData mesh = list.GetValueAtIndex(i);
                if (now - mesh.RecyclingTime < TTL) continue;
                list.RemoveAt(i);
                Discard(mesh);
            }
        }

        private MeshData GetRecycled(int minimumCapacity)
        {
            if (disposed) return null;

            int entrySize = minimumCapacity / Four;
            if (entrySize < smallLimit) return TryGet(smallSizes, entrySize);
            else if (entrySize < mediumLimit) return TryGet(mediumSizes, entrySize);
            return TryGet(largeSizes, entrySize);
        }

        private void TryAdd(SortedList<float, MeshData> list, int intkey, MeshData entry)
        {
            float key = intkey;
            while (key < intkey + 1)
            {
                if (list.TryAdd(key, entry)) return;    // Did we successfully add the entry with this key?

                float newkey = key + 0.25f;      // try up to 4 fractional values, to allow several list entries of the same size  (SortedList is not allowed to have duplicate keys);
                if (newkey == key) newkey = key + 0.5f;    // can happen due to float precision
                if (newkey == key) break;     // prevent endless looping
                key = newkey;
            }

            Discard(entry);    // Duplicate size slots are full; release both arrays and budget.
        }

        private MeshData TryGet(SortedList<float, MeshData> list, int entrySize)
        {
            if (list.Count == 0) return null;
            object keys = keysAccessor.GetValue(list);

            int index = Array.BinarySearch<float>((float[])keys, 0, list.Count, entrySize, null);
            if (index < 0)
            {
                index = ~index;    // Array.BinarySearch returns a twos-complement value if the key is not in the array, but one above it is
                if (index >= list.Count) return null;  // Required size exceeds the size of the largest in the list

                int foundSize = (int)list.GetKeyAtIndex(index);
                if (foundSize > entrySize * 5 / 4 + 64) return null;   // We allow up to 25% larger sizes (or even more for small sizes), as a smaller VerticesCount can still fit in to a larger MeshData
            }

            MeshData result = list.GetValueAtIndex(index);
            list.RemoveAt(index);
            ReleaseReservation(result);
            return result;
        }

    }


}
