using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.API.MathTools
{
    public class MultiCollisionTester
    {
        public CachedCuboidList CollisionBoxList = new();

        public Cuboidd[] entityBox = ArrayUtil.CreateFilled(10, (i) => new Cuboidd());
        protected int count;

        // Use class level fields to reduce garbage collection
        public BlockPos tmpPos = new(Dimensions.WillSetLater);
        public Vec3d tmpPosDelta = new();

        public BlockPos minPos = new(Dimensions.WillSetLater);
        public BlockPos maxPos = new(Dimensions.WillSetLater);

        public Vec3d pos = new();
        private readonly List<CollisionHit> colliderHits = new();
        private readonly Cuboidd colliderBox = new();
        private readonly Cuboidd colliderContactBox = new();
        private readonly TransformedCuboidd terrainSweepBox = new();
        private readonly Cuboidd colliderQueryBounds = new();
        private readonly List<Collider> colliders = new();
        private const double collisionEpsilon = 0.0001;
        private const double maxRotationStep = Math.PI / 180;

        private void loadColliders(ICollisionScene scene, Cuboidf[] boxes, int boxCount, EntityPos start, double dx, double dy, double dz, CollisionQueryFilter filter)
        {
            colliderQueryBounds.Set(boxes[0]);
            for (int i = 1; i < boxCount; i++)
            {
                var box = boxes[i];
                colliderQueryBounds.X1 = Math.Min(colliderQueryBounds.X1, box.X1);
                colliderQueryBounds.Y1 = Math.Min(colliderQueryBounds.Y1, box.Y1);
                colliderQueryBounds.Z1 = Math.Min(colliderQueryBounds.Z1, box.Z1);
                colliderQueryBounds.X2 = Math.Max(colliderQueryBounds.X2, box.X2);
                colliderQueryBounds.Y2 = Math.Max(colliderQueryBounds.Y2, box.Y2);
                colliderQueryBounds.Z2 = Math.Max(colliderQueryBounds.Z2, box.Z2);
            }
            colliderQueryBounds.Translate(start.X, start.Y, start.Z);
            colliderQueryBounds.X1 += Math.Min(0, dx);
            colliderQueryBounds.Y1 += Math.Min(0, dy);
            colliderQueryBounds.Z1 += Math.Min(0, dz);
            colliderQueryBounds.X2 += Math.Max(0, dx);
            colliderQueryBounds.Y2 += Math.Max(0, dy);
            colliderQueryBounds.Z2 += Math.Max(0, dz);
            scene.GetColliders(colliderQueryBounds, start.Dimension, colliders, filter);
        }

        private double clipColliderMotion(Cuboidd box, double motion, EnumAxis axis)
        {
            if (motion == 0) return 0;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider.Intersects(box))
                {
                    // Allow an overlapping hull to leave, but never move farther into the contact.
                    for (int j = 0; j < collider.CollisionBoxCount; j++)
                    {
                        if (!collider.TryGetAABBContact(j, box, 0, out var contact) || contact.Separation >= 0) continue;
                        double normal = axis == EnumAxis.X ? contact.Normal.X : axis == EnumAxis.Y ? contact.Normal.Y : contact.Normal.Z;
                        if (normal * motion < 0) return 0;
                    }
                }
                EnumPushDirection direction = EnumPushDirection.None;
                motion = axis switch
                {
                    EnumAxis.X => collider.PushOutX(box, motion, ref direction),
                    EnumAxis.Y => collider.PushOutY(box, motion, ref direction),
                    _ => collider.PushOutZ(box, motion, ref direction)
                };
            }
            return motion;
        }

        private void clipHorizontalSweep(Cuboidf[] boxes, int boxCount, EntityPos start, double dy, ref double dx, ref double dz)
        {
            if (dx == 0 || dz == 0) return;
            // As in CollisionTester, retain continuous hits on thin diagonal walls missed by the axis path.
            double fraction = 1;
            for (int i = 0; i < boxCount; i++)
            {
                colliderBox.SetAndTranslate(boxes[i], start.X, start.Y + dy, start.Z);
                for (int j = 0; j < colliders.Count; j++)
                {
                    if (colliders[j].TrySweepAABB(colliderBox, dx, 0, dz, out double hitFraction)) fraction = Math.Min(fraction, hitFraction);
                }
            }
            if (fraction < 1)
            {
                // Shortening the axis-resolved move can cross a terrain corner again.
                for (int i = 0; i < boxCount; i++)
                {
                    colliderBox.SetAndTranslate(boxes[i], start.X, start.Y + dy, start.Z);
                    for (int j = 0; j < CollisionBoxList.Count; j++)
                    {
                        if (terrainSweepBox.Set(CollisionBoxList.cuboids[j]).TrySweepAABB(colliderBox, dx, 0, dz, out double hitFraction)) fraction = Math.Min(fraction, hitFraction);
                    }
                }
            }
            dx *= fraction;
            dz *= fraction;
        }

        public bool CanTranslate(ICollisionScene scene, Cuboidf[] boxes, EntityPos position, double dx, double dy, double dz, CollisionQueryFilter filter)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                colliderBox.SetAndTranslate(boxes[i], position.X, position.Y, position.Z);
                scene.Sweep(colliderBox, dx, dy, dz, position.Dimension, colliderHits, filter);
                bool blocked = false;
                for (int j = 0; j < colliderHits.Count; j++)
                {
                    var hit = colliderHits[j];
                    if (hit.StartedOverlapping
                        ? hit.Normal.X * dx + hit.Normal.Y * dy + hit.Normal.Z * dz < -CollisionPlaneSolver.DefaultTolerance
                        : hit.Fraction < 1)
                    {
                        blocked = true;
                        break;
                    }
                }
                colliderHits.Clear();
                if (blocked) return false;
            }
            return true;
        }

        /// <summary>
        /// Tests intermediate headings with the upright boxes used by multibox physics.
        /// </summary>
        public bool CanRotate(ICollisionScene scene, Cuboidf[] boxes, EntityPos position, float targetYaw, CollisionQueryFilter filter)
        {
            return CanRotate(null, scene, boxes, position, targetYaw, filter);
        }

        public bool CanRotate(IBlockAccessor blockAccessor, ICollisionScene scene, Cuboidf[] boxes, EntityPos position, float targetYaw, CollisionQueryFilter filter)
        {
            double delta = GameMath.AngleRadDistance(position.Yaw, targetYaw);
            if (delta == 0) return true;
            double absDelta = Math.Abs(delta);
            if (blockAccessor != null)
            {
                count = boxes.Length;
                double cosYaw = Math.Cos(position.Yaw + Math.PI);
                double sinYaw = Math.Sin(position.Yaw + Math.PI);
                for (int i = 0; i < count; i++)
                {
                    var box = boxes[i];
                    float midX = box.MidX;
                    float midZ = box.MidZ;
                    float halfWidth = box.HalfExtentX;
                    float halfLength = box.HalfExtentZ;
                    double x = midX * cosYaw + midZ * sinYaw;
                    double z = -midX * sinYaw + midZ * cosYaw;
                    double travel = Math.Sqrt(midX * midX + midZ * midZ) * absDelta + collisionEpsilon;
                    entityBox[i].Set(position.X + x - halfWidth - travel, position.Y + box.Y1, position.Z + z - halfLength - travel,
                        position.X + x + halfWidth + travel, position.Y + box.Y2, position.Z + z + halfLength + travel);
                }
                GenerateCollisionBoxList(blockAccessor, 0, 0, 0, 0, 1, position.Dimension);
            }

            int steps = (int)Math.Ceiling(absDelta / maxRotationStep);
            double step = delta / steps;
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                float midX = box.MidX;
                float midZ = box.MidZ;
                float halfWidth = box.HalfExtentX;
                float halfLength = box.HalfExtentZ;
                double radius = Math.Sqrt(midX * midX + midZ * midZ);
                double padding = radius * (1 - Math.Cos(step / 2)) + collisionEpsilon;
                double angle = position.Yaw + Math.PI;
                double cosAngle = Math.Cos(angle);
                double sinAngle = Math.Sin(angle);
                double x = midX * cosAngle + midZ * sinAngle;
                double z = -midX * sinAngle + midZ * cosAngle;
                for (int j = 1; j <= steps; j++)
                {
                    angle = position.Yaw + Math.PI + step * j;
                    cosAngle = Math.Cos(angle);
                    sinAngle = Math.Sin(angle);
                    double nextX = midX * cosAngle + midZ * sinAngle;
                    double nextZ = -midX * sinAngle + midZ * cosAngle;
                    // The arc lies within the swept chord expanded by its sagitta.
                    colliderBox.Set(position.X + x - halfWidth - padding, position.Y + box.Y1, position.Z + z - halfLength - padding,
                        position.X + x + halfWidth + padding, position.Y + box.Y2, position.Z + z + halfLength + padding);
                    scene.Sweep(colliderBox, nextX - x, 0, nextZ - z, position.Dimension, colliderHits, filter);
                    colliderContactBox.Set(position.X + x - halfWidth, position.Y + box.Y1, position.Z + z - halfLength,
                        position.X + x + halfWidth, position.Y + box.Y2, position.Z + z + halfLength);
                    bool blocked = false;
                    for (int hitIndex = 0; hitIndex < colliderHits.Count; hitIndex++)
                    {
                        var hit = colliderHits[hitIndex];
                        if (!hit.Collider.TryGetAABBContact(hit.CollisionBoxIndex, colliderContactBox, double.MaxValue, out var contact)
                            || rotationDeepensContact(contact, x, z, nextX, nextZ, step))
                        {
                            blocked = true;
                            break;
                        }
                    }
                    colliderHits.Clear();
                    if (blocked) return false;
                    if (blockAccessor != null)
                    {
                        for (int hitIndex = 0; hitIndex < CollisionBoxList.Count; hitIndex++)
                        {
                            terrainSweepBox.Set(CollisionBoxList.cuboids[hitIndex]);
                            if (!terrainSweepBox.Intersects(colliderBox) && !terrainSweepBox.TrySweepAABB(colliderBox, nextX - x, 0, nextZ - z, out _)) continue;
                            if (!terrainSweepBox.TryGetAABBContact(colliderContactBox, double.MaxValue, out var contact)
                                || rotationDeepensContact(contact, x, z, nextX, nextZ, step))
                            {
                                return false;
                            }
                        }
                    }
                    x = nextX;
                    z = nextZ;
                }
            }
            return true;
        }

        private static bool rotationDeepensContact(CuboidContact contact, double startX, double startZ, double endX, double endZ, double step)
        {
            // Project the actual center arc onto the separating plane, excluding the conservative sweep padding.
            double start = startX * contact.Normal.X + startZ * contact.Normal.Z;
            double end = endX * contact.Normal.X + endZ * contact.Normal.Z;
            double tangent = startZ * contact.Normal.X - startX * contact.Normal.Z;
            double minimum = Math.Min(start, end);
            double minimumAngle = Math.Atan2(tangent, start) + Math.PI;
            double distance = GameMath.Mod(step > 0 ? minimumAngle : -minimumAngle, Math.PI * 2);
            if (distance <= Math.Abs(step)) minimum = -Math.Sqrt(start * start + tangent * tangent);
            return minimum < start && contact.Separation + minimum - start < collisionEpsilon;
        }

        public void ApplyTerrainCollision(Cuboidf[] collisionBoxes, int collBoxCount, Entity entity, EntityPos entityPos, float dtFactor, ref Vec3d newPosition, float stepHeight = 1, float yExtra = 1)
        {
            this.count = collBoxCount;
            var world = entity.World;

            pos.X = entityPos.X;
            pos.Y = entityPos.Y;
            pos.Z = entityPos.Z;

            EnumPushDirection pushDirection = EnumPushDirection.None;

            for (int i = 0; i < collBoxCount; i++)
            {
                entityBox[i].SetAndTranslate(collisionBoxes[i], pos.X, pos.Y, pos.Z);
                //entityBox[i].RemoveRoundingErrors(); // Necessary to prevent unwanted clipping through blocks when there is knockback
            }

            double motionX = entityPos.Motion.X * dtFactor;
            double motionY = entityPos.Motion.Y * dtFactor;
            double motionZ = entityPos.Motion.Z * dtFactor;

            // Match CollisionTester's clearance so rounded positions do not trigger boat pushout.
            double motEpsX = 0, motEpsZ = 0;
            if (motionX > collisionEpsilon) motEpsX = collisionEpsilon;
            if (motionX < -collisionEpsilon) motEpsX = -collisionEpsilon;
            if (motionZ > collisionEpsilon) motEpsZ = collisionEpsilon;
            if (motionZ < -collisionEpsilon) motEpsZ = -collisionEpsilon;
            motionX += motEpsX;
            motionZ += motEpsZ;

            GenerateCollisionBoxList(world.BlockAccessor, motionX, motionY, motionZ, stepHeight, yExtra, entityPos.Dimension);
            loadColliders(world.CollisionScene, collisionBoxes, collBoxCount, entityPos, motionX, motionY, motionZ, new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId));

            bool collided = false;
            int collisionBoxListCount = CollisionBoxList.Count;

            // ---------- Y COLLISION. Call events and set collided vertically.
            for (int i = 0; i < collisionBoxListCount; i++)
            {
                for (int j = 0; j < collBoxCount; j++)
                {
                    motionY = CollisionBoxList.cuboids[i].pushOutY(entityBox[j], motionY, ref pushDirection);
                    if (pushDirection != EnumPushDirection.None)
                    {
                        CollisionBoxList.blocks[i].OnEntityCollide(
                            world,
                            entity,
                            CollisionBoxList.positions[i],
                            pushDirection == EnumPushDirection.Negative ? BlockFacing.UP : BlockFacing.DOWN,
                            tmpPosDelta.Set(motionX, motionY, motionZ),
                            !entity.CollidedVertically
                        );

                        collided = true;
                    }
                }

            }

            double terrainMotionY = motionY;
            for (int j = 0; j < collBoxCount; j++) motionY = clipColliderMotion(entityBox[j], motionY, EnumAxis.Y);
            collided |= motionY != terrainMotionY;
            for (int j = 0; j < collBoxCount; j++) entityBox[j].Translate(0, motionY, 0);

            entity.CollidedVertically = collided;

            // Check if horizontal collision is possible.
            bool horizontallyBlocked = false;
            for (int j = 0; j < collBoxCount; j++) entityBox[j].Translate(motionX, 0, motionZ);
            foreach (var cuboid in CollisionBoxList)
            {
                bool blocked = false;
                for (int j = 0; j < collBoxCount; j++)
                {
                    if (cuboid.Intersects(entityBox[j]))
                    {
                        horizontallyBlocked = true;
                        blocked = true;
                        break;
                    }
                }
                if (blocked) break;
            }

            for (int j = 0; j < collBoxCount; j++)
            {
                entityBox[j].Translate(-motionX, 0, -motionZ);  // cheaper than creating a new Cuboidd
            }

            // No collisions for the entity found when testing horizontally, so skip this.
            // This allows entities to move around corners without falling down on a certain axis.
            collided = false;
            if (horizontallyBlocked || colliders.Count > 0)
            {

                // X - Collision (Horizontal)
                for (int i = 0; i < collisionBoxListCount; i++)
                {
                    bool pushed = false;
                    for (int j = 0; j < collBoxCount; j++)
                    {
                        motionX = CollisionBoxList.cuboids[i].pushOutX(entityBox[j], motionX, ref pushDirection);
                        if (pushDirection != EnumPushDirection.None)
                        {
                            CollisionBoxList.blocks[i].OnEntityCollide(
                                world,
                                entity,
                                CollisionBoxList.positions[i],
                                pushDirection == EnumPushDirection.Negative ? BlockFacing.EAST : BlockFacing.WEST,
                                tmpPosDelta.Set(motionX, motionY, motionZ),
                                !entity.CollidedHorizontally
                            );
                        }
                        pushed |= pushDirection != EnumPushDirection.None;
                    }

                    collided |= pushed;
                }
                double terrainMotionX = motionX;
                for (int j = 0; j < collBoxCount; j++) motionX = clipColliderMotion(entityBox[j], motionX, EnumAxis.X);
                collided |= motionX != terrainMotionX;
                for (int j = 0; j < collBoxCount; j++) entityBox[j].Translate(motionX, 0, 0);

                // Z - Collision (Horizontal)

                for (int i = 0; i < collisionBoxListCount; i++)
                {
                    bool pushed = false;
                    for (int j = 0; j < collBoxCount; j++)
                    {
                        motionZ = CollisionBoxList.cuboids[i].pushOutZ(entityBox[j], motionZ, ref pushDirection);
                        if (pushDirection != EnumPushDirection.None)
                        {
                            CollisionBoxList.blocks[i].OnEntityCollide(
                                world,
                                entity,
                                CollisionBoxList.positions[i],
                                pushDirection == EnumPushDirection.Negative ? BlockFacing.SOUTH : BlockFacing.NORTH,
                                tmpPosDelta.Set(motionX, motionY, motionZ),
                                !entity.CollidedHorizontally
                            );
                        }
                        pushed |= pushDirection != EnumPushDirection.None;
                    }

                    collided |= pushed;
                }
                double terrainMotionZ = motionZ;
                for (int j = 0; j < collBoxCount; j++) motionZ = clipColliderMotion(entityBox[j], motionZ, EnumAxis.Z);
                collided |= motionZ != terrainMotionZ;
            }

            double axisMotionX = motionX;
            double axisMotionZ = motionZ;
            clipHorizontalSweep(collisionBoxes, collBoxCount, entityPos, motionY, ref motionX, ref motionZ);
            entity.CollidedHorizontally = collided || motionX != axisMotionX || motionZ != axisMotionZ;

            motionX -= motEpsX;
            motionZ -= motEpsZ;

            newPosition.Set(pos.X + motionX, pos.Y + motionY, pos.Z + motionZ);
        }

        protected virtual void GenerateCollisionBoxList(IBlockAccessor blockAccessor, double motionX, double motionY, double motionZ, float stepHeight, float yExtra, int dimension)
        {
            double minx = double.MaxValue, miny = double.MaxValue, minz = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue, maxz = double.MinValue;

            for (int i = 0; i < count; i++)
            {
                var ebox = entityBox[i];
                minx = Math.Min(minx, ebox.X1);
                miny = Math.Min(miny, ebox.Y1);
                minz = Math.Min(minz, ebox.Z1);

                maxx = Math.Max(maxx, ebox.X2);
                maxy = Math.Max(maxy, ebox.Y2);
                maxz = Math.Max(maxz, ebox.Z2);
            }

            // Check if the min and max positions of the collision test are unchanged and use the old list if they are.
            minPos.Set(
                (int)(minx + Math.Min(0, motionX)),
                (int)(miny + Math.Min(0, motionY) - yExtra), // yExtra looks at blocks below to allow for the extra high collision box of fences.
                (int)(minz + Math.Min(0, motionZ))
            );

            double y2 = Math.Max(miny + stepHeight, maxy);

             maxPos.Set(
                (int)(maxx + Math.Max(0, motionX)),
                (int)(y2 + Math.Max(0, motionY)),
                (int)(maxz + Math.Max(0, motionZ))
            );

            minPos.SetDimension(dimension);
            tmpPos.SetDimension(dimension);

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
        /// If given cuboidf collides with the terrain, returns the collision box it collides with. By default also checks if the cuboid is merely touching the terrain, set alsoCheckTouch to disable that.
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="ecollisionBoxes"></param>
        /// <param name="collBoxCount"></param>
        /// <param name="pos"></param>
        /// <param name="alsoCheckTouch"></param>
        /// <returns></returns>
        public Cuboidd GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf[] ecollisionBoxes, int collBoxCount, Vec3d pos, bool alsoCheckTouch = true)
        {
            BlockPos blockPos = new(Dimensions.NormalWorld);    // The dimension is in the Vec3d pos.Y
            Vec3d blockPosVec = new();

            for (int j = 0; j < collBoxCount; j++)
            {
                var entityBoxRel = ecollisionBoxes[j];
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
                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
#pragma warning disable CS0618 // Type or member is obsolete - but it's correct here as the dimension is included in the y field
                            Block block = blockAccessor.GetMostSolidBlock(x, y, z);
#pragma warning restore CS0618
                            blockPos.Set(x, y, z);
                            blockPosVec.Set(x, y, z);

                            Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);

                            if (collisionBoxes == null) continue;
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
            }

            return null;
        }


    }
}
