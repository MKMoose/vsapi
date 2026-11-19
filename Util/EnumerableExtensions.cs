using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Vintagestory.API.Util;

public static class EnumerableExtensions
{
    public static int IndexOf<T>(this IEnumerable<T> source, Common.ActionBoolReturn<T> onelem)
    {
        int i = 0;
        foreach (var elem in source)
        {
            if (onelem(elem)) return i;
            i++;
        }

        return -1;
    }

    public static void Foreach<T>(this IEnumerable<T> array, Action<T> onelement)
    {
        foreach (var val in array)
        {
            onelement(val);
        }
    }

    [return: MaybeNull]
    public static T Nearest<T>(this IEnumerable<T> array, Func<T, double> getDistance)
    {
        var nearestDist = double.MaxValue;
        var nearest = default(T);
        foreach (var val in array)
        {
            double d = getDistance(val);

            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = val;
            }
        }

        return nearest;
    }


    public static double NearestDistance<T>(this IEnumerable<T> array, Func<T, double> getDistance)
    {
        double nearestDist = double.MaxValue;
        foreach (var val in array)
        {
            double d = getDistance(val);

            if (d < nearestDist)
            {
                nearestDist = d;
            }
        }

        return nearestDist;
    }

    public static IEnumerable<T> WithoutNulls<T>(this IEnumerable<T?> enumerable) where T : class
    {
        foreach (var val in enumerable)
            if (val != null)
                yield return val;
    }
}
