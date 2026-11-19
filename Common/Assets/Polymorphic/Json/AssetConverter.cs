using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vintagestory.API.Common;

/// <summary>
/// JSON converter for polymorphic asset definitions.
/// </summary>
/// <typeparam name="T">The asset type</typeparam>
public class AssetConverter<T> : JsonConverter
    where T : IBuildableAsset<IAssetInstance>
{
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader, JsonUtil.DefaultLoadSettings);
        if (!obj.TryGetValue("$type", out var typeValue))
        {
            throw new JsonException("Missing '$type' discriminator.");
        }

        if (!AssetRegistry.Collection<T>().TryGetVariantType(typeValue.Value<string>(), out var type))
        {
            throw new JsonException($"Unknown asset type discriminator '{typeValue.Value<string>()}'");
        }

        var target = (T)Activator.CreateInstance(type)!;
        serializer.Populate(obj.CreateReader(), target);
        return target;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }

    public override bool CanConvert(Type objectType) => typeof(T).IsAssignableFrom(objectType);
    public override bool CanWrite => false;
}
