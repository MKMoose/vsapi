using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vintagestory.API.Common;

/// <summary>
/// Manages registration, retrieval, and building of polymorphic asset types.
/// </summary>
public static class AssetRegistry
{
    public static ILogger? Logger;
    private static readonly Dictionary<Type, IAssetCollection> collections = new();

    public static void Clear()
    {
        collections.Clear();
    }

    public static AssetCollection<TAssetInterface> RegisterCollection<TAssetInterface>()
        where TAssetInterface : IBuildableAsset<IAssetInstance>
    {
        if (collections.TryGetValue(typeof(TAssetInterface), out var existing))
        {
            Logger?.Warning($"Attempting to register duplicate asset collection for type '{typeof(TAssetInterface).Name}'.");
            return (AssetCollection<TAssetInterface>)existing;
        }

        var collection = new AssetCollection<TAssetInterface>(Logger);
        collections[typeof(TAssetInterface)] = collection;
        return collection;
    }

    public static AssetCollection<TDefinition> Collection<TDefinition>()
        where TDefinition : IBuildableAsset<IAssetInstance>
    {
        var definitionType = typeof(TDefinition);
        if (!collections.TryGetValue(definitionType, out var existing))
        {
            Logger?.Warning($"Attempting to access nonexistent asset collection for type '{definitionType.Name}'.");
            return null;
        }

        return (AssetCollection<TDefinition>)existing;
    }

    public static bool TryGetCollectionFor(Type assetInterface, out IAssetCollection collection) => collections.TryGetValue(assetInterface, out collection);
    public static bool TryGetCollectionFor<TDefinition>(out IAssetCollection collection) => collections.TryGetValue(typeof(TDefinition), out collection);

    public static bool TryGetPolymorphicElementsFor(Type assetInterface, out IReadOnlyList<Type> elementTypes)
    {
        if (TryGetCollectionFor(assetInterface, out var collection))
        {
            var types = new List<Type>();

            foreach (var elementType in collection.GetRegisteredElementTypes())
            {
                types.Add(elementType);
            }

            elementTypes = types;
            return true;
        }

        elementTypes = [];
        return false;
    }

    public static IReadOnlyDictionary<string, JObject> BuildJsonSchemas()
    {
        var result = new Dictionary<string, JObject>(StringComparer.Ordinal);

        foreach (var (interfaceType, collection) in collections)
        {
            foreach (var variantType in collection.GetRegisteredVariantTypes())
            {
                var generator = new AssetSchemaGenerator(interfaceType, variantType);
                var schema = generator.Generate();
                var fileName = $"{interfaceType.Name}.{variantType.Name}.schema.json";
                result[fileName] = schema;
            }
        }

        return result;
    }

    public static void WriteJsonSchemasToDisk(string path)
    {
        var schemas = BuildJsonSchemas();
        Directory.CreateDirectory(path);
        foreach (var (fileName, schema) in schemas)
            File.WriteAllText(Path.Combine(path, fileName), schema.ToString(Formatting.Indented));
    }
}
