using System;

namespace Vintagestory.API.Common;

/// <summary>
/// Marks a method as registering polymorphic asset types via <see cref="AssetRegistry"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RegistersAssetTypesAttribute : Attribute;
