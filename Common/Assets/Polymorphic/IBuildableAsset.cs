using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common;

/// <summary>
/// Interface for buildable asset types.
/// </summary>
/// <typeparam name="TInstance">The instance type that is built from this asset</typeparam>
public interface IBuildableAsset<out TInstance>
{
    string Id { get; set; }

    /// <summary>
    /// Builds a TInstance from this asset.
    /// </summary>
    public TInstance Build()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Builds a TInstance from this asset for a specific entity.
    /// </summary>
    /// <param name="entity">The entity that owns the instance</param>
    /// <param name="entityJson">The subobject (from the entity's JSON data) that references this asset (if any). This can be used to override or configure certain aspects of the asset on a per-entity basis.</param>
    /// <returns>The built instance</returns>
    public TInstance Build(Entity entity, JsonObject entityJson)
    {
        throw new NotImplementedException();
    }
}
