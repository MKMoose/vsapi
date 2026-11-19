using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

#nullable disable

namespace Vintagestory.API.MathTools
{
    public class Line3D
    {
        public double[] Start;
        public double[] End;
    }


    public delegate bool BlockFilter(BlockPos pos, Block block);
    public delegate bool EntityFilter(Entity entity);


    // Based on https://www.scratchapixel.com/lessons/3d-basic-rendering/minimal-ray-tracer-rendering-simple-shapes/ray-plane-and-ray-disk-intersection
    // Our picking ray will always be a block at any position along the ray, because empty areas are considered air blocks.
    // So what we are really interested in is on what block face is the ray exiting and continue search in that direction
    // => This code is a ray-intersects-plane algo. It wanders along the exiting faces of full blocks and checks at each position for collisions with the blocks selection box
    // TODO: Replace with Slab method? -> https://tavianator.com/fast-branchless-raybounding-box-intersections/
    public class AABBIntersectionTest
    {
        BlockFacing hitOnBlockFaceTmp = BlockFacing.DOWN;
        Vec3d hitPositionTmp = new Vec3d();

        Vec3d lastExitedBlockFacePos = new Vec3d();
        public IWorldIntersectionSupplier bsTester;
        Cuboidd tmpCuboidd = new Cuboidd();
        readonly Cuboidd rotatedLocalBox = new();
        readonly double[] rotatedInverseMatrix = Mat4d.Create();
        readonly Vec4d rotatedInput = new();
        readonly Vec4d rotatedOutput = new();
        Vec3d rotatedHitPosition = new();
        readonly Ray rotatedRay = new();

        public Vec3d hitPosition = new Vec3d();
        public Ray ray = new Ray();
        public BlockPos pos = new BlockPos(Config.Dimensions.WillSetLater);
        public BlockFacing hitOnBlockFace = BlockFacing.DOWN;
        public int hitOnSelectionBox = 0;
        public string hitOnSelectionBoxId = null;




        public AABBIntersectionTest(IWorldIntersectionSupplier blockSelectionTester)
        {
            this.bsTester = blockSelectionTester;
        }

        public void LoadRayAndPos(Line3D line3d)
        {
            ray.origin.Set(line3d.Start[0], line3d.Start[1], line3d.Start[2]);
            ray.dir.Set(line3d.End[0] - line3d.Start[0],
                 line3d.End[1] - line3d.Start[1],
                 line3d.End[2] - line3d.Start[2]
            );
            pos.SetAndCorrectDimension((int)line3d.Start[0], (int)line3d.Start[1], (int)line3d.Start[2]);
        }

        public void LoadRayAndPos(Ray ray)
        {
            this.ray = ray;
            pos.SetAndCorrectDimension(ray.origin);
        }

        public BlockSelection GetSelectedBlock(Vec3d from, Vec3d to, BlockFilter filter = null)
        {
            LoadRayAndPos(new Line3D()
            {
                Start = new double[] { from.X, from.Y, from.Z },
                End = new double[] { to.X, to.Y, to.Z }
            });

            float maxDistance = from.DistanceTo(to);

            return GetSelectedBlock(maxDistance, filter);
        }

        public BlockSelection GetSelectedBlock(float maxDistance, BlockFilter filter = null, bool testCollide = false)
        {
            float distanceSq = 0;

            // Get the face where our ray will exit
            BlockFacing lastExitedBlockFace = GetExitingFullBlockFace(pos, ref lastExitedBlockFacePos);
            if (lastExitedBlockFace == null) return null;

            float maxDistanceSq = (maxDistance + 1) * (maxDistance + 1);

            // Wander along the block exiting faces until we collide with a block selection box
            while (!RayIntersectsBlockSelectionBox(pos, filter, testCollide))
            {
                if (distanceSq >= maxDistanceSq) return null;

                pos.Offset(lastExitedBlockFace);

                lastExitedBlockFace = GetExitingFullBlockFace(pos, ref lastExitedBlockFacePos);
                if (lastExitedBlockFace == null) return null;

                distanceSq = pos.DistanceSqTo(ray.origin.X - 0.5f, ray.origin.Y - 0.5f, ray.origin.Z - 0.5f);
            }


            if (hitPosition.SquareDistanceTo(ray.origin) > maxDistance * maxDistance) return null;

            return new BlockSelection()
            {
                Face = hitOnBlockFace,
                Position = pos.CopyAndCorrectDimension(),
                HitPosition = hitPosition.SubCopy(pos.X, pos.InternalY, pos.Z),
                SelectionBoxIndex = hitOnSelectionBox,
                SelectionBoxId = hitOnSelectionBoxId,
                Block = blockIntersected
            };
        }


