using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Generates AABB hitboxes from Vintage Story Shape models.
    /// </summary>
    public class ModelHitboxGenerator
    {
        readonly ICoreClientAPI capi;

        public ModelHitboxGenerator(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>
        /// Extracts hitboxes from a Shape object using hierarchy transforms.
        /// </summary>
        public List<ElementHitbox> ExtractHitboxesFromShape(Shape shape)
        {
            var hitboxes = new List<ElementHitbox>();

            if (shape?.Elements == null) return hitboxes;

            Matrixf initialTransform = new Matrixf();

            SearchShapeElements(shape.Elements, initialTransform, "", hitboxes);

            return hitboxes;
        }

        void SearchShapeElements(ShapeElement[] elements, Matrixf parentTransform, string parentPath, List<ElementHitbox> hitboxes)
        {
            if (elements == null) return;

            foreach (var element in elements)
            {
                if (element == null || string.IsNullOrEmpty(element.Name)) continue;

                string fullPath = string.IsNullOrEmpty(parentPath) ? element.Name : $"{parentPath}/{element.Name}";

                double x1 = element.From[0] / 16.0;
                double y1 = element.From[1] / 16.0;
                double z1 = element.From[2] / 16.0;
                double x2 = element.To[0] / 16.0;
                double y2 = element.To[1] / 16.0;
                double z2 = element.To[2] / 16.0;

                if (x1 != x2 || y1 != y2 || z1 != z2)
                {
                    const double MinExtent = 0.002;
                    double minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
                    double minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);
                    double minZ = Math.Min(z1, z2), maxZ = Math.Max(z1, z2);

                    if (maxX - minX < MinExtent) { double mid = (minX + maxX) / 2; minX = mid - MinExtent / 2; maxX = mid + MinExtent / 2; }
                    if (maxY - minY < MinExtent) { double mid = (minY + maxY) / 2; minY = mid - MinExtent / 2; maxY = mid + MinExtent / 2; }
                    if (maxZ - minZ < MinExtent) { double mid = (minZ + maxZ) / 2; minZ = mid - MinExtent / 2; maxZ = mid + MinExtent / 2; }

                    Cuboidd bounds = new Cuboidd(minX, minY, minZ, maxX, maxY, maxZ);

                    Matrixf transformCopy = new Matrixf();
                    transformCopy.Set(parentTransform.Values);

                    hitboxes.Add(new ElementHitbox(element.Name, fullPath, bounds, transformCopy) { JointId = element.JointId });
                }

                Matrixf childTransform = new Matrixf();
                childTransform.Set(parentTransform.Values);
                ApplyElementTransform(element, childTransform);

                if (element.Children != null)
                {
                    SearchShapeElements(element.Children, childTransform, fullPath, hitboxes);
                }
            }
        }

        void ApplyElementTransform(ShapeElement element, Matrixf transform)
        {
            float x = (float)(element.From[0] / 16.0);
            float y = (float)(element.From[1] / 16.0);
            float z = (float)(element.From[2] / 16.0);
            transform.Translate(x, y, z);

            if (element.RotationX != 0) transform.RotateX((float)(element.RotationX * GameMath.DEG2RAD));
            if (element.RotationY != 0) transform.RotateY((float)(element.RotationY * GameMath.DEG2RAD));
            if (element.RotationZ != 0) transform.RotateZ((float)(element.RotationZ * GameMath.DEG2RAD));
        }
    }
}
