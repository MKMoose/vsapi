using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common
{
    /// <summary>
    /// A collectible that emits custom particles from its rendered position in an entity's hands.
    /// </summary>
    public interface IHeldParticleEmitter
    {
        /// <summary>
        /// Called once per rendered frame, outside shadow passes and while the game is not paused.
        /// </summary>
        /// <param name="stack">The rendered item stack.</param>
        /// <param name="byEntity">The entity holding the stack.</param>
        /// <param name="worldPos">The collectible's TopMiddlePos transformed into world space, including first-person projection adjustments.</param>
        /// <param name="dt">Time since the previous frame, in seconds.</param>
        void SpawnHeldParticles(ItemStack stack, EntityAgent byEntity, Vec3d worldPos, float dt);
    }
}
