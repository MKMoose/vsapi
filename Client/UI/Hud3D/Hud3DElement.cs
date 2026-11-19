using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Abstract base class for HUD elements rendered as 3D models in orthographic projection.
    /// </summary>
    public abstract class Hud3DElement : HudElement, IRenderer, ITexPositionSource
    {
        public double RenderOrder => 0.2;
        public int RenderRange => 1;

        protected Shape shape;
        protected MultiTextureMeshRef meshRef;
        protected ShapeTextureSource shapeTextureSource;

        protected Matrixf modelMatrix;
        protected IShaderProgram prog;
        protected bool shouldRender;

        protected const float OverlayMaxAlpha = 0.5f;

        LoadedTexture overlayTexture;

        protected ClientAnimator animator;
        protected Dictionary<string, AnimationMetaData> activeAnimationsByAnimCode = new Dictionary<string, AnimationMetaData>();
        protected Queue<AnimationMetaData> pendingAnimations = new Queue<AnimationMetaData>();

        protected float yaw = GameMath.PIHALF;
        protected float pitch = 0f;

        Size2i atlasSize;
        protected int atlasTextureId;

        public Size2i AtlasSize => atlasSize;

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                var dynamicPos = GetDynamicTexturePosition(textureCode);
                if (dynamicPos != null)
                {
                    atlasSize = GetDynamicAtlasSize(textureCode);
                    return dynamicPos;
                }

                atlasSize = capi.ItemTextureAtlas.Size;
                return shapeTextureSource?[textureCode];
            }
        }

        protected abstract string ShapePath { get; }
        protected abstract string RendererName { get; }

        /// <summary>
        /// We should aim for 100 scale as it fits best on the UI
        /// but other scales are supported for special elements or
        /// elements that require manual rescaling.
        /// </summary>
        protected virtual float GetModelScale()
        {
            return (float)GuiElement.scaled(100);
        }

        protected virtual (float x, float y) GetModelScreenPosition()
        {
            float screenWidth = capi.Render.FrameWidth;
            float screenHeight = capi.Render.FrameHeight;
            return (screenWidth / 2f, screenHeight / 2f);
        }

        protected virtual bool FlipY => true;

        /// <summary>
        /// If true, offsets the model by -0.5, 0, -0.5 (in model space) to center it.
        /// VS block/shape models have their origin at (8,0,8) in pixel coords = (0.5, 0, 0.5) in units.
        /// </summary>
        protected virtual bool CenterModelOrigin => false;

        protected virtual TextureAtlasPosition GetDynamicTexturePosition(string textureCode) => null;
        protected virtual Size2i GetDynamicAtlasSize(string textureCode) => capi.ItemTextureAtlas.Size;

        /// <summary>
        /// Z position for the model in orthographic space.
        /// Only affects depth clipping, not apparent size.
        /// </summary>
        protected virtual float GetModelZOffset() => 100f;
        protected virtual void OnShapeTessellated() { }
        protected virtual void OnPreRender3D(float deltaTime) { }
        protected virtual void OnBeforeRenderMesh() { }
        protected virtual void OnPostRender3D(float deltaTime, EnumRenderStage stage) { }
        protected virtual bool ShouldRenderMesh() => true;

        public Hud3DElement(ICoreClientAPI capi) : base(capi)
        {
            capi.Event.RegisterRenderer(this, EnumRenderStage.Ortho, RendererName);
            modelMatrix = new Matrixf();

            capi.Event.ReloadShader += LoadShader;
            LoadShader();
        }

        bool LoadShader()
        {
            try
            {
                prog = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
                return prog != null;
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{RendererName}]: Error loading shader: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads shape, inserts textures into atlas, tessellates, uploads mesh, creates animator.
        /// </summary>
        protected void LoadAndTessellateShape()
        {
            try
            {
                shape = Shape.TryGet(capi, new AssetLocation(ShapePath));
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{RendererName}]: Error loading shape: {ex.Message}");
                return;
            }

            if (shape == null)
            {
                capi.Logger.Warning($"[{RendererName}]: Shape is null at {ShapePath}");
                return;
            }

            try
            {
                string typeForLogging = RendererName;
                var elementsByName = shape.CollectAndResolveReferences(capi.Logger, typeForLogging);
                shape.CacheInvTransforms();
                shape.ResolveAndFindJoints(capi.Logger, typeForLogging, elementsByName);

                shapeTextureSource = new ShapeTextureSource(capi, shape, RendererName);

                foreach (var kvp in shape.Textures)
                {
                    if (kvp.Value?.Path?.Length > 0)
                    {
                        capi.ItemTextureAtlas.GetOrInsertTexture(kvp.Value, out _, out var texPos);
                        atlasTextureId = texPos.atlasTextureId;
                    }
                }

                TesselationMetaData meta = new TesselationMetaData()
                {
                    TexSource = this,
                    WithJointIds = true,
                    WithDamageEffect = true,
                    TypeForLogging = typeForLogging
                };
                capi.Tesselator.TesselateShape(meta, shape, out var meshData);

                meshRef?.Dispose();
                meshRef = capi.Render.UploadMultiTextureMesh(meshData);

                if (shape.Animations != null && shape.Animations.Length > 0)
                {
                    animator = new ClientAnimator(
                        () => 1.0,
                        shape.Animations,
                        shape.Elements,
                        shape.JointsById
                    );
                }

                OnShapeTessellated();
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{RendererName}]: Error tessellating shape: {ex.Message}");
            }
        }

        /// <summary>
        /// Tessellates the current shape with element name filters.
        /// Uses the already-resolved shape (joints, textures, etc.) from LoadAndTessellateShape.
        /// </summary>
        /// <param name="selectiveElements">If set, only these element names are tessellated.</param>
        /// <param name="ignoreElements">If set, these element names are excluded.</param>
        /// <returns>Uploaded mesh ref, or null on failure. Caller owns disposal.</returns>
        protected MultiTextureMeshRef TessellateFiltered(string[] selectiveElements = null, string[] ignoreElements = null)
        {
            if (shape == null) return null;

            try
            {
                var meta = new TesselationMetaData()
                {
                    TexSource = this,
                    WithJointIds = true,
                    WithDamageEffect = true,
                    TypeForLogging = RendererName,
                    SelectiveElements = selectiveElements,
                    IgnoreElements = ignoreElements
                };
                capi.Tesselator.TesselateShape(meta, shape, out var meshData);
                return capi.Render.UploadMultiTextureMesh(meshData);
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{RendererName}]: Error in TessellateFiltered: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Renders any MultiTextureMeshRef using the current model transform, shader, and animation state.
        /// </summary>
        protected void RenderMeshWithCurrentState(MultiTextureMeshRef mesh, bool applyAnimation = true)
        {
            if (mesh == null || prog == null) return;

            var prevProg = capi.Render.CurrentActiveShader;
            prevProg?.Stop();

            try
            {
                var renderer = capi.Render;
                float scale = GetModelScale();
                var (centerX, centerY) = GetModelScreenPosition();

                renderer.GlToggleBlend(true);
                prog.Use();

                prog.UniformMatrix("projectionMatrix", renderer.CurrentProjectionMatrix);
                prog.Uniform("rgbaIn", ColorUtil.WhiteArgbVec);
                prog.Uniform("extraGlow", 0);
                prog.Uniform("applyColor", 0);
                prog.Uniform("noTexture", 0f);

                if (animator != null && applyAnimation)
                {
                    prog.Uniform("applyAnimation", 1);
                    prog.UBOs["Animation"].Update(animator.Matrices, 0, animator.MaxJointId * 16 * 4);
                }
                else
                {
                    prog.Uniform("applyAnimation", 0);
                }

                modelMatrix.Identity();
                modelMatrix.Translate(centerX, centerY, GetModelZOffset());
                modelMatrix.RotateX(pitch);
                modelMatrix.RotateY(yaw);
                float yScale = FlipY ? -scale : scale;
                modelMatrix.Scale(scale, yScale, scale);
                if (CenterModelOrigin)
                {
                    modelMatrix.Translate(-0.5f, 0f, -0.5f);
                }

                var mvmat = new float[16];
                Mat4f.Mul(mvmat, renderer.CurrentModelviewMatrix, modelMatrix.Values);
                prog.UniformMatrix("modelViewMatrix", mvmat);

                if (atlasTextureId != 0)
                {
                    renderer.BindTexture2d(atlasTextureId);
                    prog.Uniform("tex2d", 0);
                }

                renderer.RenderMultiTextureMesh(mesh, "tex2d");

                prog.Uniform("applyAnimation", 0);

                prog.Stop();
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{RendererName}]: Error in RenderMeshWithCurrentState: {ex.Message}");
            }
            finally
            {
                prevProg?.Use();
            }
        }

        /// <summary>
        /// Renders a GuiComposer into a framebuffer, saving and restoring GL state.
        /// </summary>
        protected void RenderComposerToFbo(GuiComposer composer, FrameBufferRef fbo, RawTexture colorTexture, float deltaTime)
        {
            if (fbo == null || composer == null || prog == null) return;
            if (!composer.Composed) return;

            try
            {
                if (composer.Bounds == null || composer.Bounds.ChildBounds == null) return;
            }
            catch
            {
                return;
            }

            var prevProg = capi.Render.CurrentActiveShader;
            prevProg?.Stop();

            var prevFb = capi.Render.CurrentFrameBuffer;
            float prevWidth = capi.Render.FrameWidth;
            float prevHeight = capi.Render.FrameHeight;

            try
            {
                prog.Use();
                capi.Render.GlViewport(0, 0, colorTexture.Width, colorTexture.Height);
                capi.Render.OrthoMode(colorTexture.Width, colorTexture.Height);
                capi.Render.CurrentFrameBuffer = fbo;
                capi.Render.ClearFrameBuffer(fbo, new float[] { 0, 0, 0, 0 }, true, true);

                composer.Render(deltaTime);
                composer.PostRender(deltaTime);
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{RendererName}]: Error rendering composer to FBO: {ex.Message}");
            }
            finally
            {
                capi.Render.CurrentFrameBuffer = prevFb;
                capi.Render.GlViewport(0, 0, (int)prevWidth, (int)prevHeight);
                capi.Render.PerspectiveMode();

                prog.Stop();
                prevProg?.Use();
            }
        }

        public bool StartAnimation(string code, float speed = 1f, bool stopOthers = false, EnumAnimationBlendMode blendMode = EnumAnimationBlendMode.Average)
        {
            if (animator == null) return false;

            var meta = new AnimationMetaData()
            {
                Code = code,
                Animation = code,
                AnimationSpeed = speed,
                BlendMode = blendMode
            }.Init();

            if (stopOthers)
            {
                activeAnimationsByAnimCode.Clear();
                pendingAnimations.Clear();
            }
            else
            {
                activeAnimationsByAnimCode.Remove(code);
            }

            pendingAnimations.Enqueue(meta);
            return true;
        }

        public void StopAnimation(string code)
        {
            activeAnimationsByAnimCode.Remove(code);
        }

        /// <summary>
        /// Stops all animations whose code starts with the given prefix.
        /// </summary>
        public void StopAnimationsWithPrefix(string prefix)
        {
            var toRemove = new List<string>();
            foreach (var key in activeAnimationsByAnimCode.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                activeAnimationsByAnimCode.Remove(key);
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!shouldRender) return;
            if (meshRef == null) return;
            if (prog == null) return;
            if (stage != EnumRenderStage.Ortho) return;

            try
            {
                if (animator != null && (activeAnimationsByAnimCode.Count > 0 || animator.ActiveAnimationCount > 0))
                {
                    animator.OnFrame(activeAnimationsByAnimCode, deltaTime);
                }

                while (pendingAnimations.Count > 0 && animator != null)
                {
                    var anim = pendingAnimations.Dequeue();
                    activeAnimationsByAnimCode[anim.Code] = anim;
                }

                OnPreRender3D(deltaTime);

                var renderer = capi.Render;
                float scale = GetModelScale();
                var (centerX, centerY) = GetModelScreenPosition();

                renderer.GlToggleBlend(true);
                renderer.GLDepthMask(true);
                capi.Render.GlClearDepthBuffer();
                prog.Use();

                prog.UniformMatrix("projectionMatrix", renderer.CurrentProjectionMatrix);
                prog.Uniform("rgbaIn", ColorUtil.WhiteArgbVec);
                prog.Uniform("extraGlow", 0);
                prog.Uniform("applyColor", 0);
                prog.Uniform("noTexture", 0f);

                if (animator != null)
                {
                    prog.Uniform("applyAnimation", 1);
                    prog.UBOs["Animation"].Update(animator.Matrices, 0, animator.MaxJointId * 16 * 4);
                }

                modelMatrix.Identity();

                modelMatrix.Translate(centerX, centerY, GetModelZOffset());
                modelMatrix.RotateX(pitch);
                modelMatrix.RotateY(yaw);
                float yScale = FlipY ? -scale : scale;
                modelMatrix.Scale(scale, yScale, scale);
                if (CenterModelOrigin)
                {
                    modelMatrix.Translate(-0.5f, 0f, -0.5f);
                }

                var mvmat = new float[16];
                Mat4f.Mul(mvmat, capi.Render.CurrentModelviewMatrix, modelMatrix.Values);
                prog.UniformMatrix("modelViewMatrix", mvmat);

                if (atlasTextureId != 0)
                {
                    renderer.BindTexture2d(atlasTextureId);
                    prog.Uniform("tex2d", 0);
                }

                OnBeforeRenderMesh();

                if (ShouldRenderMesh())
                {
                    renderer.RenderMultiTextureMesh(meshRef, "tex2d");
                }

                OnPostRender3D(deltaTime, stage);

                if (animator != null)
                {
                    prog.Uniform("applyAnimation", 0);
                }

                // Clear depth buffer after all rendering so our depth values
                // don't interfere with item rendering in OnRenderGUI or other elements.
                capi.Render.GlClearDepthBuffer();
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{RendererName}]: Error rendering: {ex.Message}");
            }
        }

        protected void RenderDimOverlay(float alpha)
        {
            if (alpha <= 0f) return;

            if (overlayTexture == null)
            {
                overlayTexture = capi.Gui.Icons.GenTexture(1, 1, (ctx, surface) =>
                {
                    ctx.SetSourceRGBA(1, 1, 1, 1);
                    ctx.Rectangle(0, 0, 1, 1);
                    ctx.Fill();
                });
            }

            capi.Render.Render2DTexture(
                overlayTexture.TextureId,
                0, 0,
                capi.Render.FrameWidth, capi.Render.FrameHeight,
                50, new Vec4f(0, 0, 0, alpha)
            );
        }

        public override void Dispose()
        {
            base.Dispose();
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            meshRef?.Dispose();
            overlayTexture?.Dispose();
        }
    }
}