        Block blockIntersected;
        public bool RayIntersectsBlockSelectionBox(BlockPos pos, BlockFilter filter, bool testCollide = false)
        {
            Cuboidf[] hitboxes;
            Block block = bsTester.blockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            if (block.SideSolid.Any)   // It's ice!
            {
                hitboxes = testCollide ? block.GetCollisionBoxes(bsTester.blockAccessor, pos) : block.GetSelectionBoxes(bsTester.blockAccessor, pos);
            }
            else
            {
                block = bsTester.GetBlock(pos);
                hitboxes = testCollide ? block.GetCollisionBoxes(bsTester.blockAccessor, pos) : bsTester.GetBlockIntersectionBoxes(pos);
            }
            if (hitboxes == null) return false;
            if (filter?.Invoke(pos, block) == false) return false;

            bool intersects = false;
            bool wasDecor = false;

            for (int i = 0; i < hitboxes.Length; i++)
            {
                Cuboidf hitbox = hitboxes[i];
                bool didHit;

                if (hitbox.IsRotated() && hitbox is RotatedCuboidf rotatedBox)
                {
                    didHit = RayIntersectsWithRotatedCuboid(rotatedBox, pos.X, pos.InternalY, pos.Z, ref hitOnBlockFaceTmp, ref hitPositionTmp);
                }
                else
                {
                    tmpCuboidd.Set(hitbox).Translate(pos.X, pos.InternalY, pos.Z);
                    didHit = RayIntersectsWithCuboid(tmpCuboidd, ref hitOnBlockFaceTmp, ref hitPositionTmp);
                }

                if (didHit)
                {
                    bool isDecor = hitbox is DecorSelectionBox;
                    if (intersects && (!wasDecor || isDecor) && hitPosition.SquareDistanceTo(ray.origin) <= hitPositionTmp.SquareDistanceTo(ray.origin))
                    {
                        continue;
                    }

                    hitOnSelectionBox = i;
                    hitOnSelectionBoxId = (hitbox as CuboidfWithId)?.Id;
                    intersects = true;
                    wasDecor = isDecor;
                    hitOnBlockFace = hitOnBlockFaceTmp;
                    hitPosition.Set(hitPositionTmp);
                }
            }

            if (intersects && hitboxes[hitOnSelectionBox] is DecorSelectionBox dsb)
            {
                Vec3i posAdjust = dsb.PosAdjust;
                if (posAdjust != null)
                {
                    pos.Add(posAdjust);
                    block = bsTester.GetBlock(pos);
                }
            }

            if (intersects) blockIntersected = block;
            return intersects;
        }

        public bool RayIntersectsWithCuboid(Cuboidd selectionBox)
        {
            if (selectionBox == null) return false;
            return RayIntersectsWithCuboid(selectionBox, ref hitOnBlockFace, ref hitPosition);
        }

        public bool RayIntersectsWithCuboid(Cuboidf selectionBox, double posX, double posY, double posZ)
        {
            if (selectionBox == null) return false;

            tmpCuboidd.Set(selectionBox).Translate(posX, posY, posZ);
            return RayIntersectsWithCuboid(tmpCuboidd, ref hitOnBlockFace, ref hitPosition);
        }


        private bool RayIntersectsWithRotatedCuboid(RotatedCuboidf rotatedBox, double posX, double posY, double posZ, ref BlockFacing hitOnBlockFace, ref Vec3d hitPosition)
        {
            float[] rotMat = rotatedBox.GetRotationMatrix();
            float[] invMat = Mat4f.Create();
            Mat4f.Invert(invMat, rotMat);

            float[] localOrigin = new float[4];
            float[] localDir = new float[4];
            Mat4f.MulWithVec4(invMat, new float[] { (float)(ray.origin.X - posX), (float)(ray.origin.Y - posY), (float)(ray.origin.Z - posZ), 1 }, localOrigin);
            Mat4f.MulWithVec4(invMat, new float[] { (float)ray.dir.X, (float)ray.dir.Y, (float)ray.dir.Z, 0 }, localDir);

            Cuboidd localAABB = new Cuboidd(rotatedBox.X1, rotatedBox.Y1, rotatedBox.Z1, rotatedBox.X2, rotatedBox.Y2, rotatedBox.Z2);
            Vec3d localHitPos = new Vec3d();
            BlockFacing localFace = BlockFacing.DOWN;

            Ray originalRay = ray;
            ray = new Ray(new Vec3d(localOrigin[0], localOrigin[1], localOrigin[2]), new Vec3d(localDir[0], localDir[1], localDir[2]));
            bool hit = RayIntersectsWithCuboid(localAABB, ref localFace, ref localHitPos);
            ray = originalRay;
            if (!hit) return false;

            hitPosition.Set(TransformBack(rotMat, localHitPos.X, localHitPos.Y, localHitPos.Z, posX, posY, posZ));
            hitOnBlockFace = worldFaceFromLocal(localFace, rotMat);
            return true;
        }

