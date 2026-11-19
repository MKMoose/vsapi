using System;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common;

public interface IAssetCollection
{
    IEnumerable<Type> GetRegisteredVariantTypes();
    IEnumerable<Type> GetRegisteredElementTypes();
    bool TryGetVariantType(string discriminator, out Type variantType);
    bool TryGetElementType(string discriminator, out Type type);
}

/// <summary>
/// Represents a collection of polymorphic asset types.
/// </summary>
/// <param name="logger"></param>
/// <typeparam name="TAssetInterface">The base asset type this collection is for</typeparam>
public sealed class AssetCollection<TAssetInterface>(ILogger? logger) : IAssetCollection
    where TAssetInterface : IBuildableAsset<IAssetInstance>
{
    private readonly Dictionary<string, Type> variantTypes = new();
    private readonly Dictionary<string, Type> elementTypes = new();
    private readonly Dictionary<string, TAssetInterface> assets = new(StringComparer.Ordinal);

    private Type AssetInterface => typeof(TAssetInterface);

    public Action<Entity?, TAssetInterface> OnAssetBuilt;

    public AssetCollection<TAssetInterface> RegisterVariant<TAssetVariant>()
        where TAssetVariant : TAssetInterface
    {
        var discriminator = typeof(TAssetVariant).Name;
        if (variantTypes.ContainsKey(discriminator))
        {
            logger?.Warning($"Attempting to register asset variant with discriminator '{discriminator}' multiple times.");
            return this;
        }

        var variantType = typeof(TAssetVariant);
        variantTypes[discriminator] = variantType;
        return this;
    }

    public IEnumerable<Type> GetRegisteredVariantTypes() => variantTypes.Values;

    public bool TryGetVariantType(string discriminator, out Type variantType) => variantTypes.TryGetValue(discriminator, out variantType);

    public AssetCollection<TAssetInterface> RegisterElement<TElement>()
    {
        var type = typeof(TElement);
        var discriminator = type.Name;

        if (!elementTypes.TryAdd(discriminator, type))
        {
            logger?.Warning($"Attempting to register element with discriminator '{discriminator}' multiple times.");
        }

        return this;
    }

    public IEnumerable<Type> GetRegisteredElementTypes() => elementTypes.Values;

    public bool TryGetElementType(string discriminator, out Type type) => elementTypes.TryGetValue(discriminator, out type);

    public void AddAsset<TAssetVariant>(TAssetVariant asset, bool replaceExisting = false)
        where TAssetVariant : TAssetInterface
    {
        if (!variantTypes.ContainsValue(asset.GetType()))
        {
            logger?.Error($"Asset of type '{asset.GetType().Name}' is not registered. Register it in the {AssetInterface.Name} collection first.");
            return;
        }

        var assetId = GetAssetId(asset);
        if (assets.ContainsKey(assetId) && !replaceExisting)
        {
            logger?.Warning($"Asset with ID '{assetId}' already registered and will not be replaced.");
            return;
        }

        assets[assetId] = asset;
    }

    public bool TryGetAsset(string id, out TAssetInterface asset)
    {
        return assets.TryGetValue(id, out asset!);
    }

    /// <summary>
    /// Attempts to build an asset by asset ID.
    /// </summary>
    /// <typeparam name="TInstance">The asset instance type.</typeparam>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="instance">The built instance.</param>
    /// <returns>Bool indicating success.</returns>
    public bool TryBuildAsset<TInstance>(string assetId, out TInstance instance)
    {
        if (!TryGetAsset(assetId, out var asset))
        {
            logger?.Error($"Could not find asset with ID '{assetId}'");
            instance = default!;
            return false;
        }

        return TryBuildAsset(asset, out instance);
    }

    /// <summary>
    /// Attempts to build an asset by asset ID for a specific entity.
    /// </summary>
    /// <typeparam name="TInstance">The asset instance type.</typeparam>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="entity">The entity that owns the instance.</param>
    /// <param name="entityJson">The subobject (from the entity's JSON data) that references this asset (if any). This can be used to override or configure certain aspects of the asset on a per-entity basis.</param>
    /// <param name="instance">The built instance.</param>
    /// <returns>Bool indicating success.</returns>
    public bool TryBuildAsset<TInstance>(string assetId, Entity entity, JsonObject entityJson, out TInstance instance)
    {
        if (!TryGetAsset(assetId, out var asset))
        {
            logger?.Error($"Could not find asset with ID '{assetId}'");
            instance = default!;
            return false;
        }

        return TryBuildAsset(asset, entity, entityJson, out instance);
    }

    private bool TryBuildAsset<TInstance>(TAssetInterface asset, out TInstance instance)
    {
        try
        {
            instance = (TInstance)asset.Build();
            OnAssetBuilt?.Invoke(null, asset);
            return instance == null ? throw new Exception() : true;
        }
        catch (Exception exception)
        {
            throw new Exception(
                $"Error while building instance from asset '{GetAssetId(asset)}':\n{exception}",
                exception
            );
        }
    }

    private bool TryBuildAsset<TInstance>(TAssetInterface asset, Entity entity, JsonObject entityJson, out TInstance instance)
    {
        try
        {
            instance = (TInstance)asset.Build(entity, entityJson);
            OnAssetBuilt?.Invoke(entity, asset);
            return instance == null ? throw new Exception() : true;
        }
        catch (Exception exception)
        {
            throw new Exception(
                $"Error while building instance from asset '{GetAssetId(asset)}' for '{entity.Code}':\n{exception}",
                exception
            );
        }
    }

    private static string GetAssetId<TAssetVariant>(TAssetVariant asset)
        where TAssetVariant : TAssetInterface
    {
        var prop = typeof(TAssetVariant).GetProperty(nameof(IBuildableAsset<>.Id));
        if (prop == null)
        {
            throw new InvalidOperationException(
                $"Type '{typeof(TAssetVariant).Name}' must expose an Id property."
            );
        }

        return (string)(prop.GetValue(asset) ?? throw new InvalidOperationException("Asset Id cannot be null."));
    }
}
