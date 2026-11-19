using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.API.Common;

[JsonConverter(typeof(VariableJsonConverter))]
public struct Variable<T>
    where T : unmanaged
{
    private readonly T literal;
    private readonly string? expression;

    internal Variable(T literal) : this(literal, null) { }
    internal Variable(string expression) : this(default!, expression) { }

    private Variable(T literal, string? expression = null)
    {
        this.literal = literal;
        this.expression = expression;
    }

    private object? Literal => literal;
    private string? Expression => expression;
    private T? resolvedValue { get; set; } = null;

    public T GetValue(Entity entity)
    {
        if (expression == null) return literal;

        if (resolvedValue == null)
        {
            resolvedValue = ExpressionEvaluator.Instance?.ParseExpression<T>(entity, expression!) ?? default;
        }

        return (T)resolvedValue!;
    }

    public static implicit operator Variable<T>(T literal) => new(literal);
}

public class VariableJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Variable<>);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var token = JToken.Load(reader, JsonUtil.DefaultLoadSettings);
        var innerType = objectType.GetGenericArguments()[0];

        if (token.Type == JTokenType.String)
        {
            var s = token.Value<string>();
            if (s.Contains('{'))
            {
                return Activator.CreateInstance(objectType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [s], null);
            }
        }

        var literal = token.ToObject(innerType, serializer);

        return Activator.CreateInstance(objectType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [literal], null);
    }

    public override bool CanWrite => false;

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotImplementedException();
}
