using System;

#nullable disable

namespace Vintagestory.API.Common
{
    /// <summary>
    /// Types of nutrition for foods.
    /// </summary>
    [DocumentAsJson]
    public enum EnumFoodCategory
    {
        NoNutrition = -1,
        Fruit = 0,
        Vegetable = 1,
        Protein = 2,
        Grain = 3,
        Dairy = 4,
        Unknown = 5
    }

    /// <summary>
    /// Allows you to make collectibles edible, and adds data about their nutrition.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"nutritionPropsByType": {
	///	"*-flyagaric-*": {
	///		"satiety": 80,
	///		"health": -6.5,
	///		"foodcategory": "Vegetable"
	///	},
	///	"*-earthball-*": {
	///		"satiety": 80,
	///		"health": -8,
	///		"foodcategory": "Vegetable"
	///	},
	/// ...
    /// </code>
    /// </example>
    [DocumentAsJson]
    public class FoodNutritionProperties
    {
        /// <summary>
        /// The category of the food.
        /// </summary>
        [DocumentAsJson("Recommended", "Fruit")]
        public EnumFoodCategory FoodCategory;

        /// <summary>
        /// The saturation restored by the food.
        /// </summary>
        [DocumentAsJson("Recommended", "0")]
        public float Satiety = 0f;

        /// <summary>
        /// Obsolete - Please use <see cref="Satiety"/> instead.
        /// </summary>
        [DocumentAsJson("Obsolete")]
        [Obsolete("Use Satiety instead.")]
        public float Saturation { get => Satiety; set => Satiety = value; }

        /// <summary>
        /// How much eating this will affect the player's intoxication. (0..1)
        /// </summary>
        [DocumentAsJson("Optional", "0")]
        public float Intoxication { get; set; }


        /// <summary>
        /// How much eating this will affect the player's psychedelic perception. (0..1)
        /// </summary>
        [DocumentAsJson("Optional", "0")]
        public float Psychedelic { get; set; }

        /// <summary>
        /// Obsolete - Please use <see cref="SatietyLossDelay"/> instead.
        /// </summary>
        [DocumentAsJson("Obsolete")]
        [Obsolete("Use SatietyLossDelay instead.")]
        public float SaturationLossDelay { get => SatietyLossDelay; set => SatietyLossDelay = value; }

        /// <summary>
        /// The delay before that extra saturation starts to go away.
        /// </summary>
        [DocumentAsJson("Optional", "10")]
        public float SatietyLossDelay = 10f;

        /// <summary>
        /// The health restored by the food. Usually actually used to hurt the player with negative values.
        /// </summary>
        [DocumentAsJson("Optional", "0")]
        public float Health = 0f;

        /// <summary>
        /// If greater than zero, healing will be turned into healing over time and will be fully applied over this time.
        /// If HealthTicks is set to 1, it will mean that healing will just be delayed by HealthDurationSec.
        /// </summary>
        [DocumentAsJson("Optional", "0")]
        public float HealthDurationSec { get; set; }

        /// <summary>
        /// If HealthDurationSec greater than zero, total healing will be split into this many portions, each applied in HealthDurationSec/HealthTicks time after another.
        /// </summary>
        [DocumentAsJson("Optional", "1")]
        public int HealthTicks { get; set; } = 1;

        /// <summary>
        /// When an instance of this collectible is eaten, what item stack should be returned to the player?
        /// Possible example: Eating a 'meat on a stick' item would return a single stick.
        /// (Note: Bowl meals/liquids are specially designed to do this through their attributes and class.)
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public JsonItemStack EatenStack;

        /// <summary>
        /// Duplicates the nutrition properties, which includes cloning the stack that was eaten.
        /// </summary>
        /// <returns></returns>
        public FoodNutritionProperties Clone()
        {
            return new FoodNutritionProperties
            {
                FoodCategory = FoodCategory,
                Satiety = Satiety,
                Health = Health,
                HealthDurationSec = HealthDurationSec,
                HealthTicks = HealthTicks,
                SatietyLossDelay = SatietyLossDelay,
                Intoxication = Intoxication,
                Psychedelic = Psychedelic,
                EatenStack = EatenStack?.Clone()
            };
        }

    }
}
