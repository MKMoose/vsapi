using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Config;

namespace Vintagestory.API.Common
{
    public class StatModifiers
    {
        public float rangedWeaponsSpeed = 0f;
        public float rangedWeaponsAcc = 0f;
        public float walkSpeed = 0f;
        public float healingeffectivness = 0f;
        public float hungerrate = 0f;
        public bool canEat = true;

        /// <summary>
        /// Unaffected by item being broken
        /// </summary>
        public Dictionary<string, float>? generalStats = null;
        /// <summary>
        /// When item broken only negative values applied
        /// </summary>
        public Dictionary<string, float>? positiveEffectStats = null;
        /// <summary>
        /// When item broken only positive values applied
        /// </summary>
        public Dictionary<string, float>? negativeEffectStats = null;

        public virtual void AddFrom(StatModifiers other, bool broken = false, float multiplier = 1, float walkSpeedDebufMultiplier = 1)
        {
            if (broken)
            {
                rangedWeaponsSpeed += Math.Min(0, other.rangedWeaponsSpeed) * multiplier;
                rangedWeaponsAcc += Math.Min(0, other.rangedWeaponsAcc) * multiplier;
                healingeffectivness += Math.Min(0, other.healingeffectivness) * multiplier;
                hungerrate += Math.Max(0, other.hungerrate) * multiplier;

                if (other.walkSpeed < 0)
                {
                    walkSpeed += other.walkSpeed * multiplier * walkSpeedDebufMultiplier;
                }

                if (other.positiveEffectStats != null && other.positiveEffectStats.Count > 0)
                {
                    positiveEffectStats ??= [];
                    foreach ((string category, float otherValue) in other.positiveEffectStats)
                    {
                        if (positiveEffectStats.ContainsKey(category))
                        {
                            positiveEffectStats[category] += Math.Min(0, otherValue * multiplier);
                        }
                        else
                        {
                            positiveEffectStats[category] = Math.Min(0, otherValue * multiplier);
                        }
                    }
                }

                if (other.negativeEffectStats != null && other.negativeEffectStats.Count > 0)
                {
                    negativeEffectStats ??= [];
                    foreach ((string category, float otherValue) in other.negativeEffectStats)
                    {
                        if (negativeEffectStats.ContainsKey(category))
                        {
                            negativeEffectStats[category] += Math.Max(0, otherValue * multiplier);
                        }
                        else
                        {
                            negativeEffectStats[category] = Math.Max(0, otherValue * multiplier);
                        }
                    }
                }
            }
            else
            {
                rangedWeaponsSpeed += other.rangedWeaponsSpeed * multiplier;
                rangedWeaponsAcc += other.rangedWeaponsAcc * multiplier;
                healingeffectivness += other.healingeffectivness * multiplier;
                hungerrate += other.hungerrate * multiplier;

                if (other.walkSpeed < 0)
                {
                    walkSpeed += other.walkSpeed * multiplier * walkSpeedDebufMultiplier;
                }
                else
                {
                    walkSpeed += other.walkSpeed * multiplier;
                }

                if (other.positiveEffectStats != null && other.positiveEffectStats.Count > 0)
                {
                    positiveEffectStats ??= [];
                    foreach ((string category, float otherValue) in other.positiveEffectStats)
                    {
                        if (positiveEffectStats.ContainsKey(category))
                        {
                            positiveEffectStats[category] += otherValue * multiplier;
                        }
                        else
                        {
                            positiveEffectStats[category] = otherValue * multiplier;
                        }
                    }
                }

                if (other.negativeEffectStats != null && other.negativeEffectStats.Count > 0)
                {
                    negativeEffectStats ??= [];
                    foreach ((string category, float otherValue) in other.negativeEffectStats)
                    {
                        if (negativeEffectStats.ContainsKey(category))
                        {
                            negativeEffectStats[category] += otherValue * multiplier;
                        }
                        else
                        {
                            negativeEffectStats[category] = otherValue * multiplier;
                        }
                    }
                }
            }

            if (other.generalStats != null && other.generalStats.Count > 0)
            {
                generalStats ??= [];
                foreach ((string category, float otherValue) in other.generalStats)
                {
                    if (generalStats.ContainsKey(category))
                    {
                        generalStats[category] += otherValue * multiplier;
                    }
                    else
                    {
                        generalStats[category] = otherValue * multiplier;
                    }
                }
            }

            canEat &= other.canEat;
        }