        private bool rayIntersectsWithRotatedCuboid(RotatedCuboidd rotatedBox, ref BlockFacing hitOnBlockFace, ref Vec3d hitPosition)
        {
            double[] rotationMatrix = rotatedBox.GetRotationMatrix();
            Mat4d.Invert(rotatedInverseMatrix, rotationMatrix);

            rotatedInput.Set(ray.origin.X, ray.origin.Y, ray.origin.Z, 1);
            Mat4d.MulWithVec4(rotatedInverseMatrix, rotatedInput, rotatedOutput);
            rotatedRay.origin.Set(rotatedOutput.X, rotatedOutput.Y, rotatedOutput.Z);

            rotatedInput.Set(ray.dir.X, ray.dir.Y, ray.dir.Z, 0);
            Mat4d.MulWithVec4(rotatedInverseMatrix, rotatedInput, rotatedOutput);
            rotatedRay.dir.Set(rotatedOutput.X, rotatedOutput.Y, rotatedOutput.Z);

            rotatedLocalBox.Set(rotatedBox.X1, rotatedBox.Y1, rotatedBox.Z1, rotatedBox.X2, rotatedBox.Y2, rotatedBox.Z2);
            Ray originalRay = ray;
            ray = rotatedRay;
            bool hit = RayIntersectsWithCuboid(rotatedLocalBox, ref hitOnBlockFace, ref rotatedHitPosition);
            ray = originalRay;
            if (!hit) return false;

            rotatedInput.Set(rotatedHitPosition.X, rotatedHitPosition.Y, rotatedHitPosition.Z, 1);
            Mat4d.MulWithVec4(rotationMatrix, rotatedInput, rotatedOutput);
            hitPosition.Set(rotatedOutput.X, rotatedOutput.Y, rotatedOutput.Z);
            hitOnBlockFace = worldFaceFromLocal(hitOnBlockFace, rotationMatrix);
            return true;
        }

        private static Vec3d TransformBack(float[] mat, double lx, double ly, double lz, double posX, double posY, double posZ)
        {
            float[] world = new float[4];
            Mat4f.MulWithVec4(mat, new float[] { (float)lx, (float)ly, (float)lz, 1 }, world);
            return new Vec3d(world[0] + posX, world[1] + posY, world[2] + posZ);
        }

        // The ray test already resolved the exact face in the box's local frame; rotate that
        // face normal into world space and snap to the nearest axis instead of guessing by distance.
        private static BlockFacing worldFaceFromLocal(BlockFacing localFace, float[] rotMat)
        {
            Vec3f n = localFace.Normalf;
            float[] worldNormal = new float[4];
            Mat4f.MulWithVec4(rotMat, new float[] { n.X, n.Y, n.Z, 0 }, worldNormal);
            return worldFaceFromNormal(worldNormal[0], worldNormal[1], worldNormal[2]);
        }

        private BlockFacing worldFaceFromLocal(BlockFacing localFace, double[] rotationMatrix)
        {
            Vec3f normal = localFace.Normalf;
            rotatedInput.Set(normal.X, normal.Y, normal.Z, 0);
            Mat4d.MulWithVec4(rotationMatrix, rotatedInput, rotatedOutput);
            return worldFaceFromNormal(rotatedOutput.X, rotatedOutput.Y, rotatedOutput.Z);
        }

        private static BlockFacing worldFaceFromNormal(double x, double y, double z)
        {
            BlockFacing best = BlockFacing.DOWN;
            double bestDot = double.MinValue;
            for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
            {
                BlockFacing face = BlockFacing.ALLFACES[i];
                Vec3f faceNormal = face.Normalf;
                double dot = x * faceNormal.X + y * faceNormal.Y + z * faceNormal.Z;
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = face;
                }
            }

            return best;
        }

