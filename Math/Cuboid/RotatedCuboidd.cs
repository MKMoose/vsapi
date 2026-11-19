using System;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.API.MathTools
{
    public class RotatedCuboidd : Cuboidd, IRotatedCuboid<double, RotatedCuboidd>
    {
        private const double quatIdentityEpsilon = 0.0001;
        private const double contactEpsilon = 1e-6;

        private Vec3d rotationCenter = new Vec3d();
        private double[] rotationQuat = Quaterniond.Create();
        private bool isRotated;
        private double[]? cachedMatrix;
        private double[]? cachedMatrixInv;
        private bool matrixDirty = true;
        private bool matrixInvDirty = true;
        private TransformedCuboidd? cachedShape;
        private bool shapeDirty = true;
        private double shapeX1;
        private double shapeY1;
        private double shapeZ1;
        private double shapeX2;
        private double shapeY2;
        private double shapeZ2;

        public Vec3d RotationCenterDouble => rotationCenter;

        Vec3f IRotatedCuboid<double, RotatedCuboidd>.RotationCenter => rotationCenter.ToVec3f();

        public override bool IsRotated() => isRotated;

        public RotatedCuboidd() : base() { }

        public RotatedCuboidd(double x1, double y1, double z1, double x2, double y2, double z2) : base(x1, y1, z1, x2, y2, z2)
        {
            rotationCenter = new Vec3d(MidX, MidY, MidZ);
        }

        public RotatedCuboidd(Cuboidd cuboid) : this(cuboid.X1, cuboid.Y1, cuboid.Z1, cuboid.X2, cuboid.Y2, cuboid.Z2) { }

        public RotatedCuboidd(Cuboidd cuboid, Vec3d rotationCenter) : base()
        {
            Set(cuboid.X1, cuboid.Y1, cuboid.Z1, cuboid.X2, cuboid.Y2, cuboid.Z2);
            this.rotationCenter = rotationCenter.Clone();
        }

        public RotatedCuboidd(RotatedCuboidf source, double offsetX, double offsetY, double offsetZ) : base()
        {
            Set(source.X1 + offsetX, source.Y1 + offsetY, source.Z1 + offsetZ,
                source.X2 + offsetX, source.Y2 + offsetY, source.Z2 + offsetZ);
            Vec3f sc = source.RotationCenter;
            rotationCenter = new Vec3d(sc.X + offsetX, sc.Y + offsetY, sc.Z + offsetZ);
            rotationQuat = RotatedCuboidMath.FromFloatQuaternion(source.GetRotationQuaternion());
            UpdateRotationFlag();
        }

        public RotatedCuboidd(Cuboidd cuboid, Vec3d rotationCenter, double[] quaternion) : base()
        {
            Set(cuboid.X1, cuboid.Y1, cuboid.Z1, cuboid.X2, cuboid.Y2, cuboid.Z2);
            this.rotationCenter = rotationCenter.Clone();
            rotationQuat = Quaterniond.CloneIt(quaternion);
            UpdateRotationFlag();
        }

        public new RotatedCuboidd Set(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            double dx = x1 * 0.5 + x2 * 0.5 - MidX;
            double dy = y1 * 0.5 + y2 * 0.5 - MidY;
            double dz = z1 * 0.5 + z2 * 0.5 - MidZ;

            base.Set(x1, y1, z1, x2, y2, z2);
            rotationCenter.X += dx;
            rotationCenter.Y += dy;
            rotationCenter.Z += dz;
            InvalidateCache();
            return this;
        }

        public RotatedCuboidd SetRotationEuler(double degX, double degY, double degZ)
        {
            rotationQuat = Quaterniond.Create();
            Quaterniond.RotateX(rotationQuat, rotationQuat, degX * GameMath.DEG2RAD);
            Quaterniond.RotateY(rotationQuat, rotationQuat, degY * GameMath.DEG2RAD);
            Quaterniond.RotateZ(rotationQuat, rotationQuat, degZ * GameMath.DEG2RAD);
            UpdateRotationFlag();
            return this;
        }

        public RotatedCuboidd SetRotationQuaternion(double[] quaternion)
        {
            rotationQuat = Quaterniond.CloneIt(quaternion);
            UpdateRotationFlag();
            return this;
        }

        public RotatedCuboidd SetRotationCenter(Vec3d center)
        {
            rotationCenter = center.Clone();
            InvalidateCache();
            return this;
        }

        /// <summary>
        /// Copies the supplied bounds and rotation into this cuboid while retaining its reusable collision state.
        /// </summary>
        public RotatedCuboidd Set(double x1, double y1, double z1, double x2, double y2, double z2, Vec3d rotationCenter, double[] quaternion)
        {
            base.Set(x1, y1, z1, x2, y2, z2);
            this.rotationCenter.Set(rotationCenter);
            if (rotationQuat.Length != quaternion.Length) rotationQuat = Quaterniond.CloneIt(quaternion);
            else Array.Copy(quaternion, rotationQuat, quaternion.Length);
            isRotated = RotatedCuboidMath.IsRotatedQuaternion(rotationQuat, quatIdentityEpsilon);
            InvalidateCache();
            return this;
        }

        public double[] GetRotationQuaternion() => Quaterniond.CloneIt(rotationQuat);

        public double[] GetRotationMatrix() => Matrix();

        private void UpdateRotationFlag()
        {
            isRotated = RotatedCuboidMath.IsRotatedQuaternion(rotationQuat, quatIdentityEpsilon);
            InvalidateCache();
        }

        private void InvalidateCache()
        {
            matrixDirty = true;
            matrixInvDirty = true;
            shapeDirty = true;
        }

        // Cache replacements are built completely before assignment, so an existing cached object is never mutated in place.

        private double[] Matrix()
        {
            double[]? matrix = cachedMatrix;
            if (matrixDirty || matrix == null)
            {
                matrix = Mat4d.Create();
                RotatedCuboidMath.BuildMatrix(matrix, rotationQuat, rotationCenter.X, rotationCenter.Y, rotationCenter.Z);
                cachedMatrix = matrix;
                matrixDirty = false;
            }
            return matrix;
        }

        private double[] MatrixInv()
        {
            double[]? matrixInv = cachedMatrixInv;
            if (matrixInvDirty || matrixInv == null)
            {
                matrixInv = Mat4d.Create();
                Mat4d.Invert(matrixInv, Matrix());
                cachedMatrixInv = matrixInv;
                matrixInvDirty = false;
            }
            return matrixInv;
        }

        private TransformedCuboidd getShape()
        {
            bool boundsChanged = shapeX1 != X1 || shapeY1 != Y1 || shapeZ1 != Z1
                || shapeX2 != X2 || shapeY2 != Y2 || shapeZ2 != Z2;
            TransformedCuboidd? shape = cachedShape;
            if (shapeDirty || boundsChanged || shape == null)
            {
                shape = isRotated ? new TransformedCuboidd(this, Matrix()) : new TransformedCuboidd(this);
                cachedShape = shape;
                shapeX1 = X1;
                shapeY1 = Y1;
                shapeZ1 = Z1;
                shapeX2 = X2;
                shapeY2 = Y2;
                shapeZ2 = Z2;
                shapeDirty = false;
            }
            return shape;
        }

        public Cuboidd GetAABB()
        {
            return GetAABB(new Cuboidd());
        }

        /// <summary>
        /// Writes the world-space axis-aligned bounds of this cuboid into the supplied result.
        /// </summary>
        public Cuboidd GetAABB(Cuboidd result)
        {
            return getShape().GetAABB(result);
        }

        public new bool Contains(double x, double y, double z)
        {
            if (!isRotated) return base.Contains(x, y, z);
            RotatedCuboidMath.TransformPoint(MatrixInv(), x, y, z, out double lx, out double ly, out double lz);
            return base.Contains(lx, ly, lz);
        }

        private bool IntersectsImpl(double ax1, double ay1, double az1, double ax2, double ay2, double az2, bool strict)
        {
            if (!isRotated)
            {
                return strict
                    ? ax2 > X1 && ax1 < X2 && ay2 > Y1 && ay1 < Y2 && az2 > Z1 && az1 < Z2
                    : ax2 >= X1 && ax1 <= X2 && ay2 >= Y1 && ay1 <= Y2 && az2 >= Z1 && az1 <= Z2;
            }
            return strict
                ? getShape().Intersects(ax1, ay1, az1, ax2, ay2, az2)
                : getShape().IntersectsOrTouches(ax1, ay1, az1, ax2, ay2, az2);
        }

        public override bool Intersects(Cuboidd other)
            => isRotated ? IntersectsImpl(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, true) : base.Intersects(other);
        public override bool Intersects(Cuboidf other)
            => isRotated ? IntersectsImpl(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, true) : base.Intersects(other);
        public bool Intersects(Cuboidd other, Vec3d off) => IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, true);
        public override bool Intersects(Cuboidf other, Vec3d off)
            => isRotated ? IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, true) : base.Intersects(other, off);
        public override bool Intersects(Cuboidf other, double offsetx, double offsety, double offsetz)
            => isRotated ? IntersectsImpl(other.X1 + offsetx, other.Y1 + offsety, other.Z1 + offsetz, other.X2 + offsetx, other.Y2 + offsety, other.Z2 + offsetz, true) : base.Intersects(other, offsetx, offsety, offsetz);
        public override bool IntersectsOrTouches(Cuboidd other)
            => isRotated ? IntersectsImpl(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, false) : base.IntersectsOrTouches(other);
        public bool IntersectsOrTouches(Cuboidd other, Vec3d off) => IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, false);
        public override bool IntersectsOrTouches(Cuboidf other, Vec3d off)
            => isRotated ? IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, false) : base.IntersectsOrTouches(other, off);
        public override bool IntersectsOrTouches(Cuboidf other, double offsetX, double offsetY, double offsetZ)
            => isRotated ? IntersectsImpl(other.X1 + offsetX, other.Y1 + offsetY, other.Z1 + offsetZ, other.X2 + offsetX, other.Y2 + offsetY, other.Z2 + offsetZ, false) : base.IntersectsOrTouches(other, offsetX, offsetY, offsetZ);

        private double pushOutAxis(Cuboidd from, EnumAxis axis, double motion, ref EnumPushDirection direction)
        {
            direction = EnumPushDirection.None;
            if (!isRotated)
            {
                return axis switch
                {
                    EnumAxis.X => base.pushOutX(from, motion, ref direction),
                    EnumAxis.Y => base.pushOutY(from, motion, ref direction),
                    _ => base.pushOutZ(from, motion, ref direction),
                };
            }
            double motionX = axis == EnumAxis.X ? motion : 0;
            double motionY = axis == EnumAxis.Y ? motion : 0;
            double motionZ = axis == EnumAxis.Z ? motion : 0;
            if (!getShape().TrySweepAABB(from, motionX, motionY, motionZ, out double collisionFraction)) return motion;

            direction = motion > 0 ? EnumPushDirection.Positive : EnumPushDirection.Negative;
            // A strict intersect needs tolerance for rounding errors on either side, a small contact threshold accounts for this.
            double allowed = Math.Max(0, Math.Abs(motion) * collisionFraction - contactEpsilon);
            return motion > 0 ? allowed : -allowed;
        }

        public override double pushOutX(Cuboidd from, double x, ref EnumPushDirection direction) => pushOutAxis(from, EnumAxis.X, x, ref direction);
        public override double pushOutY(Cuboidd from, double y, ref EnumPushDirection direction) => pushOutAxis(from, EnumAxis.Y, y, ref direction);
        public override double pushOutZ(Cuboidd from, double z, ref EnumPushDirection direction) => pushOutAxis(from, EnumAxis.Z, z, ref direction);

        public new RotatedCuboidd OffsetCopy(double x, double y, double z)
        {
            return new RotatedCuboidd(X1 + x, Y1 + y, Z1 + z, X2 + x, Y2 + y, Z2 + z)
            {
                rotationCenter = new Vec3d(rotationCenter.X + x, rotationCenter.Y + y, rotationCenter.Z + z),
                rotationQuat = Quaterniond.CloneIt(rotationQuat),
                isRotated = isRotated
            };
        }

        public new RotatedCuboidd OffsetCopy(IVec3 vec) => OffsetCopy(vec.XAsDouble, vec.YAsDouble, vec.ZAsDouble);

        public override RotatedCuboidd Clone() => new RotatedCuboidd(this, rotationCenter.Clone(), rotationQuat);

        public bool Equals(RotatedCuboidd? other)
        {
            if (other == null || !base.Equals(other) || !rotationCenter.Equals(other.rotationCenter)) return false;
            if (rotationQuat.Length != other.rotationQuat.Length) return false;
            for (int i = 0; i < rotationQuat.Length; i++)
            {
                if (Math.Abs(rotationQuat[i] - other.rotationQuat[i]) > quatIdentityEpsilon) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is RotatedCuboidd other && Equals(other);

        public override int GetHashCode()
        {
            int hash = base.GetHashCode();
            hash = hash * 31 + rotationCenter.GetHashCode();
            for (int i = 0; i < rotationQuat.Length; i++)
            {
                hash = hash * 31 + rotationQuat[i].GetHashCode();
            }
            return hash;
        }
    }
}
