using System;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Handles transformation matrix building and ray transformations for 3D model raycasting.
    /// Encapsulates the logic for converting between screen space and model local space.
    /// </summary>
    public class ModelHitboxTransform
    {
        readonly ICoreClientAPI capi;
        readonly Func<float> getYaw;
        readonly Func<float> getPitch;
        readonly Func<float> getScale;
        readonly Func<(float, float)> getScreenPosition;
        readonly bool flipY;

        /// <summary>
        /// Optional offset applied in model space after scale (e.g. -0.5, 0, -0.5 to center VS block models).
        /// </summary>
        public (float x, float y, float z) ModelOriginOffset { get; set; }

        /// <summary>
        /// Optional accessor for the animator's joint matrices. When set, BuildTransformMatrix
        /// will apply joint animation transforms so raycasting matches animated positions.
        /// </summary>
        public Func<float[]> GetAnimatorMatrices { get; set; }

        public ModelHitboxTransform(ICoreClientAPI capi, Func<float> getYaw, Func<float> getPitch, Func<float> getScale,
            Func<(float, float)> getScreenPosition = null, bool flipY = false)
        {
            this.capi = capi;
            this.getYaw = getYaw;
            this.getPitch = getPitch;
            this.getScale = getScale;
            this.getScreenPosition = getScreenPosition;
            this.flipY = flipY;
        }

        /// <summary>
        /// Builds a transformation matrix for rendering a hitbox.
        /// Matches the rendering transformation order.
        /// </summary>
        public Matrixf BuildTransformMatrix(ElementHitbox hitbox)
        {
            float yaw = getYaw();
            float pitch = getPitch();
            float scale = getScale();

            float centerX, centerY;
            if (getScreenPosition != null)
            {
                (centerX, centerY) = getScreenPosition();
            }
            else
            {
                var renderer = capi.Render;
                centerX = renderer.FrameWidth / 2.0f;
                centerY = renderer.FrameHeight / 2.0f;
            }

            Matrixf mvMat = new Matrixf();
            mvMat.Identity();

            float yScale = flipY ? -scale : scale;
            mvMat.Translate(centerX, centerY, scale);
            mvMat.RotateX(pitch);
            mvMat.RotateY(yaw);
            mvMat.Scale(scale, yScale, scale);
            var offset = ModelOriginOffset;
            if (offset.x != 0 || offset.y != 0 || offset.z != 0)
            {
                mvMat.Translate(offset.x, offset.y, offset.z);
            }

            float[] animMatrices = GetAnimatorMatrices?.Invoke();
            if (hitbox.JointId > 0 && animMatrices != null)
            {
                int index = hitbox.JointId * 16;
                if (index + 16 <= animMatrices.Length)
                {
                    float[] animMat = new float[16];
                    Array.Copy(animMatrices, index, animMat, 0, 16);
                    float[] tempMat = new float[16];
                    Mat4f.Mul(tempMat, mvMat.Values, animMat);
                    Array.Copy(tempMat, mvMat.Values, 16);
                }
            }

            if (hitbox.Transform != null)
            {
                float[] tempMat = new float[16];
                Mat4f.Mul(tempMat, mvMat.Values, hitbox.Transform.Values);
                Array.Copy(tempMat, mvMat.Values, 16);
            }

            return mvMat;
        }

        /// <summary>
        /// Builds the full transformation matrix for rendering (including hitbox position and size).
        /// Used for wireframe rendering.
        /// </summary>
        public Matrixf BuildFullTransformMatrix(ElementHitbox hitbox)
        {
            Cuboidd bounds = hitbox.Bounds;
            Vec3d minCorner = new Vec3d(bounds.X1, bounds.Y1, bounds.Z1);
            Vec3d size = new Vec3d(
                bounds.X2 - bounds.X1,
                bounds.Y2 - bounds.Y1,
                bounds.Z2 - bounds.Z1
            );

            Matrixf mvMat = BuildTransformMatrix(hitbox);

            mvMat.Translate((float)minCorner.X, (float)minCorner.Y, (float)minCorner.Z);
            mvMat.Scale((float)size.X, (float)size.Y, (float)size.Z);

            return mvMat;
        }

        /// <summary>
        /// Transforms a ray from screen space to local hitbox space using matrix inversion.
        /// </summary>
        public void TransformRayToLocal(Vec3d rayOrigin, Vec3d rayDir, ElementHitbox hitbox,
            out Vec3d localOrigin, out Vec3d localDir)
        {
            Matrixf mvMat = BuildTransformMatrix(hitbox);
            Matrixf mvMatInv = new Matrixf();
            mvMatInv.Set(mvMat.Values).Invert();

            Vec4d rayOrigin4 = new Vec4d(rayOrigin.X, rayOrigin.Y, rayOrigin.Z, 1);
            Vec4d rayDir4 = new Vec4d(rayDir.X, rayDir.Y, rayDir.Z, 0);

            var localOrigin4 = mvMatInv.TransformVector(rayOrigin4);
            var localDir4 = mvMatInv.TransformVector(rayDir4);

            localOrigin = localOrigin4.XYZ;
            localDir = localDir4.XYZ;

            double length = Math.Sqrt(localDir.X * localDir.X + localDir.Y * localDir.Y + localDir.Z * localDir.Z);
            if (length > 1e-10)
            {
                localDir.X /= length;
                localDir.Y /= length;
                localDir.Z /= length;
            }
        }

        /// <summary>
        /// Transforms a local space hit position back to screen space.
        /// </summary>
        public Vec3d TransformHitToScreen(Vec3d localHitPos, ElementHitbox hitbox)
        {
            Matrixf mvMat = BuildTransformMatrix(hitbox);
            Vec4d hitPos4 = new Vec4d(localHitPos.X, localHitPos.Y, localHitPos.Z, 1);
            var screenHitPos = mvMat.TransformVector(hitPos4);
            return screenHitPos.XYZ;
        }

        /// <summary>
        /// Gets the screen center coordinates.
        /// </summary>
        public void GetScreenCenter(out float centerX, out float centerY)
        {
            if (getScreenPosition != null)
            {
                (centerX, centerY) = getScreenPosition();
            }
            else
            {
                var renderer = capi.Render;
                centerX = renderer.FrameWidth / 2.0f;
                centerY = renderer.FrameHeight / 2.0f;
            }
        }
    }
}
