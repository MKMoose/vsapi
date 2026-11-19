using System;

namespace Vintagestory.API.MathTools
{
    /// <summary>
    /// The closest-axis contact between two cuboids. Separation is positive for a gap, zero for touching, and negative for penetration.
    /// </summary>
    public readonly struct CuboidContact(FastVec3d normal, double separation)
    {
        /// <summary>
        /// The unit normal pointing from the source cuboid toward the queried cuboid.
        /// </summary>
        public readonly FastVec3d Normal = normal;

        /// <summary>
        /// The distance between the cuboids along <see cref="Normal"/>, or a negative penetration depth.
        /// </summary>
        public readonly double Separation = separation;
    }

    /// <summary>
    /// A cuboid transformed by an affine matrix. Instances may be reused with <see cref="Set(Cuboidd, double[])"/>.
    /// </summary>
    public sealed class TransformedCuboidd
    {
        private const double axisEpsilon = 1e-24;
        private const double affineEpsilon = 1e-9;

        private struct ShapeData
        {
            public FastVec3d AxisX;
            public FastVec3d AxisY;
            public FastVec3d AxisZ;
            public double CenterX;
            public double CenterY;
            public double CenterZ;
            public double HalfAxisXX;
            public double HalfAxisXY;
            public double HalfAxisXZ;
            public double HalfAxisYX;
            public double HalfAxisYY;
            public double HalfAxisYZ;
            public double HalfAxisZX;
            public double HalfAxisZY;
            public double HalfAxisZZ;

            public void SetAabb(Cuboidd bounds)
            {
                SetAabb(bounds.X1, bounds.Y1, bounds.Z1, bounds.X2, bounds.Y2, bounds.Z2);
            }

            public void SetAabb(double x1, double y1, double z1, double x2, double y2, double z2)
            {
                Set(
                    x1 * 0.5 + x2 * 0.5,
                    y1 * 0.5 + y2 * 0.5,
                    z1 * 0.5 + z2 * 0.5,
                    x2 * 0.5 - x1 * 0.5, 0, 0,
                    0, y2 * 0.5 - y1 * 0.5, 0,
                    0, 0, z2 * 0.5 - z1 * 0.5
                );
            }

            public bool HasFiniteBounds()
            {
                double halfExtentX = Math.Abs(HalfAxisXX) + Math.Abs(HalfAxisYX) + Math.Abs(HalfAxisZX);
                double halfExtentY = Math.Abs(HalfAxisXY) + Math.Abs(HalfAxisYY) + Math.Abs(HalfAxisZY);
                double halfExtentZ = Math.Abs(HalfAxisXZ) + Math.Abs(HalfAxisYZ) + Math.Abs(HalfAxisZZ);
                return double.IsFinite(CenterX - halfExtentX) && double.IsFinite(CenterX + halfExtentX)
                    && double.IsFinite(CenterY - halfExtentY) && double.IsFinite(CenterY + halfExtentY)
                    && double.IsFinite(CenterZ - halfExtentZ) && double.IsFinite(CenterZ + halfExtentZ);
            }

            public void Set(double centerX, double centerY, double centerZ, double halfAxisXX, double halfAxisXY, double halfAxisXZ, double halfAxisYX, double halfAxisYY, double halfAxisYZ, double halfAxisZX, double halfAxisZY, double halfAxisZZ)
            {
                CenterX = centerX;
                CenterY = centerY;
                CenterZ = centerZ;
                HalfAxisXX = halfAxisXX;
                HalfAxisXY = halfAxisXY;
                HalfAxisXZ = halfAxisXZ;
                HalfAxisYX = halfAxisYX;
                HalfAxisYY = halfAxisYY;
                HalfAxisYZ = halfAxisYZ;
                HalfAxisZX = halfAxisZX;
                HalfAxisZY = halfAxisZY;
                HalfAxisZZ = halfAxisZZ;
                AxisX = getDirection(halfAxisXX, halfAxisXY, halfAxisXZ);
                AxisY = getDirection(halfAxisYX, halfAxisYY, halfAxisYZ);
                AxisZ = getDirection(halfAxisZX, halfAxisZY, halfAxisZZ);

                // Retain a complete separating basis for flat boxes, lines, and point queries.
                if (AxisX.Equals(default(FastVec3d)))
                {
                    AxisX = getCrossDirection(AxisY, AxisZ);
                    if (AxisX.Equals(default(FastVec3d))) AxisX = getPerpendicular(AxisY.Equals(default(FastVec3d)) ? AxisZ : AxisY);
                }
                if (AxisY.Equals(default(FastVec3d)))
                {
                    AxisY = getCrossDirection(AxisZ, AxisX);
                    if (AxisY.Equals(default(FastVec3d))) AxisY = getPerpendicular(AxisX);
                }
                if (AxisZ.Equals(default(FastVec3d))) AxisZ = getCrossDirection(AxisX, AxisY);
            }

            private static FastVec3d getDirection(double x, double y, double z)
            {
                if (y == 0 && z == 0) return new FastVec3d(x > 0 ? 1 : x < 0 ? -1 : 0, 0, 0);
                if (x == 0 && z == 0) return new FastVec3d(0, y > 0 ? 1 : y < 0 ? -1 : 0, 0);
                if (x == 0 && y == 0) return new FastVec3d(0, 0, z > 0 ? 1 : z < 0 ? -1 : 0);
                double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
                return new FastVec3d(x / scale, y / scale, z / scale).NormalizedCopy();
            }

            private static FastVec3d getCrossDirection(FastVec3d first, FastVec3d second)
            {
                return getDirection(first.Y * second.Z - first.Z * second.Y, first.Z * second.X - first.X * second.Z, first.X * second.Y - first.Y * second.X);
            }

