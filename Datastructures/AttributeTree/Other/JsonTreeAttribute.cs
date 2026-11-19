using System;
using System.Collections.Generic;
using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Vintagestory.API.Datastructures
{
    public class JsonTreeAttribute
    {
        public string? value;
        public string[]? values;

        public Dictionary<string, JsonTreeAttribute>? elems = new Dictionary<string, JsonTreeAttribute>();
        public EnumAttributeType type;


        public IAttribute? ToAttribute(IWorldAccessor resolver)
        {
            if (type == EnumAttributeType.Unknown)
            {
                if (elems != null)
                {
                    type = EnumAttributeType.Tree;
                } else if(values != null)
                {
                    type = EnumAttributeType.StringArray;
                } else
                {
                    type = EnumAttributeType.String;
                }
            }

            switch (type)
            {
                case EnumAttributeType.Bool:
                    {
                        return new BoolAttribute(value == "true");
                    }
                case EnumAttributeType.Int:
                    {
                        int.TryParse(value, out int val);
                        return new IntAttribute(val);
                    }

                case EnumAttributeType.Double:
                    {
                        double.TryParse(value, out double val);
                        return new DoubleAttribute(val);
                    }

                case EnumAttributeType.Float:
                    {
                        float.TryParse(value, NumberStyles.Any, GlobalConstants.DefaultCultureInfo, out float val);
                        return new FloatAttribute(val);
                    }

                case EnumAttributeType.String:
                    return new StringAttribute(value ?? "");

                case EnumAttributeType.StringArray:
                    return new StringArrayAttribute(values ?? []);

                case EnumAttributeType.Tree:
                    ITreeAttribute tree = new TreeAttribute();
                    if (elems == null) return tree;

                    foreach (var val in elems)
                    {
                        IAttribute? attribute = val.Value.ToAttribute(resolver);
                        if (attribute == null) continue;
                        tree[val.Key] = attribute;
                    }

                    return tree;

                case EnumAttributeType.Itemstack:
                    if (elems == null) return null;

                    if (!elems.TryGetValue("class", out var elemClass) || elemClass.type != EnumAttributeType.String || elemClass.value == null) return null;

                    if (!elems.TryGetValue("code", out var elemCode) || elemCode.type != EnumAttributeType.String) return null;

                    if (!elems.TryGetValue("quantity", out var elemQuantity) || elemQuantity.type != EnumAttributeType.Int) return null;

                    EnumItemClass itemclass;
                    try
                    {
                        itemclass = (EnumItemClass)Enum.Parse(typeof(EnumItemClass), elemClass.value);
                    } catch (Exception)
                    {
                        return null;
                    }

                    int quantity = 0;
                    if (!int.TryParse(elemQuantity.value, out quantity))
                    {
                        return null;
                    }                    

                    ItemStack itemstack;

                    if (itemclass == EnumItemClass.Block)
                    {
                        Block? block = resolver.GetBlock(new AssetLocation(elemCode.value));
                        if (block == null) return null;
                        itemstack = new ItemStack(block, quantity);
                    } else
                    {
                        Item? item = resolver.GetItem(new AssetLocation(elemCode.value));
                        if (item == null) return null;
                        itemstack = new ItemStack(item, quantity);
                    }

                    if (elems.TryGetValue("attributes", out var jsonAttribs))
                    {
                        IAttribute? attributes = jsonAttribs.ToAttribute(resolver);
                        if (attributes is ITreeAttribute)
                        {
                            itemstack.Attributes = (ITreeAttribute)attributes;
                        }
                    }

                    return new ItemstackAttribute(itemstack);
            }

            return null;
        }

        public JsonTreeAttribute Clone()
        {
            JsonTreeAttribute attribute = new JsonTreeAttribute()
            {
                type = type,
                value = value,
            };

            if (elems != null)
            {
                attribute.elems = new Dictionary<string, JsonTreeAttribute>(elems);
            }

            return attribute;
        }
    }
}
