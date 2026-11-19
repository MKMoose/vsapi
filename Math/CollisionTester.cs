using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.API.MathTools
{
    public enum EnumIntersect
    {
        NoIntersect,
        IntersectX,
        IntersectY,
        IntersectZ,
        Stuck
    }

    public class CollisionTester
    {
        private const double collisionEpsilon = 0.0001;
        private const double colliderContactTolerance = collisionEpsilon * 4;
        private const double maxColliderRecovery = 0.05;
        private const int colliderRecoveryIterations = 12;
        private const double maxColliderDepenetrationSpeed = 3;
        private const double maxColliderDepenetrationPerStep = 0.05;
        private const double colliderPenetrationToleranceSq = 1 / ((16d * 8) * (16d * 8));
        private const int maxHorizontalColliderPlanes = 4;
        private const double horizontalColliderNormalSimilarity = 0.999999;
        private const double colliderSlopeTolerance = 1e-10;

        public CachedCuboidListFaster CollisionBoxList = new();

        public Cuboidd entityBox = new();

        // Use class level fields to reduce garbage collection
        public BlockPos tmpPos = new(Dimensions.WillSetLater);
        public Vec3d tmpPosDelta = new();

        public BlockPos minPos = new(Dimensions.WillSetLater);
        public BlockPos maxPos = new(Dimensions.WillSetLater);

        public Vec3d pos = new();

        readonly Cuboidd tmpBox = new();
        readonly BlockPos blockPos = new(Dimensions.WillSetLater);
        readonly Vec3d blockPosVec = new();
        readonly BlockPos collBlockPos = new(Dimensions.WillSetLater);
        readonly Cuboidd colliderQueryBounds = new();
        readonly Cuboidd colliderRecoveryBox = new();
        readonly Cuboidd colliderSweepBox = new();
        readonly Cuboidd horizontalColliderTestBox = new();
        readonly List<Collider> colliders = new();
        readonly TransformedCuboidd colliderVerticalBox = new();
        readonly List<CollisionHit> colliderVerticalHits = new();
        readonly CollisionPlane[] horizontalColliderPlanes = new CollisionPlane[maxHorizontalColliderPlanes];
        int horizontalColliderPlaneCount;
        readonly Cuboidd colliderDepenetrationStartBox = new();
        readonly Cuboidd colliderDepenetrationBox = new();
        readonly Vec3d colliderDepenetrationPos = new();
        readonly TransformedCuboidd colliderDepenetrationTerrainShape = new();
        CollisionPlane[] colliderDepenetrationPlanes = new CollisionPlane[4];
        Collider[] depenetrationColliders = new Collider[4];
        CollisionPlane[] colliderSlidePlanes = new CollisionPlane[4];
        double maxWalkableSlopeGrade = 1;

        /// <summary>
        /// The registered collider which supported the entity during the most recent terrain collision, or null for terrain and non-floor collisions.
        /// </summary>
        public Collider SupportingCollider { get; private set; }

        /// <summary>
        /// Whether the latest collision contacted a steep collider without being walkable
        /// </summary>
        public bool SlidingOnSteepCollider { get; private set; }

        public static bool IsWalkableColliderSurface(FastVec3d normal, double maxSlopeGrade)
        {
            return normal.Y > 0 && normal.X * normal.X + normal.Z * normal.Z <= normal.Y * normal.Y * maxSlopeGrade * maxSlopeGrade + colliderSlopeTolerance;
        }

        /// <summary>
        /// Clips horizontal velocity against registered collider walls which constrained the latest movement solve.
        /// </summary>
        /// <returns>True when the latest solve applied at least one wall constraint.</returns>
        public bool ClipColliderVelocity(Vec3d velocity)
        {
            if (velocity == null) throw new ArgumentNullException(nameof(velocity));
            if (horizontalColliderPlaneCount == 0) return false;

            FastVec3d clipped = CollisionPlaneSolver.ClipVector(new FastVec3d(velocity.X, 0, velocity.Z), horizontalColliderPlanes.AsSpan(0, horizontalColliderPlaneCount));
            velocity.X = clipped.X;
            velocity.Z = clipped.Z;
            return true;
        }

        /// <summary>
        /// Takes the entity positiona and motion and adds them, respecting any colliding blocks. The resulting new position is put into outposition
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="entityPos"></param>
        /// <param name="dtFactor"></param>
        /// <param name="newPosition"></param>
        /// <param name="stepHeight"></param>
        /// <param name="yExtra">Default 1 for the extra high collision boxes of fences</param>
        public void ApplyTerrainCollision(Entity entity, EntityPos entityPos, float dtFactor, ref Vec3d newPosition, float stepHeight = 1, float yExtra = 1)
        {
            applyTerrainCollision(
                entity,
                entityPos,
                entityPos.Motion.X * dtFactor,
                entityPos.Motion.Y * dtFactor,
                entityPos.Motion.Z * dtFactor,
                dtFactor,
                ref newPosition,
                stepHeight,
                yExtra,
                0
            );
        }

        /// <summary>
        /// Applies an explicit displacement while respecting terrain and registered colliders.
        /// </summary>
        /// <param name="excludedColliderId">A collider which supplies the displacement and must not block it.</param>
        public void ApplyTerrainCollision(Entity entity, EntityPos entityPos, Vec3d displacement, float dtFactor, ref Vec3d newPosition, long excludedColliderId = 0, float stepHeight = 1, float yExtra = 1)
        {
            applyTerrainCollision(entity, entityPos, displacement.X, displacement.Y, displacement.Z, dtFactor, ref newPosition, stepHeight, yExtra, excludedColliderId);
        }

        private void applyTerrainCollision(Entity entity, EntityPos entityPos, double motionX, double motionY, double motionZ, float dtFactor, ref Vec3d newPosition, float stepHeight, float yExtra, long excludedColliderId)
        {
            SupportingCollider = null;
            SlidingOnSteepCollider = false;
            maxWalkableSlopeGrade = entity.GetBehavior<PhysicsBehaviorBase>()?.MaxWalkableSlopeGrade ?? 1;
            double requestedMotionY = motionY;
            horizontalColliderPlaneCount = 0;
            minPos.SetDimension(entityPos.Dimension);

            var worldAccessor = entity.World;
            Vec3d pos = this.pos;           // Local copy for efficiency
            Cuboidd entityBox = this.entityBox; // Local copy for efficiency

            pos.X = entityPos.X;
            pos.Y = entityPos.Y;
            pos.Z = entityPos.Z;

            EnumPushDirection pushDirection = EnumPushDirection.None;

            entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);

            // We need to make sure that rounding errors do not place us inside a block, because once inside a block, this algorithm no longer pushes the entity out of it
            // So lets collide with blocks a tiny bit earlier - i.e. by the amount of rounding error. In other words, lets push out the entity out of collision boxes once he gets within epsilon meters instead of 0 meters,
            // so that the position+motion addition at the end of the method never ends up being inside a block

            // A double value has ~15 digits. Our max map size of 64mil means we need 8 digits for the non-fractional part, leaving us with 7 digits for the fraction - so the rounding error is on the 8th digit
            // But for some reason we still clip through blocks if we use an epsilon that is less than 0.0001. Not sure why.
            double motEpsX = 0, motEpsY = 0, motEpsZ = 0;
            if (motionX > collisionEpsilon) motEpsX = collisionEpsilon;
            if (motionX < -collisionEpsilon) motEpsX = -collisionEpsilon;

            if (motionY > collisionEpsilon) motEpsY = collisionEpsilon;
            if (motionY < -collisionEpsilon) motEpsY = -collisionEpsilon;

            if (motionZ > collisionEpsilon) motEpsZ = collisionEpsilon;
            if (motionZ < -collisionEpsilon) motEpsZ = -collisionEpsilon;

            // We pretend we are by epsilon meters further and push the entity out of it
            // but at the end of the method we do not add this epsilon to the final position
            motionX += motEpsX;
            motionY += motEpsY;
            motionZ += motEpsZ;


            // Generate a cube that encompasses every block between the old and new position.
            // This could also just take the new position and old position without using motion.
            GenerateCollisionBoxList(worldAccessor.BlockAccessor, motionX, motionY, motionZ, stepHeight, yExtra, entityPos.Dimension);
            loadColliders(worldAccessor.CollisionScene, entity, entityPos.Dimension, excludedColliderId);

            double preCollisionMotionY = motionY;
            double colliderRecovery = getColliderRecovery(entityBox, motionY);
            bool collided = colliderRecovery < 0;
            if (colliderRecovery != 0)
            {
                motionY = colliderRecovery;
                if (colliderRecovery > 0)
                {
                    colliderRecoveryBox.Set(entityBox);
                    colliderRecoveryBox.Translate(0, colliderRecovery, 0);
                    SupportingCollider = getSupportingCollider(colliderRecoveryBox);
                    collided = SupportingCollider != null;
                    SlidingOnSteepCollider = !collided;
                }
            }

            int collisionBoxListCount = CollisionBoxList.Count;
            Cuboidd[] CollisionBoxListCuboids = CollisionBoxList.cuboids;   // Local reference for efficiency

            collBlockPos.SetDimension(entityPos.Dimension);
            // ---------- Y COLLISION. Call events and set collided vertically.
            for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
            {
                if (i >= collisionBoxListCount) break;
                motionY = CollisionBoxListCuboids[i].pushOutY(entityBox, motionY, ref pushDirection);
                if (pushDirection == EnumPushDirection.None) continue;

                collided = true;

                collBlockPos.Set(CollisionBoxList.positions[i]);
                CollisionBoxList.blocks[i].OnEntityCollide(
                    worldAccessor,
                    entity,
                    collBlockPos,
                    pushDirection == EnumPushDirection.Negative ? BlockFacing.UP : BlockFacing.DOWN,
                    tmpPosDelta.Set(motionX, motionY, motionZ),
                    !entity.CollidedVertically
                );
            }
            // This should classify support at the impact before horizontal movement can leave the surface.
            colliderVerticalBox.Set(entityBox);
            colliderVerticalHits.Clear();
            for (int i = 0; i < colliders.Count; i++) colliders[i].CollectSweepHits(colliderVerticalBox, 0, motionY, 0, colliderVerticalHits, includeInitialOverlap: false);

            double verticalFraction = 1;
            for (int i = 0; i < colliderVerticalHits.Count; i++) verticalFraction = Math.Min(verticalFraction, colliderVerticalHits[i].Fraction);
            if (verticalFraction < 1)
            {
                collided = false;
                SupportingCollider = null;
            }
            for (int i = 0; i < colliderVerticalHits.Count; i++)
            {
                CollisionHit hit = colliderVerticalHits[i];
                if (hit.Fraction > verticalFraction) continue;
                if (motionY > 0 || IsWalkableColliderSurface(hit.Normal, maxWalkableSlopeGrade))
                {
                    collided = true;
                    if (motionY < 0) SupportingCollider = hit.Collider;
                }
                else SlidingOnSteepCollider = true;
            }
            SlidingOnSteepCollider &= !collided;
            motionY *= verticalFraction;
            motionY -= motEpsY;
            entityBox.Translate(0, motionY, 0);

            entity.CollidedVertically = collided;

            // Registered walls need a surface-normal solve. Follow a supporting slope while resolving horizontal movement against those walls.
            bool resolvedByHorizontalPlanes = false;
            double supportMotionY = 0;
            if (!intersectsTerrainAtOffset(entityBox, motionX, 0, motionZ))
            {
                double unresolvedMotionX = motionX;
                double unresolvedMotionZ = motionZ;
                resolvedByHorizontalPlanes = TryResolveHorizontalColliderMotion(entityBox, colliders, SupportingCollider, ref motionX, ref motionZ, out supportMotionY);
                if ((resolvedByHorizontalPlanes || supportMotionY > 0)
                    && (intersectsTerrainAtOffset(entityBox, motionX, supportMotionY, motionZ) || colliderPathIntersects(entityBox, motionX, supportMotionY, motionZ)))
                {
                    motionX = unresolvedMotionX;
                    motionZ = unresolvedMotionZ;
                    supportMotionY = 0;
                    resolvedByHorizontalPlanes = false;
                    horizontalColliderPlaneCount = 0;
                }
            }

            // A swept hit enters the axis solver even when fast motion ends beyond a thin collider. Resolving each axis retains the usual sliding movement.
            bool colliderSwept = trySweepColliders(entityBox, motionX, supportMotionY, motionZ, out _);
            if (colliderSwept) colliderSweepBox.Set(entityBox);

            bool horizontallyBlocked = colliderSwept;
            entityBox.Translate(motionX, supportMotionY, motionZ);
            if (!horizontallyBlocked)
            {
                foreach (var cuboid in CollisionBoxList)
                {
                    if (cuboid.Intersects(entityBox))
                    {
                        horizontallyBlocked = true;
                        break;
                    }
                }
            }
            if (!horizontallyBlocked)
            {
                horizontallyBlocked = intersectsLoadedColliders(entityBox);
            }
            entityBox.Translate(-motionX, -supportMotionY, -motionZ);  // cheaper than creating a new Cuboidd

            // No collisions for the entity found when testing horizontally, so skip this.
            // This allows entities to move around corners without falling down on a certain axis.
            collided = resolvedByHorizontalPlanes;
            bool resolvedByCollider = false;
            if (horizontallyBlocked)
            {
                supportMotionY = 0;

                // X - Collision (Horizontal)
                for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                {
                    if (i >= collisionBoxListCount) break;
                    motionX = CollisionBoxListCuboids[i].pushOutX(entityBox, motionX, ref pushDirection);
                    if (pushDirection == EnumPushDirection.None) continue;

                    collided = true;
                    horizontalColliderPlaneCount = 0;

                    collBlockPos.Set(CollisionBoxList.positions[i]);
                    CollisionBoxList.blocks[i].OnEntityCollide(
                        worldAccessor,
                        entity,
                        collBlockPos,
                        pushDirection == EnumPushDirection.Negative ? BlockFacing.EAST : BlockFacing.WEST,
                        tmpPosDelta.Set(motionX, motionY, motionZ),
                        !entity.CollidedHorizontally
                    );
                }
                for (int i = 0; i < colliders.Count; i++)
                {
                    motionX = colliders[i].PushOutX(entityBox, motionX, ref pushDirection);
                    if (pushDirection == EnumPushDirection.None) continue;

                    collided = true;
                    resolvedByCollider = true;
                    horizontalColliderPlaneCount = 0;
                }
                entityBox.Translate(motionX, 0, 0);

                // Z - Collision (Horizontal)

                for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                {
                    if (i >= collisionBoxListCount) break;
                    motionZ = CollisionBoxListCuboids[i].pushOutZ(entityBox, motionZ, ref pushDirection);
                    if (pushDirection == EnumPushDirection.None) continue;

                    collided = true;
                    horizontalColliderPlaneCount = 0;

                    collBlockPos.Set(CollisionBoxList.positions[i]);
                    CollisionBoxList.blocks[i].OnEntityCollide(
                        worldAccessor,
                        entity,
                        collBlockPos,
                        pushDirection == EnumPushDirection.Negative ? BlockFacing.SOUTH : BlockFacing.NORTH,
                        tmpPosDelta.Set(motionX, motionY, motionZ),
                        !entity.CollidedHorizontally
                    );
                }
                for (int i = 0; i < colliders.Count; i++)
                {
                    motionZ = colliders[i].PushOutZ(entityBox, motionZ, ref pushDirection);
                    if (pushDirection == EnumPushDirection.None) continue;

                    collided = true;
                    resolvedByCollider = true;
                    horizontalColliderPlaneCount = 0;
                }
            }

            // A direct sweep can hit a thin diagonal collider while the axis path misses it. Keep the continuous hit in that case instead of tunnelling through.
            if (colliderSwept && !resolvedByCollider && trySweepColliders(colliderSweepBox, motionX, 0, motionZ, out double colliderFraction))
            {
                for (int i = 0; i < collisionBoxListCount; i++)
                {
                    if (colliderDepenetrationTerrainShape.Set(CollisionBoxListCuboids[i]).TrySweepAABB(colliderSweepBox, motionX, 0, motionZ, out double terrainFraction)) colliderFraction = Math.Min(colliderFraction, terrainFraction);
                }
                motionX *= colliderFraction;
                motionZ *= colliderFraction;
                collided = true;
                horizontalColliderPlaneCount = 0;
            }

            entity.CollidedHorizontally = collided;
            motionY += supportMotionY;

            // fix for player on ladder clipping into block above issue  (caused by the .CollisionBox not always having height precisely 1.85)
            // Apply ceiling clearance only to upward motion, since floor recovery can also move the body up.
            if (preCollisionMotionY > 0 && motionY > 0 && entity.CollidedVertically)
            {
                motionY -= entity.LadderFixDelta;
            }

            motionX -= motEpsX;
            motionZ -= motEpsZ;

            // A moving collider can enter an entity during its own update, which the entity-motion solver above cannot detect. Nudge the entity clear when that is safe.
            depenetrateFromColliders(entity, worldAccessor, entityPos.Dimension, dtFactor, ref motionX, ref motionY, ref motionZ);

            if (requestedMotionY < 0) slideDownSteepColliders(entity, entityPos.Dimension, excludedColliderId, requestedMotionY, ref motionX, ref motionY, ref motionZ);

            newPosition.Set(pos.X + motionX, pos.Y + motionY, pos.Z + motionZ);
        }

        private void slideDownSteepColliders(Entity entity, int dimension, long excludedColliderId, double requestedMotionY, ref double motionX, ref double motionY, ref double motionZ)
        {
            if (colliders.Count == 0) return;
            entityBox.SetAndTranslate(entity.CollisionBox, pos.X + motionX, pos.Y + motionY, pos.Z + motionZ);
            int planeCount = 0;
            for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++)
            {
                Collider collider = colliders[colliderIndex];
                for (int boxIndex = 0; boxIndex < collider.CollisionBoxCount; boxIndex++)
                {
                    if (!collider.TryGetAABBContact(boxIndex, entityBox, colliderContactTolerance, out CuboidContact contact)) continue;
                    if (contact.Separation < -colliderContactTolerance || contact.Normal.Y <= 0) continue;
                    if (IsWalkableColliderSurface(contact.Normal, maxWalkableSlopeGrade)) return;

                    if (planeCount == colliderSlidePlanes.Length) Array.Resize(ref colliderSlidePlanes, planeCount * 2);
                    colliderSlidePlanes[planeCount++] = new CollisionPlane(contact.Normal, 2 * collisionEpsilon - contact.Separation);
                }
            }
            if (planeCount == 0) return;

            foreach (Cuboidd terrainBox in CollisionBoxList)
            {
                EnumPushDirection direction = EnumPushDirection.None;
                terrainBox.pushOutY(entityBox, -colliderContactTolerance, ref direction);
                if (direction == EnumPushDirection.Negative) return;
            }

            SupportingCollider = null;
            entity.CollidedVertically = false;
            SlidingOnSteepCollider = true;
            double remainingFall = requestedMotionY - motionY;
            if (remainingFall >= 0) return;

            FastVec3d slide = CollisionPlaneSolver.Solve(new FastVec3d(0, remainingFall, 0), colliderSlidePlanes.AsSpan(0, planeCount)).Delta;
            if (slide.Y >= 0) return;

            GenerateCollisionBoxList(entity.World.BlockAccessor, slide.X, slide.Y, slide.Z, 0, 1, dimension);
            loadColliders(entity.World.CollisionScene, entity, dimension, excludedColliderId);
            trySweepColliders(entityBox, slide.X, slide.Y, slide.Z, out double fraction);
            foreach (Cuboidd terrainBox in CollisionBoxList)
            {
                if (terrainBox is RotatedCuboidd rotated) colliderDepenetrationTerrainShape.Set(rotated, rotated.GetRotationMatrix());
                else colliderDepenetrationTerrainShape.Set(terrainBox);
                if (colliderDepenetrationTerrainShape.TrySweepAABB(entityBox, slide.X, slide.Y, slide.Z, out double terrainFraction)) fraction = Math.Min(fraction, terrainFraction);
            }
            // Leave clearance at the hit so rounding cannot put the next tick inside the floor or wall.
            if (fraction < 1) fraction = Math.Max(0, fraction - collisionEpsilon / slide.Length());
            motionX += slide.X * fraction;
            motionY += slide.Y * fraction;
            motionZ += slide.Z * fraction;
            entityBox.Translate(slide.X * fraction, slide.Y * fraction, slide.Z * fraction);
        }

        private void depenetrateFromColliders(Entity entity, IWorldAccessor worldAccessor, int dimension, float dtFactor, ref double motionX, ref double motionY, ref double motionZ)
        {
            double remainingCorrection = Math.Min(maxColliderDepenetrationPerStep, maxColliderDepenetrationSpeed / 60 * dtFactor);
            if (remainingCorrection <= 0) return;

            if (getSafeRollbackCollider(entity, worldAccessor, dimension, remainingCorrection, motionX, motionY, motionZ) != null)
            {
                bool rollbackIsVertical = Math.Abs(motionY) >= Math.Sqrt(motionX * motionX + motionZ * motionZ);
                if (rollbackIsVertical)
                {
                    if (motionY > 0) entity.CollidedVertically = true;
                    else
                    {
                        colliderRecoveryBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
                        SupportingCollider = getSupportingCollider(colliderRecoveryBox);
                        entity.CollidedVertically |= SupportingCollider != null;
                        SlidingOnSteepCollider = !entity.CollidedVertically;
                    }
                }
                else
                {
                    entity.CollidedHorizontally = true;
                }

                motionX = 0;
                motionY = 0;
                motionZ = 0;
                return;
            }

            colliderDepenetrationStartBox.SetAndTranslate(entity.CollisionBox, pos.X + motionX, pos.Y + motionY, pos.Z + motionZ);
            int planeCount = loadColliderDepenetrationPlanes(colliderDepenetrationStartBox, remainingCorrection);
            if (planeCount == 0) return;

            CollisionPlaneSolverResult result = CollisionPlaneSolver.Solve(new FastVec3d(), colliderDepenetrationPlanes.AsSpan(0, planeCount));
            double correctionX = result.Delta.X;
            double correctionY = result.Delta.Y;
            double correctionZ = result.Delta.Z;
            double correctionSq = correctionX * correctionX + correctionY * correctionY + correctionZ * correctionZ;
            if (correctionSq <= collisionEpsilon * collisionEpsilon || !makesDepenetrationProgress(correctionX, correctionY, correctionZ, planeCount)) return;

            double correction = Math.Sqrt(correctionSq);
            if (correction > remainingCorrection)
            {
                double correctionScale = remainingCorrection / correction;
                correctionX *= correctionScale;
                correctionY *= correctionScale;
                correctionZ *= correctionScale;
            }

            if (!isDepenetrationCandidateClear(entity, worldAccessor, dimension, planeCount, motionX, motionY, motionZ, correctionX, correctionY, correctionZ)
                || !isDepenetrationPathClear(colliderDepenetrationStartBox, planeCount, correctionX, correctionY, correctionZ))
            {
                return;
            }

            motionX += correctionX;
            motionY += correctionY;
            motionZ += correctionZ;

            bool verticalCollision = Math.Abs(correctionY) >= Math.Sqrt(correctionX * correctionX + correctionZ * correctionZ);
            if (verticalCollision)
            {
                if (correctionY < 0) entity.CollidedVertically = true;
                else
                {
                    SupportingCollider = getSupportingDepenetrationCollider(planeCount);
                    entity.CollidedVertically |= SupportingCollider != null;
                    SlidingOnSteepCollider = !entity.CollidedVertically;
                }
            }
            else
            {
                entity.CollidedHorizontally = true;
            }
        }

        private Collider getSafeRollbackCollider(Entity entity, IWorldAccessor worldAccessor, int dimension, double maxCorrection, double motionX, double motionY, double motionZ)
        {
            double correctionSq = motionX * motionX + motionY * motionY + motionZ * motionZ;
            if (correctionSq <= colliderPenetrationToleranceSq || correctionSq > maxCorrection * maxCorrection) return null;

            colliderDepenetrationStartBox.SetAndTranslate(entity.CollisionBox, pos.X + motionX, pos.Y + motionY, pos.Z + motionZ);
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (!isMovingCollider(collider)) continue;
                if (!collider.Intersects(colliderDepenetrationStartBox)) continue;
                depenetrationColliders[0] = collider;
                if (!isDepenetrationCandidateClear(entity, worldAccessor, dimension, 1, motionX, motionY, motionZ, -motionX, -motionY, -motionZ)) continue;
                if (isDepenetrationPathClear(colliderDepenetrationStartBox, 1, -motionX, -motionY, -motionZ)) return collider;
            }

            return null;
        }

        private int loadColliderDepenetrationPlanes(Cuboidd box, double pushLimit)
        {
            int planeCount = 0;
            for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++)
            {
                Collider collider = colliders[colliderIndex];
                if (!isMovingCollider(collider)) continue;

                for (int boxIndex = 0; boxIndex < collider.CollisionBoxCount; boxIndex++)
                {
                    if (!collider.TryGetAABBPenetration(boxIndex, box, out double penetrationX, out double penetrationY, out double penetrationZ)) continue;

                    double penetrationSq = penetrationX * penetrationX + penetrationY * penetrationY + penetrationZ * penetrationZ;
                    if (penetrationSq <= colliderPenetrationToleranceSq) continue;

                    if (planeCount == colliderDepenetrationPlanes.Length)
                    {
                        int newLength = planeCount * 2;
                        Array.Resize(ref colliderDepenetrationPlanes, newLength);
                        Array.Resize(ref depenetrationColliders, newLength);
                    }

                    double penetration = Math.Sqrt(penetrationSq);
                    var normal = new FastVec3d(penetrationX / penetration, penetrationY / penetration, penetrationZ / penetration);
                    colliderDepenetrationPlanes[planeCount] = new CollisionPlane(normal, penetration + 2 * collisionEpsilon, pushLimit);
                    depenetrationColliders[planeCount++] = collider;
                }
            }

            return planeCount;
        }

        private bool makesDepenetrationProgress(double correctionX, double correctionY, double correctionZ, int planeCount)
        {
            for (int i = 0; i < planeCount; i++)
            {
                FastVec3d normal = colliderDepenetrationPlanes[i].Normal;
                if (correctionX * normal.X + correctionY * normal.Y + correctionZ * normal.Z <= 0) return false;
            }

            return true;
        }

        private Collider getSupportingDepenetrationCollider(int planeCount)
        {
            Collider supportingCollider = null;
            double bestVerticalPush = 0;
            for (int i = 0; i < planeCount; i++)
            {
                CollisionPlane plane = colliderDepenetrationPlanes[i];
                if (!IsWalkableColliderSurface(plane.Normal, maxWalkableSlopeGrade)) continue;
                double verticalPush = plane.Normal.Y * plane.Push;
                if (verticalPush <= bestVerticalPush) continue;

                bestVerticalPush = verticalPush;
                supportingCollider = depenetrationColliders[i];
            }

            return supportingCollider;
        }

        // Only a collider which actually moved this step can have entered an entity that the axis solve already resolved.
        private static bool isMovingCollider(Collider collider)
        {
            return collider.IsTransformed && collider.TransformElapsedSeconds > 0 && !collider.PreviousTransform.Equals(collider.Transform);
        }

        private bool isDepenetrationPathClear(Cuboidd startBox, int planeCount, double motionX, double motionY, double motionZ)
        {
            for (int i = 0; i < CollisionBoxList.Count; i++)
            {
                Cuboidd terrainBox = CollisionBoxList.cuboids[i];
                if (terrainBox.Intersects(startBox)) return false;

                colliderDepenetrationTerrainShape.Set(terrainBox);
                if (colliderDepenetrationTerrainShape.TrySweepAABB(startBox, motionX, motionY, motionZ, out _)) return false;
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (isDepenetrationCollider(collider.Id, planeCount)) continue;

                if (collider.Intersects(startBox) || collider.TrySweepAABB(startBox, motionX, motionY, motionZ, out _)) return false;
            }

            return true;
        }

        // A clamped correction can stop inside the colliders it escapes, so only other obstructions are checked here.
        private bool isDepenetrationCandidateClear(Entity entity, IWorldAccessor worldAccessor, int dimension, int planeCount, double motionX, double motionY, double motionZ, double translationX, double translationY, double translationZ)
        {
            colliderDepenetrationBox.SetAndTranslate(entity.CollisionBox, pos.X + motionX + translationX, pos.Y + motionY + translationY, pos.Z + motionZ + translationZ);
            for (int i = 0; i < colliders.Count; i++)
            {
                if (isDepenetrationCollider(colliders[i].Id, planeCount)) continue;
                if (colliders[i].Intersects(colliderDepenetrationBox)) return false;
            }

            colliderDepenetrationPos.Set(pos.X + motionX + translationX, pos.Y + motionY + translationY, pos.Z + motionZ + translationZ);
            return !IsColliding(worldAccessor.BlockAccessor, entity.CollisionBox, colliderDepenetrationPos, dimension, false);
        }

        private bool isDepenetrationCollider(long colliderId, int planeCount)
        {
            for (int i = 0; i < planeCount; i++)
            {
                if (depenetrationColliders[i].Id == colliderId) return true;
            }

            return false;
        }

        private void loadColliders(ICollisionScene collisionScene, Entity entity, int dimension, long excludedColliderId)
        {
            colliderQueryBounds.Set(minPos.X, minPos.Y, minPos.Z, maxPos.X + 1, maxPos.Y + 1, maxPos.Z + 1);
            collisionScene.GetColliders(colliderQueryBounds, dimension, colliders, new CollisionQueryFilter(CollisionLayer.Entity, excludedColliderId: excludedColliderId, excludedOwnerEntityId: entity.EntityId));

            long mountEntityId = (entity as EntityAgent)?.MountedOn?.Entity?.EntityId ?? 0;

            int includedColliderCount = 0;
            for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++)
            {
                Collider collider = colliders[colliderIndex];
                if (collider.OwnerEntityId != 0 && collider.OwnerEntityId == mountEntityId) continue;

                colliders[includedColliderCount++] = collider;
            }

            if (includedColliderCount < colliders.Count)
            {
                colliders.RemoveRange(includedColliderCount, colliders.Count - includedColliderCount);
            }
        }

        // This only corrects the vertical axis
        private double getColliderRecovery(Cuboidd entityBox, double motionY)
        {
            // Rotated sweeps do not depenetrate, so this will recover shallow overlap with a registered collider before applying the motion.
            if (!intersectsLoadedColliders(entityBox)) return 0;

            if (motionY < 0)
            {
                return findColliderRecoveryOffset(entityBox, 1);
            }

            // Not falling so any overlap is from above, e.g. jumping into a beam's underside, push down clear of it instead of up.
            return findColliderRecoveryOffset(entityBox, -1);
        }

        private double findColliderRecoveryOffset(Cuboidd entityBox, double direction)
        {
            colliderRecoveryBox.Set(entityBox);
            colliderRecoveryBox.Translate(0, direction * maxColliderRecovery, 0);
            if (intersectsLoadedColliders(colliderRecoveryBox)) return 0;

            double lower = 0;
            double upper = maxColliderRecovery;
            for (int i = 0; i < colliderRecoveryIterations; i++)
            {
                double recovery = (lower + upper) * 0.5;
                colliderRecoveryBox.Set(entityBox);
                colliderRecoveryBox.Translate(0, direction * recovery, 0);
                if (intersectsLoadedColliders(colliderRecoveryBox)) lower = recovery;
                else upper = recovery;
            }

            return direction * upper;
        }

        private bool intersectsLoadedColliders(Cuboidd box)
        {
            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i].Intersects(box)) return true;
            }

            return false;
        }

        private bool intersectsTerrainAtOffset(Cuboidd box, double motionX, double motionY, double motionZ)
        {
            horizontalColliderTestBox.Set(box);
            horizontalColliderTestBox.Translate(motionX, motionY, motionZ);
            foreach (Cuboidd collisionBox in CollisionBoxList)
            {
                if (collisionBox.Intersects(horizontalColliderTestBox)) return true;
            }

            return false;
        }

        private bool colliderPathIntersects(Cuboidd box, double motionX, double motionY, double motionZ)
        {
            if (trySweepColliders(box, motionX, motionY, motionZ, out _)) return true;

            horizontalColliderTestBox.Set(box);
            horizontalColliderTestBox.Translate(motionX, motionY, motionZ);
            return intersectsLoadedColliders(horizontalColliderTestBox);
        }

        internal bool TryResolveHorizontalColliderMotion(Cuboidd box, List<Collider> candidateColliders, ref double motionX, ref double motionZ)
        {
            return TryResolveHorizontalColliderMotion(box, candidateColliders, null, ref motionX, ref motionZ, out _);
        }

        internal bool TryResolveHorizontalColliderMotion(Cuboidd box, List<Collider> candidateColliders, Collider supportingCollider, ref double motionX, ref double motionZ, out double supportMotionY)
        {
            horizontalColliderPlaneCount = 0;
            supportMotionY = 0;
            double motionLengthSq = motionX * motionX + motionZ * motionZ;
            if (motionLengthSq <= collisionEpsilon * collisionEpsilon) return false;

            double contactTolerance = CollisionPlaneSolver.DefaultTolerance * 2;
            double maxSeparation = Math.Sqrt(motionLengthSq) + contactTolerance;
            for (int colliderIndex = 0; colliderIndex < candidateColliders.Count; colliderIndex++)
            {
                Collider collider = candidateColliders[colliderIndex];
                for (int boxIndex = 0; boxIndex < collider.CollisionBoxCount; boxIndex++)
                {
                    // Floor boxes can expose horizontal end faces at seams.
                    if (box.Y1 >= collider.GetCollisionBoxBounds(boxIndex).Y2 - contactTolerance) continue;

                    if (!collider.TryGetAABBContact(boxIndex, box, maxSeparation, out CuboidContact contact)) continue;
                    // Existing depenetration owns established overlaps this path only predicts approaching wall contacts.
                    if (contact.Separation < -contactTolerance) continue;

                    double normalX = contact.Normal.X;
                    double normalZ = contact.Normal.Z;
                    double horizontalNormalLengthSq = normalX * normalX + normalZ * normalZ;
                    if (horizontalNormalLengthSq <= contact.Normal.Y * contact.Normal.Y * maxWalkableSlopeGrade * maxWalkableSlopeGrade + colliderSlopeTolerance)
                    {
                        if (supportingCollider != null && collider.Id == supportingCollider.Id && contact.Normal.Y > 0)
                        {
                            updateSupportMotionY(contact, motionX, motionZ, contactTolerance, ref supportMotionY);
                        }
                        continue;
                    }

                    double horizontalNormalLength = Math.Sqrt(horizontalNormalLengthSq);
                    normalX /= horizontalNormalLength;
                    normalZ /= horizontalNormalLength;
                    double motionIntoPlane = motionX * normalX + motionZ * normalZ;
                    if (motionIntoPlane >= 0) continue;

                    double targetSeparation = contact.Separation + motionIntoPlane * horizontalNormalLength;
                    if (targetSeparation >= contactTolerance) continue;

                    double minimumDistance = (contactTolerance - contact.Separation) / horizontalNormalLength;
                    addHorizontalColliderPlane(normalX, normalZ, minimumDistance, motionX, motionZ);
                }
            }

            if (horizontalColliderPlaneCount == 0) return false;

            FastVec3d targetMotion = new(motionX, 0, motionZ);
            CollisionPlaneSolverResult result = CollisionPlaneSolver.Solve(targetMotion, horizontalColliderPlanes.AsSpan(0, horizontalColliderPlaneCount));
            motionX = result.Delta.X;
            motionZ = result.Delta.Z;
            if (supportingCollider != null)
            {
                supportMotionY = getSupportMotionY(box, supportingCollider, motionX, motionZ, contactTolerance);
            }
            return true;
        }

        private static void updateSupportMotionY(CuboidContact contact, double motionX, double motionZ, double contactTolerance, ref double supportMotionY)
        {
            double horizontalMotionIntoPlane = motionX * contact.Normal.X + motionZ * contact.Normal.Z;
            if (horizontalMotionIntoPlane >= -collisionEpsilon) return;

            double requiredMotionY = (contactTolerance - contact.Separation - horizontalMotionIntoPlane) / contact.Normal.Y;
            if (requiredMotionY > supportMotionY) supportMotionY = requiredMotionY;
        }

        private double getSupportMotionY(Cuboidd box, Collider supportingCollider, double motionX, double motionZ, double contactTolerance)
        {
            double supportMotionY = 0;
            double maxSeparation = Math.Sqrt(motionX * motionX + motionZ * motionZ) + contactTolerance;
            for (int boxIndex = 0; boxIndex < supportingCollider.CollisionBoxCount; boxIndex++)
            {
                if (box.Y1 >= supportingCollider.GetCollisionBoxBounds(boxIndex).Y2 - contactTolerance) continue;
                if (!supportingCollider.TryGetAABBContact(boxIndex, box, maxSeparation, out CuboidContact contact)) continue;
                if (contact.Separation < -contactTolerance || contact.Normal.Y <= 0) continue;

                if (!IsWalkableColliderSurface(contact.Normal, maxWalkableSlopeGrade)) continue;

                updateSupportMotionY(contact, motionX, motionZ, contactTolerance, ref supportMotionY);
            }

            return supportMotionY;
        }

        private void addHorizontalColliderPlane(double normalX, double normalZ, double minimumDistance, double motionX, double motionZ)
        {
            for (int i = 0; i < horizontalColliderPlaneCount; i++)
            {
                CollisionPlane plane = horizontalColliderPlanes[i];
                double alignment = plane.Normal.X * normalX + plane.Normal.Z * normalZ;
                if (alignment < horizontalColliderNormalSimilarity) continue;

                if (minimumDistance > plane.MinimumDistance)
                {
                    horizontalColliderPlanes[i] = new CollisionPlane(new FastVec3d(normalX, 0, normalZ), minimumDistance);
                }
                return;
            }

            var newPlane = new CollisionPlane(new FastVec3d(normalX, 0, normalZ), minimumDistance);
            if (horizontalColliderPlaneCount < horizontalColliderPlanes.Length)
            {
                horizontalColliderPlanes[horizontalColliderPlaneCount++] = newPlane;
                return;
            }

            int leastRestrictiveIndex = 0;
            double leastViolation = getHorizontalPlaneViolation(horizontalColliderPlanes[0], motionX, motionZ);
            for (int i = 1; i < horizontalColliderPlanes.Length; i++)
            {
                double violation = getHorizontalPlaneViolation(horizontalColliderPlanes[i], motionX, motionZ);
                if (violation >= leastViolation) continue;

                leastRestrictiveIndex = i;
                leastViolation = violation;
            }

            if (getHorizontalPlaneViolation(newPlane, motionX, motionZ) > leastViolation)
            {
                horizontalColliderPlanes[leastRestrictiveIndex] = newPlane;
            }
        }

        private static double getHorizontalPlaneViolation(CollisionPlane plane, double motionX, double motionZ)
        {
            return plane.MinimumDistance - motionX * plane.Normal.X - motionZ * plane.Normal.Z;
        }

        private bool trySweepColliders(Cuboidd box, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            collisionFraction = 1;
            if (motionX == 0 && motionY == 0 && motionZ == 0) return false;

            bool collided = false;
            for (int i = 0; i < colliders.Count; i++)
            {
                if (!colliders[i].TrySweepAABB(box, motionX, motionY, motionZ, out double fraction) || fraction >= collisionFraction) continue;

                collisionFraction = fraction;
                collided = true;
                if (fraction == 0) return true;
            }

            return collided;
        }

        private Collider getSupportingCollider(Cuboidd box)
        {
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                for (int boxIndex = 0; boxIndex < collider.CollisionBoxCount; boxIndex++)
                {
                    if (collider.TryGetAABBContact(boxIndex, box, colliderContactTolerance, out CuboidContact contact)
                        && contact.Separation >= -colliderContactTolerance && IsWalkableColliderSurface(contact.Normal, maxWalkableSlopeGrade))
                    {
                        return collider;
                    }
                }
            }

            return null;
        }

        protected virtual void GenerateCollisionBoxList(IBlockAccessor blockAccessor, double motionX, double motionY, double motionZ, float stepHeight, float yExtra, int dimension)
        {
            // NEVER CALLED IN 1.20 as all invocations are in CachingCollisionTester.  But we retain this in case a mod calls it.

            // Check if the min and max positions of the collision test are unchanged and use the old list if they are.
            bool minPosIsUnchanged = minPos.SetAndEquals(
                (int)(entityBox.X1 + Math.Min(0, motionX)),
                (int)(entityBox.Y1 + Math.Min(0, motionY) - yExtra), // yExtra looks at blocks below to allow for the extra high collision box of fences.
                (int)(entityBox.Z1 + Math.Min(0, motionZ))
            );

            double y2 = Math.Max(entityBox.Y1 + stepHeight, entityBox.Y2);

            bool maxPosIsUnchanged = maxPos.SetAndEquals(
                (int)(entityBox.X2 + Math.Max(0, motionX)),
                (int)(y2 + Math.Max(0, motionY)),
                (int)(entityBox.Z2 + Math.Max(0, motionZ))
            );

            if (minPosIsUnchanged && maxPosIsUnchanged) return;

            // Clear the list and add every cuboid the block has to it.
            CollisionBoxList.Clear();
            blockAccessor.WalkBlocks(minPos, maxPos, (block, x, y, z) => {
                Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, tmpPos.Set(x, y, z));
                if (collisionBoxes != null)
                {
                    CollisionBoxList.Add(collisionBoxes, x, y, z, block);
                }
            }, true);
        }


        /// <summary>
        /// Tests given cuboidf collides with the terrain. By default also checks if the cuboid is merely touching the terrain, set alsoCheckTouch to disable that.
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="entityBoxRel"></param>
        /// <param name="pos"></param>
        /// <param name="alsoCheckTouch"></param>
        /// <returns></returns>
        public bool IsColliding(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, bool alsoCheckTouch = true)
        {
            return getCollidingBlock(blockAccessor, entityBoxRel, pos, 0, false, alsoCheckTouch) != null;
        }

        /// <summary>
        /// Tests terrain collision at a dimension-local position in the supplied dimension.
        /// </summary>
        public bool IsColliding(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, int dimension, bool alsoCheckTouch = true)
        {
            return getCollidingBlock(blockAccessor, entityBoxRel, pos, dimension, true, alsoCheckTouch) != null;
        }

        /// <summary>
        /// Tests terrain and registered colliders at a dimension-local position in the supplied dimension.
        /// </summary>
        public bool IsColliding(IWorldAccessor worldAccessor, Cuboidf entityBoxRel, Vec3d pos, int dimension, bool alsoCheckTouch = true)
        {
            if (IsColliding(worldAccessor.BlockAccessor, entityBoxRel, pos, dimension, alsoCheckTouch)) return true;

            return worldAccessor.CollisionScene.IntersectsAny(tmpBox.SetAndTranslate(entityBoxRel, pos), dimension, alsoCheckTouch, new CollisionQueryFilter(CollisionLayer.Entity));
        }

        public Block GetCollidingBlock(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, bool alsoCheckTouch = true)
        {
            return getCollidingBlock(blockAccessor, entityBoxRel, pos, 0, false, alsoCheckTouch);
        }

        /// <summary>
        /// Gets the colliding terrain block at a dimension-local position in the supplied dimension.
        /// </summary>
        public Block GetCollidingBlock(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, int dimension, bool alsoCheckTouch = true)
        {
            return getCollidingBlock(blockAccessor, entityBoxRel, pos, dimension, true, alsoCheckTouch);
        }

        private Block getCollidingBlock(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, int dimension, bool hasExplicitDimension, bool alsoCheckTouch)
        {
            Cuboidd entityBox = tmpBox.SetAndTranslate(entityBoxRel, pos);

            int minX = (int)entityBox.X1;
            int minY = (int)entityBox.Y1 - 1; // -1 for the extra high collision box of fences.
            int minZ = (int)entityBox.Z1;

            int maxX = (int)entityBox.X2;
            int maxY = (int)entityBox.Y2;
            int maxZ = (int)entityBox.Z2;

            entityBox.Y1 = Math.Round(entityBox.Y1, 5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.

            BlockPos blockPos = this.blockPos;   // Local reference for efficiency
            Vec3d blockPosVec = this.blockPosVec;   // Local reference for efficiency
            if (hasExplicitDimension) blockPos.dimension = dimension;
            for (int y = minY; y <= maxY; y++)
            {
                if (hasExplicitDimension) blockPos.Set(minX, y, minZ);
                else blockPos.SetAndCorrectDimension(minX, y, minZ);
                blockPosVec.Set(minX, y, minZ);
                for (int x = minX; x <= maxX; x++)
                {
                    blockPos.X = x;
                    blockPosVec.X = x;
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockPos.Z = z;
                        Block block = blockAccessor.GetBlock(blockPos, BlockLayersAccess.MostSolid);

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);
                        if (collisionBoxes == null || collisionBoxes.Length == 0) continue;

                        blockPosVec.Z = z;
                        for (int i = 0; i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            if (alsoCheckTouch ? entityBox.IntersectsOrTouches(collBox, blockPosVec) : entityBox.Intersects(collBox, blockPosVec)) return block;
                        }
                    }
                }
            }

            return null;
        }



        /// <summary>
        /// If given cuboidf collides with the terrain, returns the collision box it collides with. By default also checks if the cuboid is merely touching the terrain, set alsoCheckTouch to disable that.
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="entityBoxRel"></param>
        /// <param name="pos"></param>
        /// <param name="alsoCheckTouch"></param>
        /// <returns></returns>
        public Cuboidd GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, bool alsoCheckTouch = true)
        {
            BlockPos blockPos = new(Dimensions.NormalWorld);   // The dimension is included in the Vec3d pos.Y field
            Vec3d blockPosVec = new();
            Cuboidd entityBox = entityBoxRel.ToDouble().Translate(pos);

            entityBox.Y1 = Math.Round(entityBox.Y1, 5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.

            int minX = (int)(entityBoxRel.X1 + pos.X);
            int minY = (int)(entityBoxRel.Y1 + pos.Y - 1); // -1 for the extra high collision box of fences
            int minZ = (int)(entityBoxRel.Z1 + pos.Z);

            int maxX = (int)Math.Ceiling(entityBoxRel.X2 + pos.X);
            int maxY = (int)Math.Ceiling(entityBoxRel.Y2 + pos.Y);
            int maxZ = (int)Math.Ceiling(entityBoxRel.Z2 + pos.Z);

            for (int y = minY; y <= maxY; y++)
            {
                blockPos.Set(minX, y, minZ);
                blockPosVec.Set(minX, y, minZ);
                for (int x = minX; x <= maxX; x++)
                {
                    blockPos.X = x;
                    blockPosVec.X = x;
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockPos.Z = z;
#pragma warning disable CS0618 // Type or member is obsolete - but it's correct here as the dimension is included in the y field
                        Block block = blockAccessor.GetMostSolidBlock(x, y, z);
#pragma warning restore CS0618

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);
                        if (collisionBoxes == null) continue;

                        blockPosVec.Z = z;
                        for (int i = 0; i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            bool colliding = alsoCheckTouch ? entityBox.IntersectsOrTouches(collBox, blockPosVec) : entityBox.Intersects(collBox, blockPosVec);
                            if (colliding)
                            {
                                return collBox.ToDouble().Translate(blockPos);
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Tests given cuboidf collides with the terrain. By default also checks if the cuboid is merely touching the terrain, set alsoCheckTouch to disable that.
        /// <br/>NOTE: currently not dimension-aware unless the supplied Vec3d pos is dimension-aware
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="entityBoxRel"></param>
        /// <param name="pos"></param>
        /// <param name="intoCuboid"></param>
        /// <param name="alsoCheckTouch"></param>
        /// <param name="dimension"></param>
        /// <returns></returns>
        public bool GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, ref Cuboidd intoCuboid, bool alsoCheckTouch = true, int dimension = 0)
        {
            BlockPos blockPos = new(dimension);
            Vec3d blockPosVec = new();
            Cuboidd entityBox = entityBoxRel.ToDouble().Translate(pos);

            entityBox.Y1 = Math.Round(entityBox.Y1, 5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.

            int minX = (int)(entityBoxRel.X1 + pos.X);
            int minY = (int)(entityBoxRel.Y1 + pos.Y - 1); // -1 for the extra high collision box of fences.
            int minZ = (int)(entityBoxRel.Z1 + pos.Z);

            int maxX = (int)Math.Ceiling(entityBoxRel.X2 + pos.X);
            int maxY = (int)Math.Ceiling(entityBoxRel.Y2 + pos.Y);
            int maxZ = (int)Math.Ceiling(entityBoxRel.Z2 + pos.Z);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    blockPos.Set(x, y, minZ);
                    blockPosVec.Set(x, y, minZ);
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockPos.Z = z;
                        Block block = blockAccessor.GetBlock(blockPos, BlockLayersAccess.MostSolid);

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);
                        if (collisionBoxes == null) continue;

                        blockPosVec.Z = z;
                        for (int i = 0; i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            bool colliding = alsoCheckTouch ? entityBox.IntersectsOrTouches(collBox, blockPosVec) : entityBox.Intersects(collBox, blockPosVec);
                            if (colliding)
                            {
                                intoCuboid.Set(collBox).Translate(blockPos);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        public static bool AabbIntersect(Cuboidf aabb, double x, double y, double z, Cuboidf aabb2, Vec3d pos)
        {
            if (aabb2 == null) return true;

            return
                x + aabb.X1 < aabb2.X2 + pos.X &&
                x + aabb.X2 > aabb2.X1 + pos.X &&
                z + aabb.Z1 < aabb2.Z2 + pos.Z &&
                z + aabb.Z2 > aabb2.Z1 + pos.Z &&
                y + aabb.Y1 < aabb2.Y2 + pos.Y &&
                y + aabb.Y2 > aabb2.Y1 + pos.Y
                ;
        }

        public static EnumIntersect AabbIntersect(Cuboidd aabb, Cuboidd aabb2, Vec3d motion)
        {
            if (aabb.Intersects(aabb2)) return EnumIntersect.Stuck;

            // X
            if (
                aabb.X1 < aabb2.X2 + motion.X &&
                aabb.X2 > aabb2.X1 + motion.X &&
                aabb.Z1 < aabb2.Z2 &&
                aabb.Z2 > aabb2.Z1 &&
                aabb.Y1 < aabb2.Y2 &&
                aabb.Y2 > aabb2.Y1
            ) return EnumIntersect.IntersectX;

            // Y
            if (
                aabb.X1 < aabb2.X2 &&
                aabb.X2 > aabb2.X1 &&
                aabb.Z1 < aabb2.Z2 &&
                aabb.Z2 > aabb2.Z1 &&
                aabb.Y1 < aabb2.Y2 + motion.Y &&
                aabb.Y2 > aabb2.Y1 + motion.Y
            ) return EnumIntersect.IntersectY;

            // Z
            if (
                aabb.X1 < aabb2.X2 &&
                aabb.X2 > aabb2.X1 &&
                aabb.Z1 < aabb2.Z2 + motion.Z &&
                aabb.Z2 > aabb2.Z1 + motion.Z &&
                aabb.Y1 < aabb2.Y2 &&
                aabb.Y2 > aabb2.Y1
            ) return EnumIntersect.IntersectZ;

            return EnumIntersect.NoIntersect;
        }

    }

    /// <summary>
    /// Originally intended to be a special version of CollisionTester for BehaviorControlledPhysics, which does not re-do the WalkBlocks() call and re-generate the CollisionBoxList more than once in the same entity tick
    /// <br/>Currently in 1.20 the caching is not very useful when we loop through all entities sequentially - but empirical testing shows it is actually faster not to cache
    /// </summary>
    public class CachingCollisionTester : CollisionTester
    {
        public void NewTick(EntityPos entityPos)
        {
            minPos.Set(int.MinValue, int.MinValue, int.MinValue);
            minPos.SetDimension(entityPos.Dimension);
            tmpPos.SetDimension(entityPos.Dimension);
        }

        public void AssignToEntity(PhysicsBehaviorBase entityPhysics, int dimension)
        {
            minPos.SetDimension(dimension);
            tmpPos.SetDimension(dimension);
        }

        protected override void GenerateCollisionBoxList(IBlockAccessor blockAccessor, double motionX, double motionY, double motionZ, float stepHeight, float yExtra, int dimension)
        {
            Cuboidd entityBox = this.entityBox;  // Local reference for efficiency

            bool minPosIsUnchanged = minPos.SetAndEquals(
                (int)(entityBox.X1 + Math.Min(0, motionX)),
                (int)(entityBox.Y1 + Math.Min(0, motionY) - yExtra), // yExtra looks at blocks below to allow for the extra high collision box of fences
                (int)(entityBox.Z1 + Math.Min(0, motionZ))
            );

            double y2 = Math.Max(entityBox.Y1 + stepHeight, entityBox.Y2);

            bool maxPosIsUnchanged = maxPos.SetAndEquals(
                (int)(entityBox.X2 + Math.Max(0, motionX)),
                (int)(y2 + Math.Max(0, motionY)),
                (int)(entityBox.Z2 + Math.Max(0, motionZ))
            );

            if (minPosIsUnchanged && maxPosIsUnchanged)
            {
                return;
            }

            CollisionBoxList.Clear();
            blockAccessor.WalkBlocks(minPos, maxPos, (block, x, y, z) => {
                Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, tmpPos.Set(x, y, z));
                if (collisionBoxes != null)
                {
                    CollisionBoxList.Add(collisionBoxes, x, y, z, block);
                }
            }, true);
        }

        public void PushOutFromBlocks(IBlockAccessor blockAccessor, Entity entity, Vec3d tmpVec, float clippingLimit)
        {
            if (IsColliding(blockAccessor, entity.CollisionBox, tmpVec, false))
            {
                Vec3d pos = entity.Pos.XYZ;
                entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);

                GenerateCollisionBoxList(blockAccessor, 0, 0, 0, 0.5f, 0, entity.Pos.Dimension);

                int collisionBoxListCount = CollisionBoxList.Count;
                if (collisionBoxListCount == 0) return;
                Cuboidd[] CollisionBoxListCuboids = CollisionBoxList.cuboids;   // Local reference for efficiency

                double deltaX = 0;
                double deltaZ = 0;
                EnumPushDirection pushDirection = EnumPushDirection.None;
                var reducedBox = entity.CollisionBox.ToDouble();
                reducedBox.Translate(pos.X, pos.Y, pos.Z);
                reducedBox.GrowBy(-clippingLimit, 0, -clippingLimit);

                for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                {
                    if (i >= collisionBoxListCount) break;
                    deltaX = CollisionBoxListCuboids[i].pushOutX(reducedBox, clippingLimit, ref pushDirection);
                }
                if (deltaX == clippingLimit)
                {
                    for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                    {
                        if (i >= collisionBoxListCount) break;
                        deltaX = CollisionBoxListCuboids[i].pushOutX(reducedBox, -clippingLimit, ref pushDirection);
                    }
                    deltaX += clippingLimit;
                }
                else deltaX -= clippingLimit;

                for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                {
                    if (i >= collisionBoxListCount) break;
                    deltaZ = CollisionBoxListCuboids[i].pushOutZ(reducedBox, clippingLimit, ref pushDirection);
                }
                if (deltaZ == clippingLimit)
                {
                    for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                    {
                        if (i >= collisionBoxListCount) break;
                        deltaZ = CollisionBoxListCuboids[i].pushOutZ(reducedBox, -clippingLimit, ref pushDirection);
                    }
                    deltaZ += clippingLimit;
                }
                else deltaZ -= clippingLimit;

                entity.Pos.X = pos.X + deltaX;
                entity.Pos.Z = pos.Z + deltaZ;
            }
        }
    }
}
