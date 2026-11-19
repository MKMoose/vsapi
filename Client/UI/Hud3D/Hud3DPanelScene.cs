using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Reusable 3D panel scene that owns a shape, mesh, animator, raycaster, and
    /// scene-local tessellation state. This avoids mutating shared dialog texture
    /// source state when a dialog needs multiple independently rendered shapes.
    /// </summary>
    public class Hud3DPanelScene : ITexPositionSource, IDisposable
    {
        readonly ICoreClientAPI capi;
        readonly string rendererName;
        readonly string shapePath;
        readonly Func<float> getYaw;
        readonly Func<float> getPitch;
        readonly Func<float> getScale;
        readonly Func<(float x, float y)> getScreenPosition;
        readonly Func<float> getZOffset;
        readonly bool flipY;
        readonly bool centerModelOrigin;
        readonly System.Func<string, bool> isInteractiveHitbox;

        readonly Dictionary<string, AnimationMetaData> activeAnimations = new Dictionary<string, AnimationMetaData>();
        readonly Queue<AnimationMetaData> pendingAnimations = new Queue<AnimationMetaData>();

        ShapeTextureSource textureSource;

        public Shape Shape { get; private set; }
        public MultiTextureMeshRef MeshRef { get; private set; }
        public ClientAnimator Animator { get; private set; }
        public ModelRayCaster RayCaster { get; private set; }
        public int AtlasTextureId { get; private set; }

        public Size2i AtlasSize => capi.ItemTextureAtlas.Size;
        public TextureAtlasPosition this[string textureCode] => textureSource?[textureCode];

        public Hud3DPanelScene(
            ICoreClientAPI capi,
            string rendererName,
            string shapePath,
            Func<float> getYaw,
            Func<float> getPitch,
            Func<float> getScale,
            Func<(float x, float y)> getScreenPosition,
            bool flipY,
            bool centerModelOrigin,
            System.Func<string, bool> isInteractiveHitbox,
            Func<float> getZOffset = null)
        {
            this.capi = capi;
            this.rendererName = rendererName;
            this.shapePath = shapePath;
            this.getYaw = getYaw;
            this.getPitch = getPitch;
            this.getScale = getScale;
            this.getScreenPosition = getScreenPosition;
            this.getZOffset = getZOffset;
            this.flipY = flipY;
            this.centerModelOrigin = centerModelOrigin;
            this.isInteractiveHitbox = isInteractiveHitbox;
        }

        public bool Load()
        {
            try
            {
                Shape = Shape.TryGet(capi, shapePath);
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{rendererName}]: Error loading shape: {ex.Message}");
                return false;
            }

            if (Shape == null)
            {
                capi.Logger.Warning($"[{rendererName}]: Shape is null at {shapePath}");
                return false;
            }

            try
            {
                var elementsByName = Shape.CollectAndResolveReferences(capi.Logger, rendererName);
                Shape.CacheInvTransforms();
                Shape.ResolveAndFindJoints(capi.Logger, rendererName, elementsByName);

                textureSource = new ShapeTextureSource(capi, Shape, rendererName);

                foreach (var kvp in Shape.Textures)
                {
                    if (kvp.Value?.Path?.Length > 0)
                    {
                        capi.ItemTextureAtlas.GetOrInsertTexture(kvp.Value, out _, out var texPos);
                        if (texPos != null)
                        {
                            AtlasTextureId = texPos.atlasTextureId;
                        }
                    }
                }

                var meta = CreateTessellationMeta();
                capi.Tesselator.TesselateShape(meta, Shape, out var meshData);
                MeshRef?.Dispose();
                MeshRef = capi.Render.UploadMultiTextureMesh(meshData);

                if (Shape.Animations != null && Shape.Animations.Length > 0)
                {
                    Animator = new ClientAnimator(
                        () => 1.0,
                        Shape.Animations,
                        Shape.Elements,
                        Shape.JointsById
                    );
                }

                return true;
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{rendererName}]: Error tessellating shape: {ex.Message}");
                return false;
            }
        }

        public void CreateRayCaster()
        {
            if (Shape == null) return;

            RayCaster = new ModelRayCaster(
                capi,
                getYaw,
                getPitch,
                getScale,
                getScale(),
                getScreenPosition,
                flipY
            );

            if (centerModelOrigin)
            {
                RayCaster.SetModelOriginOffset(-0.5f, 0f, -0.5f);
            }

            RayCaster.SetAnimatorMatrices(() => Animator?.Matrices);

            var generator = new ModelHitboxGenerator(capi);
            var hitboxes = generator.ExtractHitboxesFromShape(Shape);
            if (isInteractiveHitbox != null)
            {
                hitboxes.RemoveAll(hb => !isInteractiveHitbox(hb.Name));
            }
            RayCaster.LoadHitboxes(hitboxes);
        }

        public bool RayCastToHitbox(int x, int y, out ElementHitbox hitbox, out Vec3d hitPosition)
        {
            hitbox = null;
            hitPosition = null;
            return RayCaster != null && RayCaster.RayCastToHitbox(x, y, out hitbox, out hitPosition);
        }

        public Matrixf BuildModelMatrix()
        {
            float scale = getScale();
            var (cx, cy) = getScreenPosition();
            float yScale = flipY ? -scale : scale;
            float zOffset = getZOffset?.Invoke() ?? scale;

            var matrix = new Matrixf();
            matrix.Identity();
            matrix.Translate(cx, cy, zOffset);
            matrix.RotateX(getPitch());
            matrix.RotateY(getYaw());
            matrix.Scale(scale, yScale, scale);
            if (centerModelOrigin)
            {
                matrix.Translate(-0.5f, 0f, -0.5f);
            }

            return matrix;
        }

        public void UpdateAnimations(float deltaTime)
        {
            if (Animator != null && (activeAnimations.Count > 0 || Animator.ActiveAnimationCount > 0))
            {
                Animator.OnFrame(activeAnimations, deltaTime);
            }

            while (pendingAnimations.Count > 0 && Animator != null)
            {
                var anim = pendingAnimations.Dequeue();
                activeAnimations[anim.Code] = anim;
            }
        }

        public bool StartAnimation(string code, float speed = 1f, bool stopOthers = false, EnumAnimationBlendMode blendMode = EnumAnimationBlendMode.Average)
        {
            if (Animator == null) return false;

            var meta = new AnimationMetaData()
            {
                Code = code,
                Animation = code,
                AnimationSpeed = speed,
                BlendMode = blendMode
            }.Init();

            if (stopOthers)
            {
                activeAnimations.Clear();
                pendingAnimations.Clear();
            }
            else
            {
                activeAnimations.Remove(code);
            }

            pendingAnimations.Enqueue(meta);
            return true;
        }

        public void StopAnimation(string code)
        {
            activeAnimations.Remove(code);
        }

        public void StopAnimationsWithPrefix(string prefix)
        {
            var toRemove = new List<string>();
            foreach (var key in activeAnimations.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    toRemove.Add(key);
                }
            }

            foreach (var key in toRemove)
            {
                activeAnimations.Remove(key);
            }
        }

        public MultiTextureMeshRef TessellateFiltered(string[] selectiveElements = null, string[] ignoreElements = null)
        {
            if (Shape == null) return null;

            try
            {
                var meta = CreateTessellationMeta(selectiveElements, ignoreElements);
                capi.Tesselator.TesselateShape(meta, Shape, out var meshData);
                return capi.Render.UploadMultiTextureMesh(meshData);
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{rendererName}]: Error in TessellateFiltered: {ex.Message}");
                return null;
            }
        }

        public MultiTextureMeshRef TessellateElementWithTemporaryFaceTexture(string elementName, string elementPath, string faceTextureCode, int faceIndex = 4)
        {
            if (Shape?.Elements == null) return null;

            var element = FindElementByName(Shape.Elements, elementName);
            if (element == null) return null;

            var face = element.FacesResolved?[faceIndex];
            if (face == null) return null;

            string originalTexture = face.Texture;
            try
            {
                face.Texture = faceTextureCode;
                return TessellateFiltered(selectiveElements: new[] { elementPath });
            }
            finally
            {
                face.Texture = originalTexture;
            }
        }

        public void ReplaceMesh(MultiTextureMeshRef mesh)
        {
            MeshRef?.Dispose();
            MeshRef = mesh;
        }

        public void RenderMeshInline(IShaderProgram prog, MultiTextureMeshRef mesh = null, bool applyAnimation = true)
        {
            mesh ??= MeshRef;
            if (mesh == null || prog == null) return;

            try
            {
                ApplyShaderState(prog, applyAnimation);
                capi.Render.RenderMultiTextureMesh(mesh, "tex2d");
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{rendererName}]: Error rendering inline mesh: {ex.Message}");
            }
        }

        public void RenderMeshStandalone(IShaderProgram prog, MultiTextureMeshRef mesh = null, bool applyAnimation = true)
        {
            mesh ??= MeshRef;
            if (mesh == null || prog == null) return;

            var prevProg = capi.Render.CurrentActiveShader;
            prevProg?.Stop();

            try
            {
                capi.Render.GlToggleBlend(true);
                prog.Use();
                ApplyShaderState(prog, applyAnimation);
                capi.Render.RenderMultiTextureMesh(mesh, "tex2d");
                prog.Uniform("applyAnimation", 0);
                prog.Stop();
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[{rendererName}]: Error rendering mesh: {ex.Message}");
            }
            finally
            {
                prevProg?.Use();
            }
        }

        void ApplyShaderState(IShaderProgram prog, bool applyAnimation)
        {
            var renderer = capi.Render;

            prog.UniformMatrix("projectionMatrix", renderer.CurrentProjectionMatrix);
            prog.Uniform("rgbaIn", ColorUtil.WhiteArgbVec);
            prog.Uniform("extraGlow", 0);
            prog.Uniform("applyColor", 0);
            prog.Uniform("noTexture", 0f);

            if (Animator != null && applyAnimation)
            {
                prog.Uniform("applyAnimation", 1);
                prog.UBOs["Animation"].Update(Animator.Matrices, 0, Animator.MaxJointId * 16 * 4);
            }
            else
            {
                prog.Uniform("applyAnimation", 0);
            }

            Matrixf modelMatrix = BuildModelMatrix();
            var mvmat = new float[16];
            Mat4f.Mul(mvmat, renderer.CurrentModelviewMatrix, modelMatrix.Values);
            prog.UniformMatrix("modelViewMatrix", mvmat);

            if (AtlasTextureId != 0)
            {
                renderer.BindTexture2d(AtlasTextureId);
                prog.Uniform("tex2d", 0);
            }
        }

        TesselationMetaData CreateTessellationMeta(string[] selectiveElements = null, string[] ignoreElements = null)
        {
            return new TesselationMetaData()
            {
                TexSource = this,
                WithJointIds = true,
                WithDamageEffect = true,
                TypeForLogging = rendererName,
                SelectiveElements = selectiveElements,
                IgnoreElements = ignoreElements
            };
        }

        public static ShapeElement FindElementByName(ShapeElement[] elements, string name)
        {
            if (elements == null) return null;
            foreach (var elem in elements)
            {
                if (elem?.Name == name) return elem;
                var found = FindElementByName(elem?.Children, name);
                if (found != null) return found;
            }

            return null;
        }

        public void Dispose()
        {
            MeshRef?.Dispose();
        }
    }
}