            private static FastVec3d getPerpendicular(FastVec3d axis)
            {
                if (axis.Equals(default(FastVec3d))) return new FastVec3d(1, 0, 0);
                return Math.Abs(axis.X) > Math.Abs(axis.Z) ? getDirection(-axis.Y, axis.X, 0) : getDirection(0, -axis.Z, axis.Y);
            }
        }

        private ShapeData shape;

        public double BoundsX1 { get; private set; }
        public double BoundsY1 { get; private set; }
        public double BoundsZ1 { get; private set; }
        public double BoundsX2 { get; private set; }
        public double BoundsY2 { get; private set; }
        public double BoundsZ2 { get; private set; }

        public TransformedCuboidd()
        {
            shape.SetAabb(0, 0, 0, 0, 0, 0);
        }

        public TransformedCuboidd(Cuboidd bounds)
        {
            Set(bounds);
        }

        public TransformedCuboidd(Cuboidd localBounds, double[] transform)
        {
            Set(localBounds, transform);
        }

        /// <summary>
        /// Sets this shape to an axis-aligned cuboid.
        /// </summary>
        public TransformedCuboidd Set(Cuboidd bounds)
        {
            validateBounds(bounds);
            ShapeData newShape = new();
            newShape.SetAabb(bounds);
            if (!newShape.HasFiniteBounds()) throw new ArgumentException("The cuboid must have finite bounds.", nameof(bounds));

            shape = newShape;
            setBounds();
            return this;
        }

        /// <summary>
        /// Transforms local cuboid bounds by a column-major affine matrix.
        /// </summary>
        public TransformedCuboidd Set(Cuboidd localBounds, double[] transform)
        {
            validateBounds(localBounds);
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            if (transform.Length < 16) throw new ArgumentException("A cuboid transform requires sixteen matrix values.", nameof(transform));
            if (!isAffineTransform(transform)) throw new ArgumentException("A cuboid transform must be finite and affine.", nameof(transform));

            double localCenterX = localBounds.MidX;
            double localCenterY = localBounds.MidY;
            double localCenterZ = localBounds.MidZ;
            double halfSizeX = localBounds.HalfExtentX;
            double halfSizeY = localBounds.HalfExtentY;
            double halfSizeZ = localBounds.HalfExtentZ;

            ShapeData newShape = new();
            newShape.Set(
                transform[0] * localCenterX + transform[4] * localCenterY + transform[8] * localCenterZ + transform[12],
                transform[1] * localCenterX + transform[5] * localCenterY + transform[9] * localCenterZ + transform[13],
                transform[2] * localCenterX + transform[6] * localCenterY + transform[10] * localCenterZ + transform[14],
                transform[0] * halfSizeX, transform[1] * halfSizeX, transform[2] * halfSizeX,
                transform[4] * halfSizeY, transform[5] * halfSizeY, transform[6] * halfSizeY,
                transform[8] * halfSizeZ, transform[9] * halfSizeZ, transform[10] * halfSizeZ
            );
            if (!newShape.HasFiniteBounds()) throw new ArgumentException("The transformed cuboid must have finite bounds.", nameof(transform));

            shape = newShape;
            setBounds();
            return this;
        }

        /// <summary>
        /// Sets this shape from its center and three transformed half-axes.
        /// </summary>
        public TransformedCuboidd Set(Vec3d center, Vec3d halfAxisX, Vec3d halfAxisY, Vec3d halfAxisZ)
        {
            if (center == null) throw new ArgumentNullException(nameof(center));
            if (halfAxisX == null) throw new ArgumentNullException(nameof(halfAxisX));
            if (halfAxisY == null) throw new ArgumentNullException(nameof(halfAxisY));
            if (halfAxisZ == null) throw new ArgumentNullException(nameof(halfAxisZ));

            ShapeData newShape = new();
            newShape.Set(
                center.X, center.Y, center.Z,
                halfAxisX.X, halfAxisX.Y, halfAxisX.Z,
                halfAxisY.X, halfAxisY.Y, halfAxisY.Z,
                halfAxisZ.X, halfAxisZ.Y, halfAxisZ.Z
            );
            if (!newShape.HasFiniteBounds()) throw new ArgumentException("The transformed cuboid must have finite bounds.");

            shape = newShape;
            setBounds();
            return this;
        }

        /// <summary>
        /// Copies this shape into a new independently reusable instance.
        /// </summary>
        public TransformedCuboidd Clone()
        {
            var clone = new TransformedCuboidd { shape = shape };
            clone.setBounds();
            return clone;
        }

