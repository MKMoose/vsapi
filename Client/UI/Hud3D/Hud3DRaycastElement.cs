using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Abstract middle layer that adds raycaster-based mouse interaction to a 3D HUD element.
    /// Extracts hitboxes from the shape, raycasts on mouse events, and dispatches to virtual handlers.
    /// </summary>
    public abstract class Hud3DRaycastElement : Hud3DElement
    {
        protected ModelRayCaster rayCaster;
        protected bool debugRenderHitboxes = false;

        public Hud3DRaycastElement(ICoreClientAPI capi) : base(capi) { }

        /// <summary>
        /// Creates the raycaster from the current shape. Call after LoadAndTessellateShape().
        /// </summary>
        protected void CreateRayCaster()
        {
            if (shape == null) return;

            rayCaster = new ModelRayCaster(
                capi,
                () => yaw,
                () => pitch,
                () => GetModelScale(),
                GetModelScale(),
                () => GetModelScreenPosition(),
                FlipY
            );

            if (CenterModelOrigin)
            {
                rayCaster.SetModelOriginOffset(-0.5f, 0f, -0.5f);
            }

            rayCaster.SetAnimatorMatrices(() => animator?.Matrices);

            var generator = new ModelHitboxGenerator(capi);
            var hitboxes = generator.ExtractHitboxesFromShape(shape);
            FilterHitboxes(hitboxes);
            rayCaster.LoadHitboxes(hitboxes);
        }

        /// <summary>
        /// Override to filter out unwanted hitboxes before they are loaded into the raycaster.
        /// </summary>
        protected virtual void FilterHitboxes(List<ElementHitbox> hitboxes) { }

        public override void OnMouseDown(MouseEvent args)
        {
            if (args.Handled) return;
            if (rayCaster == null) return;

            ElementHitbox hitElement;
            Vec3d hitPosition;
            if (rayCaster.RayCastToHitbox(args.X, args.Y, out hitElement, out hitPosition))
            {
                OnHitboxMouseDown(hitElement, args);
                args.Handled = true;
            }
        }

        public override void OnMouseUp(MouseEvent args)
        {
            if (rayCaster == null) return;

            ElementHitbox hitElement;
            Vec3d hitPosition;
            if (rayCaster.RayCastToHitbox(args.X, args.Y, out hitElement, out hitPosition))
            {
                OnHitboxMouseUp(hitElement, args);
                args.Handled = true;
            }
        }

        public override void OnMouseMove(MouseEvent args)
        {
            if (rayCaster == null) return;

            ElementHitbox hitElement;
            Vec3d hitPosition;
            if (rayCaster.RayCastToHitbox(args.X, args.Y, out hitElement, out hitPosition))
            {
                OnHitboxMouseMove(hitElement, args);
                args.Handled = true;
            }
            else
            {
                OnMouseMoveNoHit(args);
            }
        }

        /// <summary>Called when a mouse-down hits a hitbox.</summary>
        protected virtual void OnHitboxMouseDown(ElementHitbox hitbox, MouseEvent args) { }

        /// <summary>Called when a mouse-up hits a hitbox.</summary>
        protected virtual void OnHitboxMouseUp(ElementHitbox hitbox, MouseEvent args) { }

        /// <summary>Called when a mouse-move hits a hitbox.</summary>
        protected virtual void OnHitboxMouseMove(ElementHitbox hitbox, MouseEvent args) { }

        /// <summary>Called when a mouse-move misses all hitboxes.</summary>
        protected virtual void OnMouseMoveNoHit(MouseEvent args) { }

        protected override void OnPostRender3D(float deltaTime, EnumRenderStage stage)
        {
            if (debugRenderHitboxes)
            {
                rayCaster?.OnRenderFrame(deltaTime, stage, prog);
            }
        }
    }
}
