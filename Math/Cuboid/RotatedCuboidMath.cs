using System;

namespace Vintagestory.API.MathTools
{
    /// <summary>
    /// Math helpers for rotated cuboid (OBB) operations
    /// </summary>
    public static class RotatedCuboidMath
    {
        public const double EpsFloat = 1e-6;
        public const double EpsDouble = 1e-9;

        /// <summary>
        /// Builds a 4x4 rotation matrix from a quaternion, pivoted around the given center point
        /// </summary>
        public static double[] BuildMatrix(double[] quaternion, double cx, double cy, double cz)
        {
            double[] result = Mat4d.Create();
            BuildMatrix(result, quaternion, cx, cy, cz);
            return result;
        }

        /// <summary>
        /// Writes a 4x4 rotation matrix from a quaternion, pivoted around the given center point
        /// Formula mostly adapted from glMatrix (mat4.fromRotationTranslationScaleOrigin).
        /// </summary>
        public static void BuildMatrix(double[] result, double[] quaternion, double cx, double cy, double cz)
        {
            Mat4d.FromQuat(result, quaternion);
            result[12] = cx - result[0] * cx - result[4] * cy - result[8] * cz;
            result[13] = cy - result[1] * cx - result[5] * cy - result[9] * cz;
            result[14] = cz - result[2] * cx - result[6] * cy - result[10] * cz;
        }

        /// <summary>
        /// Widens a float quaternion to double precision
        /// </summary>
        public static double[] FromFloatQuaternion(float[] quaternion)
            => new double[] { quaternion[0], quaternion[1], quaternion[2], quaternion[3] };

        /// <summary>
        /// Returns the inverse of a 4x4 matrix
        /// </summary>
        public static double[] Invert(double[] m)
        {
            double[] inv = Mat4d.Create();
            Mat4d.Invert(inv, m);
            return inv;
        }

        /// <summary>
        /// Transforms a point (x,y,z) by a 4x4 matrix
        /// </summary>
        public static void TransformPoint(double[] m, double x, double y, double z, out double rx, out double ry, out double rz)
        {
            rx = m[0] * x + m[4] * y + m[8] * z + m[12];
            ry = m[1] * x + m[5] * y + m[9] * z + m[13];
            rz = m[2] * x + m[6] * y + m[10] * z + m[14];
        }

        /// <summary>
        /// Transforms a direction vector (x,y,z) by a 4x4 matrix, ignoring translation
        /// </summary>
        public static void TransformDirection(double[] m, double x, double y, double z, out double rx, out double ry, out double rz)
        {
            rx = m[0] * x + m[4] * y + m[8] * z;
            ry = m[1] * x + m[5] * y + m[9] * z;
            rz = m[2] * x + m[6] * y + m[10] * z;
        }

        /// <summary>
        /// Computes the world-space AABB enclosing an OBB defined by local bounds and rotation matrix R
        /// </summary>
        public static void WorldAabbOfOBB(double[] R,
            double x1, double y1, double z1, double x2, double y2, double z2,
            out double minX, out double minY, out double minZ,
            out double maxX, out double maxY, out double maxZ)
        {
            TransformPoint(R, x1, y1, z1, out minX, out minY, out minZ);
            maxX = minX; maxY = minY; maxZ = minZ;
            for (int i = 1; i < 8; i++)
            {
                double cx = (i & 1) != 0 ? x2 : x1;
                double cy = (i & 2) != 0 ? y2 : y1;
                double cz = (i & 4) != 0 ? z2 : z1;
                TransformPoint(R, cx, cy, cz, out double tx, out double ty, out double tz);
                if (tx < minX) minX = tx; else if (tx > maxX) maxX = tx;
                if (ty < minY) minY = ty; else if (ty > maxY) maxY = ty;
                if (tz < minZ) minZ = tz; else if (tz > maxZ) maxZ = tz;
            }
        }

