using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.API.Common
{
    /// <summary>
    /// A registerable object with variants, i.e. an item, a block or an entity
    /// </summary>
    public abstract class RegistryObject
    {
        /// <summary>
        /// A unique domain + code of the object. Must be globally unique for all items / all blocks / all entities.
        /// </summary>
        public AssetLocation Code = null;

        /// <summary>
        /// Variant values as resolved from blocktype/itemtype.  NOT set for entities - use entity.Properties.VariantStrict instead.
        /// </summary>
        public Datastructures.OrderedDictionary<string, string> VariantStrict = new();

        /// <summary>
        /// Variant values as resolved from blocktype/itemtype. Will not throw an null pointer exception when the key does not exist, but return null instead. NOT set for entities - use entity.Properties.Variant instead
        /// </summary>
        public RelaxedReadOnlyDictionary<string, string> Variant;

        /// <summary>
        /// The class handeling the object
        /// </summary>
        public string Class;

        /// <summary>
        /// Non-null if developer mode is enabled and this was loaded from file
        /// </summary>
        public string SourceFilePath;

        [ThreadStatic] private static StringBuilder tempSB;


        public RegistryObject()
        {
            Variant = new RelaxedReadOnlyDictionary<string, string>(VariantStrict);
        }

        /// <summary>
        /// Returns a new assetlocation with an equal domain and the given path
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public AssetLocation CodeWithPath(string path)
        {
            return Code.CopyWithPath(path);
        }

        // Shared right-to-left dash search, can be reused without
        // forcing a substring allocation when the caller doesn't need one.
        static int getCutIndexFromRight(string path, int componentsToRemove)
        {
            int i = path.Length;
            int index = 0;
            while (--i > 0 && componentsToRemove > 0)
            {
                if (path[i] == '-')
                {
                    index = i;
                    componentsToRemove--;
                }
            }
            return index;
        }

        // Shared left-to-right dash search, can be reused without
        // forcing a substring allocation when the caller doesn't need one.
        static int getCutIndexFromLeft(string path, int componentsToRemove)
        {
            int i = 0;
            int index = 0;
            while (++i < path.Length && componentsToRemove > 0)
            {
                if (path[i] == '-')
                {
                    index = i + 1;
                    componentsToRemove--;
                }
            }
            return index;
        }

        /// <summary>
        /// Removes componentsToRemove parts from the blocks code end by splitting it up at every occurence of a dash ('-'). Right to left.
        /// </summary>
        /// <param name="componentsToRemove"></param>
        /// <returns></returns>
        public string CodeWithoutParts(int componentsToRemove)
        {
            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            return Code.Path[..getCutIndexFromRight(Code.Path, componentsToRemove)];
        }


        /// <summary>
        /// Removes componentsToRemove parts from the blocks code beginning by splitting it up at every occurence of a dash ('-'). Left to Right
        /// </summary>
        /// <param name="componentsToRemove"></param>
        /// <returns></returns>
        public string CodeEndWithoutParts(int componentsToRemove)
        {
            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            return Code.Path[getCutIndexFromLeft(Code.Path, componentsToRemove)..];
        }

        /// <summary>
        /// Replaces the last parts from the blocks code and replaces it with components by splitting it up at every occurence of a dash ('-')
        /// </summary>
        public AssetLocation CodeWithParts(params string[] components)
        {
            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            if (Code == null) return null;

            // Calculate the exact length of the final string and use string.Create
            // to avoid unnecessary allocations during concatenation and Span operations.
            string path = Code.Path;
            int cutIndex = getCutIndexFromRight(path, components.Length);

            // Account for possible null in components.
            int totalLength = cutIndex;
            foreach (string comp in components) totalLength += 1 + (comp?.Length ?? 0);

            string newPath = string.Create(totalLength, (path, cutIndex, components), static (span, state) =>
            {
                (string path, int pos, string[] components) = state;
                // Copy the immutable prefix.
                path.AsSpan(0, pos).CopyTo(span);
                // Append components sequentially with a dash.
                foreach (string comp in components)
                {
                    span[pos++] = '-';
                    if (comp == null) continue;
                    comp.AsSpan().CopyTo(span[pos..]);
                    pos += comp.Length;
                }
            });

            return Code.CopyWithPath(newPath);
        }

        /// <summary>
        /// More efficient version of CodeWithParts if there is only a single parameter
        /// </summary>
        public AssetLocation CodeWithParts(string component)
        {
            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            if (Code == null) return null;

            // Optimized version for a single component: concatenate the string in a single allocation.
            string path = Code.Path;
            int cutIndex = getCutIndexFromRight(path, 1);

            string newPath = string.Create(cutIndex + 1 + (component?.Length ?? 0), (path, cutIndex, component), static (span, state) =>
            {
                (string path, int pos, string component) = state;
                path.AsSpan(0, pos).CopyTo(span);
                span[pos++] = '-';
                if (component == null) return;
                component.AsSpan().CopyTo(span[pos..]);
            });

            return Code.CopyWithPath(newPath);
        }

        public AssetLocation CodeWithVariant(string type, string value)
        {
            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            string firstPart = FirstCodePart();
            if (firstPart == null) return null;

            int totalLength = firstPart.Length;
            int typeIndex = -1;

            // Iterate over VariantStrict instead of Variant to avoid wrapper overhead.
            for (int i = 0; i < VariantStrict.Count; i++)
            {
                string chosen;
                if (VariantStrict.GetKeyAtIndex(i) == type)
                {
                    typeIndex = i;
                    chosen = value;
                }
                else chosen = VariantStrict.GetValueAtIndex(i);
                totalLength += 1 + (chosen?.Length ?? 0);
            }

            // No matching type found, return clone of original
            if (typeIndex == -1) return Code.Clone();

            return new AssetLocation(Code.Domain, string.Create(totalLength, (VariantStrict, firstPart, typeIndex, value), static (span, state) =>
            {
                (var variantStrict, string firstPart, int typeIndex, string value) = state;
                firstPart.AsSpan().CopyTo(span);
                int pos = firstPart.Length;
                for (int i = 0; i < variantStrict.Count; i++)
                {
                    span[pos++] = '-';
                    string chosen = i == typeIndex ? value : variantStrict.GetValueAtIndex(i);
                    if (chosen == null) continue;
                    chosen.AsSpan().CopyTo(span[pos..]);
                    pos += chosen.Length;
                }
            }));
        }

        public AssetLocation CodeWithVariants(Dictionary<string, string> valuesByType)
        {
            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            string firstPart = FirstCodePart();
            if (firstPart == null) return null;

            int totalLength = firstPart.Length;
            bool matchFound = false;
            foreach ((string key, string value) in VariantStrict)
            {
                string chosen = valuesByType.GetValueOrDefault(key, null);
                if (chosen != null) matchFound = true;
                else chosen = value;

                totalLength += 1 + (chosen?.Length ?? 0);
            }

            // No matching type found, return clone of original
            if (!matchFound) return Code.Clone();

            return new AssetLocation(Code.Domain, string.Create(totalLength, (VariantStrict, firstPart, valuesByType), static (span, state) =>
            {
                (var variantStrict, string firstPart, var valuesByType) = state;
                firstPart.AsSpan().CopyTo(span);
                int pos = firstPart.Length;
                foreach ((string key, string value) in variantStrict)
                {
                    span[pos++] = '-';
                    string chosen = valuesByType.GetValueOrDefault(key, value);
                    if (chosen == null) continue;
                    chosen.AsSpan().CopyTo(span[pos..]);
                    pos += chosen.Length;
                }
            }));
        }

        public AssetLocation CodeWithVariants(string[] types, string[] values)
        {
            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            string firstPart = FirstCodePart();
            if (firstPart == null) return null;

            int totalLength = firstPart.Length;
            int[] typeIndex = new int[VariantStrict.Count];
            bool matchFound = false;
            for (int i = 0; i < VariantStrict.Count; i++)
            {
                int index = types.IndexOf(VariantStrict.GetKeyAtIndex(i));
                typeIndex[i] = index;
                string chosen;
                if (index >= 0)
                {
                    matchFound = true;
                    chosen = values[index];
                }
                else chosen = VariantStrict.GetValueAtIndex(i);

                totalLength += 1 + (chosen?.Length ?? 0);
            }

            // No matching type found, return clone of original
            if (!matchFound) return Code.Clone();

            return new AssetLocation(Code.Domain, string.Create(totalLength, (VariantStrict, firstPart, typeIndex, values), static (span, state) =>
            {
                (var variantStrict, string firstPart, int[] typeIndex, string[] values) = state;
                firstPart.AsSpan().CopyTo(span);
                int pos = firstPart.Length;
                for (int i = 0; i < variantStrict.Count; i++)
                {
                    span[pos++] = '-';
                    int index = typeIndex[i];
                    string chosen = index >= 0 ? values[index] : variantStrict.GetValueAtIndex(i);
                    if (chosen == null) continue;
                    chosen.AsSpan().CopyTo(span[pos..]);
                    pos += chosen.Length;
                }
            }));
        }

        static void findPartSpan(string path, int atPosition, out int leftBoundary, out int rightBoundary)
        {
            leftBoundary = 0;
            int partIndex = 0;

            for (rightBoundary = 0; rightBoundary < path.Length; rightBoundary++)
            {
                if (path[rightBoundary] != '-') continue;

                if (partIndex == atPosition) return;

                leftBoundary = rightBoundary + 1;
                partIndex++;
            }

            // If there are no more dashes, the requested part (if index is valid) is the last one.
            if (atPosition != partIndex) throw new IndexOutOfRangeException("Index was outside the bounds of the array.");

            rightBoundary = path.Length;
        }

        /// <summary>
        /// Replaces one part from the blocks code and replaces it with components by splitting it up at every occurence of a dash ('-')
        /// </summary>
        public AssetLocation CodeWithPart(string part, int atPosition = 0)
        {
            if (Code == null) return null;

            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/StratumServer/Stratum/pull/140)
            string path = Code.Path;
            findPartSpan(path, atPosition, out int leftBoundary, out int rightBoundary);

            var safePart = part ?? string.Empty;
            int totalLength = path.Length - (rightBoundary - leftBoundary) + safePart.Length;

            string newPath = string.Create(totalLength, (path, leftBoundary, rightBoundary, safePart), static (span, state) =>
            {
                (string path, int leftBoundary, int rightBoundary, string part) = state;
                // Copy the beginning of the string.
                path.AsSpan(0, leftBoundary).CopyTo(span);
                // Insert the new part.
                part.AsSpan().CopyTo(span[leftBoundary..]);
                // Copy the remaining tail of the string.
                path.AsSpan(rightBoundary).CopyTo(span[(leftBoundary + part.Length)..]);
            });

            return Code.CopyWithPath(newPath);
        }


        /// <summary>
        /// Returns the n-th code part in inverse order. If the code contains no dash ('-') the whole code is returned. Returns null if posFromRight is too high.
        /// </summary>
        /// <param name="posFromRight"></param>
        /// <returns></returns>
        public string LastCodePart(int posFromRight = 0)
        {
            if (Code == null) return null;

            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/anegostudios/vsapi/pull/87)
            string path = Code.Path;
            int rightBoundary = path.Length; // Right boundary of the current part (initially end of string)
            int partIndex = 0; // Index of the current part, in reverse order

            // Traverse the string right to left searching for dashes
            for (int leftBoundary = path.Length; leftBoundary > 0; leftBoundary--)
            {
                if (path[leftBoundary - 1] != '-') continue;

                // Return the needed part if found
                if (partIndex == posFromRight) return path[leftBoundary..rightBoundary];

                // Otherwise shift to the next part
                rightBoundary = leftBoundary - 1;
                partIndex++;
            }

            // Reached the start of the string
            if (partIndex != posFromRight) return null;

            // If we're looking for the last part, we can return the full string
            return posFromRight == 0 ? path : path[..rightBoundary];
        }

        /// <summary>
        /// Returns the n-th code part. If the code contains no dash ('-') the whole code is returned. Returns null if posFromLeft is too high.
        /// </summary>
        /// <param name="posFromLeft"></param>
        /// <returns></returns>
        public string FirstCodePart(int posFromLeft = 0)
        {
            if (Code == null) return null;

            // We're not using Code.Path.Split('-') split here for performance reasons (PR: https://github.com/anegostudios/vsapi/pull/87)
            string path = Code.Path;
            int leftBoundary = 0; // Left boundary of the current part (initially start of string)
            int partIndex = 0; // Index of the current part

            // Traverse the string left to right searching for dashes
            for (int rightBoundary = 0; rightBoundary < path.Length; rightBoundary++)
            {
                if (path[rightBoundary] != '-') continue;

                // Return the needed part if found
                if (partIndex == posFromLeft) return path[leftBoundary..rightBoundary];

                // Otherwise shift to the next part
                leftBoundary = rightBoundary + 1;
                partIndex++;
            }

            // Reached the end of the string
            if (partIndex != posFromLeft) return null;

            // If we're looking for the first part, we can return the full string
            return posFromLeft == 0 ? path : path[leftBoundary..];
        }

        /// <summary>
        /// Returns true if any given wildcard matches the blocks/items code. E.g. water-* will match all water blocks
        /// </summary>
        /// <param name="wildcards"></param>
        /// <returns></returns>
        public bool WildCardMatch(AssetLocation[] wildcards)
        {
            foreach (AssetLocation wildcard in wildcards)
            {
                if (WildCardMatch(wildcard)) return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if given wildcard matches the blocks/items code. E.g. water-* will match all water blocks
        /// </summary>
        /// <param name="wildCard"></param>
        /// <returns></returns>
        public bool WildCardMatch(AssetLocation wildCard)
        {
            return Code != null && WildcardUtil.Match(wildCard, Code);
        }


        /// <summary>
        /// Returns true if any given wildcard matches the blocks/items code. E.g. water-* will match all water blocks
        /// </summary>
        /// <param name="wildcards"></param>
        /// <returns></returns>
        public bool WildCardMatch(string[] wildcards)
        {
            foreach (string wildcard in wildcards)
            {
                if (WildCardMatch(wildcard)) return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if given wildcard matches the blocks/items code. E.g. water-* will match all water blocks
        /// </summary>
        /// <param name="wildCard"></param>
        /// <returns></returns>
        public bool WildCardMatch(string wildCard)
        {
            return Code != null && WildcardUtil.Match(wildCard, Code.Path);
        }


        /// <summary>
        /// Used by the block loader to replace wildcards with their final values
        /// </summary>
        /// <param name="input"></param>
        /// <param name="searchReplace"></param>
        /// <returns></returns>
        public static AssetLocation FillPlaceHolder(AssetLocation input, Datastructures.OrderedDictionary<string, string> searchReplace)
        {
            foreach (var val in searchReplace)
            {
                input.Path = FillPlaceHolder(input.Path, val.Key, val.Value);
            }

            return input;
        }

        /// <summary>
        /// Used by the block loader to replace wildcards with their final values
        /// </summary>
        /// <param name="input"></param>
        /// <param name="searchReplace"></param>
        /// <returns></returns>
        public static string FillPlaceHolder(string input, Datastructures.OrderedDictionary<string, string> searchReplace)
        {
            foreach (var val in searchReplace)
            {
                input = FillPlaceHolder(input, val.Key, val.Value);
            }

            return input;
        }

        /// <summary>
        /// Used by the block loader to replace wildcards with their final values
        /// </summary>
        /// <param name="input"></param>
        /// <param name="search"></param>
        /// <param name="replace"></param>
        /// <returns></returns>
        public static string FillPlaceHolder(string input, string search, string replace)
        {
            string pattern = @"\{((" + search + @")|([^\{\}]*\|" + search + @")|(" + search + @"\|[^\{\}]*)|([^\{\}]*\|" + search + @"\|[^\{\}]*))\}";

            return Regex.Replace(input, pattern, replace);
        }

        /// <summary>
        /// Fast variant of FillPlaceHolder that processes all replacements in a single pass
        /// </summary>
        public static string FillPlaceHolderOptimized(string input, Datastructures.OrderedDictionary<string, string> searchReplace)
        {
            if (searchReplace == null || searchReplace.Count == 0 || string.IsNullOrWhiteSpace(input) || !input.Contains('{'))
            {
                return input;
            }

            var inputSpan = input.AsSpan();
            int length = inputSpan.Length;
            tempSB ??= new StringBuilder(length);
            tempSB.Clear();
            tempSB.EnsureCapacity(length);

            var pos = 0;
            int closePos = 0;
            while (pos < length)
            {
                int openPos = input.IndexOf('{', pos);
                if (openPos < 0)
                {
                    openPos = length;
                }
                else
                {
                    closePos = input.IndexOf('}', openPos + 1);
                    if (closePos < 0)
                    {
                        openPos = length;
                    }
                }

                tempSB.Append(inputSpan[pos..openPos]);       // append everything we have up to either the {...} or the end
                if (openPos >= length) break;

                var placeholder = inputSpan[(openPos + 1)..closePos];   // does not include the curly braces
                if (tryResolvePlaceholder(placeholder, searchReplace, out var value))
                {
                    tempSB.Append(value);
                }
                else tempSB.Append(inputSpan[openPos..(closePos + 1)]);   // includes the curly braces

                pos = closePos + 1;
            }

            return tempSB.ToString();
        }

        private static bool tryResolvePlaceholder(ReadOnlySpan<char> placeholder, Datastructures.OrderedDictionary<string, string> searchReplace, out string value)
        {
            if (placeholder.IndexOf('|') < 0)
            {
                // Single variant type placeholder: this is the common case and simpler to handle

                foreach ((string key, string val) in searchReplace)
                {
                    var keySpan = key.AsSpan();
                    if (keySpan.SequenceEqual(placeholder))
                    {
                        value = val;
                        return true;
                    }
                }
            }
            else
            {
                // Multiple variants in placeholder, separated by '|': this is the complex case, never used in Vanilla

                Span<Range> parts = stackalloc Range[placeholder.Length / 2];
                var partCount = 0;

                var start = 0;
                for (var i = 0; i <= placeholder.Length; i++)
                {
                    if (i < placeholder.Length && placeholder[i] != '|') continue;

                    parts[partCount++] = start..i;
                    start = i + 1;
                }

                foreach ((string key, string val) in searchReplace)
                {
                    var keySpan = key.AsSpan();
                    foreach (var r in parts[..partCount])
                    {
                        var part = placeholder[r];
                        if (!part.SequenceEqual(keySpan)) continue;

                        value = val;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }
    }

}
