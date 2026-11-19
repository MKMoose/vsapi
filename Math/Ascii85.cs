using System;
using System.Text;
using System.IO;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.API.MathTools
{
    /// <summary>
    /// Converts between binary data and an Ascii85-encoded string.
    /// </summary>
    /// <remarks>See <a href="http://en.wikipedia.org/wiki/Ascii85">Ascii85 at Wikipedia</a>.</remarks>
    public static class Ascii85
    {
        // the first and last characters used in the Ascii85 encoding character set
        const char c_firstCharacter = '!';
        const char c_lastCharacter = 'u';

        /// <summary>
        /// Encodes the specified byte array in Ascii85.
        /// </summary>
        /// <param name="bytes">The bytes to encode.</param>
        /// <returns>An Ascii85-encoded string representing the input byte array.</returns>
        public static string Encode(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            int exactLength = GetEncodedLength(bytes, bytes.Length);
            return string.Create(exactLength, bytes, (chars, bytes) => CreateAscii85Chars(chars, bytes, bytes.Length));
        }


        public static string Encode(FastMemoryStream ms)
        {
            ArgumentNullException.ThrowIfNull(ms);

            byte[] bytes = ms.GetBuffer();
            int bytesLength = (int)ms.Length;
            int exactLength = GetEncodedLength(bytes, bytesLength);
            return string.Create(exactLength, bytes, (chars, bytes) => CreateAscii85Chars(chars, bytes, bytesLength));
        }


        private static void CreateAscii85Chars(Span<char> chars, byte[] bytes, int bytesLength)
        {
            int charIdx = 0;
            int i = 0;

            Span<char> temp = stackalloc char[5];

            // Process full groups of 4 bytes
            int mainLength = bytesLength - 4;
            while (i <= mainLength)
            {
                uint val = ((uint)bytes[i] << 24) | ((uint)bytes[i + 1] << 16) | ((uint)bytes[i + 2] << 8) | bytes[i + 3];

                if (val == 0)
                {
                    chars[charIdx++] = 'z';
                }
                else
                {
                    for (int k = 4; k >= 0; k--)
                    {
                        temp[k] = (char)(c_firstCharacter + (val % 85));
                        val /= 85;
                    }
                    temp.CopyTo(chars.Slice(charIdx, 5));
                    charIdx += 5;
                }
                i += 4;
            }

            // Process the tail (1–3 bytes)
            int remainder = bytesLength - i;
            if (remainder > 0)
            {
                uint val = 0;
                for (int j = 0; j < remainder; j++) val |= (uint)(bytes[i + j]) << (24 - 8 * j);

                int charsToWrite = remainder + 1; // the final partial block between 2 and 4 characters

                for (int k = 4; k >= 0; k--)      // for the padding, the least significant characters (e.g. temp[4]) are irrelevant but we still calculate them so that val is correct
                {
                    temp[k] = (char)(c_firstCharacter + (val % 85));
                    val /= 85;              
                }
                temp[..charsToWrite].CopyTo(chars.Slice(charIdx, charsToWrite));
            }
        }


        /// <summary>
        /// Decodes the specified Ascii85 string into the corresponding byte array.
        /// </summary>
        /// <param name="encoded">The Ascii85 string.</param>
        /// <returns>The decoded byte array.</returns>
        public static byte[] Decode(string encoded)
        {
            ArgumentNullException.ThrowIfNull(encoded);

            int decodedLength = GetDecodedLength(encoded);
            byte[] result = new byte[decodedLength];

            int byteIdx = 0;
            int count = 0;
            long value = 0;

            for (int i = 0; i < encoded.Length; i++)
            {
                char ch = encoded[i];
                switch (ch)
                {
                    case 'z' when count == 0:
                        result[byteIdx++] = 0;
                        result[byteIdx++] = 0;
                        result[byteIdx++] = 0;
                        result[byteIdx++] = 0;
                        break;
                    case < c_firstCharacter:
                    case > c_lastCharacter:
                        throw new FormatException($"Invalid character '{ch}' in Ascii85 block.");
                    default:
                        value = value * 85 + (uint)(ch - c_firstCharacter);

                        count++;

                        if (count == 5)
                        {
                            if (value > uint.MaxValue)
                            {
                                throw new FormatException("Ascii85 decoding: the current group of characters decodes to a value greater than UInt32.MaxValue.");
                            }

                            result[byteIdx++] = (byte)(value >> 24);
                            result[byteIdx++] = (byte)(value >> 16);
                            result[byteIdx++] = (byte)(value >> 8);
                            result[byteIdx++] = (byte)(value);
                            value = 0;
                            count = 0;
                        }

                        break;
                }
            }

            // Handle final partial block
            if (count > 0)
            {
                if (count == 1)
                {
                    throw new FormatException("The final Ascii85 block must contain more than one character.");
                }

                int byteCount = count - 1;

                // Pad missing characters with maximum values (84) - this is needed to counteract character truncation on the encoding side in the final block
                for (; count < 5; count++)
                {
                    value = value * 85 + 84u;
                }

                if (value > uint.MaxValue)
                {
                    throw new FormatException("Ascii85 decoding: the final group of characters decodes to a value greater than UInt32.MaxValue.");
                }

                result[byteIdx++] = (byte)(value >> 24);
                if (byteCount > 1) result[byteIdx++] = (byte)(value >> 16);
                if (byteCount > 2) result[byteIdx++] = (byte)(value >> 8);
            }

            return result;
        }


        private static int GetEncodedLength(byte[] bytes, int length)
        {
            int len = 0;
            int i = 0;
            while (i <= length - 4)
            {
                if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 0 && bytes[i + 3] == 0)
                {
                    len++;
                }
                else len += 5;
                i += 4;
            }
            int remainder = length - i;
            if (remainder > 0) len += remainder + 1; // partial group
            return len;
        }


        private static int GetDecodedLength(string encoded)
        {
            int count = 0;
            int bytes = 0;
            foreach (char ch in encoded)
            {
                switch (ch)
                {
                    case 'z' when count == 0:
                        bytes += 4;
                        break;
                    case >= c_firstCharacter and <= c_lastCharacter:
                        {
                            count++;
                            if (count == 5)
                            {
                                bytes += 4;
                                count = 0;
                            }

                            break;
                        }
                    default:
                        throw new FormatException($"Invalid character '{ch}' in Ascii85 block.");
                }
            }

            if (count == 1)
            {
                throw new FormatException("The final Ascii85 block must contain more than one character.");
            }

            if (count > 1)
            {
                bytes += count - 1;
            }

            return bytes;
        }
    }
}
