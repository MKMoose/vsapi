using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vintagestory.API.Common;

/// <summary>
/// JSON converter for polymorphic asset element definitions.
/// </summary>
/// <typeparam name="TAsset">The asset type</typeparam>
/// <typeparam name="TDefinition">The element definition type</typeparam>
public class AssetElementJsonConverter<TAsset, TDefinition> : JsonConverter
    where TAsset : IBuildableAsset<IAssetInstance>
{
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader, JsonUtil.DefaultLoadSettings);
        if (!obj.TryGetValue("$type", out var typeValue))
        {
            throw new JsonException("Missing '$type' discriminator.");
        }

        if (!AssetRegistry.Collection<TAsset>().TryGetElementType(typeValue.Value<string>(), out var type))
        {
            throw new JsonException($"Unknown asset element discriminator '{typeValue.Value<string>()}'");
        }

        var target = (TDefinition)Activator.CreateInstance(type)!;
        serializer.Populate(obj.CreateReader(), target);
        return target;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }

    public override bool CanConvert(Type objectType) => typeof(TDefinition).IsAssignableFrom(objectType);
    public override bool CanWrite => false;
}
