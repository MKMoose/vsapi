using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.API.Common;

/// <summary>
/// Builds collision boxes from the cuboid elements of a model shape.
/// </summary>
public static class ShapeColliderBuilder
{
    /// <summary>
    /// Builds model-local collider boxes for the selected shape elements.
    /// Selectors match element names or full hierarchy paths and may contain wildcards.
    /// </summary>
    public static Cuboidd[] Build(Shape shape, IReadOnlyList<string>? selectiveElements = null, IReadOnlyList<string>? ignoreElements = null, double[]? modelTransform = null)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (modelTransform != null && modelTransform.Length < 16) throw new ArgumentException("A model transform requires sixteen matrix values.", nameof(modelTransform));
        if (shape.Elements == null || shape.Elements.Length == 0) return [];

        var collisionBoxes = new List<Cuboidd>();
        double[] rootTransform = modelTransform == null ? Mat4d.Create() : (double[])modelTransform.Clone();
        build(shape.Elements, rootTransform, null, selectiveElements, ignoreElements, collisionBoxes);
        return collisionBoxes.ToArray();
    }

    private static void build(ShapeElement[] elements, double[] parentTransform, string? parentPath, IReadOnlyList<string>? selectiveElements, IReadOnlyList<string>? ignoreElements, List<Cuboidd> collisionBoxes)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            ShapeElement element = elements[i];
            if (element?.Name == null) continue;

            string path = parentPath == null ? element.Name : parentPath + "/" + element.Name;
            if (element.From?.Length != 3 || element.To?.Length != 3)
            {
                throw new ArgumentException($"Shape element '{path}' requires three from and to coordinates.");
            }

            double[] transform = (double[])parentTransform.Clone();
            float[] localTransform = element.GetLocalTransformMatrix(0);
            double[] widenedLocalTransform = new double[16];
            for (int valueIndex = 0; valueIndex < widenedLocalTransform.Length; valueIndex++)
            {
                widenedLocalTransform[valueIndex] = localTransform[valueIndex];
            }
            Mat4d.Mul(transform, transform, widenedLocalTransform);

            bool selected = selectiveElements == null || matches(element.Name, path, selectiveElements);
            bool ignored = ignoreElements != null && matches(element.Name, path, ignoreElements);
            if (selected && !ignored)
            {
                double sizeX = (element.To[0] - element.From[0]) / 16;
                double sizeY = (element.To[1] - element.From[1]) / 16;
                double sizeZ = (element.To[2] - element.From[2]) / 16;
                if (sizeX > 0 && sizeY > 0 && sizeZ > 0)
                {
                    var transformed = new TransformedCuboidd(new Cuboidd(0, 0, 0, sizeX, sizeY, sizeZ), transform);
                    RotatedCuboidd rotated = transformed.ToRotatedCuboidd();
                    collisionBoxes.Add(rotated.IsRotated() ? rotated : new Cuboidd(rotated.X1, rotated.Y1, rotated.Z1, rotated.X2, rotated.Y2, rotated.Z2));
                }
            }

            if (element.Children != null)
            {
                build(element.Children, transform, path, selectiveElements, ignoreElements, collisionBoxes);
            }
        }
    }

    private static bool matches(string name, string path, IReadOnlyList<string> selectors)
    {
        for (int i = 0; i < selectors.Count; i++)
        {
            string selector = selectors[i];
            if (WildcardUtil.Match(selector, selector.Contains('/') ? path : name)) return true;
        }

        return false;
    }
}