        public bool RayIntersectsWithCuboid(Cuboidd selectionBox, ref BlockFacing hitOnBlockFace, ref Vec3d hitPosition)
        {
            if (selectionBox == null) return false;
            if (selectionBox is RotatedCuboidd rotatedBox && rotatedBox.IsRotated()) return rayIntersectsWithRotatedCuboid(rotatedBox, ref hitOnBlockFace, ref hitPosition);

            double w = selectionBox.X2 - selectionBox.X1;
            double h = selectionBox.Y2 - selectionBox.Y1;
            double l = selectionBox.Z2 - selectionBox.Z1;

            for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
            {
                BlockFacing blockSideFacing = BlockFacing.ALLFACES[i];
                Vec3i planeNormal = blockSideFacing.Normali;

                // Dot product of 2 vectors
                // If they are parallel the dot product is 1
                // At 90 degrees the dot product is 0
                double demon = planeNormal.X * ray.dir.X + planeNormal.Y * ray.dir.Y + planeNormal.Z * ray.dir.Z;

                // Does intersect this plane somewhere (only negative because we are not interested in the ray leaving a face, negative because the ray points into the cube, the plane normal points away from the cube)
                if (demon < -0.00001)
                {
                    FastVec3d planeCenterPosition = new FastVec3d(blockSideFacing.PlaneCenter)
                        .Mul(w, h, l)
                        .Add(selectionBox.X1, selectionBox.Y1, selectionBox.Z1)
                    ;

                    FastVec3d pt = new FastVec3d(planeCenterPosition).Sub(ray.origin);
                    double t = (pt.X * planeNormal.X + pt.Y * planeNormal.Y + pt.Z * planeNormal.Z) / demon;

                    if (t >= 0)
                    {
                        hitPosition.Set(ray.origin.X + ray.dir.X * t, ray.origin.Y + ray.dir.Y * t, ray.origin.Z + ray.dir.Z * t);
                        lastExitedBlockFacePos.Set(planeCenterPosition.ReverseSub(hitPosition));

                        // Does intersect this plane within the block
                        if (Math.Abs(lastExitedBlockFacePos.X) <= w / 2 && Math.Abs(lastExitedBlockFacePos.Y) <= h / 2 && Math.Abs(lastExitedBlockFacePos.Z) <= l / 2)
                        {
                            hitOnBlockFace = blockSideFacing;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// This will test a ray against an axis-aligned or rotated cuboid without replacing the loaded picking ray.
        /// </summary>
        public bool RayIntersectsWithCuboid(Ray testRay, Cuboidd selectionBox, ref BlockFacing hitOnBlockFace, ref Vec3d hitPosition)
        {
            Ray originalRay = ray;
            ray = testRay;
            bool intersects = RayIntersectsWithCuboid(selectionBox, ref hitOnBlockFace, ref hitPosition);
            ray = originalRay;
            return intersects;
        }



        private BlockFacing GetExitingFullBlockFace(BlockPos pos, ref Vec3d exitPos)
        {
            for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
            {
                BlockFacing blockSideFacing = BlockFacing.ALLFACES[i];
                Vec3i planeNormal = blockSideFacing.Normali;

                double demon = planeNormal.X * ray.dir.X + planeNormal.Y * ray.dir.Y + planeNormal.Z * ray.dir.Z;

                if (demon > 0.00001)
                {
                    FastVec3d planePosition = new FastVec3d(pos.X, pos.InternalY, pos.Z).Add(blockSideFacing.PlaneCenter);

                    FastVec3d pt = new FastVec3d(planePosition).Sub(ray.origin);
                    double t = (pt.X * planeNormal.X + pt.Y * planeNormal.Y + pt.Z * planeNormal.Z) / demon;

                    if (t >= 0)
                    {
                        FastVec3d pHit = new FastVec3d(ray.origin.X + ray.dir.X * t, ray.origin.Y + ray.dir.Y * t, ray.origin.Z + ray.dir.Z * t);
                        exitPos.Set(pHit.Sub(planePosition));

                        if (Math.Abs(exitPos.X) <= 0.5 && Math.Abs(exitPos.Y) <= 0.5 && Math.Abs(exitPos.Z) <= 0.5)
                        {
                            return blockSideFacing;
                        }
                    }
                }
            }

            return null;
        }

        public static bool Testintersection(Cuboidd b, Ray r, ref Vec3d hitPosition)
        {
            return Testintersection(b, r, ref hitPosition, out _);
        }

        /// <summary>
        /// Tests if a ray intersects with a cuboid. The cuboid is defined by its minimum and maximum coordinates (X1, Y1, Z1) and (X2, Y2, Z2).
        /// The ray is defined by its origin and direction. If an intersection occurs, the hit position is returned in the hitPosition parameter.
        /// </summary>
        /// <param name="b"></param>
        /// <param name="r"></param>
        /// <param name="hitPosition"></param>
        /// <returns></returns>
        public static bool Testintersection(Cuboidd b, Ray r, ref Vec3d hitPosition, out BlockFacing hitOnBlockFace)
        {
            double w = b.X2 - b.X1;
            double h = b.Y2 - b.Y1;
            double l = b.Z2 - b.Z1;

            for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
            {
                BlockFacing blockSideFacing = BlockFacing.ALLFACES[i];
                Vec3i planeNormal = blockSideFacing.Normali;

                // Dot product of 2 vectors
                // If they are parallel the dot product is 1
                // At 90 degrees the dot product is 0
                double demon = planeNormal.X * r.dir.X + planeNormal.Y * r.dir.Y + planeNormal.Z * r.dir.Z;

                // Does intersect this plane somewhere (only negative because we are not interested in the ray leaving a face,
                // negative because the ray points into the cube, the plane normal points away from the cube)
                if (demon < -0.00001)
                {
                    Vec3d planeCenterPosition = blockSideFacing.PlaneCenter
                        .ToVec3d()
                        .Mul(w, h, l)
                        .Add(b.X1, b.Y1, b.Z1)
                    ;

                    Vec3d pt = Vec3d.Sub(planeCenterPosition, r.origin);
                    double t = (pt.X * planeNormal.X + pt.Y * planeNormal.Y + pt.Z * planeNormal.Z) / demon;

                    if (t >= 0)
                    {
                        hitPosition = new Vec3d(r.origin.X + r.dir.X * t, r.origin.Y + r.dir.Y * t, r.origin.Z + r.dir.Z * t);
                        var lastExitedBlockFacePos = Vec3d.Sub(hitPosition, planeCenterPosition);

                        // Does intersect this plane within the face dimensions
                        if (Math.Abs(lastExitedBlockFacePos.X) <= w / 2 && Math.Abs(lastExitedBlockFacePos.Y) <= h / 2 && Math.Abs(lastExitedBlockFacePos.Z) <= l / 2)
                        {
                            hitOnBlockFace = blockSideFacing;
                            return true;
                        }
                    }
                }
            }

            hitOnBlockFace = null;
            return false;
        }



        public static bool TestintersectionInfiniteFace(Cuboidd b, Ray r, BlockFacing blockSideFacing, ref Vec3d hitPosition)
        {
            double w = b.X2 - b.X1;
            double h = b.Y2 - b.Y1;
            double l = b.Z2 - b.Z1;

            Vec3i planeNormal = blockSideFacing.Normali;

            // Dot product of 2 vectors
            // If they are parallel the dot product is 1
            // At 90 degrees the dot product is 0
            double demon = planeNormal.X * r.dir.X + planeNormal.Y * r.dir.Y + planeNormal.Z * r.dir.Z;

            // Does intersect this plane somewhere (only negative because we are not interested in the ray leaving a face,
            // negative because the ray points into the cube, the plane normal points away from the cube)
            if (demon < -0.00001)
            {
                Vec3d planeCenterPosition = blockSideFacing.PlaneCenter
                    .ToVec3d()
                    .Mul(w, h, l)
                    .Add(b.X1, b.Y1, b.Z1)
                ;

                Vec3d pt = Vec3d.Sub(planeCenterPosition, r.origin);
                double t = (pt.X * planeNormal.X + pt.Y * planeNormal.Y + pt.Z * planeNormal.Z) / demon;

                if (t >= 0)
                {
                    hitPosition = new Vec3d(r.origin.X + r.dir.X * t, r.origin.Y + r.dir.Y * t, r.origin.Z + r.dir.Z * t);
                    var lastExitedBlockFacePos = Vec3d.Sub(hitPosition, planeCenterPosition);
                    return true;
                }
            }

            return false;
        }



    }





    public interface IWorldIntersectionSupplier
    {
        Block GetBlock(BlockPos pos);

        Cuboidf[] GetBlockIntersectionBoxes(BlockPos pos);

        Entity[] GetEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches = null);

        bool IsValidPos(BlockPos pos);

        Vec3i MapSize { get; }

        IBlockAccessor blockAccessor { get; }
    }

}
