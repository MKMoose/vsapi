using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common
{
    public class RecipeRegistryGeneric<T> : CustomRegistryBase where T : IByteSerializable, new()
    {
        public List<T> Recipes;

        public RecipeRegistryGeneric()
        {
            Recipes = new List<T>();
        }

        public RecipeRegistryGeneric(List<T> recipes)
        {
            this.Recipes = recipes;
        }

        public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
        {
            var reader = new BinarySpanReader(data);

            for (int j = 0; j < quantity; j++)
            {
                T rec = new T();
                rec.FromBytes(ref reader, resolver);
                Recipes.Add(rec);
            }
        }

        public override void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity)
        {
            using FastMemoryStream ms = new FastMemoryStream();
            ToBytes(resolver, out data, out quantity, ms);
        }

        public void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity, FastMemoryStream ms)
        {
            quantity = Recipes.Count;

            ms.Reset();
            BinaryWriter writer = new BinaryWriter(ms);

            foreach (T recipe in Recipes)
            {
                recipe.ToBytes(writer);
            }

            data = ms.ToArray();
        }

        public virtual void FreeRAMServer()
        {
            foreach (T recipe in Recipes)
            {
                if (recipe is GridRecipe gr) gr.FreeRAMServer();
            }
        }
    }

    [Obsolete("Use CustomRegistryBase instead")]
    public abstract class RecipeRegistryBase : CustomRegistryBase
    {
    }

    public abstract class CustomRegistryBase
    {
        public abstract void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity);

        public abstract void FromBytes(IWorldAccessor resolver, int quantity, byte[] data);
    }

}
