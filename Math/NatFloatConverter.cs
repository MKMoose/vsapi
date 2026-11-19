using System;
using Newtonsoft.Json;

namespace Vintagestory.API.MathTools
{
    /// <summary>
    /// Reads the existing NatFloat JSON format into a mutable distribution or a shared immutable default.
    /// </summary>
    public class NatFloatConverter : JsonConverter<INatFloat>
    {
        public override INatFloat? ReadJson(JsonReader reader, Type objectType, INatFloat? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            // Populate a concrete object rather than asking the serializer to instantiate INatFloat.
            // Copy existing values so that populating a member cannot modify a shared default.
            NatFloat value = hasExistingValue && existingValue != null ? existingValue.ToNatFloat() : NatFloat.Zero;
            serializer.Populate(reader, value);

            // Concrete NatFloat members must remain mutable, even when their values match a default.
            if (objectType == typeof(INatFloat) && value.offset == 0 && value.avg == 1 && value.dist == EnumDistribution.UNIFORM)
            {
                if (value.var == 0) return INatFloat.One;
                if (value.var == 0.25f) return INatFloat.Var25Random;
            }

            return value;
        }

        public override void WriteJson(JsonWriter writer, INatFloat? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            NatFloat distribution = value as NatFloat ?? value.ToNatFloat();
            writer.WriteStartObject();
            writer.WritePropertyName("offset");
            writer.WriteValue(distribution.offset);
            writer.WritePropertyName("avg");
            writer.WriteValue(distribution.avg);
            writer.WritePropertyName("var");
            writer.WriteValue(distribution.var);
            writer.WritePropertyName("dist");
            serializer.Serialize(writer, distribution.dist);
            writer.WriteEndObject();
        }
    }
}
