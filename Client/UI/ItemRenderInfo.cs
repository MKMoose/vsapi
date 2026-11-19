using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Holds information on how a collectible should be rendered in hands, on the ground or in gui.
    /// In collectible.OnBeforeRender() the values are prefilled with the default ones.
    /// </summary>
    public class ItemRenderInfo
    {
        /// <summary>
        /// The model to be rendered
        /// </summary>
        public MultiTextureMeshRef ModelRef;
        /// <summary>
        /// A version of ModelRef tesselated for the animated entity shader.
        /// </summary>
        public MultiTextureMeshRef AnimationModelRef;
        /// <summary>
        /// The shape used to animate AnimationModelRef.
        /// </summary>
        public Shape AnimationShape;
        /// <summary>
        /// The selected shape variant used to create AnimationModelRef.
        /// </summary>
        public int AnimationModelVariant;
        /// <summary>
        /// An item stack to render on an element of AnimationShape while it is animated.
        /// </summary>
        public ItemStack AnimatedAttachmentStack;
        /// <summary>
        /// The animated shape element to render AnimationAttachmentStack on.
        /// </summary>
        public string AnimatedAttachmentElement;
        /// <summary>
        /// Local transform applied to AnimationAttachmentStack after the animated element transform.
        /// </summary>
        public ModelTransform AnimatedAttachmentTransform;
        /// <summary>
        /// The transform to be applied when rendered
        /// </summary>
        public ModelTransform Transform;
        /// <summary>
        /// Wether or not to cull back faces
        /// </summary>
        public bool CullFaces;
        /// <summary>
        /// The texture to be used when rendering. Should probalby be the texture id of the block or item texture atlas
        /// </summary>
        public int TextureId;
        public Size2i TextureSize = new Size2i();
        /// <summary>
        /// For discarding fragments with alpha value below this threshold
        /// </summary>
        public float AlphaTest;
        /// <summary>
        /// (Currently) not used.
        /// </summary>
        public bool HalfTransparent;

        public bool NormalShaded;

        public bool ApplyColor;

        public LoadedTexture OverlayTexture;

        public float OverlayOpacity;

        public float DamageEffect = 0;

        /// <summary>
        /// The slot the stack is currently in
        /// </summary>
        public ItemSlot InSlot;

        public float dt;

        public void SetRotOverlay(ICoreClientAPI capi, float opacity)
        {
            if (OverlayTexture == null) OverlayTexture = new LoadedTexture(capi);
            
            capi.Render.GetOrLoadTexture(new AssetLocation("textures/gui/rot.png"), ref OverlayTexture);

            this.OverlayOpacity = opacity;
        }
    }

}
