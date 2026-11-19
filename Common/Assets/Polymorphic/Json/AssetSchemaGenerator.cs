using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class AssetSchemaMetadataAttribute(string? title = null, string? description = null) : Attribute
{
    public string? Title { get; } = title;
    public string? Description { get; } = description;
}

/// <summary>
/// JSON schema generator for polymorphic asset definitions.
/// </summary>
public class AssetSchemaGenerator(Type assetInterface, Type variantType)
{
    private static readonly JToken Draft = "https://json-schema.org/draft/2020-12/schema";

    private IReadOnlyList<Type> elementTypes = [];
    private readonly JObject defs = new();

    /// <summary>
    /// Types that are not strictly enforced by the schema. The generator will set allowAdditionalProperties to true for
    /// these.
    /// </summary>
    private static readonly List<Type> NonStrictTypes = [
        typeof(JToken)
    ];

    private static readonly JObject StringArraySchema = new()
    {
        ["type"] = "array",
        ["items"] = new JObject { ["type"] = "string" }
    };

    public JObject Generate()
    {
        AssetRegistry.TryGetPolymorphicElementsFor(assetInterface, out elementTypes);

        defs.RemoveAll();
        BuildElementDefs();
        var (properties, required) = BuildProperties(variantType, variantType.Name);

        var schema = new JObject
        {
            ["$schema"] = Draft,
            ["$id"] = $"{assetInterface.Name}.{variantType.Name}",
            ["$defs"] = defs,
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private void BuildElementDefs()
    {
        foreach (var elementType in elementTypes)
        {
            var (properties, required) = BuildProperties(elementType);

            properties["$type"] = new JObject
            {
                ["const"] = elementType.Name
            };
            required.Insert(0, "$type");

            var elementSchema = new JObject
            {
                ["type"] = "object"
            };

            AddMetadata(elementSchema, elementType);

            elementSchema["additionalProperties"] = false;
            elementSchema["properties"] = properties;

            if (required.Count > 0)
            {
                elementSchema["required"] = required;
            }

            defs[elementType.Name] = elementSchema;
        }
    }

    private JObject BuildPropertySchema(Type propertyType, Type? ownerType = null)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        switch (underlyingType.IsGenericType)
        {
            case true when underlyingType.GetGenericTypeDefinition() == typeof(Variable<>):
            {
                var inner = GetJsonSchemaType(underlyingType.GetGenericArguments()[0]);
                return new JObject { ["oneOf"] = new JArray(
                    new JObject { ["type"] = inner },
                    new JObject { ["type"] = "string" }) };
            }
            case true when underlyingType.GetGenericTypeDefinition() == typeof(ComplexTagCondition<>):
            {
                // This provides limited support for ComplexTagCondition, accepting any of "allOf", "anyOf", or "noneOf" as
                // arrays of strings. It doesn't support the more complex cases that have nested objects/arrays.
                return new JObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JObject
                    {
                        ["allOf"] = StringArraySchema,
                        ["anyOf"] = StringArraySchema,
                        ["noneOf"] = StringArraySchema
                    }
                };
            }
        }

        if (underlyingType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(underlyingType))
        {
            var elementInterface = underlyingType.IsArray
                ? underlyingType.GetElementType()
                : underlyingType.GetGenericArguments().FirstOrDefault();

            if (elementInterface != null)
            {
                if (elementInterface.GetCustomAttribute<PolymorphicAssetElementAttribute>() != null)
                {
                    var oneOf = new JArray();
                    foreach (var type in elementTypes)
                    {
                        if (!type.IsAssignableTo(elementInterface))
                            continue;

                        oneOf.Add(new JObject { ["$ref"] = $"#/$defs/{type.Name}" });
                    }

                    return new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["oneOf"] = oneOf }
                    };
                }

                return new JObject
                {
                    ["type"] = "array",
                    ["items"] = BuildPropertySchema(elementInterface, ownerType)
                };
            }
        }

        if (underlyingType.IsEnum)
        {
            var enumValues = new JArray();
            foreach (var value in Enum.GetNames(underlyingType))
            {
                enumValues.Add(value);
            }

            return new JObject
            {
                ["type"] = "string",
                ["enum"] = enumValues
            };
        }

        if (underlyingType.GetCustomAttribute<PolymorphicAssetElementAttribute>() != null)
        {
            var oneOf = new JArray();
            foreach (var type in elementTypes)
            {
                if (type == ownerType || !type.IsAssignableTo(underlyingType))
                    continue;

                oneOf.Add(new JObject { ["$ref"] = $"#/$defs/{type.Name}" });
            }

            return new JObject { ["oneOf"] = oneOf };
        }

        if (underlyingType.IsClass && underlyingType != typeof(string))
        {
            if (!defs.ContainsKey(underlyingType.Name))
            {
                BuildTypeDef(underlyingType);
            }

            return new JObject { ["$ref"] = $"#/$defs/{underlyingType.Name}" };
        }

        return new JObject { ["type"] = GetJsonSchemaType(underlyingType) };
    }

    private void BuildTypeDef(Type type)
    {
        if (defs.ContainsKey(type.Name)) return;

        var schema = new JObject
        {
            ["type"] = "object"
        };

        AddMetadata(schema, type);

        schema["additionalProperties"] = NonStrictTypes.Contains(type);

        defs[type.Name] = schema;

        var (properties, required) = BuildProperties(type);
        schema["properties"] = properties;

        if (required.Count > 0)
        {
            schema["required"] = required;
        }
    }

    private (JObject properties, JArray required) BuildProperties(Type type, string? discriminator = null)
    {
        var properties = new JObject();
        var required = new JArray();

        if (discriminator != null)
        {
            properties["$type"] = new JObject
            {
                ["const"] = discriminator
            };
            required.Add("$type");
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var jsonProp = prop.GetCustomAttribute<JsonPropertyAttribute>();
            if (jsonProp == null) continue;

            var propName = jsonProp.PropertyName ?? prop.Name;
            var propSchema = BuildPropertySchema(prop.PropertyType, type);

            if (TryGetPropertyDefaultValue(prop, out var defaultValue))
            {
                if (defaultValue is ComplexTagCondition<TagSetFast> or ComplexTagCondition<TagSet>)
                {
                    propSchema["default"] = new JObject();
                }
                else
                {
                    propSchema["default"] = JToken.FromObject(defaultValue);
                }
            }

            AddMetadata(propSchema, prop);

            properties[propName] = propSchema;

            if (jsonProp.Required is not Required.Default)
            {
                required.Add(propName);
            }
        }

        return (properties, required);
    }

    private static bool TryGetPropertyDefaultValue(PropertyInfo prop, [NotNullWhen(true)] out object? defaultValue)
    {
        var defaultValueAttribute = prop.GetCustomAttribute<DefaultValueAttribute>();
        if (defaultValueAttribute?.Value != null)
        {
            defaultValue = defaultValueAttribute.Value;
            return true;
        }

        if (prop.CanRead)
        {
            var declaringType = prop.DeclaringType;
            if (declaringType is { IsAbstract: false })
            {
                var instance = Activator.CreateInstance(declaringType);

                defaultValue = prop.GetValue(instance);
                if (defaultValue != null) return true;
            }
        }

        defaultValue = null;
        return false;
    }

    private string GetJsonSchemaType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        return "string";
    }

    private static T? GetAttributeFromParentOrInterface<T>(Type type) where T : Attribute
    {
        var attr = type.GetCustomAttribute<T>(inherit: true);
        if (attr != null) return attr;

        return type.GetInterfaces()
            .Select(i => i.GetCustomAttribute<T>(inherit: true))
            .FirstOrDefault(a => a != null);
    }

    private static void AddMetadata(JObject schema, Type type)
    {
        AddMetadata(schema, (MemberInfo)type);

        var polymorphicElement = GetAttributeFromParentOrInterface<PolymorphicAssetElementAttribute>(type);
        if (polymorphicElement?.Name != null) schema["elementType"] = polymorphicElement.Name;
    }

    private static void AddMetadata(JObject schema, MemberInfo member)
    {
        var metadata = member.GetCustomAttribute<AssetSchemaMetadataAttribute>();
        if (metadata == null) return;
        if (metadata?.Title != null) schema["title"] = metadata.Title;
        if (metadata?.Description != null) schema["description"] = metadata.Description;
    }
}