        /// <summary>
        /// Converts this orthogonal transformed cuboid to a rotated cuboid with the same center and half-axes.
        /// </summary>
        public RotatedCuboidd ToRotatedCuboidd()
        {
            double halfSizeX = Math.Sqrt(shape.HalfAxisXX * shape.HalfAxisXX + shape.HalfAxisXY * shape.HalfAxisXY + shape.HalfAxisXZ * shape.HalfAxisXZ);
            double halfSizeY = Math.Sqrt(shape.HalfAxisYX * shape.HalfAxisYX + shape.HalfAxisYY * shape.HalfAxisYY + shape.HalfAxisYZ * shape.HalfAxisYZ);
            double halfSizeZ = Math.Sqrt(shape.HalfAxisZX * shape.HalfAxisZX + shape.HalfAxisZY * shape.HalfAxisZY + shape.HalfAxisZZ * shape.HalfAxisZZ);
            if (halfSizeX <= axisEpsilon || halfSizeY <= axisEpsilon || halfSizeZ <= axisEpsilon)
            {
                throw new InvalidOperationException("A rotated cuboid requires three nonzero half-axes.");
            }

            double axisXX = shape.HalfAxisXX / halfSizeX;
            double axisXY = shape.HalfAxisXY / halfSizeX;
            double axisXZ = shape.HalfAxisXZ / halfSizeX;
            double axisYX = shape.HalfAxisYX / halfSizeY;
            double axisYY = shape.HalfAxisYY / halfSizeY;
            double axisYZ = shape.HalfAxisYZ / halfSizeY;
            double axisZX = shape.HalfAxisZX / halfSizeZ;
            double axisZY = shape.HalfAxisZY / halfSizeZ;
            double axisZZ = shape.HalfAxisZZ / halfSizeZ;

            const double orthogonalEpsilon = 1e-6;
            if (Math.Abs(axisXX * axisYX + axisXY * axisYY + axisXZ * axisYZ) > orthogonalEpsilon
                || Math.Abs(axisXX * axisZX + axisXY * axisZY + axisXZ * axisZZ) > orthogonalEpsilon
                || Math.Abs(axisYX * axisZX + axisYY * axisZY + axisYZ * axisZZ) > orthogonalEpsilon)
            {
                throw new InvalidOperationException("A sheared cuboid cannot be represented as a rotated cuboid.");
            }

            double determinant = axisXX * (axisYY * axisZZ - axisYZ * axisZY)
                - axisYX * (axisXY * axisZZ - axisXZ * axisZY)
                + axisZX * (axisXY * axisYZ - axisXZ * axisYY);
            if (determinant < 0)
            {
                axisZX = -axisZX;
                axisZY = -axisZY;
                axisZZ = -axisZZ;
            }

            double[] rotationMatrix =
            [
                axisXX, axisYX, axisZX,
                axisXY, axisYY, axisZY,
                axisXZ, axisYZ, axisZZ
            ];
            double[] quaternion = Quaterniond.Create();
            Quaterniond.Normalize(quaternion, Quaterniond.FromMat3(quaternion, rotationMatrix));
            var center = new Vec3d(shape.CenterX, shape.CenterY, shape.CenterZ);
            return new RotatedCuboidd(new Cuboidd(
                shape.CenterX - halfSizeX,
                shape.CenterY - halfSizeY,
                shape.CenterZ - halfSizeZ,
                shape.CenterX + halfSizeX,
                shape.CenterY + halfSizeY,
                shape.CenterZ + halfSizeZ
            ), center, quaternion);
        }

        /// <summary>
        /// Writes the transformed center into <paramref name="result"/>.
        /// </summary>
        public Vec3d GetCenter(Vec3d result)
        {
            return result.Set(shape.CenterX, shape.CenterY, shape.CenterZ);
        }

        /// <summary>
        /// Writes the transformed half-axis corresponding to the local X axis.
        /// </summary>
        public Vec3d GetHalfAxisX(Vec3d result)
        {
            return result.Set(shape.HalfAxisXX, shape.HalfAxisXY, shape.HalfAxisXZ);
        }

        /// <summary>
        /// Writes the transformed half-axis corresponding to the local Y axis.
        /// </summary>
        public Vec3d GetHalfAxisY(Vec3d result)
        {
            return result.Set(shape.HalfAxisYX, shape.HalfAxisYY, shape.HalfAxisYZ);
        }

        /// <summary>
        /// Writes the transformed half-axis corresponding to the local Z axis.
        /// </summary>
        public Vec3d GetHalfAxisZ(Vec3d result)
        {
            return result.Set(shape.HalfAxisZX, shape.HalfAxisZY, shape.HalfAxisZZ);
        }

        /// <summary>
        /// Writes the conservative axis-aligned bounds into <paramref name="result"/>.
        /// </summary>
        public Cuboidd GetAABB(Cuboidd result)
        {
            return result.Set(BoundsX1, BoundsY1, BoundsZ1, BoundsX2, BoundsY2, BoundsZ2);
        }

        public bool Intersects(Cuboidd other)
        {
            return Intersects(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2);
        }

        public bool IntersectsOrTouches(Cuboidd other)
        {
            return IntersectsOrTouches(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2);
        }

        public bool Intersects(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            if (!boundsIntersect(x1, y1, z1, x2, y2, z2, false)) return false;
            ShapeData otherShape = new();
            otherShape.SetAabb(x1, y1, z1, x2, y2, z2);
            return intersects(otherShape, false);
        }

        public bool IntersectsOrTouches(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            if (!boundsIntersect(x1, y1, z1, x2, y2, z2, true)) return false;
            ShapeData otherShape = new();
            otherShape.SetAabb(x1, y1, z1, x2, y2, z2);
            return intersects(otherShape, true);
        }

        public bool Intersects(TransformedCuboidd other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return boundsIntersect(other, false) && intersects(other.shape, false);
        }

        public bool IntersectsOrTouches(TransformedCuboidd other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return boundsIntersect(other, true) && intersects(other.shape, true);
        }

        /// <summary>
        /// Returns an edge segment contained in both boxes, including touching and containment.
        /// These endpoints describe the current overlap and not the path or time of an impact.
        /// </summary>
        public bool TryGetIntersectionSegment(TransformedCuboidd other, out FastVec3d start, out FastVec3d end)
        {
            start = end = default;
            if (!IntersectsOrTouches(other)) return false;
            return tryGetIntersectingEdge(other, out start, out end) || other.tryGetIntersectingEdge(this, out start, out end);
        }

        private bool tryGetIntersectingEdge(TransformedCuboidd other, out FastVec3d start, out FastVec3d end)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                FastVec3d origin = getCorner(corner);
                FastVec3d offset = origin - new FastVec3d(other.shape.CenterX, other.shape.CenterY, other.shape.CenterZ);
                for (int axis = 1; axis <= 4; axis <<= 1)
                {
                    if ((corner & axis) != 0) continue;
                    FastVec3d displacement = getCorner(corner | axis) - origin;
                    double enter = 0, exit = 1;
                    if (!other.clipEdgeOnCross(other.shape.AxisY, other.shape.AxisZ, offset, displacement, ref enter, ref exit)
                        || !other.clipEdgeOnCross(other.shape.AxisZ, other.shape.AxisX, offset, displacement, ref enter, ref exit)
                        || !other.clipEdgeOnCross(other.shape.AxisX, other.shape.AxisY, offset, displacement, ref enter, ref exit))
                    {
                        continue;
                    }

                    start = origin + displacement * enter;
                    end = origin + displacement * exit;
                    return true;
                }
            }