        public virtual void Apply(EntityStats entityStats, string entityStatCode)
        {
            entityStats.Remove(entityStatCode);

            entityStats.Set("walkspeed", entityStatCode, walkSpeed, true);
            entityStats.Set("healingeffectivness", entityStatCode, healingeffectivness, true);
            entityStats.Set("hungerrate", entityStatCode, hungerrate, true);
            entityStats.Set("rangedWeaponsAcc", entityStatCode, rangedWeaponsAcc, true);
            entityStats.Set("rangedWeaponsSpeed", entityStatCode, rangedWeaponsSpeed, true);

            Dictionary<string, float> aggregatedValues = [];

            AggregateStatsFrom(generalStats, aggregatedValues, entityStatCode);
            AggregateStatsFrom(positiveEffectStats, aggregatedValues, entityStatCode);
            AggregateStatsFrom(negativeEffectStats, aggregatedValues, entityStatCode);

            foreach ((string category, float value) in aggregatedValues)
            {
                entityStats.Set(category, entityStatCode, value, persistent: true);
            }
        }

        public virtual void GetDescription(StringBuilder description, bool broken = false)
        {
            if (healingeffectivness != 0)
            {
                description.AppendLine(GetStatDescription("healingeffectivness", healingeffectivness));
            }

            if (hungerrate != 0)
            {
                description.AppendLine(GetStatDescription("hungerrate", hungerrate));
            }

            if (rangedWeaponsAcc != 0)
            {
                description.AppendLine(GetStatDescription("rangedWeaponsAcc", rangedWeaponsAcc));
            }

            if (rangedWeaponsSpeed != 0)
            {
                description.AppendLine(GetStatDescription("rangedWeaponsSpeed", rangedWeaponsSpeed));
            }

            if (walkSpeed != 0)
            {
                description.AppendLine(GetStatDescription("walkSpeed", walkSpeed));
            }

            if (positiveEffectStats != null)
            {
                foreach ((string category, float value) in positiveEffectStats)
                {
                    if ((broken && value <= 0) || value == 0) continue; 

                    description.AppendLine(GetStatDescription(category, value));
                }
            }

            if (negativeEffectStats != null)
            {
                foreach ((string category, float value) in negativeEffectStats)
                {
                    if ((broken && value >= 0) || value == 0) continue;

                    description.AppendLine(GetStatDescription(category, value));
                }
            }

            if (generalStats != null)
            {
                foreach ((string category, float value) in generalStats)
                {
                    if (value == 0) continue;

                    description.AppendLine(GetStatDescription(category, value));
                }
            }
        }

        /// <summary>
        /// Gets translated stat value description entry<br/>
        /// "wearable-stat-description-hungerrate": "Hunger rate: {1}{0:0%}" - will result in 'Hunger rate: +12%' from value of 0.123<br/>
        /// "wearable-stat-description-rangedweaponsspeed": "Ranged Weapon Charge Time: {2}{0:0.#%}" - will result in 'Ranged Weapon Charge Time: -12.3%' from value of 0.123
        /// </summary>
        /// <param name="entityStatCategory"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string GetStatDescription(string entityStatCategory, float value)
        {
            string valueSign = value > 0 ? "+" : "-";
            string inverseValueSign = value < 0 ? "+" : "-";
            return Lang.Get($"wearable-stat-description-{entityStatCategory}", Math.Abs(value), valueSign, inverseValueSign);
        }

        protected static void AggregateStatsFrom(Dictionary<string, float>? statValues, Dictionary<string, float> aggregatedValues, string code)
        {
            if (statValues == null) return;

            foreach ((string category, float value) in statValues)
            {
                if (aggregatedValues.ContainsKey(category))
                {
                    aggregatedValues[category] += value;
                }
                else
                {
                    aggregatedValues[category] = value;
                }
            }
        }
    }
}