        // 15-axis SAT, see Ericson "Real-Time Collision Detection" §4.4.1.
        // Reference implementation: https://github.com/RandyGaul/qu3e/blob/master/src/collision/q3Box.cpp
        /// <summary>
        /// 15-axis Separating Axis Theorem test between an OBB (defined by local bounds and rotation matrix R) and an AABB
        /// </summary>
        public static bool IntersectsOBBvsAABB(double[] R,
            double bx1, double by1, double bz1, double bx2, double by2, double bz2,
            double ax1, double ay1, double az1, double ax2, double ay2, double az2,
            bool strict, double absEps)
        {
            double r00 = R[0],  r10 = R[1],  r20 = R[2];
            double r01 = R[4],  r11 = R[5],  r21 = R[6];
            double r02 = R[8],  r12 = R[9],  r22 = R[10];

            double bex = (bx2 - bx1) * 0.5, bey = (by2 - by1) * 0.5, bez = (bz2 - bz1) * 0.5;
            TransformPoint(R, (bx1 + bx2) * 0.5, (by1 + by2) * 0.5, (bz1 + bz2) * 0.5, out double cBx, out double cBy, out double cBz);

            double aex = (ax2 - ax1) * 0.5, aey = (ay2 - ay1) * 0.5, aez = (az2 - az1) * 0.5;
            double cAx = (ax1 + ax2) * 0.5, cAy = (ay1 + ay2) * 0.5, cAz = (az1 + az2) * 0.5;

            double tx = cBx - cAx, ty = cBy - cAy, tz = cBz - cAz;

            double a00 = Math.Abs(r00) + absEps, a01 = Math.Abs(r01) + absEps, a02 = Math.Abs(r02) + absEps;
            double a10 = Math.Abs(r10) + absEps, a11 = Math.Abs(r11) + absEps, a12 = Math.Abs(r12) + absEps;
            double a20 = Math.Abs(r20) + absEps, a21 = Math.Abs(r21) + absEps, a22 = Math.Abs(r22) + absEps;

            if (Sep(tx, aex, bex*a00 + bey*a01 + bez*a02, strict)) return false;
            if (Sep(ty, aey, bex*a10 + bey*a11 + bez*a12, strict)) return false;
            if (Sep(tz, aez, bex*a20 + bey*a21 + bez*a22, strict)) return false;

            if (Sep(tx*r00 + ty*r10 + tz*r20, aex*a00 + aey*a10 + aez*a20, bex, strict)) return false;
            if (Sep(tx*r01 + ty*r11 + tz*r21, aex*a01 + aey*a11 + aez*a21, bey, strict)) return false;
            if (Sep(tx*r02 + ty*r12 + tz*r22, aex*a02 + aey*a12 + aez*a22, bez, strict)) return false;

            if (Sep(tz*r10 - ty*r20, aey*a20 + aez*a10, bey*a02 + bez*a01, strict)) return false;
            if (Sep(tz*r11 - ty*r21, aey*a21 + aez*a11, bex*a02 + bez*a00, strict)) return false;
            if (Sep(tz*r12 - ty*r22, aey*a22 + aez*a12, bex*a01 + bey*a00, strict)) return false;

            if (Sep(tx*r20 - tz*r00, aex*a20 + aez*a00, bey*a12 + bez*a11, strict)) return false;
            if (Sep(tx*r21 - tz*r01, aex*a21 + aez*a01, bex*a12 + bez*a10, strict)) return false;
            if (Sep(tx*r22 - tz*r02, aex*a22 + aez*a02, bex*a11 + bey*a10, strict)) return false;

            if (Sep(ty*r00 - tx*r10, aex*a10 + aey*a00, bey*a22 + bez*a21, strict)) return false;
            if (Sep(ty*r01 - tx*r11, aex*a11 + aey*a01, bex*a22 + bez*a20, strict)) return false;
            if (Sep(ty*r02 - tx*r12, aex*a12 + aey*a02, bex*a21 + bey*a20, strict)) return false;

            return true;
        }

        private static bool Sep(double t, double rA, double rB, bool strict)
            => strict ? Math.Abs(t) >= rA + rB : Math.Abs(t) > rA + rB;

        /// <summary>
        /// Returns true if the quaternion deviates from identity by more than the given epsilon
        /// </summary>
        public static bool IsRotatedQuaternion(double[] q, double epsilon)
            => Math.Abs(q[0]) > epsilon || Math.Abs(q[1]) > epsilon || Math.Abs(q[2]) > epsilon || Math.Abs(q[3] - 1.0) > epsilon;

        /// <summary>
        /// Returns true if the quaternion deviates from identity by more than the given epsilon
        /// </summary>
        public static bool IsRotatedQuaternion(float[] q, float epsilon)
            => Math.Abs(q[0]) > epsilon || Math.Abs(q[1]) > epsilon || Math.Abs(q[2]) > epsilon || Math.Abs(q[3] - 1.0f) > epsilon;
    }
}
