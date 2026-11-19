using System;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Owns the selected-state overlay meshes for a group of filter buttons.
    /// </summary>
    public class Hud3DFilterOverlaySet : IDisposable
    {
        readonly MultiTextureMeshRef[] meshes;

        public Hud3DFilterOverlaySet(
            string[] elementNames,
            string parentPath,
            string selectedTextureCode,
            Func<string, string, string, MultiTextureMeshRef> tessellateElement)
        {
            meshes = new MultiTextureMeshRef[elementNames.Length];

            for (int i = 0; i < elementNames.Length; i++)
            {
                string elementName = elementNames[i];
                meshes[i] = tessellateElement(elementName, parentPath + "/" + elementName, selectedTextureCode);
            }
        }

        public void RenderSelected(int selectedIndex, Action<MultiTextureMeshRef> renderMesh)
        {
            if (selectedIndex < 0 || selectedIndex >= meshes.Length || renderMesh == null)
            {
                return;
            }

            var mesh = meshes[selectedIndex];
            if (mesh != null)
            {
                renderMesh(mesh);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i]?.Dispose();
            }
        }
    }
}
