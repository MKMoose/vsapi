using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Performs raycasting against a 3D mesh to determine UV coordinates from screen clicks.
    /// Also supports AABB-based hitbox detection for precise element selection.
    /// Useful for making clickable 3D UI elements.
    ///
    /// Usage:
    /// 1. Create raycaster: new ModelRayCaster(capi, () => yaw, () => pitch, () => scale, -1000)
    /// 2. Load hitboxes: rayCaster.LoadHitboxes(hitboxes)
    /// 3. On click: rayCaster.RayCastToHitbox(mouseX, mouseY, out ElementHitbox hitbox)
    /// </summary>
    public class ModelRayCaster
    {
        readonly ICoreClientAPI capi;
        readonly Func<float> getYaw;
        readonly Func<float> getPitch;
        readonly Func<float> getScale;
        readonly double nearPlaneZ;
        readonly ModelHitboxTransform transform;

        List<ElementHitbox> hitboxes;

        WireframeCube wireframeCube;
        ElementHitbox lastHitHitbox;

        /// <summary>
        /// Creates a new ModelRayCaster.
        /// </summary>
        /// <param name="capi">Client API</param>
        /// <param name="getYaw">Function returning current Y rotation (yaw) in radians</param>
        /// <param name="getPitch">Function returning current X rotation (pitch) in radians</param>
        /// <param name="getScale">Function returning current model scale</param>
        /// <param name="nearPlaneZ">Z coordinate of the near plane (typically -1000)</param>
        /// <param name="getScreenPosition">Optional function returning screen position (x, y). Defaults to screen center if null.</param>
        /// <param name="flipY">Whether the model Y axis is flipped</param>
        public ModelRayCaster(ICoreClientAPI capi, Func<float> getYaw, Func<float> getPitch, Func<float> getScale,
            double nearPlaneZ, Func<(float, float)> getScreenPosition = null, bool flipY = false)
        {
            this.capi = capi;
            this.getYaw = getYaw;
            this.getPitch = getPitch;
            this.getScale = getScale;
            this.nearPlaneZ = nearPlaneZ;
            transform = new ModelHitboxTransform(capi, getYaw, getPitch, getScale, getScreenPosition, flipY);
            wireframeCube = WireframeCube.CreateUnitCube(capi, ColorUtil.WhiteArgb);
        }

        /// <summary>
        /// Sets the model origin offset (applied in model space after scale).
        /// </summary>
        public void SetModelOriginOffset(float x, float y, float z)
        {
            transform.ModelOriginOffset = (x, y, z);
        }

        /// <summary>
        /// Sets the animator matrices accessor so raycasting accounts for joint animations.
        /// </summary>
        public void SetAnimatorMatrices(Func<float[]> getAnimatorMatrices)
        {
            transform.GetAnimatorMatrices = getAnimatorMatrices;
        }

        /// <summary>
        /// Loads hitboxes for AABB-based raycasting.
        /// </summary>
        public void LoadHitboxes(List<ElementHitbox> hitboxes)
        {
            this.hitboxes = hitboxes;
        }

        /// <summary>
        /// Casts a ray from screen coordinates and tests against loaded AABB hitboxes.
        /// Returns the closest hit hitbox and the hit position.
        /// </summary>
        public bool RayCastToHitbox(int screenX, int screenY, out ElementHitbox hitbox, out Vec3d hitPosition)
        {
            hitbox = null;
            hitPosition = Vec3d.Zero;

            if (hitboxes == null || hitboxes.Count == 0)
            {
                return false;
            }

            Vec3d rayOrigin, rayDir;
            GetMouseRay(screenX, screenY, out rayOrigin, out rayDir);

            double closestDist = double.MaxValue;
            ElementHitbox closestHit = null;
            Vec3d closestHitPosition = Vec3d.Zero;

            foreach (var hb in hitboxes)
            {
                Vec3d hitPos;
                if (RayIntersectsAABB(rayOrigin, rayDir, hb, out hitPos))
                {
                    double dist = (hitPos - rayOrigin).Length();
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestHit = hb;
                        closestHitPosition = hitPos;
                    }
                }
            }

            lastHitHitbox = closestHit;

            hitbox = closestHit;
            hitPosition = closestHitPosition;
            return closestHit != null;
        }

        /// <summary>
        /// Converts a 3D hit position on an AABB to UV coordinates (0-1 range).
        /// </summary>
        public bool HitPositionToUV(Vec3d hitPosition, ElementHitbox hitbox, out Vec2f uv)
        {
            uv = new Vec2f(0, 0);

            Matrixf mvMat = transform.BuildTransformMatrix(hitbox);
            Matrixf mvMatInv = new Matrixf();
            mvMatInv.Set(mvMat.Values).Invert();

            Vec4d hitPos4 = new Vec4d(hitPosition.X, hitPosition.Y, hitPosition.Z, 1);
            var localPos4 = mvMatInv.TransformVector(hitPos4);
            Vec3d localPos = localPos4.XYZ;

            Cuboidd bounds = hitbox.Bounds;
            Vec3d minCorner = new Vec3d(bounds.X1, bounds.Y1, bounds.Z1);
            Vec3d size = new Vec3d(bounds.X2 - bounds.X1, bounds.Y2 - bounds.Y1, bounds.Z2 - bounds.Z1);

            localPos.X -= minCorner.X;
            localPos.Y -= minCorner.Y;
            localPos.Z -= minCorner.Z;

            uv.Y = (float)(localPos.X / size.X);
            uv.X = (float)(localPos.Z / size.Z);

            return true;
        }

        /// <summary>
        /// Renders wireframe boxes for all hitboxes.
        /// </summary>
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage, IShaderProgram shader = null)
        {
            if (hitboxes == null || hitboxes.Count == 0 || wireframeCube == null)
                return;

            foreach (var hitbox in hitboxes)
            {
                Vec4f color = hitbox == lastHitHitbox
                    ? new Vec4f(1, 0, 0, 1)
                    : ColorUtil.WhiteArgbVec;

                hitbox.RenderWireframe(capi, transform, wireframeCube, shader, color);
            }
        }

        /// <summary>
        /// Converts screen coordinates to a 3D ray (origin and direction).
        /// Uses orthographic projection for UI-space raycasting.
        /// </summary>
        public void GetMouseRay(int screenX, int screenY, out Vec3d origin, out Vec3d direction)
        {
            origin = new Vec3d(screenX, screenY, nearPlaneZ * 2);
            direction = new Vec3d(0, 0, -1);
        }

        bool RayIntersectsAABB(Vec3d rayOrigin, Vec3d rayDir, ElementHitbox hitbox, out Vec3d hitPosition)
        {
            hitPosition = new Vec3d();

            Vec3d localRayOrigin, localRayDir;
            transform.TransformRayToLocal(rayOrigin, rayDir, hitbox, out localRayOrigin, out localRayDir);

            Cuboidd bounds = hitbox.Bounds;
            Vec3d localHitPos = new Vec3d();

            bool hit = RayIntersectsWithCuboid(bounds, localRayOrigin, localRayDir, ref localHitPos);

            if (hit)
            {
                Vec3d screenHitPos = transform.TransformHitToScreen(localHitPos, hitbox);
                hitPosition = screenHitPos;
            }

            return hit;
        }

        bool RayIntersectsWithCuboid(Cuboidd box, Vec3d rayOrigin, Vec3d rayDir, ref Vec3d hitPosition)
        {
            double w = box.X2 - box.X1;
            double h = box.Y2 - box.Y1;
            double l = box.Z2 - box.Z1;

            for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
            {
                BlockFacing face = BlockFacing.ALLFACES[i];
                Vec3i planeNormal = face.Normali;

                double d = planeNormal.X * rayDir.X + planeNormal.Y * rayDir.Y + planeNormal.Z * rayDir.Z;

                if (d < -0.00001)
                {
                    Vec3d planeCenterPosition = new Vec3d(
                        face.PlaneCenter[0] * w + box.X1,
                        face.PlaneCenter[1] * h + box.Y1,
                        face.PlaneCenter[2] * l + box.Z1
                    );

                    Vec3d pt = planeCenterPosition - rayOrigin;
                    double t = (pt.X * planeNormal.X + pt.Y * planeNormal.Y + pt.Z * planeNormal.Z) / d;

                    if (t >= 0)
                    {
                        hitPosition.Set(
                            rayOrigin.X + rayDir.X * t,
                            rayOrigin.Y + rayDir.Y * t,
                            rayOrigin.Z + rayDir.Z * t
                        );

                        Vec3d localHitPos = hitPosition - planeCenterPosition;

                        if (Math.Abs(localHitPos.X) <= w / 2 && Math.Abs(localHitPos.Y) <= h / 2 && Math.Abs(localHitPos.Z) <= l / 2)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
