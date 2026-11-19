using System.Collections.Concurrent;
using Vintagestory.API.Common;

namespace Vintagestory.API.Util;

public static class ShapeCache
{
    private static readonly ConcurrentDictionary<AssetLocation, Shape> shapes = new();

    // Resolves references on load so ShapeElement.ParentElement is populated.
    public static Shape Load(ICoreAPI api, AssetLocation shapeBase)
    {
        var fullPath = shapeBase.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
        if (shapes.TryGetValue(fullPath, out var cached)) return cached;

        var shape = api.Assets.TryGet(fullPath)?.ToObject<Shape>();
        if (shape == null) return null;

        shape.ResolveReferences(api.Logger, fullPath.ToShortString());
        return shapes.GetOrAdd(fullPath, shape);
    }
}
