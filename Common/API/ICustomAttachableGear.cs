using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Vintagestory.API.Common
{
    /// <summary>
    /// Attachable that has custom logic for attaching gear shape to entity.
    /// </summary>
    public interface ICustomAttachableGear
    {
        /// <summary>
        /// Overrides standard method for attaching gears to entity shape in 'EntityBehaviorContainer'
        /// </summary>
        Shape AddGearToShape(Shape entityShape, ItemSlot gearslot, string slotCode, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements, IDictionary<string, CompositeTexture>? collectedTextures);
    }
}
