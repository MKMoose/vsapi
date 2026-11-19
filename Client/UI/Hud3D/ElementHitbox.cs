using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Represents an AABB hitbox for a model element with rendering capabilities.
    /// </summary>
    public class ElementHitbox
    {
        /// <summary>Full hierarchical path (e.g. "Root/Book/Spine/BookRight/Bookmarks/Bookmark1")</summary>
        public string FullPath { get; set; }

        /// <summary>Simple element name (e.g. "Bookmark1")</summary>
        public string Name { get; set; }

        /// <summary>AABB bounds in model space (before scale/rotation/translation)</summary>
        public Cuboidd Bounds { get; set; }

        /// <summary>Accumulated transformation matrix from all parent elements</summary>
        public Matrixf Transform { get; set; }

        /// <summary>Joint ID for animation-aware raycasting</summary>
        public int JointId { get; set; }

        public ElementHitbox(string name, string fullPath, Cuboidd bounds, Matrixf transform)
        {
            Name = name;
            FullPath = fullPath;
            Bounds = bounds;
            Transform = transform;
        }

        /// <summary>
        /// Renders the hitbox wireframe using the provided transformation.
        /// </summary>
        public void RenderWireframe(ICoreClientAPI capi, ModelHitboxTransform transform, WireframeCube wireframeCube,
            IShaderProgram shader, Vec4f color)
        {
            Matrixf mvMat = transform.BuildFullTransformMatrix(this);

            var finalMat = new float[16];
            Mat4f.Mul(finalMat, capi.Render.CurrentModelviewMatrix, mvMat.Values);

            RenderCube(wireframeCube, capi, finalMat, shader, 1.0f, color);
        }

        void RenderCube(WireframeCube wireframeCube, ICoreClientAPI capi, float[] mvMatrix,
            IShaderProgram shader, float lineWidth = 1.6f, Vec4f color = null)
        {
            if (shader == null) return;

            capi.Render.LineWidth = lineWidth;
            capi.Render.GLEnableDepthTest();
            capi.Render.GLDepthMask(false);
            capi.Render.GlToggleBlend(true);

            shader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", mvMatrix);
            shader.Uniform("rgbaIn", color ?? ColorUtil.WhiteArgbVec);
            shader.Uniform("extraGlow", 0);
            shader.Uniform("applyColor", 0);
            shader.Uniform("noTexture", 1f);

            capi.Render.RenderMesh(wireframeCube.modelRef);

            if (lineWidth != 1.6f)
            {
                capi.Render.LineWidth = 1.6f;
            }

            if (RuntimeEnv.OS != OS.Mac)
            {
                capi.Render.GLDepthMask(true);
            }
        }
    }
}
