using System.Diagnostics.CodeAnalysis;
﻿using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Vintagestory.API.Datastructures
{
    public abstract class ScalarAttribute<T> where T : notnull
    {
        public T value = default(T)!;

        public virtual bool Equals(IWorldAccessor worldForResolve, [NotNullWhen(true)] IAttribute? attr)
        {
            if (attr is null) return false;
            return value.Equals(attr.GetValue()) || EqualityUtil.NumberEquals(value as object, attr.GetValue());
        }

        
        public override bool Equals(object? b)
        {
            return value.Equals(b);
        }

        public virtual object GetValue()
        {
            return value;
        }

        public virtual void SetValue(T newval)
        {
            value = newval;
        }

        public override string? ToString()
        {
            return value.ToString();
        }

        public virtual string ToJsonToken()
        {
            return value.ToString() ?? "null";
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }
    }
}
