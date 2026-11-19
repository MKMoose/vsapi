using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common
{
    /// <summary>
    /// This is a representation of an item stack in JSON.
    /// It resembles a standard in-game item stack but can be stored before the game is loaded.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///	"output": {
    ///		"type": "item",
    ///		"code": "knifeblade-flint",
    ///		"stacksize": 1
    ///	},
    /// </code>
    /// <code language="json">
    ///	"output": {
    ///		"type": "block",
    ///		"code": "ladder-wood-north",
    ///		"quantity": 3
    ///	},
    /// </code>
    /// </example>
    [DocumentAsJson]
    [ProtoContract]
    public class JsonItemStack : IRecipeOutput, IConcreteCloneable<JsonItemStack>
    {
        #region From JSON
        /// <summary>
        /// Block or Item?
        /// </summary>
        [ProtoMember(1)]
        [DocumentAsJson("Recommended", "Block")]
        public EnumItemClass Type;

        /// <summary>
        /// The asset location code of the block or item.
        /// </summary>
        [ProtoMember(2)]
        [DocumentAsJson("Required")]
        public required AssetLocation Code;

        /// <summary>
        /// Amount of items in this stacks
        /// </summary>
        [ProtoMember(3)]
        [DocumentAsJson("Optional", "1")]
        public int StackSize = 1;

        /// <summary>
        /// Alias of <see cref="StackSize"/>. No real need to use this instead of it.
        /// </summary>
        [DocumentAsJson("Optional", "1")]
        public int Quantity
        {
            get { return StackSize;  }
            set { StackSize = value; }
        }

        /// <summary>
        /// Tree Attributes that should be attached to the resulting itemstack.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        [ProtoMember(4)]
        [DocumentAsJson("Optional", "None")]
        public JsonObject? Attributes;
        #endregion

        /// <summary>
        /// The resolved item after conversion.
        /// </summary>
        public ItemStack? ResolvedItemstack;

        /// <summary>
        /// Was added for interface.<br/>
        /// Field 'ResolvedItemstack' is left for compatibility reasons
        /// </summary>
        public ItemStack? ResolvedItemStack
        {
            get => ResolvedItemstack;
            set => ResolvedItemstack = value;
        }


        public static JsonItemStack? FromString(string jsonItemstack)
        {
            return JsonObject.FromJson(jsonItemstack).AsObject<JsonItemStack>();
        }

        public static JsonItemStack FromItemStack(IWorldAccessor resolver, ItemStack stack, params string[] ignoreAttributes)
        {
            var jStack = new JsonItemStack
            {
                Code = stack.Collectible.Code,
                StackSize = stack.StackSize,
                Type = stack.Class
            };

            if (ignoreAttributes?.Length > 0)
            {
                var attributes = stack.Attributes.Clone();
                foreach (var key in ignoreAttributes) attributes.RemoveAttribute(key);
                jStack.Attributes = JsonObject.FromJson(attributes.ToJsonToken());
            }
            else jStack.Attributes = JsonObject.FromJson(stack.Attributes.ToJsonToken());

            jStack.Resolve(resolver, "Eatenstack of " + jStack.Type.ToString() + " ", jStack.Code);

            return jStack;
        }

        /// <summary>
        /// Sets itemstack.block or itemstack.item
        /// </summary>
        /// <param name="resolver"></param>
        /// <param name="sourceForErrorLogging"></param>
        /// <param name="assetLoc"></param>
        /// <param name="printWarningOnError"></param>
        public bool Resolve(IWorldAccessor resolver, string sourceForErrorLogging, AssetLocation assetLoc, bool printWarningOnError = true)
        {
            if (Type == EnumItemClass.Block)
            {
                Block? block = resolver.GetBlock(Code);
                if (block == null || block.IsMissing)
                {
                    if (printWarningOnError)
                    {
                        resolver.Logger.Warning("Failed resolving a blocks blockdrop or smeltedstack with code {0} in {1}", Code, sourceForErrorLogging + assetLoc);
                    }
                    return false;
                }

                ResolvedItemStack = new ItemStack(block, StackSize);

            }
            else
            {
                Item? item = resolver.GetItem(Code);
                if (item == null || item.IsMissing)
                {
                    if (printWarningOnError)
                    {
                        resolver.Logger.Warning("Failed resolving a blocks itemdrop or smeltedstack with code {0} in {1}", Code, sourceForErrorLogging + assetLoc);
                    }
                    return false;
                }
                ResolvedItemStack = new ItemStack(item, StackSize);
            }

            if (Attributes != null)
            {
                IAttribute? attributes = Attributes.ToAttribute();
                if (attributes is ITreeAttribute treeAttribute)
                {
                    ResolvedItemStack.Attributes = treeAttribute;
                }
            }

            return true;
        }

        public bool Resolve(IWorldAccessor resolver, string sourceForErrorLogging, bool printWarningOnError)
        {
            if (Type == EnumItemClass.Block)
            {
                Block? block = resolver.GetBlock(Code);
                if (block == null || block.IsMissing)
                {
                    if (printWarningOnError)
                    {
                        resolver.Logger.Warning("Failed resolving a blocks blockdrop or smeltedstack with code {0} in {1}", Code, sourceForErrorLogging);
                    }
                    return false;
                }

                ResolvedItemStack = new ItemStack(block, StackSize);

            }
            else
            {
                Item? item = resolver.GetItem(Code);
                if (item == null || item.IsMissing)
                {
                    if (printWarningOnError)
                    {
                        resolver.Logger.Warning("Failed resolving a blocks itemdrop or smeltedstack with code {0} in {1}", Code, sourceForErrorLogging);
                    }
                    return false;
                }
                ResolvedItemStack = new ItemStack(item, StackSize);
            }

            if (Attributes != null)
            {
                IAttribute? attributes = Attributes.ToAttribute();
                if (attributes is ITreeAttribute treeAttribute)
                {
                    ResolvedItemStack.Attributes = treeAttribute;
                }
            }

            return true;
        }

        [MemberNotNullWhen(true, nameof(ResolvedItemStack), nameof(ResolvedItemstack))]
        public virtual bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
        {
            return Resolve(world, sourceForErrorLogging, true);
        }

        public virtual bool Matches(IWorldAccessor worldForResolve, ItemStack inputStack)
        {
            return ResolvedItemStack?.Equals(worldForResolve, inputStack, GlobalConstants.IgnoredStackAttributes) == true;
        }

        /// <summary>
        /// Creates a deep copy of this object
        /// </summary>
        /// <returns></returns>
        public virtual JsonItemStack Clone()
        {
            JsonItemStack result = new JsonItemStack { Code = null! };

            CloneTo(result);

            return result;
        }

        /// <summary>
        /// Loads the ItemStack from the reader.
        /// </summary>
        /// <param name="reader">The reader to get the ItemStack from</param>
        /// <param name="instancer">The instancer for the ItemStack.</param>
        [Obsolete("Use the BinarySpanReader overload.", true)]
        public virtual void FromBytes(BinaryReader reader, IClassRegistryAPI instancer)
        {
            var thisT = this.GetType();
            Trace.WriteLine($"You are calling obsolete overload {thisT}.{nameof(this.FromBytes)}(BinaryReader). If you are the developer make sure to move to the BinarySpanReader overload as soon as possible.");

            var newFromBytes = thisT.GetMethod(nameof(this.FromBytes), BindingFlags.Public | BindingFlags.Instance, [ typeof(BinarySpanReader), typeof(IClassRegistryAPI) ]);
            if (newFromBytes != null && newFromBytes.DeclaringType != typeof(JsonItemStack))
            {
                reader.UnsafeActAsBinarySpanReader(instancer, FromBytes);
            }
            else
            {
                reader.UnsafeActAsBinarySpanReader(instancer, fromBytes);
            }
        }

        /// <summary>
        /// Loads the ItemStack from the reader.
        /// </summary>
        /// <param name="reader">The reader to get the ItemStack from</param>
        /// <param name="instancer">The instancer for the ItemStack.</param>
        public virtual void FromBytes(ref BinarySpanReader reader, IClassRegistryAPI instancer)
        {
            var thisT = this.GetType();
            var oldFromBytes = thisT.GetMethod(nameof(this.FromBytes), BindingFlags.Public | BindingFlags.Instance, [ typeof(BinaryReader), typeof(IClassRegistryAPI) ]);
            if (oldFromBytes != null && oldFromBytes.DeclaringType != typeof(JsonItemStack))
            {
                Trace.WriteLine($"Calling obsolete overload {thisT}.{nameof(this.FromBytes)}(BinaryReader). If you are the developer make sure to move to the new overload as soon as possible.");
                #pragma warning disable CS0618 // Type or member is obsolete
                reader.ActAsBinaryReader(instancer, oldFromBytes.CreateDelegate<BinarySpanReaderExtensions.BinaryReaderAction<IClassRegistryAPI>>(this));
                #pragma warning restore CS0618
            }
            else
            {
                fromBytes(ref reader, instancer);
            }
        }

        void fromBytes(ref BinarySpanReader reader, IClassRegistryAPI instancer)
        {
            Type = (EnumItemClass)reader.ReadInt16();
            Code = new AssetLocation(reader.ReadString());
            StackSize = reader.ReadInt32();

            if (reader.ReadBoolean())
            {
                ResolvedItemStack = new ItemStack(ref reader);
            }

            if (reader.ReadBoolean())
            {
                Attributes = new JsonObject(JToken.Parse(reader.ReadString(), JsonUtil.DefaultLoadSettings));
            }
        }

        /// <summary>
        /// Loads the ItemStack from the reader.
        /// </summary>
        public virtual void FromBytes(ref BinarySpanReader reader, IWorldAccessor resolver)
        {
            FromBytes(ref reader, resolver.ClassRegistry);
        }

        /// <summary>
        /// Saves the ItemStack to file.
        /// </summary>
        /// <param name="writer">The writer to save the item to.</param>
        public virtual void ToBytes(BinaryWriter writer)
        {
            writer.Write((short)Type);
            writer.Write(Code.ToShortString());
            writer.Write(StackSize);
            writer.Write(ResolvedItemStack != null);
            ResolvedItemStack?.ToBytes(writer);

            string? jsonAttruibutes = Attributes?.ToString();
            writer.Write(jsonAttruibutes != null);
            if (jsonAttruibutes != null)
            {
                writer.Write(jsonAttruibutes);
            }
        }

        public virtual void FillPlaceHolder(string key, string value)
        {
            Code = Code.CopyWithPath(Code.Path.Replace("{" + key + "}", value));
            Attributes?.FillPlaceHolder(key, value);
        }

        object ICloneable.Clone() => Clone();

        protected virtual void CloneTo(object stack)
        {
            if (stack is not JsonItemStack jsonItemStack)
            {
                return;
            }

            jsonItemStack.Code = Code.Clone();
            jsonItemStack.ResolvedItemStack = ResolvedItemStack?.Clone();
            jsonItemStack.StackSize = StackSize;
            jsonItemStack.Type = Type;
            jsonItemStack.Attributes = Attributes?.Clone();
        }
    }
}
