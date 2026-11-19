using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Vintagestory.API.Config;

namespace Vintagestory.API.Common
{
    public static class JsonUtil
    {
        public static JsonLoadSettings DefaultLoadSettings { get; private set; } = new();

        public static void setupLoadSettings(bool ramOptimized)
        {
            DefaultLoadSettings = new JsonLoadSettings()
            {
                LineInfoHandling = ramOptimized ? LineInfoHandling.Ignore : LineInfoHandling.Load
            };
        }

        public static System.Text.Json.JsonSerializerOptions DefaultTextJsonSerializerOptions { get; } = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            IncludeFields = true,
            WriteIndented = true
        };

        public static void Populate<T>(this JToken value, T target) where T : class
        {
            using (var sr = value.CreateReader())
            {
                JsonSerializer.CreateDefault().Populate(sr, target); // Uses the system default JsonSerializerSettings
            }
        }

        /// <summary>
        /// Reads a Json object, and converts it to the designated type.
        /// </summary>
        /// <typeparam name="T">The designated type</typeparam>
        /// <param name="data">The json object.</param>
        /// <returns></returns>
        public static T? FromBytes<T>(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var sr = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return JsonConvert.DeserializeObject<T>(sr.ReadToEnd());
                }
            }
        }

        public static T? FromString<T>(string data)
        {
            return JsonConvert.DeserializeObject<T>(data);
        }

        /// <summary>
        /// Converts the object to json.
        /// </summary>
        /// <typeparam name="T">The type to convert</typeparam>
        /// <param name="obj">The object to convert</param>
        /// <returns></returns>
        public static byte[] ToBytes<T>(T obj)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));
        }

        public static string ToString<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public static string ToPrettyString<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }


        public static void PopulateObject(object toPopulate, string text, string domain, JsonSerializerSettings? settings = null)
        {
            if (domain != GlobalConstants.DefaultDomain)
            {
                if (settings == null)
                {
                    settings = new JsonSerializerSettings();
                }
                settings.Converters.Add(new AssetLocationJsonParser(domain));
            }
            JsonConvert.PopulateObject(text, toPopulate, settings);
        }

        public static JsonSerializer CreateSerializerForDomain(string domain, JsonSerializerSettings? settings = null)
        {
            if (domain != GlobalConstants.DefaultDomain)
            {
                if (settings == null)
                {
                    settings = new JsonSerializerSettings();
                }
                settings.Converters.Add(new AssetLocationJsonParser(domain));
            }
            return JsonSerializer.CreateDefault(settings);
        }

        public static void PopulateObject(object toPopulate, JToken token, JsonSerializer js)
        {
            using (JsonReader reader = token.CreateReader())
            {
                js.Populate(reader, toPopulate);
            }
        }

        /// <summary>
        /// Converts a Json object to a typed object.
        /// </summary>
        /// <typeparam name="T">The type to convert.</typeparam>
        /// <param name="text">The text to deserialize</param>
        /// <param name="domain">The domain of the text.</param>
        /// <param name="settings">The settings of the deserializer. (default: Null)</param>
        /// <returns></returns>
        public static T? ToObject<T>(string text, string domain, JsonSerializerSettings? settings = null)
        {
            // Special parsing for JTokens to leave out array comments, which would otherwise get included from deserialize
            if (typeof(JToken).IsAssignableFrom(typeof(T)))
            {
                if (string.IsNullOrWhiteSpace(text)) return default;
                return (T)(object)JToken.Parse(text, DefaultLoadSettings);
            }
            if (domain != GlobalConstants.DefaultDomain)
            {
                if (settings == null)
                {
                    settings = new JsonSerializerSettings();
                }
                settings.Converters.Add(new AssetLocationJsonParser(domain));
            }

            return JsonConvert.DeserializeObject<T>(text, settings);
        }

        /// <summary>
        /// Converts a Json token to a typed object.
        /// </summary>
        /// <typeparam name="T">The type to convert.</typeparam>
        /// <param name="token">The token to deserialize</param>
        /// <param name="domain">The domain of the text.</param>
        /// <param name="settings">The settings of the deserializer. (default: Null)</param>
        /// <returns></returns>
        public static T? ToObject<T>(this JToken token, string domain, JsonSerializerSettings? settings = null)
        {
            if (domain != GlobalConstants.DefaultDomain)
            {
                if (settings == null)
                {
                    settings = new JsonSerializerSettings();
                }
                settings.Converters.Add(new AssetLocationJsonParser(domain));
            }

            return token.ToObject<T>(JsonSerializer.Create(settings));
        }

        /// <summary>
        /// Serializes an object to a file using System.Text.Json.
        /// Which means you need to make sure your objects do have the proper fields marked with System.Text.Json annotations to include and exclude them.
        /// The defaults for what to include and exclude are different from Newtonsoft.Json.
        /// </summary>
        /// <param name="path">Filepath</param>
        /// <param name="data"></param>
        /// <param name="settings">If this is not specified, we set IncludeFields=true, WriteIndented=true and use Encoder=JavaScriptEncoder.Create(UnicodeRanges.All) </param>
        /// <typeparam name="T"></typeparam>
        public static void SerializeFastToFile<T>(string path, T data, System.Text.Json.JsonSerializerOptions? settings = null)
        {
            settings ??= DefaultTextJsonSerializerOptions;
            using var fileStream = File.Open(path, FileMode.Create);
            System.Text.Json.JsonSerializer.Serialize(fileStream, data, settings);
        }

        /// <summary>
        /// Serializes an object to a string using System.Text.Json.
        /// Which means you need to make sure your objects do have the proper fields marked with System.Text.Json annotation to include and exclude them.
        /// The defaults for what to include and exclude are different from Newtonsoft.Json.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="settings">If this is not specified, we set IncludeFields=true, WriteIndented=true and use Encoder=JavaScriptEncoder.Create(UnicodeRanges.All) </param>
        /// <typeparam name="T"></typeparam>
        public static string SerializeFast<T>(T data, System.Text.Json.JsonSerializerOptions? settings = null)
        {
            settings ??= DefaultTextJsonSerializerOptions;
            return System.Text.Json.JsonSerializer.Serialize(data, settings);
        }

        /// <summary>
        /// Deserializes a string using System.Text.Json.
        /// Which means you need to make sure your objects do have the proper fields marked with System.Text.Json annotation to include and exclude them.
        /// The defaults for what to include and exclude are different from Newtonsoft.Json.
        /// </summary>
        /// <param name="json"></param>
        /// <param name="settings">If this is not specified, we set IncludeFields=true, WriteIndented=true and use Encoder=JavaScriptEncoder.Create(UnicodeRanges.All) </param>
        /// <typeparam name="T"></typeparam>
        public static T? DeserializeFast<T>(string json, System.Text.Json.JsonSerializerOptions? settings = null)
        {
            settings ??= DefaultTextJsonSerializerOptions;
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, settings);
        }

        /// <summary>
        /// Deserializes a file using System.Text.Json.
        /// Which means you need to make sure your objects do have the proper fields marked with System.Text.Json annotations to include and exclude them.
        /// The defaults for what to include and exclude are different from Newtonsoft.Json.
        /// </summary>
        /// <param name="path">Filepath</param>
        /// <param name="settings">If this is not specified, we set IncludeFields=true, WriteIndented=true and use Encoder=JavaScriptEncoder.Create(UnicodeRanges.All) </param>
        /// <typeparam name="T"></typeparam>
        public static T? DeserializeFastFromFile<T>(string path, System.Text.Json.JsonSerializerOptions? settings = null)
        {
            settings ??= DefaultTextJsonSerializerOptions;
            using var fileStream = File.Open(path, FileMode.Open);
            return System.Text.Json.JsonSerializer.Deserialize<T>(fileStream, settings);
        }
    }

    public class AssetLocationJsonParser : JsonConverter<AssetLocation>
    {
        protected string domain;

        public AssetLocationJsonParser(string domain)
        {
            this.domain = domain;
        }

        public override AssetLocation? ReadJson(JsonReader reader, Type objectType, AssetLocation? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.Value is string)
            {
                return AssetLocation.Create(reader.Value as string, domain);
            }
            return null;
        }

        public override bool CanWrite { get => false; }

        public override void WriteJson(JsonWriter writer, AssetLocation? value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
