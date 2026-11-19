#nullable disable

namespace Vintagestory.API.Datastructures
{
    /// <summary>
    /// An object that can contain attributes and ignore them when calculating hash
    /// </summary>
    public interface IHashWithIgnoredAttributes
    {
        int GetHashCode(string[] ignoredAttributes);
    }
}
