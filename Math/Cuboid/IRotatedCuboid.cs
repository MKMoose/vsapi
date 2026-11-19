using System;

namespace Vintagestory.API.MathTools
{
    public interface IRotatedCuboid<T, C> : IEquatable<C>
    {
        Vec3f RotationCenter { get; }
    }
}
