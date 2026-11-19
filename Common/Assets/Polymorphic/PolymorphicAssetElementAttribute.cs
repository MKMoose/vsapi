using System;

namespace Vintagestory.API.Common;

/// <summary>
/// Marks an interface as a polymorphic asset element type.
/// </summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
public sealed class PolymorphicAssetElementAttribute : Attribute
{
    public string Name { get; }

    public PolymorphicAssetElementAttribute(string name)
    {
        Name = name;
    }
}
