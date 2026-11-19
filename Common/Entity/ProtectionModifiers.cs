using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Config;

namespace Vintagestory.API.Common
{
    public class ProtectionModifiers
    {
        public enum EnumRoundingType
        {
            Midpoint,
            RoundUp,
            RoundDown
        }

        public Dictionary<EnumDamageType, int> ProtectionTiers { get; set; } = [];


        public static List<List<float>> DamageReductionValues { get; set; } = [];

        public static float MinDamageReductionValue { get; set; } = 0;

        public static EnumRoundingType ProtectionTierRounding { get; set; } = EnumRoundingType.Midpoint;


        protected class DamageReductionTableFile
        {
            public float minDamageReductionValue = 0;
            public float[][]? damageReductionTable;
            public EnumRoundingType protectionTierRounding = EnumRoundingType.Midpoint;
        }



        public static float GetDamageReduction(int attackTier, int protectionTier)
        {
            if (protectionTier == 0) return 1;

            if (attackTier == 0) return MinDamageReductionValue;

            int maxDefinedAttackTier = DamageReductionValues.Count - 1;
            if (attackTier > maxDefinedAttackTier)
            {
                int attackTierOvershoot = attackTier - maxDefinedAttackTier;
                protectionTier = Math.Max(0, protectionTier - attackTierOvershoot);
                attackTier = maxDefinedAttackTier;
            }

            List<float> valuesForAttackTier = DamageReductionValues[attackTier];

            if (valuesForAttackTier.Count == 0) return 1;

            if (valuesForAttackTier.Count <= protectionTier)
            {
                return Math.Max(MinDamageReductionValue, valuesForAttackTier[^1]);
            }

            return Math.Max(MinDamageReductionValue, valuesForAttackTier[protectionTier]);
        }

        public static void LoadDamageReductionTable(ICoreAPI api)
        {
            DamageReductionTableFile? damageReductionTable = api.Assets.Get("config/damagereductiontable.json").ToObject<DamageReductionTableFile>();

            if (damageReductionTable?.damageReductionTable == null) return;

            MinDamageReductionValue = damageReductionTable.minDamageReductionValue;
            ProtectionTierRounding = damageReductionTable.protectionTierRounding;

            SetDamageReductionTable(damageReductionTable.damageReductionTable);
        }

        public static void SetDamageReductionTable(float[][] values)
        {
            DamageReductionValues.Clear();
            for (int attackTier = 0; attackTier < values.Length; attackTier++)
            {
                if (values[attackTier].Length == 0)
                {
                    DamageReductionValues.Add([1]);
                    continue;
                }
                else
                {
                    DamageReductionValues.Add([]);
                }

                for (int protectionTier = 0; protectionTier < values[attackTier].Length; protectionTier++)
                {
                    DamageReductionValues[attackTier].Add(values[attackTier][protectionTier]);
                }
            }
        }

        public virtual int GetProtectionTier(EntityAgent receiver, DamageSource damageStats)
        {
            if (ProtectionTiers.TryGetValue(damageStats.Type, out int result))
            {
                return result;
            }

            return 0;
        }

        public static ProtectionModifiers Add(params ProtectionModifiers[] modifiers)
        {
            ProtectionModifiers result = new();

            foreach (ProtectionModifiers layerProtection in modifiers)
            {
                foreach ((EnumDamageType damgeType, int tier) in layerProtection.ProtectionTiers)
                {
                    if (result.ProtectionTiers.ContainsKey(damgeType))
                    {
                        result.ProtectionTiers[damgeType] += tier;
                    }
                    else
                    {
                        result.ProtectionTiers[damgeType] = tier;
                    }
                }
            }

            return result;
        }

        public static ProtectionModifiers Average(params (ProtectionModifiers modifiers, float weight)[] modifiers)
        {
            ProtectionModifiers result = new();
            float totalWieght = 0;

            Dictionary<EnumDamageType, float> tiers = [];

            foreach ((ProtectionModifiers modifier, float weight) in modifiers)
            {
                foreach ((EnumDamageType damgeType, int tier) in modifier.ProtectionTiers)
                {
                    if (tiers.ContainsKey(damgeType))
                    {
                        tiers[damgeType] += tier * weight;
                    }
                    else
                    {
                        tiers[damgeType] = tier * weight;
                    }
                }

                totalWieght += weight;
            }

            foreach ((EnumDamageType damageType, float tier) in tiers)
            {
                switch (ProtectionTierRounding)
                {
                    case EnumRoundingType.Midpoint:
                        result.ProtectionTiers[damageType] = (int)(tier / totalWieght);
                        break;
                    case EnumRoundingType.RoundDown:
                        result.ProtectionTiers[damageType] = (int)Math.Floor(tier / totalWieght);
                        break;
                    case EnumRoundingType.RoundUp:
                        result.ProtectionTiers[damageType] = (int)Math.Ceiling(tier / totalWieght);
                        break;
                }
            }

            return result;
        }

        public virtual void GetDescription(StringBuilder description, ICoreAPI api, bool broken = false, bool header = true)
        {
            if (header)
            {
                description.AppendLine(Lang.Get("protectiontierdesc-tiervalue-header"));
            }

            foreach ((EnumDamageType damageType, int tier) in ProtectionTiers)
            {
                string damageTypeName = Lang.Get($"damagetype-{damageType.ToString().ToLowerInvariant()}");
                description.AppendLine(Lang.Get("protectiontierdesc-tiervalue", tier, damageTypeName));
            }
        }

        public virtual void Apply(ref float damage, out int durabilityLoss, EntityAgent receiver, DamageSource damageStats)
        {
            float damageReduction = GetDamageReduction(damageStats.DamageTier, GetProtectionTier(receiver, damageStats));

            durabilityLoss = (int)Math.Ceiling(damage * (1 - damageReduction));

            damage *= damageReduction;
        }
    }
}
