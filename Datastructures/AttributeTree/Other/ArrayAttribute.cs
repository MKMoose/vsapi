using System;
using System.Collections;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Vintagestory.API.Datastructures
{
    public abstract class ArrayAttribute<T>
    {
        public T[] value = null!;

        public virtual bool Equals(IWorldAccessor worldForResolve, IAttribute? attr)
        {
            if (attr is null) return false;

            object? othervalue = attr.GetValue();
            if (othervalue == null) return false;
            if (!othervalue.GetType().IsArray) return false;

            IList a = (IList)value;
            IList? b = othervalue as IList;

            if (b == null) return false;
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!Equals(a[i], b[i]) && !EqualityUtil.NumberEquals(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        

        public virtual object GetValue()
        {
            return value;
        }


        public virtual string ToJsonToken()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");

            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0) sb.Append(", ");

                if (value[i] is IAttribute attribute)
                {
                    sb.Append(attribute.ToJsonToken());
                } else
                {
                    sb.Append(value[i]);
                }                
            }
            sb.Append("]");

            return sb.ToString();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");

            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0) sb.Append(", ");

                sb.Append(value[i]);
            }

            sb.Append("]");

            return sb.ToString();
        }


        public override int GetHashCode()
        {
            HashCode hash = new();
            for (int i = 0; i < value.Length; i++)
            {
                hash.Add(value[i]);
            }

            return hash.ToHashCode();
        }
    }
}
