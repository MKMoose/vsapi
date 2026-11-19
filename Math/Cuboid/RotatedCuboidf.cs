using System;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.API.MathTools
{
    public class RotatedCuboidf : Cuboidf, IRotatedCuboid<float, RotatedCuboidf>
    {
        private const float quatIdentityEpsilon = 0.0001f;

        private Vec3f rotationCenter = new Vec3f();
        private float[] rotationQuat = Quaternionf.Create();
        private bool isRotated;
        private float[]? cachedMatrixF;
        private double[]? cachedMatrixD;
        private double[]? cachedMatrixDInv;
        private TransformedCuboidd? cachedShape;
        private bool shapeDirty = true;
        private float shapeX1;
        private float shapeY1;
        private float shapeZ1;
        private float shapeX2;
        private float shapeY2;
        private float shapeZ2;

        public Vec3f RotationCenter => rotationCenter;

        public override bool IsRotated() => isRotated;

        public RotatedCuboidf() : base() { }

        public RotatedCuboidf(float x1, float y1, float z1, float x2, float y2, float z2) : base(x1, y1, z1, x2, y2, z2)
        {
            Vec3d c = Center;
            rotationCenter = new Vec3f((float)c.X, (float)c.Y, (float)c.Z);
        }

        public RotatedCuboidf(Cuboidf cuboid) : this(cuboid.X1, cuboid.Y1, cuboid.Z1, cuboid.X2, cuboid.Y2, cuboid.Z2) { }

        public RotatedCuboidf(Cuboidf cuboid, Vec3d rotationCenter) : base()
        {
            Set(cuboid.X1, cuboid.Y1, cuboid.Z1, cuboid.X2, cuboid.Y2, cuboid.Z2);
            this.rotationCenter = rotationCenter.ToVec3f();
        }

        public RotatedCuboidf(Cuboidf cuboid, Vec3f rotationCenter) : base()
        {
            Set(cuboid.X1, cuboid.Y1, cuboid.Z1, cuboid.X2, cuboid.Y2, cuboid.Z2);
            this.rotationCenter = rotationCenter.Clone();
        }

        public RotatedCuboidf(Cuboidf cuboid, Vec3f rotationCenter, float[] quaternion) : base()
        {
            Set(cuboid.X1, cuboid.Y1, cuboid.Z1, cuboid.X2, cuboid.Y2, cuboid.Z2);
            this.rotationCenter = rotationCenter.Clone();
            this.rotationQuat = Quaternionf.CloneIt(quaternion);
            UpdateRotationFlag();
        }

        public RotatedCuboidf SetRotationEuler(float degX, float degY, float degZ)
        {
            rotationQuat = Quaternionf.Create();
            Quaternionf.RotateX(rotationQuat, rotationQuat, degX * GameMath.DEG2RAD);
            Quaternionf.RotateY(rotationQuat, rotationQuat, degY * GameMath.DEG2RAD);
            Quaternionf.RotateZ(rotationQuat, rotationQuat, degZ * GameMath.DEG2RAD);
            UpdateRotationFlag();
            return this;
        }

        public RotatedCuboidf SetRotationQuaternion(float[] quaternion)
        {
            rotationQuat = Quaternionf.CloneIt(quaternion);
            UpdateRotationFlag();
            return this;
        }

        public RotatedCuboidf SetRotationCenter(Vec3f center)
        {
            rotationCenter = center.Clone();
            InvalidateCache();
            return this;
        }

        public float[] GetRotationQuaternion() => Quaternionf.CloneIt(rotationQuat);

        private void UpdateRotationFlag()
        {
            isRotated = RotatedCuboidMath.IsRotatedQuaternion(rotationQuat, quatIdentityEpsilon);
            InvalidateCache();
        }

        private void InvalidateCache()
        {
            cachedMatrixF = null;
            cachedMatrixD = null;
            cachedMatrixDInv = null;
            shapeDirty = true;
        }

        public float[] GetRotationMatrix()
        {
            if (cachedMatrixF == null)
            {
                cachedMatrixF = Mat4f.Create();
                float[] rot = Mat4f.Create();
                Mat4f.FromQuat(rot, rotationQuat);
                Mat4f.Translate(cachedMatrixF, cachedMatrixF, rotationCenter.X, rotationCenter.Y, rotationCenter.Z);
                Mat4f.Multiply(cachedMatrixF, cachedMatrixF, rot);
                Mat4f.Translate(cachedMatrixF, cachedMatrixF, -rotationCenter.X, -rotationCenter.Y, -rotationCenter.Z);
            }
            return cachedMatrixF;
        }

        private double[] MatrixD()
            => cachedMatrixD ??= RotatedCuboidMath.BuildMatrix(
                RotatedCuboidMath.FromFloatQuaternion(rotationQuat),
                rotationCenter.X, rotationCenter.Y, rotationCenter.Z);

        private double[] MatrixDInv() => cachedMatrixDInv ??= RotatedCuboidMath.Invert(MatrixD());

        private TransformedCuboidd getShape()
        {
            bool boundsChanged = shapeX1 != X1 || shapeY1 != Y1 || shapeZ1 != Z1
                || shapeX2 != X2 || shapeY2 != Y2 || shapeZ2 != Z2;
            TransformedCuboidd? shape = cachedShape;
            if (shapeDirty || boundsChanged || shape == null)
            {
                shape = new TransformedCuboidd(new Cuboidd(X1, Y1, Z1, X2, Y2, Z2), MatrixD());
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

        public Cuboidf GetAABB()
        {
            if (!isRotated) return Clone();
            RotatedCuboidMath.WorldAabbOfOBB(MatrixD(), X1, Y1, Z1, X2, Y2, Z2,
                out double mnX, out double mnY, out double mnZ, out double mxX, out double mxY, out double mxZ);
            return new Cuboidf((float)mnX, (float)mnY, (float)mnZ, (float)mxX, (float)mxY, (float)mxZ);
        }

        public new bool Contains(double x, double y, double z)
        {
            if (!isRotated) return base.Contains(x, y, z);
            RotatedCuboidMath.TransformPoint(MatrixDInv(), x, y, z, out double lx, out double ly, out double lz);
            return base.Contains(lx, ly, lz);
        }

        public new bool ContainsOrTouches(float x, float y, float z)
        {
            if (!isRotated) return base.ContainsOrTouches(x, y, z);
            RotatedCuboidMath.TransformPoint(MatrixDInv(), x, y, z, out double lx, out double ly, out double lz);
            return base.ContainsOrTouches((float)lx, (float)ly, (float)lz);
        }

        private bool IntersectsImpl(double ax1, double ay1, double az1, double ax2, double ay2, double az2, bool strict)
        {
            if (!isRotated)
            {
                return strict
                    ? ax2 > X1 && ax1 < X2 && ay2 > Y1 && ay1 < Y2 && az2 > Z1 && az1 < Z2
                    : ax2 >= X1 && ax1 <= X2 && ay2 >= Y1 && ay1 <= Y2 && az2 >= Z1 && az1 <= Z2;
            }
            return RotatedCuboidMath.IntersectsOBBvsAABB(MatrixD(), X1, Y1, Z1, X2, Y2, Z2,
                ax1, ay1, az1, ax2, ay2, az2, strict, RotatedCuboidMath.EpsFloat);
        }

        public bool Intersects(Cuboidf other)
        {
            if (!isRotated && other is RotatedCuboidf rotated && rotated.IsRotated()) return rotated.Intersects(this);
            return IntersectsImpl(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, true);
        }

        public override bool Intersects(Cuboidf other, Vec3d off)
            => isRotated
                ? IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, true)
                : base.Intersects(other, off);

        public override bool Intersects(Cuboidf other, double offsetx, double offsety, double offsetz)
            => isRotated
                ? IntersectsImpl(other.X1 + offsetx, other.Y1 + offsety, other.Z1 + offsetz, other.X2 + offsetx, other.Y2 + offsety, other.Z2 + offsetz, true)
                : base.Intersects(other, offsetx, offsety, offsetz);

        public bool Intersects(Cuboidd other)
            => isRotated
                ? IntersectsImpl(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, true)
                : other.Intersects(this);

        public bool Intersects(Cuboidd other, Vec3d off)
            => isRotated
                ? IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, true)
                : other.Intersects(this, new Vec3d(-off.X, -off.Y, -off.Z));

        public override bool IntersectsOrTouches(Cuboidf other)
            => isRotated
                ? IntersectsImpl(other.X1, other.Y1, other.Z1, other.X2, other.Y2, other.Z2, false)
                : base.IntersectsOrTouches(other);

        public override bool IntersectsOrTouches(Cuboidf other, Vec3d off)
            => isRotated
                ? IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, false)
                : base.IntersectsOrTouches(other, off);

        public bool IntersectsOrTouches(Cuboidd other, Vec3d off)
            => isRotated
                ? IntersectsImpl(other.X1 + off.X, other.Y1 + off.Y, other.Z1 + off.Z, other.X2 + off.X, other.Y2 + off.Y, other.Z2 + off.Z, false)
                : other.IntersectsOrTouches(this, new Vec3d(-off.X, -off.Y, -off.Z));

        private double pushOutAxis(Cuboidf from, EnumAxis axis, float motion, ref EnumPushDirection direction)
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

            double travel = motion * collisionFraction;
            double backOff = motion > 0 ? RotatedCuboidMath.EpsFloat : -RotatedCuboidMath.EpsFloat;
            return Math.Abs(travel) <= RotatedCuboidMath.EpsFloat ? 0 : travel - backOff;
        }

        public override double pushOutX(Cuboidf from, float x, ref EnumPushDirection direction) => pushOutAxis(from, EnumAxis.X, x, ref direction);
        public override double pushOutY(Cuboidf from, float y, ref EnumPushDirection direction) => pushOutAxis(from, EnumAxis.Y, y, ref direction);
        public override double pushOutZ(Cuboidf from, float z, ref EnumPushDirection direction) => pushOutAxis(from, EnumAxis.Z, z, ref direction);

        public new RotatedCuboidf OffsetCopy(float x, float y, float z)
        {
            return new RotatedCuboidf(X1 + x, Y1 + y, Z1 + z, X2 + x, Y2 + y, Z2 + z)
            {
                rotationCenter = new Vec3f(rotationCenter.X + x, rotationCenter.Y + y, rotationCenter.Z + z),
                rotationQuat = Quaternionf.CloneIt(rotationQuat),
                isRotated = isRotated
            };
        }

        public new RotatedCuboidf OffsetCopy(IVec3 vec) => OffsetCopy(vec.XAsFloat, vec.YAsFloat, vec.ZAsFloat);

        public override Cuboidd OffsetCopyDouble(double x, double y, double z)
            => isRotated ? new RotatedCuboidd(this, x, y, z) : base.OffsetCopyDouble(x, y, z);

        public new Cuboidd OffsetCopyDouble(IVec3 vec) => OffsetCopyDouble(vec.XAsDouble, vec.YAsDouble, vec.ZAsDouble);

        public new RotatedCuboidf RotatedCopy(float degX, float degY, float degZ, Vec3d origin)
        {
            if (!isRotated)
            {
                return new RotatedCuboidf(this, origin).SetRotationEuler(degX, degY, degZ);
            }

            float[] add = Quaternionf.Create();
            Quaternionf.RotateX(add, add, degX * GameMath.DEG2RAD);
            Quaternionf.RotateY(add, add, degY * GameMath.DEG2RAD);
            Quaternionf.RotateZ(add, add, degZ * GameMath.DEG2RAD);

            float[] combined = Quaternionf.Create();
            Quaternionf.Multiply(combined, add, rotationQuat);
            return new RotatedCuboidf(this, new Vec3f((float)origin.X, (float)origin.Y, (float)origin.Z), combined);
        }

        public new RotatedCuboidf RotatedCopy(IVec3 vec, Vec3d origin) => RotatedCopy(vec.XAsFloat, vec.YAsFloat, vec.ZAsFloat, origin);

        public new RotatedCuboidf Clone() => new RotatedCuboidf(this, rotationCenter.Clone(), rotationQuat);

        public bool Equals(RotatedCuboidf? other)
        {
            if (other == null || !base.Equals(other) || !rotationCenter.Equals(other.rotationCenter)) return false;
            if (rotationQuat.Length != other.rotationQuat.Length) return false;
            for (int i = 0; i < rotationQuat.Length; i++)
            {
                if (Math.Abs(rotationQuat[i] - other.rotationQuat[i]) > quatIdentityEpsilon) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is RotatedCuboidf other && Equals(other);

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
