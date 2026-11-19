using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common
{
    /// <summary>
    /// Stores and queries persistent colliders outside the normal terrain and entity broad phases.
    /// Registration changes and queries may run concurrently. Returned snapshots remain valid after their registration is updated or removed.
    /// </summary>
    public interface ICollisionScene : IDisposable
    {
        /// <summary>
        /// Removes every collider registered to this entity, including registrations without a collider code.
        /// Returns the number removed.
        /// Entity despawn calls this after the owner callbacks on both client and server.
        /// </summary>
        int UnregisterEntityColliders(long ownerEntityId);

        /// <summary>
        /// Registers a persistent solid collider. The supplied boxes use dimension-local world coordinates and are copied by the scene.
        /// Supplying a selection position makes the boxes selectable as that block. Selection indices default to the collision-box order.
        /// </summary>
        long RegisterCollider(int dimension, IReadOnlyList<Cuboidd> collisionBoxes, BlockPos? selectionPosition = null, IReadOnlyList<int>? selectionBoxIndices = null, CollisionLayer collisionLayer = CollisionLayer.Solid, CollisionLayer collisionMask = CollisionLayer.All);

        /// <summary>
        /// Registers a collider whose boxes use collider-local coordinates.
        /// Updating its transform reuses that registered geometry. Colliders which carry entities can be used as moving platforms or floors.
        /// The owning entity's physics will ignore the collider when <paramref name="ownerEntityId"/> is nonzero.
        /// An owner collider code can identify that local registration on the client and server through <see cref="IEntityColliderProvider"/>.
        /// <paramref name="keepsSupportedEntitiesDry"/> prevents overlapping block liquid from affecting an entity while this collider supports it.
        /// Supplying a selection position makes the transformed boxes selectable as that block.
        /// </summary>
        long RegisterTransformedCollider(int dimension, IReadOnlyList<Cuboidd> localCollisionBoxes, ColliderTransform transform, bool canCarryEntities = false, long ownerEntityId = 0, string? ownerColliderCode = null, bool keepsSupportedEntitiesDry = false, CollisionLayer collisionLayer = CollisionLayer.Solid, CollisionLayer collisionMask = CollisionLayer.All, BlockPos? selectionPosition = null, IReadOnlyList<int>? selectionBoxIndices = null);

        /// <summary>
        /// Replaces the dimension, boxes, and optional block-selection metadata of a registered collider.
        /// Filtering is retained unless supplied.
        /// Replacing a transformed collider removes its transform and entity-owner metadata.
        /// </summary>
        bool UpdateCollider(long colliderId, int dimension, IReadOnlyList<Cuboidd> collisionBoxes, BlockPos? selectionPosition = null, IReadOnlyList<int>? selectionBoxIndices = null, CollisionLayer? collisionLayer = null, CollisionLayer? collisionMask = null);

        /// <summary>
        /// Updates the dimension and transform of a transformed collider without replacing its local geometry.
        /// <paramref name="elapsedSeconds"/> is the time since the previous transform and is used to derive point velocity for carrying entities.
        /// It will pass zero when velocity is not known.
        /// Block-selection metadata is retained and must remain in the collider's dimension.
        /// </summary>
        bool UpdateColliderTransform(long colliderId, int dimension, ColliderTransform transform, double elapsedSeconds = 0);

        bool UpdateColliderFilter(long colliderId, CollisionLayer collisionLayer, CollisionLayer collisionMask);

        /// <summary>
        /// Resolves an entity's unique collider code to its current local snapshot.
        /// </summary>
        bool TryGetCollider(long ownerEntityId, string ownerColliderCode, [NotNullWhen(true)] out Collider? collider);

        /// <summary>
        /// Replaces the caller-owned list with one hit per collision box, ordered by fraction, collider ID, then box index.
        /// Sweeps translation against current snapshots without sweeping target motion or query rotation.
        /// Initial strict overlaps are included by default.
        /// Motion along or away from a touching surface is ignored.
        /// The query box must not be mutated until the call returns.
        /// </summary>
        void Sweep(TransformedCuboidd box, double motionX, double motionY, double motionZ, int dimension, List<CollisionHit> results, CollisionQueryFilter? filter = null, bool includeInitialOverlap = true);

        void Sweep(Cuboidd box, double motionX, double motionY, double motionZ, int dimension, List<CollisionHit> results, CollisionQueryFilter? filter = null, bool includeInitialOverlap = true);

        /// <summary>
        /// Casts a finite ray from origin to origin plus displacement using the shared sweep hit contract.
        /// The displacement need not be normalized.
        /// A zero displacement tests whether the origin is inside a box.
        /// </summary>
        void Raycast(FastVec3d origin, FastVec3d displacement, int dimension, List<CollisionHit> results, CollisionQueryFilter? filter = null, bool includeInitialOverlap = true);

        /// <summary>
        /// Removes a registered collider.
        /// </summary>
        bool UnregisterCollider(long colliderId);

        /// <summary>
        /// Gets the current immutable snapshot of a registered collider.
        /// </summary>
        bool TryGetCollider(long colliderId, [NotNullWhen(true)] out Collider? collider);

        /// <summary>
        /// Replaces <paramref name="results"/> with the colliders whose bounds overlap the supplied axis-aligned, dimension-local bounds.
        /// Returned colliders and their boxes are owned by the scene and must not be modified.
        /// The caller owns the results list and must not use the same list from concurrent queries.
        /// </summary>
        void GetColliders(Cuboidd bounds, int dimension, List<Collider> results, CollisionQueryFilter? filter = null);

        /// <summary>
        /// Replaces <paramref name="results"/> with colliders whose exact boxes intersect the supplied transformed cuboid.
        /// The query box must not be mutated until this call returns.
        /// </summary>
        void GetIntersectingColliders(TransformedCuboidd box, int dimension, List<Collider> results, bool alsoCheckTouch = true, CollisionQueryFilter? filter = null);

        /// <summary>
        /// Tests the exact boxes of the registered colliders against the supplied axis-aligned, dimension-local bounds.
        /// Unlike <see cref="GetColliders"/>, this method keeps no caller state and stops at the first hit, so concurrent calls are safe.
        /// </summary>
        bool IntersectsAny(Cuboidd bounds, int dimension, bool alsoCheckTouch = true, CollisionQueryFilter? filter = null);

        /// <summary>
        /// Tests the exact boxes of the registered colliders against a transformed cuboid.
        /// The query box must not be mutated until this call returns.
        /// </summary>
        bool IntersectsAny(TransformedCuboidd box, int dimension, bool alsoCheckTouch = true, CollisionQueryFilter? filter = null);
    }

    /// <summary>
    /// Resolves an entity-owned collider from a stable code shared by the client and server.
    /// </summary>
    public interface IEntityColliderProvider
    {
        /// <summary>
        /// Resolves a collider code to its local scene registration.
        /// </summary>
        bool TryGetColliderId(string colliderCode, out long colliderId);
    }

    /// <summary>
    /// An immutable rigid transform for collider-local geometry.
    /// </summary>
    public readonly struct ColliderTransform : IEquatable<ColliderTransform>
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double QuaternionX { get; }
        public double QuaternionY { get; }
        public double QuaternionZ { get; }
        public double QuaternionW { get; }

        public ColliderTransform(double x, double y, double z, double quaternionX, double quaternionY, double quaternionZ, double quaternionW)
        {
            double lengthSq = quaternionX * quaternionX + quaternionY * quaternionY + quaternionZ * quaternionZ + quaternionW * quaternionW;
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || !double.IsFinite(lengthSq) || lengthSq <= 0)
            {
                throw new ArgumentException("Collision transforms require a finite position and nonzero quaternion.");
            }

            double inverseLength = 1 / Math.Sqrt(lengthSq);
            X = x;
            Y = y;
            Z = z;
            QuaternionX = quaternionX * inverseLength;
            QuaternionY = quaternionY * inverseLength;
            QuaternionZ = quaternionZ * inverseLength;
            QuaternionW = quaternionW * inverseLength;
        }

        public ColliderTransform(EntityPos position, double[] quaternion)
            : this(
                requirePosition(position).X,
                position.Y,
                position.Z,
                requireQuaternion(quaternion)[0],
                quaternion[1],
                quaternion[2],
                quaternion[3])
        {
        }

        public ColliderTransform(double x, double y, double z)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                throw new ArgumentException("Collision transforms require a finite position.");
            }

            X = x;
            Y = y;
            Z = z;
            QuaternionX = 0;
            QuaternionY = 0;
            QuaternionZ = 0;
            QuaternionW = 1;
        }

        public ColliderTransform(EntityPos position) : this(requirePosition(position).X, position.Y, position.Z)
        {
        }

        /// <summary>
        /// Transforms a collider-local point into dimension-local world coordinates.
        /// </summary>
        public void TransformPoint(double x, double y, double z, out double worldX, out double worldY, out double worldZ)
        {
            rotatePoint(QuaternionX, QuaternionY, QuaternionZ, QuaternionW, x, y, z, out worldX, out worldY, out worldZ);
            worldX += X;
            worldY += Y;
            worldZ += Z;
        }

        public void TransformPoint(double x, double y, double z, out FastVec3d worldPoint)
        {
            TransformPoint(x, y, z, out double worldX, out double worldY, out double worldZ);
            worldPoint = new(worldX, worldY, worldZ);
        }

        public void TransformPoint(FastVec3d point, out double worldX, out double worldY, out double worldZ)
        {
            TransformPoint(point.X, point.Y, point.Z, out worldX, out worldY, out worldZ);
        }

        public void TransformPoint(FastVec3d point, out FastVec3d worldPoint)
        {
            TransformPoint(point.X, point.Y, point.Z, out worldPoint);
        }

        public void TransformPoint(Vec3d point, Vec3d worldPoint)
        {
            TransformPoint(point.X, point.Y, point.Z, out double worldX, out double worldY, out double worldZ);
            worldPoint.Set(worldX, worldY, worldZ);
        }

        /// <summary>
        /// Transforms a dimension-local world point into collider-local coordinates.
        /// </summary>
        public void InverseTransformPoint(double x, double y, double z, out double localX, out double localY, out double localZ)
        {
            rotatePoint(-QuaternionX, -QuaternionY, -QuaternionZ, QuaternionW, x - X, y - Y, z - Z, out localX, out localY, out localZ);
        }

        public void InverseTransformPoint(double x, double y, double z, out FastVec3d localPoint)
        {
            InverseTransformPoint(x, y, z, out double localX, out double localY, out double localZ);
            localPoint = new(localX, localY, localZ);
        }

        public void InverseTransformPoint(FastVec3d point, out double localX, out double localY, out double localZ)
        {
            InverseTransformPoint(point.X, point.Y, point.Z, out localX, out localY, out localZ);
        }

        public void InverseTransformPoint(FastVec3d point, out FastVec3d localPoint)
        {
            InverseTransformPoint(point.X, point.Y, point.Z, out localPoint);
        }

        public void InverseTransformPoint(Vec3d point, Vec3d localPoint)
        {
            InverseTransformPoint(point.X, point.Y, point.Z, out double localX, out double localY, out double localZ);
            localPoint.Set(localX, localY, localZ);
        }

        /// <summary>
        /// Rotates a collider-local direction into dimension-local world coordinates.
        /// </summary>
        public void TransformDirection(double x, double y, double z, out double worldX, out double worldY, out double worldZ)
        {
            rotatePoint(QuaternionX, QuaternionY, QuaternionZ, QuaternionW, x, y, z, out worldX, out worldY, out worldZ);
        }

        public void TransformDirection(FastVec3d direction, out FastVec3d worldDirection)
        {
            TransformDirection(direction.X, direction.Y, direction.Z, out double worldX, out double worldY, out double worldZ);
            worldDirection = new(worldX, worldY, worldZ);
        }

        public void TransformDirection(Vec3d direction, Vec3d worldDirection)
        {
            TransformDirection(direction.X, direction.Y, direction.Z, out double worldX, out double worldY, out double worldZ);
            worldDirection.Set(worldX, worldY, worldZ);
        }

        /// <summary>
        /// Rotates a dimension-local world direction into collider-local coordinates.
        /// </summary>
        public void InverseTransformDirection(double x, double y, double z, out double localX, out double localY, out double localZ)
        {
            rotatePoint(-QuaternionX, -QuaternionY, -QuaternionZ, QuaternionW, x, y, z, out localX, out localY, out localZ);
        }

        public void InverseTransformDirection(FastVec3d direction, out FastVec3d localDirection)
        {
            InverseTransformDirection(direction.X, direction.Y, direction.Z, out double localX, out double localY, out double localZ);
            localDirection = new(localX, localY, localZ);
        }

        public void InverseTransformDirection(Vec3d direction, Vec3d localDirection)
        {
            InverseTransformDirection(direction.X, direction.Y, direction.Z, out double localX, out double localY, out double localZ);
            localDirection.Set(localX, localY, localZ);
        }

        public bool Equals(ColliderTransform other)
        {
            return X == other.X
                && Y == other.Y
                && Z == other.Z
                && QuaternionX == other.QuaternionX
                && QuaternionY == other.QuaternionY
                && QuaternionZ == other.QuaternionZ
                && QuaternionW == other.QuaternionW;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is ColliderTransform other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z, QuaternionX, QuaternionY, QuaternionZ, QuaternionW);

        /// <summary>
        /// Whether this transform contains a finite position and a nonzero finite quaternion.
        /// </summary>
        public bool IsValid => double.IsFinite(X)
            && double.IsFinite(Y)
            && double.IsFinite(Z)
            && double.IsFinite(QuaternionX)
            && double.IsFinite(QuaternionY)
            && double.IsFinite(QuaternionZ)
            && double.IsFinite(QuaternionW)
            && QuaternionX * QuaternionX + QuaternionY * QuaternionY + QuaternionZ * QuaternionZ + QuaternionW * QuaternionW > 0;

        /// <summary>
        /// Returns the normalized rotation quaternion as a new array.
        /// </summary>
        public double[] GetRotationQuaternion() => [QuaternionX, QuaternionY, QuaternionZ, QuaternionW];

        private static EntityPos requirePosition(EntityPos position)
        {
            return position ?? throw new ArgumentNullException(nameof(position));
        }

        private static double[] requireQuaternion(double[] quaternion)
        {
            if (quaternion == null) throw new ArgumentNullException(nameof(quaternion));
            if (quaternion.Length != 4) throw new ArgumentException("Collision transform quaternions require four components.", nameof(quaternion));
            return quaternion;
        }

        private static void rotatePoint(double qx, double qy, double qz, double qw, double x, double y, double z, out double rotatedX, out double rotatedY, out double rotatedZ)
        {
            double tx = 2 * (qy * z - qz * y);
            double ty = 2 * (qz * x - qx * z);
            double tz = 2 * (qx * y - qy * x);
            rotatedX = x + qw * tx + qy * tz - qz * ty;
            rotatedY = y + qw * ty + qz * tx - qx * tz;
            rotatedZ = z + qw * tz + qx * ty - qy * tx;
        }
    }

    /// <summary>
    /// A registration snapshot containing one or more world-space collision boxes.
    /// </summary>
    public sealed class Collider
    {
        private readonly WorldGeometry? worldGeometry;
        private readonly LocalGeometry? localGeometry;
        private readonly int[]? selectionBoxIndices;
        private WorldGeometry? materializedWorldGeometry;

        [ThreadStatic]
        private static QueryScratch? threadQueryScratch;
        private static QueryScratch queryScratch => threadQueryScratch ??= new QueryScratch();

        /// <summary>
        /// The scene-local registration ID, retained across updates and never reused.
        /// Do not persist this ID or send it across the network.
        /// </summary>
        public long Id { get; }
        public CollisionLayer CollisionLayer { get; }
        public CollisionLayer CollisionMask { get; }
        public int Dimension { get; }

        /// <summary>
        /// The entity whose own physics should ignore this collider, or zero when the collider has no entity owner.
        /// </summary>
        public long OwnerEntityId { get; }

        /// <summary>
        /// The stable owner-local code used to resolve this collider independently on each side.
        /// </summary>
        public string? OwnerColliderCode { get; }

        /// <summary>
        /// The conservative axis-aligned bounds of this collider. The returned cuboid is owned by this snapshot and must not be modified.
        /// </summary>
        public Cuboidd Bounds { get; }

        public int CollisionBoxCount => worldGeometry?.CollisionBoxes.Length ?? localGeometry!.CollisionBoxes.Length;

        /// <summary>
        /// True when this collider was registered with local geometry and a transform.
        /// </summary>
        public bool IsTransformed => localGeometry != null;

        /// <summary>
        /// The current rigid transform. This is the identity transform for a world-space collider.
        /// </summary>
        public ColliderTransform Transform { get; }

        /// <summary>
        /// The preceding rigid transform used to derive point velocity.
        /// </summary>
        public ColliderTransform PreviousTransform { get; }

        /// <summary>
        /// The elapsed seconds between <see cref="PreviousTransform"/> and <see cref="Transform"/>, or zero when velocity is not known.
        /// </summary>
        public double TransformElapsedSeconds { get; }

        /// <summary>
        /// Whether controlled entities standing on this collider should follow its transform.
        /// </summary>
        public bool CanCarryEntities { get; }

        /// <summary>
        /// Whether controlled entities supported by this collider should ignore liquid occupying the same world position.
        /// </summary>
        public bool KeepsSupportedEntitiesDry { get; }

        /// <summary>
        /// The block represented when this collider is selected, or null when the collider is not selectable.
        /// </summary>
        public BlockPos? SelectionPosition { get; }

        internal Collider(long id, int dimension, IReadOnlyList<Cuboidd> collisionBoxes, BlockPos? selectionPosition = null, IReadOnlyList<int>? selectionBoxIndices = null, CollisionLayer collisionLayer = CollisionLayer.Solid, CollisionLayer collisionMask = CollisionLayer.All)
        {
            worldGeometry = new WorldGeometry(copyCollisionBoxes(collisionBoxes));
            validateSelection(dimension, worldGeometry.CollisionBoxes.Length, selectionPosition, selectionBoxIndices);

            Id = id;
            CollisionLayer = collisionLayer;
            CollisionMask = collisionMask;
            Dimension = dimension;
            Transform = PreviousTransform = new ColliderTransform(0, 0, 0);
            Bounds = worldGeometry.Bounds;
            SelectionPosition = selectionPosition?.Copy();
            this.selectionBoxIndices = copySelectionBoxIndices(selectionBoxIndices);
        }

        private Collider(long id, int dimension, LocalGeometry localGeometry, ColliderTransform transform, ColliderTransform previousTransform, double transformElapsedSeconds, bool canCarryEntities, long ownerEntityId, string? ownerColliderCode, bool keepsSupportedEntitiesDry, CollisionLayer collisionLayer, CollisionLayer collisionMask, BlockPos? selectionPosition, int[]? selectionBoxIndices)
        {
            validateSelection(dimension, localGeometry.CollisionBoxes.Length, selectionPosition, selectionBoxIndices);

            Id = id;
            CollisionLayer = collisionLayer;
            CollisionMask = collisionMask;
            Dimension = dimension;
            OwnerEntityId = ownerEntityId;
            OwnerColliderCode = ownerColliderCode;
            Transform = transform;
            PreviousTransform = previousTransform;
            TransformElapsedSeconds = transformElapsedSeconds;
            CanCarryEntities = canCarryEntities;
            KeepsSupportedEntitiesDry = keepsSupportedEntitiesDry;
            SelectionPosition = selectionPosition;
            this.selectionBoxIndices = selectionBoxIndices;
            this.localGeometry = localGeometry;
            Bounds = getTransformedBounds(localGeometry.Bounds, transform);
        }

        internal static Collider CreateTransformed(long id, int dimension, IReadOnlyList<Cuboidd> localCollisionBoxes, ColliderTransform transform, bool canCarryEntities, long ownerEntityId, string? ownerColliderCode, bool keepsSupportedEntitiesDry, CollisionLayer collisionLayer = CollisionLayer.Solid, CollisionLayer collisionMask = CollisionLayer.All, BlockPos? selectionPosition = null, IReadOnlyList<int>? selectionBoxIndices = null)
        {
            if (!transform.IsValid) throw new ArgumentException("A transformed collider requires a valid transform.", nameof(transform));
            if (ownerEntityId < 0) throw new ArgumentOutOfRangeException(nameof(ownerEntityId), "A collider owner must be zero or a valid entity ID.");
            if (ownerColliderCode != null)
            {
                if (ownerColliderCode.Length == 0) throw new ArgumentException("An owner collider code cannot be empty.", nameof(ownerColliderCode));
                if (ownerEntityId == 0) throw new ArgumentException("An owner collider code requires an owning entity.", nameof(ownerColliderCode));
            }

            var geometry = new LocalGeometry(copyCollisionBoxes(localCollisionBoxes));
            return new Collider(id, dimension, geometry, transform, transform, 0, canCarryEntities, ownerEntityId, ownerColliderCode, keepsSupportedEntitiesDry, collisionLayer, collisionMask, selectionPosition?.Copy(), copySelectionBoxIndices(selectionBoxIndices));
        }

        private Collider(Collider source, CollisionLayer collisionLayer, CollisionLayer collisionMask)
        {
            Id = source.Id;
            Dimension = source.Dimension;
            CollisionLayer = collisionLayer;
            CollisionMask = collisionMask;
            OwnerEntityId = source.OwnerEntityId;
            OwnerColliderCode = source.OwnerColliderCode;
            Transform = source.Transform;
            PreviousTransform = source.PreviousTransform;
            TransformElapsedSeconds = source.TransformElapsedSeconds;
            CanCarryEntities = source.CanCarryEntities;
            KeepsSupportedEntitiesDry = source.KeepsSupportedEntitiesDry;
            Bounds = source.Bounds;
            SelectionPosition = source.SelectionPosition;
            selectionBoxIndices = source.selectionBoxIndices;
            worldGeometry = source.worldGeometry;
            localGeometry = source.localGeometry;
            materializedWorldGeometry = Volatile.Read(ref source.materializedWorldGeometry);
        }

        internal Collider WithFilter(CollisionLayer collisionLayer, CollisionLayer collisionMask) => new(this, collisionLayer, collisionMask);

        /// <summary>
        /// Creates an unpublished snapshot at another pose, sharing this collider's local geometry.
        /// </summary>
        public Collider WithTransform(int dimension, ColliderTransform transform, double elapsedSeconds = 0)
        {
            if (localGeometry == null) throw new InvalidOperationException("A world-space collider does not have a transform to update.");
            if (!transform.IsValid) throw new ArgumentException("A transformed collider requires a valid transform.", nameof(transform));
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed seconds must be finite and nonnegative.");

            bool canDeriveVelocity = elapsedSeconds > 0 && dimension == Dimension;
            ColliderTransform previousTransform = canDeriveVelocity ? Transform : transform;
            return new Collider(Id, dimension, localGeometry, transform, previousTransform, canDeriveVelocity ? elapsedSeconds : 0, CanCarryEntities, OwnerEntityId, OwnerColliderCode, KeepsSupportedEntitiesDry, CollisionLayer, CollisionMask, SelectionPosition, selectionBoxIndices);
        }

        /// <summary>
        /// Gets an exact collision box owned by this snapshot. The returned cuboid must not be modified.
        /// </summary>
        public Cuboidd GetCollisionBox(int index) => getWorldGeometry().CollisionBoxes[index];

        /// <summary>
        /// Gets the conservative axis-aligned bounds of a collision box. The returned cuboid must not be modified.
        /// </summary>
        public Cuboidd GetCollisionBoxBounds(int index) => getWorldGeometry().CollisionBoxBounds[index];

        /// <summary>
        /// Gets the owning block's selection-box index for a collision box.
        /// </summary>
        public int GetSelectionBoxIndex(int collisionBoxIndex) => selectionBoxIndices?[collisionBoxIndex] ?? collisionBoxIndex;

        /// <summary>
        /// Tests whether any box in this collider intersects the supplied axis-aligned box.
        /// </summary>
        public bool Intersects(Cuboidd box) => intersects(box, false);

        /// <summary>
        /// Tests whether any box in this collider intersects or touches the supplied axis-aligned box.
        /// </summary>
        public bool IntersectsOrTouches(Cuboidd box) => intersects(box, true);

        /// <summary>
        /// Tests whether any box in this collider intersects the supplied transformed cuboid.
        /// </summary>
        public bool Intersects(TransformedCuboidd box) => intersects(box, false);

        /// <summary>
        /// Tests whether any box in this collider intersects or touches the supplied transformed cuboid.
        /// </summary>
        public bool IntersectsOrTouches(TransformedCuboidd box) => intersects(box, true);

        /// <summary>
        /// Gets the shortest translation which moves an overlapping axis-aligned cuboid out of one collision box.
        /// </summary>
        public bool TryGetAABBPenetration(int collisionBoxIndex, Cuboidd box, out double translationX, out double translationY, out double translationZ)
        {
            if (localGeometry == null)
            {
                return worldGeometry!.TransformedBoxes[collisionBoxIndex].TryGetAABBPenetration(box, out translationX, out translationY, out translationZ);
            }

            TransformedCuboidd localQuery = getLocalQuery(box);
            if (!localGeometry.TransformedBoxes[collisionBoxIndex].TryGetPenetration(localQuery, out double localX, out double localY, out double localZ))
            {
                translationX = 0;
                translationY = 0;
                translationZ = 0;
                return false;
            }

            Transform.TransformDirection(localX, localY, localZ, out translationX, out translationY, out translationZ);
            return true;
        }

        /// <summary>
        /// Gets the outward contact normal and signed separation from one collision box to a nearby axis-aligned box.
        /// </summary>
        public bool TryGetAABBContact(int collisionBoxIndex, Cuboidd box, double maxSeparation, out CuboidContact contact)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));

            if (localGeometry == null)
            {
                return worldGeometry!.TransformedBoxes[collisionBoxIndex].TryGetAABBContact(box, maxSeparation, out contact);
            }

            TransformedCuboidd localQuery = getLocalQuery(box);
            if (!localGeometry.TransformedBoxes[collisionBoxIndex].TryGetContact(localQuery, maxSeparation, out CuboidContact localContact))
            {
                contact = default;
                return false;
            }

            Transform.TransformDirection(localContact.Normal.X, localContact.Normal.Y, localContact.Normal.Z, out double normalX, out double normalY, out double normalZ);
            contact = new CuboidContact(new FastVec3d(normalX, normalY, normalZ), localContact.Separation);
            return true;
        }

        public double PushOutX(Cuboidd box, double motion, ref EnumPushDirection direction) => pushOut(box, motion, EnumAxis.X, ref direction);

        public double PushOutY(Cuboidd box, double motion, ref EnumPushDirection direction) => pushOut(box, motion, EnumAxis.Y, ref direction);

        public double PushOutZ(Cuboidd box, double motion, ref EnumPushDirection direction) => pushOut(box, motion, EnumAxis.Z, ref direction);

        /// <summary>
        /// Appends one shared hit per intersected collision box to a caller-owned list without clearing or sorting it.
        /// </summary>
        public void CollectSweepHits(TransformedCuboidd movingBox, double motionX, double motionY, double motionZ, List<CollisionHit> results, bool includeInitialOverlap = true)
        {
            if (movingBox == null) throw new ArgumentNullException(nameof(movingBox));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (!double.IsFinite(motionX) || !double.IsFinite(motionY) || !double.IsFinite(motionZ)) throw new ArgumentException("Sweep motion must be finite.");
            if (!sweepBoundsIntersect(movingBox.BoundsX1, movingBox.BoundsY1, movingBox.BoundsZ1, movingBox.BoundsX2, movingBox.BoundsY2, movingBox.BoundsZ2, motionX, motionY, motionZ)) return;

            TransformedCuboidd query = movingBox;
            double queryMotionX = motionX;
            double queryMotionY = motionY;
            double queryMotionZ = motionZ;
            if (localGeometry != null)
            {
                query = getLocalQuery(movingBox);
                Transform.InverseTransformDirection(motionX, motionY, motionZ, out queryMotionX, out queryMotionY, out queryMotionZ);
            }

            TransformedCuboidd[] boxes = localGeometry?.TransformedBoxes ?? worldGeometry!.TransformedBoxes;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!boxes[i].TrySweepContact(query, queryMotionX, queryMotionY, queryMotionZ, out double fraction, out FastVec3d normal, out bool startedOverlapping, includeInitialOverlap)) continue;
                if (localGeometry != null) Transform.TransformDirection(normal, out normal);
                results.Add(new CollisionHit(this, i, fraction, normal, startedOverlapping));
            }
        }

        /// <summary>
        /// Finds the first contact while an axis-aligned box translates through this collider.
        /// On success, <paramref name="collisionFraction"/> is in the range from zero inclusive to one exclusive; otherwise it is one.
        /// </summary>
        public bool TrySweepAABB(Cuboidd movingBox, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            if (movingBox == null) throw new ArgumentNullException(nameof(movingBox));

            collisionFraction = 1;
            if (motionX == 0 && motionY == 0 && motionZ == 0) return false;
            if (!sweepBoundsIntersect(movingBox.X1, movingBox.Y1, movingBox.Z1, movingBox.X2, movingBox.Y2, movingBox.Z2, motionX, motionY, motionZ)) return false;

            if (localGeometry == null)
            {
                return trySweepAabb(worldGeometry!.TransformedBoxes, movingBox, motionX, motionY, motionZ, out collisionFraction);
            }

            TransformedCuboidd localQuery = getLocalQuery(movingBox);
            Transform.InverseTransformDirection(motionX, motionY, motionZ, out double localMotionX, out double localMotionY, out double localMotionZ);
            return trySweep(localGeometry.TransformedBoxes, localQuery, localMotionX, localMotionY, localMotionZ, out collisionFraction);
        }

        /// <summary>
        /// Finds the first contact while a transformed cuboid translates through this collider without rotating.
        /// On success, <paramref name="collisionFraction"/> is in the range from zero inclusive to one exclusive; otherwise it is one.
        /// </summary>
        public bool TrySweep(TransformedCuboidd movingBox, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            if (movingBox == null) throw new ArgumentNullException(nameof(movingBox));

            collisionFraction = 1;
            if (motionX == 0 && motionY == 0 && motionZ == 0) return false;
            if (!sweepBoundsIntersect(movingBox.BoundsX1, movingBox.BoundsY1, movingBox.BoundsZ1, movingBox.BoundsX2, movingBox.BoundsY2, movingBox.BoundsZ2, motionX, motionY, motionZ)) return false;

            if (localGeometry == null)
            {
                return trySweep(worldGeometry!.TransformedBoxes, movingBox, motionX, motionY, motionZ, out collisionFraction);
            }

            TransformedCuboidd localQuery = getLocalQuery(movingBox);
            Transform.InverseTransformDirection(motionX, motionY, motionZ, out double localMotionX, out double localMotionY, out double localMotionZ);
            return trySweep(localGeometry.TransformedBoxes, localQuery, localMotionX, localMotionY, localMotionZ, out collisionFraction);
        }

        /// <summary>
        /// Gets the world velocity of a collider-local point in blocks per second.
        /// </summary>
        public void GetPointVelocity(double localX, double localY, double localZ, out double velocityX, out double velocityY, out double velocityZ)
        {
            if (TransformElapsedSeconds <= 0)
            {
                velocityX = 0;
                velocityY = 0;
                velocityZ = 0;
                return;
            }

            PreviousTransform.TransformPoint(localX, localY, localZ, out double previousX, out double previousY, out double previousZ);
            Transform.TransformPoint(localX, localY, localZ, out double currentX, out double currentY, out double currentZ);
            velocityX = (currentX - previousX) / TransformElapsedSeconds;
            velocityY = (currentY - previousY) / TransformElapsedSeconds;
            velocityZ = (currentZ - previousZ) / TransformElapsedSeconds;
        }

        public void GetPointVelocity(FastVec3d localPoint, out FastVec3d velocity)
        {
            GetPointVelocity(localPoint.X, localPoint.Y, localPoint.Z, out double velocityX, out double velocityY, out double velocityZ);
            velocity = new(velocityX, velocityY, velocityZ);
        }

        private bool intersects(Cuboidd box, bool alsoCheckTouch)
        {
            bool boundsIntersect = alsoCheckTouch ? Bounds.IntersectsOrTouches(box) : Bounds.Intersects(box);
            if (!boundsIntersect) return false;

            if (localGeometry != null)
            {
                TransformedCuboidd localQuery = getLocalQuery(box);
                return intersects(localGeometry.TransformedBoxes, localQuery, alsoCheckTouch);
            }

            for (int i = 0; i < worldGeometry!.TransformedBoxes.Length; i++)
            {
                Cuboidd boxBounds = worldGeometry.CollisionBoxBounds[i];
                boundsIntersect = alsoCheckTouch ? boxBounds.IntersectsOrTouches(box) : boxBounds.Intersects(box);
                if (!boundsIntersect) continue;

                TransformedCuboidd transformedBox = worldGeometry.TransformedBoxes[i];
                if (alsoCheckTouch ? transformedBox.IntersectsOrTouches(box) : transformedBox.Intersects(box)) return true;
            }
            return false;
        }

        private bool intersects(TransformedCuboidd box, bool alsoCheckTouch)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));

            bool boundsIntersect = alsoCheckTouch
                ? box.BoundsX2 >= Bounds.X1 && box.BoundsX1 <= Bounds.X2 && box.BoundsY2 >= Bounds.Y1 && box.BoundsY1 <= Bounds.Y2 && box.BoundsZ2 >= Bounds.Z1 && box.BoundsZ1 <= Bounds.Z2
                : box.BoundsX2 > Bounds.X1 && box.BoundsX1 < Bounds.X2 && box.BoundsY2 > Bounds.Y1 && box.BoundsY1 < Bounds.Y2 && box.BoundsZ2 > Bounds.Z1 && box.BoundsZ1 < Bounds.Z2;
            if (!boundsIntersect) return false;

            TransformedCuboidd query = localGeometry == null ? box : getLocalQuery(box);
            TransformedCuboidd[] collisionBoxes = localGeometry?.TransformedBoxes ?? worldGeometry!.TransformedBoxes;
            return intersects(collisionBoxes, query, alsoCheckTouch);
        }

        private static bool intersects(TransformedCuboidd[] collisionBoxes, TransformedCuboidd box, bool alsoCheckTouch)
        {
            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                if (alsoCheckTouch ? collisionBoxes[i].IntersectsOrTouches(box) : collisionBoxes[i].Intersects(box)) return true;
            }
            return false;
        }

        private double pushOut(Cuboidd box, double motion, EnumAxis axis, ref EnumPushDirection direction)
        {
            direction = EnumPushDirection.None;
            if (motion == 0) return 0;

            double motionX = axis == EnumAxis.X ? motion : 0;
            double motionY = axis == EnumAxis.Y ? motion : 0;
            double motionZ = axis == EnumAxis.Z ? motion : 0;
            if (!sweepBoundsIntersect(box.X1, box.Y1, box.Z1, box.X2, box.Y2, box.Z2, motionX, motionY, motionZ)) return motion;

            if (localGeometry == null)
            {
                EnumPushDirection colliderDirection = EnumPushDirection.None;
                for (int i = 0; i < worldGeometry!.CollisionBoxes.Length; i++)
                {
                    EnumPushDirection boxDirection = EnumPushDirection.None;
                    motion = axis switch
                    {
                        EnumAxis.X => worldGeometry.CollisionBoxes[i].pushOutX(box, motion, ref boxDirection),
                        EnumAxis.Y => worldGeometry.CollisionBoxes[i].pushOutY(box, motion, ref boxDirection),
                        _ => worldGeometry.CollisionBoxes[i].pushOutZ(box, motion, ref boxDirection)
                    };
                    if (boxDirection != EnumPushDirection.None) colliderDirection = boxDirection;
                    if (motion == 0) break;
                }

                direction = colliderDirection;
                return motion;
            }

            TransformedCuboidd localQuery = getLocalQuery(box);
            EnumPushDirection transformedDirection = EnumPushDirection.None;
            for (int i = 0; i < localGeometry.TransformedBoxes.Length; i++)
            {
                motionX = axis == EnumAxis.X ? motion : 0;
                motionY = axis == EnumAxis.Y ? motion : 0;
                motionZ = axis == EnumAxis.Z ? motion : 0;
                Transform.InverseTransformDirection(motionX, motionY, motionZ, out double localMotionX, out double localMotionY, out double localMotionZ);
                if (!localGeometry.TransformedBoxes[i].TrySweep(localQuery, localMotionX, localMotionY, localMotionZ, out double collisionFraction)) continue;

                transformedDirection = motion > 0 ? EnumPushDirection.Positive : EnumPushDirection.Negative;
                double travel = motion * collisionFraction;
                double backOff = motion > 0 ? RotatedCuboidMath.EpsDouble : -RotatedCuboidMath.EpsDouble;
                motion = Math.Abs(travel) <= RotatedCuboidMath.EpsDouble ? 0 : travel - backOff;
                if (motion == 0) break;
            }

            direction = transformedDirection;
            return motion;
        }

        private TransformedCuboidd getLocalQuery(Cuboidd box)
        {
            QueryScratch scratch = queryScratch;
            scratch.Center.Set(box.MidX, box.MidY, box.MidZ);
            scratch.HalfAxisX.Set(box.HalfExtentX, 0, 0);
            scratch.HalfAxisY.Set(0, box.HalfExtentY, 0);
            scratch.HalfAxisZ.Set(0, 0, box.HalfExtentZ);

            Transform.InverseTransformPoint(scratch.Center, scratch.Center);
            Transform.InverseTransformDirection(scratch.HalfAxisX, scratch.HalfAxisX);
            Transform.InverseTransformDirection(scratch.HalfAxisY, scratch.HalfAxisY);
            Transform.InverseTransformDirection(scratch.HalfAxisZ, scratch.HalfAxisZ);
            return scratch.Box.Set(scratch.Center, scratch.HalfAxisX, scratch.HalfAxisY, scratch.HalfAxisZ);
        }

        private TransformedCuboidd getLocalQuery(TransformedCuboidd box)
        {
            QueryScratch scratch = queryScratch;
            box.GetCenter(scratch.Center);
            box.GetHalfAxisX(scratch.HalfAxisX);
            box.GetHalfAxisY(scratch.HalfAxisY);
            box.GetHalfAxisZ(scratch.HalfAxisZ);

            Transform.InverseTransformPoint(scratch.Center, scratch.Center);
            Transform.InverseTransformDirection(scratch.HalfAxisX, scratch.HalfAxisX);
            Transform.InverseTransformDirection(scratch.HalfAxisY, scratch.HalfAxisY);
            Transform.InverseTransformDirection(scratch.HalfAxisZ, scratch.HalfAxisZ);
            return scratch.Box.Set(scratch.Center, scratch.HalfAxisX, scratch.HalfAxisY, scratch.HalfAxisZ);
        }

        private WorldGeometry getWorldGeometry()
        {
            if (worldGeometry != null) return worldGeometry;

            WorldGeometry? geometry = Volatile.Read(ref materializedWorldGeometry);
            if (geometry != null) return geometry;

            var created = new WorldGeometry(transformCollisionBoxes(localGeometry!.CollisionBoxes, Transform));
            return Interlocked.CompareExchange(ref materializedWorldGeometry, created, null) ?? created;
        }

        private bool sweepBoundsIntersect(double x1, double y1, double z1, double x2, double y2, double z2, double motionX, double motionY, double motionZ)
        {
            double sweptX1 = Math.Min(x1, x1 + motionX);
            double sweptY1 = Math.Min(y1, y1 + motionY);
            double sweptZ1 = Math.Min(z1, z1 + motionZ);
            double sweptX2 = Math.Max(x2, x2 + motionX);
            double sweptY2 = Math.Max(y2, y2 + motionY);
            double sweptZ2 = Math.Max(z2, z2 + motionZ);
            return sweptX2 >= Bounds.X1 && sweptX1 <= Bounds.X2
                && sweptY2 >= Bounds.Y1 && sweptY1 <= Bounds.Y2
                && sweptZ2 >= Bounds.Z1 && sweptZ1 <= Bounds.Z2;
        }

        private static bool trySweepAabb(TransformedCuboidd[] collisionBoxes, Cuboidd movingBox, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            collisionFraction = 1;
            bool collided = false;
            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                if (!collisionBoxes[i].TrySweepAABB(movingBox, motionX, motionY, motionZ, out double fraction) || fraction >= collisionFraction) continue;

                collisionFraction = fraction;
                collided = true;
                if (fraction == 0) return true;
            }
            return collided;
        }

        private static bool trySweep(TransformedCuboidd[] collisionBoxes, TransformedCuboidd movingBox, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            collisionFraction = 1;
            bool collided = false;
            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                if (!collisionBoxes[i].TrySweep(movingBox, motionX, motionY, motionZ, out double fraction) || fraction >= collisionFraction) continue;

                collisionFraction = fraction;
                collided = true;
                if (fraction == 0) return true;
            }
            return collided;
        }

        private static Cuboidd getTransformedBounds(Cuboidd localBounds, ColliderTransform transform)
        {
            transform.TransformPoint(localBounds.MidX, localBounds.MidY, localBounds.MidZ, out double worldCenterX, out double worldCenterY, out double worldCenterZ);
            transform.TransformDirection(localBounds.HalfExtentX, 0, 0, out double halfAxisXX, out double halfAxisXY, out double halfAxisXZ);
            transform.TransformDirection(0, localBounds.HalfExtentY, 0, out double halfAxisYX, out double halfAxisYY, out double halfAxisYZ);
            transform.TransformDirection(0, 0, localBounds.HalfExtentZ, out double halfAxisZX, out double halfAxisZY, out double halfAxisZZ);
            double halfExtentX = Math.Abs(halfAxisXX) + Math.Abs(halfAxisYX) + Math.Abs(halfAxisZX);
            double halfExtentY = Math.Abs(halfAxisXY) + Math.Abs(halfAxisYY) + Math.Abs(halfAxisZY);
            double halfExtentZ = Math.Abs(halfAxisXZ) + Math.Abs(halfAxisYZ) + Math.Abs(halfAxisZZ);
            return new Cuboidd(worldCenterX - halfExtentX, worldCenterY - halfExtentY, worldCenterZ - halfExtentZ, worldCenterX + halfExtentX, worldCenterY + halfExtentY, worldCenterZ + halfExtentZ);
        }

        private static Cuboidd[] copyCollisionBoxes(IReadOnlyList<Cuboidd> collisionBoxes)
        {
            if (collisionBoxes == null) throw new ArgumentNullException(nameof(collisionBoxes));
            if (collisionBoxes.Count == 0) throw new ArgumentException("A collider requires at least one box.", nameof(collisionBoxes));

            var copiedBoxes = new Cuboidd[collisionBoxes.Count];
            for (int i = 0; i < collisionBoxes.Count; i++)
            {
                Cuboidd sourceBox = collisionBoxes[i] ?? throw new ArgumentException("Collision boxes cannot contain null.", nameof(collisionBoxes));
                if (!hasValidBounds(sourceBox)) throw new ArgumentException("Collision boxes must have finite, ordered bounds.", nameof(collisionBoxes));
                copiedBoxes[i] = sourceBox.Clone();
            }

            return copiedBoxes;
        }

        private static int[]? copySelectionBoxIndices(IReadOnlyList<int>? selectionBoxIndices)
        {
            if (selectionBoxIndices == null) return null;

            var indices = new int[selectionBoxIndices.Count];
            for (int i = 0; i < selectionBoxIndices.Count; i++)
            {
                if (selectionBoxIndices[i] < 0) throw new ArgumentException("Selection box indices cannot be negative.", nameof(selectionBoxIndices));
                indices[i] = selectionBoxIndices[i];
            }
            return indices;
        }

        private static void validateSelection(int dimension, int collisionBoxCount, BlockPos? selectionPosition, IReadOnlyList<int>? selectionBoxIndices)
        {
            if (selectionBoxIndices != null && selectionBoxIndices.Count != collisionBoxCount) throw new ArgumentException("Selection box indices must match the collision box count.", nameof(selectionBoxIndices));
            if (selectionPosition != null && selectionPosition.dimension != dimension) throw new ArgumentException("The selection position must use the collider's dimension.", nameof(selectionPosition));
        }

        private static Cuboidd[] transformCollisionBoxes(Cuboidd[] localCollisionBoxes, ColliderTransform transform)
        {
            var transformedBoxes = new Cuboidd[localCollisionBoxes.Length];
            bool hasRotation = transform.QuaternionX != 0 || transform.QuaternionY != 0 || transform.QuaternionZ != 0 || transform.QuaternionW != 1;
            if (!hasRotation)
            {
                for (int i = 0; i < localCollisionBoxes.Length; i++)
                {
                    Cuboidd box = localCollisionBoxes[i];
                    transformedBoxes[i] = box is RotatedCuboidd rotated
                        ? rotated.OffsetCopy(transform.X, transform.Y, transform.Z)
                        : box.OffsetCopy(transform.X, transform.Y, transform.Z);
                }

                return transformedBoxes;
            }

            var center = new Vec3d();
            var halfAxisX = new Vec3d();
            var halfAxisY = new Vec3d();
            var halfAxisZ = new Vec3d();
            for (int i = 0; i < localCollisionBoxes.Length; i++)
            {
                Cuboidd box = localCollisionBoxes[i];
                TransformedCuboidd localBox = box is RotatedCuboidd rotated && rotated.IsRotated()
                    ? new TransformedCuboidd(box, rotated.GetRotationMatrix())
                    : new TransformedCuboidd(box);
                localBox.GetCenter(center);
                localBox.GetHalfAxisX(halfAxisX);
                localBox.GetHalfAxisY(halfAxisY);
                localBox.GetHalfAxisZ(halfAxisZ);
                transform.TransformPoint(center, center);
                transform.TransformDirection(halfAxisX, halfAxisX);
                transform.TransformDirection(halfAxisY, halfAxisY);
                transform.TransformDirection(halfAxisZ, halfAxisZ);
                transformedBoxes[i] = new TransformedCuboidd().Set(center, halfAxisX, halfAxisY, halfAxisZ).ToRotatedCuboidd();
            }

            return transformedBoxes;
        }

        private static bool hasValidBounds(Cuboidd box)
        {
            return double.IsFinite(box.X1)
                && double.IsFinite(box.Y1)
                && double.IsFinite(box.Z1)
                && double.IsFinite(box.X2)
                && double.IsFinite(box.Y2)
                && double.IsFinite(box.Z2)
                && box.X1 <= box.X2
                && box.Y1 <= box.Y2
                && box.Z1 <= box.Z2;
        }

        private sealed class LocalGeometry
        {
            public Cuboidd[] CollisionBoxes { get; }
            public TransformedCuboidd[] TransformedBoxes { get; }
            public Cuboidd Bounds { get; }

            public LocalGeometry(Cuboidd[] collisionBoxes)
            {
                CollisionBoxes = collisionBoxes;
                TransformedBoxes = new TransformedCuboidd[collisionBoxes.Length];
                Cuboidd[]? calculatedBounds = null;
                for (int i = 0; i < collisionBoxes.Length; i++)
                {
                    Cuboidd collisionBox = collisionBoxes[i];
                    TransformedCuboidd transformedBox = collisionBox is RotatedCuboidd rotated && rotated.IsRotated()
                        ? new TransformedCuboidd(collisionBox, rotated.GetRotationMatrix())
                        : new TransformedCuboidd(collisionBox);
                    TransformedBoxes[i] = transformedBox;

                    if (!collisionBox.IsRotated())
                    {
                        if (calculatedBounds != null) calculatedBounds[i] = collisionBox;
                        continue;
                    }

                    calculatedBounds ??= copyBounds(collisionBoxes, i);
                    calculatedBounds[i] = transformedBox.GetAABB(new Cuboidd());
                }
                Bounds = getEncompassingBounds(calculatedBounds ?? collisionBoxes);
            }

            private static Cuboidd[] copyBounds(Cuboidd[] collisionBoxes, int count)
            {
                var bounds = new Cuboidd[collisionBoxes.Length];
                Array.Copy(collisionBoxes, bounds, count);
                return bounds;
            }
        }

        private sealed class WorldGeometry
        {
            public Cuboidd[] CollisionBoxes { get; }
            public Cuboidd[] CollisionBoxBounds { get; }
            public TransformedCuboidd[] TransformedBoxes { get; }
            public Cuboidd Bounds { get; }

            public WorldGeometry(Cuboidd[] collisionBoxes)
            {
                CollisionBoxes = collisionBoxes;
                TransformedBoxes = new TransformedCuboidd[collisionBoxes.Length];
                Cuboidd[]? calculatedBounds = null;
                for (int i = 0; i < collisionBoxes.Length; i++)
                {
                    Cuboidd collisionBox = collisionBoxes[i];
                    TransformedCuboidd transformedBox = collisionBox is RotatedCuboidd rotated && rotated.IsRotated()
                        ? new TransformedCuboidd(collisionBox, rotated.GetRotationMatrix())
                        : new TransformedCuboidd(collisionBox);
                    TransformedBoxes[i] = transformedBox;

                    Cuboidd bounds = collisionBox;
                    if (transformedBox.BoundsX1 != collisionBox.X1 || transformedBox.BoundsY1 != collisionBox.Y1 || transformedBox.BoundsZ1 != collisionBox.Z1
                        || transformedBox.BoundsX2 != collisionBox.X2 || transformedBox.BoundsY2 != collisionBox.Y2 || transformedBox.BoundsZ2 != collisionBox.Z2)
                    {
                        bounds = transformedBox.GetAABB(new Cuboidd());
                    }
                    if (!hasValidBounds(bounds)) throw new ArgumentException("Rotated collision boxes must produce finite bounds.", nameof(collisionBoxes));

                    if (ReferenceEquals(bounds, collisionBox))
                    {
                        if (calculatedBounds != null) calculatedBounds[i] = bounds;
                        continue;
                    }

                    if (calculatedBounds == null)
                    {
                        calculatedBounds = new Cuboidd[collisionBoxes.Length];
                        Array.Copy(collisionBoxes, calculatedBounds, i);
                    }
                    calculatedBounds[i] = bounds;
                }

                CollisionBoxBounds = calculatedBounds ?? collisionBoxes;
                Bounds = getEncompassingBounds(CollisionBoxBounds);
            }
        }

        private sealed class QueryScratch
        {
            public readonly TransformedCuboidd Box = new();
            public readonly Vec3d Center = new();
            public readonly Vec3d HalfAxisX = new();
            public readonly Vec3d HalfAxisY = new();
            public readonly Vec3d HalfAxisZ = new();
        }

        private static Cuboidd getEncompassingBounds(Cuboidd[] collisionBoxBounds)
        {
            Cuboidd bounds = collisionBoxBounds[0].Clone();
            for (int i = 1; i < collisionBoxBounds.Length; i++)
            {
                Cuboidd boxBounds = collisionBoxBounds[i];
                bounds.X1 = Math.Min(bounds.X1, boxBounds.X1);
                bounds.Y1 = Math.Min(bounds.Y1, boxBounds.Y1);
                bounds.Z1 = Math.Min(bounds.Z1, boxBounds.Z1);
                bounds.X2 = Math.Max(bounds.X2, boxBounds.X2);
                bounds.Y2 = Math.Max(bounds.Y2, boxBounds.Y2);
                bounds.Z2 = Math.Max(bounds.Z2, boxBounds.Z2);
            }
            return bounds;
        }
    }
}
