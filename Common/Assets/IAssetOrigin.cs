using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Vintagestory.API.Common
{
    public interface IAssetOrigin
    {
        string OriginPath { get; }

        /// <summary>
        /// Loads the asset into memory.
        /// </summary>
        /// <param name="asset">The asset to be loaded</param>
        /// <returns></returns>
        void LoadAsset(IAsset asset);

        /// <summary>
        /// Attempts to resolve the asset from the given path without loading it.
        /// </summary>
        /// <param name="filePath">The path to the asset file.</param>
        /// <param name="asset">The resolved asset.</param>
        /// <returns></returns>
        bool TryResolveAsset(string filePath, [NotNullWhen(true)] out IAsset? asset);

        /// <summary>
        /// Attempts to load the asset.  Returns false if it fails.
        /// </summary>
        /// <param name="asset">The asset to be loaded.</param>
        /// <returns></returns>
        bool TryLoadAsset(IAsset asset);

        /// <summary>
        /// Returns all assets of the given category which can be found in this origin
        /// </summary>
        /// <param name="category"></param>
        /// <param name="shouldLoad"></param>
        /// <returns></returns>
        List<IAsset> GetAssets(AssetCategory category, bool shouldLoad = true);


        /// <summary>
        /// Returns all assets of the given base location path which can be found in this origin
        /// </summary>
        /// <param name="baseLocation"></param>
        /// <param name="shouldLoad"></param>
        /// <returns></returns>
        List<IAsset> GetAssets(AssetLocation baseLocation, bool shouldLoad = true);


        /// <summary>
        /// Resource packs are not allowed to affect gameplay
        /// </summary>
        /// <returns></returns>
        bool IsAllowedToAffectGameplay();
    }
}
