using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common
{
    public class NatFloatVar25Random : INatFloat
    {
        [ThreadStatic]
        static Random threadsafeRand;
        public NatFloatVar25Random() { }

        public EnumINatFloatType Type => EnumINatFloatType.Var25Random;
        public float Average => 1f;

        public INatFloat Clone()
        {
            return this;
        }

        public float nextFloat()
        {
            return nextFloat(1f, threadsafeRand ?? (threadsafeRand = new Random()));
        }

        public float nextFloat(float multiplier)
        {
            return nextFloat(multiplier, threadsafeRand ?? (threadsafeRand = new Random()));
        }

        public float nextFloat(float multiplier, Random rand)
        {
            float rnd = (float)rand.NextDouble() - 0.5f;
            return multiplier * (1f + rnd * 2 * 0.25f);
        }

        public float nextFloat(float multiplier, IRandom rand)
        {
            float rnd = rand.NextFloat() - 0.5f;
            return multiplier * (1f + rnd * 2 * 0.25f);
        }

        public NatFloat ToNatFloat()
        {
            return new NatFloat(1f, 0.25f, EnumDistribution.UNIFORM);
        }
    }

    /// <summary>
    /// Sound types, often used to determine specific volume controls.
    /// </summary>
    [DocumentAsJson]
    public enum EnumSoundType
    {
        Sound, 
        Music, 
        Ambient,
        Weather,
        Entity,
        MusicGlitchunaffected,
        AmbientGlitchunaffected,
        SoundGlitchunaffected
    }

    /// <summary>
    /// Properties that control how a sound is played. There are two forms of this that might be used in JSON files:
    /// Form 1 is simply stored as an <see cref="AssetLocation"/> that points to the sound file path. There are no extra proprties. This is what most JSON objects use.
    /// Form 2 is more complex, and uses the properties below to fully configure the sound. Only certain JSON objects use this, such as entity sounds.
    /// You can mix and match in a single sound array.
    /// </summary>
    /// <example>
    /// Form 1: <code lang="json">
    ///"sounds": {
    ///    "place": "block/plant",
    ///    "break": "block/plant",
    ///    "hit": "block/plant"
    ///},
    /// </code>
    /// Form 2. Note that entity sounds can include * at the end for multiple sound files: <code lang="json">
    ///"sounds": {
    ///    "hurt": "creature/chicken/chick*",
    ///    "death": "creature/chicken/chick*",
    ///    "idle": {
    ///        "path": "creature/chicken/chick*",
    ///        "range": 14,
    ///        "pitch": {
    ///            "avg": 1,
    ///            "var": 0.25
    ///        }
    ///    }
    ///},
    /// </code>
    /// </example>
    [DocumentAsJson]
    public struct SoundAttributes
    {
        /// <summary>
        /// <!--<jsonalias>path</jsonalias>-->
        /// The location of the sound file.
        /// </summary>
        [DocumentAsJson("Required")]
        [JsonProperty("path")]
        public AssetLocation? Location;

        /// <summary>
        /// Controls a random pitch for when the sound is played.
        /// </summary>
        [DocumentAsJson("Optional", "1")]
        public INatFloat Pitch = INatFloat.One;

        /// <summary>
        /// Controls a random volume for when the sound is played.
        /// </summary>
        [DocumentAsJson("Optional", "1")]
        public INatFloat Volume = INatFloat.One;

        /// <summary>
        /// The type of sound, used for volume control in the settings.
        /// </summary>
        [DocumentAsJson("Optional", "Sound")]
        [JsonProperty("soundType")]
        public EnumSoundType Type = EnumSoundType.Sound;

        /// <summary>
        /// The range at which the gain will be attenuated to 1% of the supplied volume. (i.e. the maximum range that the player will hear the sound)
        /// </summary>
        [DocumentAsJson("Optional", "32")]
        public float Range = 32f;


        public SoundAttributes() { }

        // The input sound path must be provided without the '.ogg' suffix, and must before use have the sounds/ or other assetcategory prefix.
        public SoundAttributes(AssetLocation assetLocation, bool withRandomPitch)
        {
            Location = assetLocation;
            if (withRandomPitch) this.Pitch = INatFloat.Var25Random;
        }

        public bool Equals(SoundAttributes other)
        {
            return other.Location == Location 
                && other.Pitch == Pitch 
                && other.Volume == Volume 
                && other.Range == Range 
                && other.Type == Type;
        }

        public override bool Equals(object? obj)
        {
            if (obj is SoundAttributes other) return Equals(other);

            return false;
        }

        public static bool operator ==(SoundAttributes? left, SoundAttributes? right)
        {
            if (object.ReferenceEquals(left, null))
            {
                return object.ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(SoundAttributes? left, SoundAttributes? right)
        {
            return !(left == right);
        }

        public override int GetHashCode()
        {
            return (Location?.GetHashCode() ?? 0) ^ Pitch.GetHashCode() ^ Volume.GetHashCode() ^ Range.GetHashCode() ^ Type.GetHashCode();
        }

        public SoundAttributes Clone()
        {
            SoundAttributes sound = new SoundAttributes();
            sound.Location = Location?.PermanentClone();
            sound.Pitch = Pitch.Clone();
            sound.Volume = Volume.Clone();
            sound.Type = Type;
            sound.Range = Range;
            return sound;
        }

        public void ToBytes(BinaryWriter writer)
        {
            writer.Write(Location != null);
            if (Location != null) writer.Write(Location.ToShortString());
            writer.Write((int)Type);
            writer.Write(Range);
            INatFloat.INatFloatToBytes(Pitch, writer);
            INatFloat.INatFloatToBytes(Volume, writer);
        }

        [Obsolete("Prefer using the BinarySpanReader overload.")]
        public static SoundAttributes FromBytes(BinaryReader reader, string version)
            => reader.UnsafeActAsBinarySpanReader(version, FromBytes);
        public static SoundAttributes FromBytes(ref BinarySpanReader reader, string version)
        {
            var location = reader.ReadBoolean() ? AssetLocation.Create(reader.ReadString()) : null;
            var type = (EnumSoundType)reader.ReadInt32();
            var range = reader.ReadSingle();
            INatFloat pitch = INatFloat.INatFloatFromBytes(ref reader);
            INatFloat volume = INatFloat.INatFloatFromBytes(ref reader);

            return new SoundAttributes()
            {
                Location = location,
                Type = type,
                Range = range,
                Pitch = pitch,
                Volume = volume
            };
        }


        public override string ToString() {
            return $"SoundAttributes {{ Location: {Location}, Pitch: {Pitch}, Volume: {Volume}, Range: {Range}, Type: {Type} }}";
        }
    }

    public class SoundAttributeConverter : JsonConverter<SoundAttributes?>
    {
        public float DefaultRange = 32f;
        public bool AddSoundsPrefix = true;
        public bool RandomPitch = false;

        public SoundAttributeConverter(bool randomPitch)
        {
            RandomPitch = randomPitch;
        }

        public SoundAttributeConverter(bool randomPitch, float defaultRange)
        {
            RandomPitch = randomPitch;
            DefaultRange = defaultRange;
        }

        public override bool CanWrite { get => false; }

        public override void WriteJson(JsonWriter writer, SoundAttributes? value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override SoundAttributes? ReadJson(JsonReader reader, Type objectType, SoundAttributes? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.ValueType == typeof(string))
            {
                AssetLocationJsonParser converter = serializer.Converters.Select(x => x as AssetLocationJsonParser).Where(x => x != null).FirstOrDefault() 
                    ?? new AssetLocationJsonParser(GlobalConstants.DefaultDomain);
                AssetLocation? location = converter.ReadJson(reader, typeof(AssetLocation), null, false, serializer);
                ArgumentNullException.ThrowIfNull(location); // This should never happen, no matter how the json is set up
                if (AddSoundsPrefix) location.WithPathPrefixOnce("sounds/");

                return new SoundAttributes(location, RandomPitch) { Range = DefaultRange, };
            }

            SoundAttributes sound = new SoundAttributes();
            sound.Range = DefaultRange;

            // Set up so json serializer will create new instances, not populate the existing ones
            sound.Pitch = null!;
            sound.Volume = null!;

            try
            {
                object boxed = sound;
                serializer.Populate(reader, boxed);
                sound = (SoundAttributes)boxed;
                if (AddSoundsPrefix) sound.Location?.WithPathPrefixOnce("sounds/");
            }
            catch (Exception e)
            {
                throw new Exception("Error while parsing " + sound, e);
            }

            sound.Pitch ??= RandomPitch ? INatFloat.Var25Random : INatFloat.One;
            sound.Volume ??= INatFloat.One;

            return sound.Location == null ? null : sound;
        }
    }
}
