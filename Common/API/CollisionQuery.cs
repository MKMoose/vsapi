using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common
{
    /// <summary>
    /// Unlisted bits are available for custom collision layers.
    /// </summary>
    [Flags]
    [JsonConverter(typeof(CollisionLayerConverter))]
    public enum CollisionLayer : ulong
    {
        None = 0,
        Solid = 1,
        Entity = 2,
        Combat = 4,
        Projectile = 8,
        Selection = 16,
        All = ulong.MaxValue
    }

    public sealed class CollisionLayerConverter : StringEnumConverter
    {
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.Integer) return base.ReadJson(reader, objectType, existingValue, serializer);
            // Parse integer masks as unsigned values so mods can use the top bit.
            if (!ulong.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
            {
                throw new JsonSerializationException("Collision layers require an unsigned 64-bit mask.");
            }
            return (CollisionLayer)value;
        }
    }

    /// <summary>
    /// Both the query mask and the collider mask must accept the other's layer.
    /// A null query filter bypasses filtering.
    /// A default filter accepts nothing.
    /// </summary>
    public readonly struct CollisionQueryFilter
    {
        public CollisionLayer Layer { get; }
        public CollisionLayer Mask { get; }
        public long ExcludedColliderId { get; }
        public long ExcludedOwnerEntityId { get; }

        public CollisionQueryFilter(CollisionLayer layer, CollisionLayer mask = CollisionLayer.All, long excludedColliderId = 0, long excludedOwnerEntityId = 0)
        {
            Layer = layer;
            Mask = mask;
            ExcludedColliderId = excludedColliderId;
            ExcludedOwnerEntityId = excludedOwnerEntityId;
        }

        public bool Matches(Collider collider)
        {
            return (Mask & collider.CollisionLayer) != 0
                && (Layer & collider.CollisionMask) != 0
                && (ExcludedColliderId == 0 || ExcludedColliderId != collider.Id)
                && (ExcludedOwnerEntityId == 0 || ExcludedOwnerEntityId != collider.OwnerEntityId);
        }
    }

    /// <summary>
    /// The collider snapshot remains valid after scene changes.
    /// Fractions run from zero to one along the supplied displacement, including endpoint contact.
    /// </summary>
    public readonly struct CollisionHit : IComparable<CollisionHit>
    {
        public Collider Collider { get; }
        public long ColliderId => Collider.Id;
        public int CollisionBoxIndex { get; }
        public double Fraction { get; }

        /// <summary>
        /// The world-space unit normal pointing from the collider toward the query box.
        /// </summary>
        public FastVec3d Normal { get; }
        public bool StartedOverlapping { get; }

        public CollisionHit(Collider collider, int collisionBoxIndex, double fraction, FastVec3d normal, bool startedOverlapping)
        {
            Collider = collider;
            CollisionBoxIndex = collisionBoxIndex;
            Fraction = fraction;
            Normal = normal;
            StartedOverlapping = startedOverlapping;
        }

        public int CompareTo(CollisionHit other)
        {
            int order = Fraction.CompareTo(other.Fraction);
            if (order != 0) return order;
            order = ColliderId.CompareTo(other.ColliderId);
            return order != 0 ? order : CollisionBoxIndex.CompareTo(other.CollisionBoxIndex);
        }
    }
}