            start = end = default;
            return false;
        }

        private bool clipEdgeOnCross(FastVec3d first, FastVec3d second, FastVec3d offset, FastVec3d displacement, ref double enter, ref double exit)
        {
            double axisX = first.Y * second.Z - first.Z * second.Y;
            double axisY = first.Z * second.X - first.X * second.Z;
            double axisZ = first.X * second.Y - first.Y * second.X;
            if (axisX * axisX + axisY * axisY + axisZ * axisZ <= axisEpsilon) return true;

            double radius = projectedRadius(axisX, axisY, axisZ, in shape);
            double distance = offset.X * axisX + offset.Y * axisY + offset.Z * axisZ;
            double velocity = displacement.X * axisX + displacement.Y * axisY + displacement.Z * axisZ;
            if (velocity == 0) return Math.Abs(distance) <= radius;

            double firstFraction = (-radius - distance) / velocity;
            double secondFraction = (radius - distance) / velocity;
            enter = Math.Max(enter, Math.Min(firstFraction, secondFraction));
            exit = Math.Min(exit, Math.Max(firstFraction, secondFraction));
            return enter <= exit;
        }

        private FastVec3d getCorner(int index)
        {
            double x = (index & 1) == 0 ? -1 : 1;
            double y = (index & 2) == 0 ? -1 : 1;
            double z = (index & 4) == 0 ? -1 : 1;
            return new FastVec3d(
                shape.CenterX + x * shape.HalfAxisXX + y * shape.HalfAxisYX + z * shape.HalfAxisZX,
                shape.CenterY + x * shape.HalfAxisXY + y * shape.HalfAxisYY + z * shape.HalfAxisZY,
                shape.CenterZ + x * shape.HalfAxisXZ + y * shape.HalfAxisYZ + z * shape.HalfAxisZZ
            );
        }

        /// <summary>
        /// Gets the shortest translation which moves an overlapping axis-aligned cuboid out of this shape.
        /// </summary>
        public bool TryGetAABBPenetration(Cuboidd other, out double translationX, out double translationY, out double translationZ)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (!boundsIntersect(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, false))
            {
                translationX = 0;
                translationY = 0;
                translationZ = 0;
                return false;
            }

            ShapeData otherShape = new();
            otherShape.SetAabb(other);
            return tryGetPenetration(in otherShape, out translationX, out translationY, out translationZ);
        }

        /// <summary>
        /// Gets the shortest translation which moves another overlapping transformed cuboid out of this shape.
        /// </summary>
        public bool TryGetPenetration(TransformedCuboidd other, out double translationX, out double translationY, out double translationZ)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (!boundsIntersect(other, false))
            {
                translationX = 0;
                translationY = 0;
                translationZ = 0;
                return false;
            }

            return tryGetPenetration(in other.shape, out translationX, out translationY, out translationZ);
        }

        /// <summary>
        /// Gets the outward contact normal and signed separation from this cuboid to a nearby axis-aligned cuboid.
        /// </summary>
        public bool TryGetAABBContact(Cuboidd other, double maxSeparation, out CuboidContact contact)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (!double.IsFinite(maxSeparation) || maxSeparation < 0) throw new ArgumentOutOfRangeException(nameof(maxSeparation), "The maximum separation must be finite and nonnegative.");
            if (!boundsWithinSeparation(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, maxSeparation))
            {
                contact = default;
                return false;
            }

            ShapeData otherShape = new();
            otherShape.SetAabb(other);
            return tryGetContact(in otherShape, maxSeparation, out contact);
        }

        /// <summary>
        /// Gets the outward contact normal and signed separation from this cuboid to another nearby transformed cuboid.
        /// </summary>
        public bool TryGetContact(TransformedCuboidd other, double maxSeparation, out CuboidContact contact)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (!double.IsFinite(maxSeparation) || maxSeparation < 0) throw new ArgumentOutOfRangeException(nameof(maxSeparation), "The maximum separation must be finite and nonnegative.");
            if (!boundsWithinSeparation(other, maxSeparation))
            {
                contact = default;
                return false;
            }

            return tryGetContact(in other.shape, maxSeparation, out contact);
        }

        /// <summary>
        /// Finds the first contact while an axis-aligned cuboid translates through this shape.
        /// An initial strict overlap is left to the caller's depenetration path and returns false.
        /// On success, <paramref name="collisionFraction"/> is at least zero and less than one. Otherwise, it is one.
        /// </summary>
        public bool TrySweepAABB(Cuboidd movingBox, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            if (movingBox == null) throw new ArgumentNullException(nameof(movingBox));
            return trySweepAABB(movingBox.X1, movingBox.Y1, movingBox.Z1, movingBox.X2, movingBox.Y2, movingBox.Z2, motionX, motionY, motionZ, out collisionFraction);
        }

        /// <summary>
        /// Finds the first contact while an axis-aligned float cuboid translates through this shape.
        /// An initial strict overlap is left to the caller's depenetration path and returns false.
        /// On success <paramref name="collisionFraction"/> is at least zero and less than one. Otherwise, it is one.
        /// </summary>
        public bool TrySweepAABB(Cuboidf movingBox, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            if (movingBox == null) throw new ArgumentNullException(nameof(movingBox));
            return trySweepAABB(movingBox.X1, movingBox.Y1, movingBox.Z1, movingBox.X2, movingBox.Y2, movingBox.Z2, motionX, motionY, motionZ, out collisionFraction);
        }

        private bool trySweepAABB(double x1, double y1, double z1, double x2, double y2, double z2, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            ShapeData movingShape = new();
            movingShape.SetAabb(x1, y1, z1, x2, y2, z2);
            return trySweep(in movingShape, motionX, motionY, motionZ, out collisionFraction);
        }

        /// <summary>
        /// Finds the first contact while another transformed cuboid translates through this shape without rotating.
        /// An initial strict overlap is left to the caller's depenetration path and returns false.
        /// On success, <paramref name="collisionFraction"/> is at least zero and less than one. Otherwise, it is one.
        /// </summary>
        public bool TrySweep(TransformedCuboidd movingShape, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            if (movingShape == null) throw new ArgumentNullException(nameof(movingShape));
            return trySweep(in movingShape.shape, motionX, motionY, motionZ, out collisionFraction);
        }

        /// <summary>
        /// Returns continuous translation contact, including the endpoint and optionally initial strict overlap.
        /// The normal points from this cuboid toward the moving cuboid.
        /// Touching motion away from or along a surface is ignored.
        /// </summary>
        public bool TrySweepContact(TransformedCuboidd movingBox, double motionX, double motionY, double motionZ, out double collisionFraction, out FastVec3d normal, out bool startedOverlapping, bool includeInitialOverlap = true)
        {
            if (movingBox == null) throw new ArgumentNullException(nameof(movingBox));
            return trySweep(in movingBox.shape, motionX, motionY, motionZ, true, includeInitialOverlap, out collisionFraction, out normal, out startedOverlapping);
        }

        private bool trySweep(in ShapeData movingShape, double motionX, double motionY, double motionZ, out double collisionFraction)
        {
            return trySweep(in movingShape, motionX, motionY, motionZ, false, false, out collisionFraction, out _, out _);
        }

        private bool trySweep(in ShapeData movingShape, double motionX, double motionY, double motionZ, bool includeEndpoint, bool includeInitialOverlap, out double collisionFraction, out FastVec3d normal, out bool startedOverlapping)
        {
            if (!double.IsFinite(motionX) || !double.IsFinite(motionY) || !double.IsFinite(motionZ)) throw new ArgumentException("Sweep motion must be finite.");
            collisionFraction = 1;
            normal = default;
            startedOverlapping = false;
            if (!sweepBoundsIntersect(in movingShape, motionX, motionY, motionZ)) return false;
            if (intersects(in movingShape, false))
            {
                if (!includeInitialOverlap) return false;
                if (!tryGetContact(in movingShape, 0, out CuboidContact contact)) return false;
                collisionFraction = 0;
                normal = contact.Normal;
                startedOverlapping = true;
                return true;
            }
            if (motionX == 0 && motionY == 0 && motionZ == 0) return false;

            double enter = 0;
            double exit = 1;
            FastVec3d enterNormal = default;

            if (!updateSweepFaceAxes(in shape, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref enterNormal)) return false;
            if (!updateSweepFaceAxes(in movingShape, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref enterNormal)) return false;
            if (!updateSweepCrossAxes(shape.AxisX.X, shape.AxisX.Y, shape.AxisX.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref enterNormal)) return false;
            if (!updateSweepCrossAxes(shape.AxisY.X, shape.AxisY.Y, shape.AxisY.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref enterNormal)) return false;
            if (!updateSweepCrossAxes(shape.AxisZ.X, shape.AxisZ.Y, shape.AxisZ.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref enterNormal)) return false;

            if ((!includeEndpoint && enter >= 1) || exit <= 0 || enter > exit) return false;
            collisionFraction = enter;
            normal = enterNormal.NormalizedCopy();
            return true;
        }

        private bool sweepBoundsIntersect(in ShapeData movingShape, double motionX, double motionY, double motionZ)
        {
            double halfExtentX = Math.Abs(movingShape.HalfAxisXX) + Math.Abs(movingShape.HalfAxisYX) + Math.Abs(movingShape.HalfAxisZX);
            double halfExtentY = Math.Abs(movingShape.HalfAxisXY) + Math.Abs(movingShape.HalfAxisYY) + Math.Abs(movingShape.HalfAxisZY);
            double halfExtentZ = Math.Abs(movingShape.HalfAxisXZ) + Math.Abs(movingShape.HalfAxisYZ) + Math.Abs(movingShape.HalfAxisZZ);
            double x1 = movingShape.CenterX - halfExtentX;
            double y1 = movingShape.CenterY - halfExtentY;
            double z1 = movingShape.CenterZ - halfExtentZ;
            double x2 = movingShape.CenterX + halfExtentX;
            double y2 = movingShape.CenterY + halfExtentY;
            double z2 = movingShape.CenterZ + halfExtentZ;
            return Math.Max(x2, x2 + motionX) >= BoundsX1 && Math.Min(x1, x1 + motionX) <= BoundsX2
                && Math.Max(y2, y2 + motionY) >= BoundsY1 && Math.Min(y1, y1 + motionY) <= BoundsY2
                && Math.Max(z2, z2 + motionZ) >= BoundsZ1 && Math.Min(z1, z1 + motionZ) <= BoundsZ2;
        }

        private void setBounds()
        {
            double halfExtentX = Math.Abs(shape.HalfAxisXX) + Math.Abs(shape.HalfAxisYX) + Math.Abs(shape.HalfAxisZX);
            double halfExtentY = Math.Abs(shape.HalfAxisXY) + Math.Abs(shape.HalfAxisYY) + Math.Abs(shape.HalfAxisZY);
            double halfExtentZ = Math.Abs(shape.HalfAxisXZ) + Math.Abs(shape.HalfAxisYZ) + Math.Abs(shape.HalfAxisZZ);
            BoundsX1 = shape.CenterX - halfExtentX;
            BoundsY1 = shape.CenterY - halfExtentY;
            BoundsZ1 = shape.CenterZ - halfExtentZ;
            BoundsX2 = shape.CenterX + halfExtentX;
            BoundsY2 = shape.CenterY + halfExtentY;
            BoundsZ2 = shape.CenterZ + halfExtentZ;
        }

        private bool intersects(in ShapeData other, bool alsoCheckTouch)
        {
            if (isSeparatedByFaceAxes(in shape, in other, alsoCheckTouch)) return false;
            if (isSeparatedByFaceAxes(in other, in other, alsoCheckTouch)) return false;
            if (isSeparatedByCrossAxes(shape.AxisX.X, shape.AxisX.Y, shape.AxisX.Z, in other, alsoCheckTouch)) return false;
            if (isSeparatedByCrossAxes(shape.AxisY.X, shape.AxisY.Y, shape.AxisY.Z, in other, alsoCheckTouch)) return false;
            return !isSeparatedByCrossAxes(shape.AxisZ.X, shape.AxisZ.Y, shape.AxisZ.Z, in other, alsoCheckTouch);
        }

        private bool tryGetPenetration(in ShapeData other, out double translationX, out double translationY, out double translationZ)
        {
            double bestDepth = double.MaxValue;
            translationX = 0;
            translationY = 0;
            translationZ = 0;

            if (!updatePenetrationFaceAxes(in shape, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)) return false;
            if (!updatePenetrationFaceAxes(in other, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)) return false;
            if (!updatePenetrationCrossAxes(shape.AxisX.X, shape.AxisX.Y, shape.AxisX.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)) return false;
            if (!updatePenetrationCrossAxes(shape.AxisY.X, shape.AxisY.Y, shape.AxisY.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)) return false;
            if (!updatePenetrationCrossAxes(shape.AxisZ.X, shape.AxisZ.Y, shape.AxisZ.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)) return false;
            return bestDepth < double.MaxValue;
        }

        private bool tryGetContact(in ShapeData other, double maxSeparation, out CuboidContact contact)
        {
            double bestSeparation = double.NegativeInfinity;
            double normalX = 0;
            double normalY = 0;
            double normalZ = 0;

            if (!updateContactFaceAxes(in shape, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                || !updateContactFaceAxes(in other, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                || !updateContactCrossAxes(shape.AxisX.X, shape.AxisX.Y, shape.AxisX.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                || !updateContactCrossAxes(shape.AxisY.X, shape.AxisY.Y, shape.AxisY.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                || !updateContactCrossAxes(shape.AxisZ.X, shape.AxisZ.Y, shape.AxisZ.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ))
            {
                contact = default;
                return false;
            }

            contact = new CuboidContact(new FastVec3d(normalX, normalY, normalZ), bestSeparation);
            return true;
        }

        private bool updateContactFaceAxes(in ShapeData axes, in ShapeData other, double maxSeparation, ref double bestSeparation, ref double normalX, ref double normalY, ref double normalZ)
        {
            return updateContactForCross(axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                && updateContactForCross(axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                && updateContactForCross(axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ);
        }

        private bool updateContactCrossAxes(double halfAxisX, double halfAxisY, double halfAxisZ, in ShapeData other, double maxSeparation, ref double bestSeparation, ref double normalX, ref double normalY, ref double normalZ)
        {
            return updateContactForCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisX.X, other.AxisX.Y, other.AxisX.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                && updateContactForCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisY.X, other.AxisY.Y, other.AxisY.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ)
                && updateContactForCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisZ.X, other.AxisZ.Y, other.AxisZ.Z, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ);
        }

        private bool updateContactForCross(double firstX, double firstY, double firstZ, double secondX, double secondY, double secondZ, in ShapeData other, double maxSeparation, ref double bestSeparation, ref double normalX, ref double normalY, ref double normalZ)
        {
            double axisX = firstY * secondZ - firstZ * secondY;
            double axisY = firstZ * secondX - firstX * secondZ;
            double axisZ = firstX * secondY - firstY * secondX;
            return updateContactOnAxis(axisX, axisY, axisZ, in other, maxSeparation, ref bestSeparation, ref normalX, ref normalY, ref normalZ);
        }

        private bool updateContactOnAxis(double axisX, double axisY, double axisZ, in ShapeData other, double maxSeparation, ref double bestSeparation, ref double normalX, ref double normalY, ref double normalZ)
        {
            double axisLengthSq = axisX * axisX + axisY * axisY + axisZ * axisZ;
            if (axisLengthSq <= axisEpsilon) return true;

            double axisLength = Math.Sqrt(axisLengthSq);
            double centerOffset = (other.CenterX - shape.CenterX) * axisX + (other.CenterY - shape.CenterY) * axisY + (other.CenterZ - shape.CenterZ) * axisZ;
            double combinedRadius = projectedRadius(axisX, axisY, axisZ, in shape) + projectedRadius(axisX, axisY, axisZ, in other);
            double separation = (Math.Abs(centerOffset) - combinedRadius) / axisLength;
            if (separation > maxSeparation) return false;
            if (separation <= bestSeparation) return true;

            double scale = (centerOffset < 0 ? -1 : 1) / axisLength;
            bestSeparation = separation;
            normalX = axisX * scale;
            normalY = axisY * scale;
            normalZ = axisZ * scale;
            return true;
        }

        private bool updatePenetrationFaceAxes(in ShapeData axes, in ShapeData other, ref double bestDepth, ref double translationX, ref double translationY, ref double translationZ)
        {
            return updatePenetrationForCross(axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)
                && updatePenetrationForCross(axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)
                && updatePenetrationForCross(axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ);
        }

        private bool updatePenetrationCrossAxes(double halfAxisX, double halfAxisY, double halfAxisZ, in ShapeData other, ref double bestDepth, ref double translationX, ref double translationY, ref double translationZ)
        {
            return updatePenetrationForCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisX.X, other.AxisX.Y, other.AxisX.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)
                && updatePenetrationForCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisY.X, other.AxisY.Y, other.AxisY.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ)
                && updatePenetrationForCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisZ.X, other.AxisZ.Y, other.AxisZ.Z, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ);
        }

        private bool updatePenetrationForCross(double firstX, double firstY, double firstZ, double secondX, double secondY, double secondZ, in ShapeData other, ref double bestDepth, ref double translationX, ref double translationY, ref double translationZ)
        {
            double axisX = firstY * secondZ - firstZ * secondY;
            double axisY = firstZ * secondX - firstX * secondZ;
            double axisZ = firstX * secondY - firstY * secondX;
            return updatePenetrationOnAxis(axisX, axisY, axisZ, in other, ref bestDepth, ref translationX, ref translationY, ref translationZ);
        }

        private bool updatePenetrationOnAxis(double axisX, double axisY, double axisZ, in ShapeData other, ref double bestDepth, ref double translationX, ref double translationY, ref double translationZ)
        {
            double axisLengthSq = axisX * axisX + axisY * axisY + axisZ * axisZ;
            if (axisLengthSq <= axisEpsilon) return true;

            double centerOffset = (other.CenterX - shape.CenterX) * axisX + (other.CenterY - shape.CenterY) * axisY + (other.CenterZ - shape.CenterZ) * axisZ;
            double overlap = projectedRadius(axisX, axisY, axisZ, in shape) + projectedRadius(axisX, axisY, axisZ, in other) - Math.Abs(centerOffset);
            if (overlap <= 0) return false;

            double axisLength = Math.Sqrt(axisLengthSq);
            double depth = overlap / axisLength;
            if (depth >= bestDepth) return true;

            double scale = (centerOffset < 0 ? -1 : 1) * depth / axisLength;
            bestDepth = depth;
            translationX = axisX * scale;
            translationY = axisY * scale;
            translationZ = axisZ * scale;
            return true;
        }

        private bool isSeparatedByFaceAxes(in ShapeData axes, in ShapeData other, bool alsoCheckTouch)
        {
            return isSeparatedByCross(axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, in other, alsoCheckTouch)
                || isSeparatedByCross(axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, in other, alsoCheckTouch)
                || isSeparatedByCross(axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, in other, alsoCheckTouch);
        }

        private bool isSeparatedByCrossAxes(double halfAxisX, double halfAxisY, double halfAxisZ, in ShapeData other, bool alsoCheckTouch)
        {
            return isSeparatedByCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisX.X, other.AxisX.Y, other.AxisX.Z, in other, alsoCheckTouch)
                || isSeparatedByCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisY.X, other.AxisY.Y, other.AxisY.Z, in other, alsoCheckTouch)
                || isSeparatedByCross(halfAxisX, halfAxisY, halfAxisZ, other.AxisZ.X, other.AxisZ.Y, other.AxisZ.Z, in other, alsoCheckTouch);
        }

        private bool isSeparatedByCross(double firstX, double firstY, double firstZ, double secondX, double secondY, double secondZ, in ShapeData other, bool alsoCheckTouch)
        {
            double axisX = firstY * secondZ - firstZ * secondY;
            double axisY = firstZ * secondX - firstX * secondZ;
            double axisZ = firstX * secondY - firstY * secondX;
            return isSeparatedOnAxis(axisX, axisY, axisZ, in other, alsoCheckTouch);
        }

        private bool isSeparatedOnAxis(double axisX, double axisY, double axisZ, in ShapeData other, bool alsoCheckTouch)
        {
            if (axisX * axisX + axisY * axisY + axisZ * axisZ <= axisEpsilon) return false;

            double centerDistance = Math.Abs((shape.CenterX - other.CenterX) * axisX + (shape.CenterY - other.CenterY) * axisY + (shape.CenterZ - other.CenterZ) * axisZ);
            double combinedRadius = projectedRadius(axisX, axisY, axisZ, in shape) + projectedRadius(axisX, axisY, axisZ, in other);
            return alsoCheckTouch ? centerDistance > combinedRadius : centerDistance >= combinedRadius;
        }

        private bool updateSweepCrossAxes(double halfAxisX, double halfAxisY, double halfAxisZ, in ShapeData movingShape, double motionX, double motionY, double motionZ, ref double enter, ref double exit, ref FastVec3d normal)
        {
            return updateSweepForCross(halfAxisX, halfAxisY, halfAxisZ, movingShape.AxisX.X, movingShape.AxisX.Y, movingShape.AxisX.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref normal)
                && updateSweepForCross(halfAxisX, halfAxisY, halfAxisZ, movingShape.AxisY.X, movingShape.AxisY.Y, movingShape.AxisY.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref normal)
                && updateSweepForCross(halfAxisX, halfAxisY, halfAxisZ, movingShape.AxisZ.X, movingShape.AxisZ.Y, movingShape.AxisZ.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref normal);
        }

        private bool updateSweepFaceAxes(in ShapeData axes, in ShapeData movingShape, double motionX, double motionY, double motionZ, ref double enter, ref double exit, ref FastVec3d normal)
        {
            return updateSweepForCross(axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref normal)
                && updateSweepForCross(axes.AxisZ.X, axes.AxisZ.Y, axes.AxisZ.Z, axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref normal)
                && updateSweepForCross(axes.AxisX.X, axes.AxisX.Y, axes.AxisX.Z, axes.AxisY.X, axes.AxisY.Y, axes.AxisY.Z, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref normal);
        }

        private bool updateSweepForCross(double firstX, double firstY, double firstZ, double secondX, double secondY, double secondZ, in ShapeData movingShape, double motionX, double motionY, double motionZ, ref double enter, ref double exit, ref FastVec3d normal)
        {
            double axisX = firstY * secondZ - firstZ * secondY;
            double axisY = firstZ * secondX - firstX * secondZ;
            double axisZ = firstX * secondY - firstY * secondX;
            return updateSweepOnAxis(axisX, axisY, axisZ, in movingShape, motionX, motionY, motionZ, ref enter, ref exit, ref normal);
        }

        private bool updateSweepOnAxis(double axisX, double axisY, double axisZ, in ShapeData movingShape, double motionX, double motionY, double motionZ, ref double enter, ref double exit, ref FastVec3d normal)
        {
            double axisLengthSq = axisX * axisX + axisY * axisY + axisZ * axisZ;
            if (axisLengthSq <= axisEpsilon) return true;

            double centerDistance = (shape.CenterX - movingShape.CenterX) * axisX + (shape.CenterY - movingShape.CenterY) * axisY + (shape.CenterZ - movingShape.CenterZ) * axisZ;
            double combinedRadius = projectedRadius(axisX, axisY, axisZ, in shape) + projectedRadius(axisX, axisY, axisZ, in movingShape);
            double velocity = motionX * axisX + motionY * axisY + motionZ * axisZ;
            if (velocity == 0)
            {
                return Math.Abs(centerDistance) < combinedRadius;
            }

            double first = (centerDistance - combinedRadius) / velocity;
            double second = (centerDistance + combinedRadius) / velocity;
            if (first > second) (first, second) = (second, first);
            if (first > enter || (first == enter && normal.X == 0 && normal.Y == 0 && normal.Z == 0))
            {
                enter = first;
                double sign = velocity > 0 ? -1 : 1;
                normal = new FastVec3d(sign * axisX, sign * axisY, sign * axisZ);
            }
            exit = Math.Min(exit, second);
            return enter <= exit;
        }

        private bool boundsIntersect(double x1, double y1, double z1, double x2, double y2, double z2, bool alsoCheckTouch)
        {
            return alsoCheckTouch
                ? BoundsX2 >= x1 && BoundsX1 <= x2 && BoundsY2 >= y1 && BoundsY1 <= y2 && BoundsZ2 >= z1 && BoundsZ1 <= z2
                : BoundsX2 > x1 && BoundsX1 < x2 && BoundsY2 > y1 && BoundsY1 < y2 && BoundsZ2 > z1 && BoundsZ1 < z2;
        }

        private bool boundsIntersect(TransformedCuboidd other, bool alsoCheckTouch)
        {
            return alsoCheckTouch
                ? BoundsX2 >= other.BoundsX1 && BoundsX1 <= other.BoundsX2 && BoundsY2 >= other.BoundsY1 && BoundsY1 <= other.BoundsY2 && BoundsZ2 >= other.BoundsZ1 && BoundsZ1 <= other.BoundsZ2
                : BoundsX2 > other.BoundsX1 && BoundsX1 < other.BoundsX2 && BoundsY2 > other.BoundsY1 && BoundsY1 < other.BoundsY2 && BoundsZ2 > other.BoundsZ1 && BoundsZ1 < other.BoundsZ2;
        }

        private bool boundsWithinSeparation(double x1, double y1, double z1, double x2, double y2, double z2, double maxSeparation)
        {
            return BoundsX2 + maxSeparation >= x1 && BoundsX1 - maxSeparation <= x2
                && BoundsY2 + maxSeparation >= y1 && BoundsY1 - maxSeparation <= y2
                && BoundsZ2 + maxSeparation >= z1 && BoundsZ1 - maxSeparation <= z2;
        }

        private bool boundsWithinSeparation(TransformedCuboidd other, double maxSeparation)
        {
            return BoundsX2 + maxSeparation >= other.BoundsX1 && BoundsX1 - maxSeparation <= other.BoundsX2
                && BoundsY2 + maxSeparation >= other.BoundsY1 && BoundsY1 - maxSeparation <= other.BoundsY2
                && BoundsZ2 + maxSeparation >= other.BoundsZ1 && BoundsZ1 - maxSeparation <= other.BoundsZ2;
        }

        private static double projectedRadius(double axisX, double axisY, double axisZ, in ShapeData value)
        {
            return Math.Abs(value.HalfAxisXX * axisX + value.HalfAxisXY * axisY + value.HalfAxisXZ * axisZ)
                 + Math.Abs(value.HalfAxisYX * axisX + value.HalfAxisYY * axisY + value.HalfAxisYZ * axisZ)
                 + Math.Abs(value.HalfAxisZX * axisX + value.HalfAxisZY * axisY + value.HalfAxisZZ * axisZ);
        }

        private static bool isAffineTransform(double[] transform)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!double.IsFinite(transform[i])) return false;
            }

            return Math.Abs(transform[3]) <= affineEpsilon
                && Math.Abs(transform[7]) <= affineEpsilon
                && Math.Abs(transform[11]) <= affineEpsilon
                && Math.Abs(transform[15] - 1) <= affineEpsilon;
        }

        private static void validateBounds(Cuboidd bounds)
        {
            if (bounds == null) throw new ArgumentNullException(nameof(bounds));
            if (!double.IsFinite(bounds.X1) || !double.IsFinite(bounds.Y1) || !double.IsFinite(bounds.Z1)
                || !double.IsFinite(bounds.X2) || !double.IsFinite(bounds.Y2) || !double.IsFinite(bounds.Z2)
                || bounds.X1 > bounds.X2 || bounds.Y1 > bounds.Y2 || bounds.Z1 > bounds.Z2)
            {
                throw new ArgumentException("Cuboid bounds must be finite and ordered.", nameof(bounds));
            }
        }

    }
}
