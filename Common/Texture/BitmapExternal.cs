using Cairo;
using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Util;


namespace Vintagestory.API.Common
{
    public static class SurfaceDrawImage
    {
        [Obsolete("Use 'Image' method that has 'BitmapExternal' as argument.")]
        public static void Image(this ImageSurface surface, BitmapRef bmp, int xPos, int yPos, int width, int height)
        {
            if (bmp is not BitmapExternal bmpExternal)
            {
                throw new ArgumentException($"'SurfaceDrawImage.Image' should receive 'BitmapExternal' as its '{nameof(bmp)}' argument, instead received '{bmp.GetType()}'", nameof(bmp));
            }

            surface.Image(bmpExternal.bmp, xPos, yPos, width, height);
        }

        public static void Image(this ImageSurface surface, BitmapExternal bmp, int xPos, int yPos, int width, int height)
        {
            surface.Image(bmp.bmp, xPos, yPos, width, height);
        }
    }

    public class BitmapExternal : BitmapRef
    {
        public SKBitmap bmp
        {
            get => bitmap ?? throw new ObjectDisposedException(nameof(bitmap), "Trying to access bitmap from bitmap reference after it was disposed.");
        }

        public override int Height => bmp.Height;

        public override int Width => bmp.Width;

        public override int[] Pixels => Array.ConvertAll(bmp.Pixels, p => (int)(uint)p);

        public IntPtr PixelsPtrAndLock => bmp.GetPixels();

        #nullable disable
        [Obsolete("This requires to manually set the underlying SKBitmap, prefer other overloads.")]
        public BitmapExternal()
        {
        }
        #nullable restore

        public BitmapExternal(SKBitmap bmp)
        {
            bitmap = bmp;
        }

        public BitmapExternal(int width, int height)
        {
            bitmap = new SKBitmap(width, height);
        }

        public BitmapExternal(MemoryStream ms, ILogger logger, AssetLocation? loc = null)
        {
            try
            {
                bitmap = Decode(ms.ToArray());
            }
            catch (Exception e)
            {
                if (loc != null)
                {
                    logger.Error("Failed loading bitmap from png file {0}. Will default to an empty 1x1 bitmap.", loc);
                    logger.Error(e);
                }
                else
                {
                    logger.Error("Failed loading bitmap. Will default to an empty 1x1 bitmap.");
                    logger.Error(e);
                }
                bitmap = new SKBitmap(1, 1);
                bitmap.SetPixel(0, 0, SKColors.Orange);
            }
        }

        ///// <summary>
        ///// Create a BitmapExternal from a path to an existing image file.  Calling code should check that the file exists
        ///// </summary>
        public BitmapExternal(string filePath, ILogger? logger = null)
        {
            try
            {
                bitmap = Decode(File.ReadAllBytes(filePath));
            }
            catch (Exception ex)
            {
                logger?.Error("Failed loading bitmap from data. Will default to an empty 1x1 bitmap.");
                logger?.Error(ex);
                bitmap = new SKBitmap(1, 1);
                bitmap.SetPixel(0, 0, SKColors.Orange);
            }
        }

        ///// <summary>
        ///// Create a BitmapExternal from a stream.  Calling code should close the stream.  The stream must have the Length property.
        ///// </summary>
        public BitmapExternal(Stream stream, ILogger? logger = null)
        {
            try
            {
                byte[] buffer = new byte[stream.Length];
                stream.ReadExactly(buffer);
                bitmap = Decode(buffer);
            }
            catch (Exception ex)
            {
                logger?.Error("Failed loading bitmap from data. Will default to an empty 1x1 bitmap.");
                logger?.Error(ex);
                bitmap = new SKBitmap(1, 1);
                bitmap.SetPixel(0, 0, SKColors.Orange);
            }
        }

        /// <summary>
        /// Create a BitmapExternal from a byte array
        /// </summary>
        public BitmapExternal(byte[] data, int dataLength, ILogger logger)
        {
            try
            {
                bitmap = Decode(data.AsSpan()[..dataLength]);
            }
            catch (Exception ex)
            {
                logger.Error("Failed loading bitmap from data. Will default to an empty 1x1 bitmap.");
                logger.Error(ex);
                bitmap = new SKBitmap(1, 1);
                bitmap.SetPixel(0, 0, SKColors.Orange);
            }
        }

        // Copypasted from SkBitmap.cs because the simplified Decode() changes AlphaType.Unpremul into AlphaType.Premul. wtf.
        public unsafe static SKBitmap Decode(ReadOnlySpan<byte> buffer)
        {
            fixed (byte* ptr = buffer)
            {
                using SKData data = SKData.Create((IntPtr)ptr, buffer.Length);
                using SKCodec codec = SKCodec.Create(data);

                SKImageInfo bitmapInfo = codec.Info;
                bitmapInfo.AlphaType = SKAlphaType.Unpremul;
                // needs to be set so on MacOS so we load the pixel in the correct color format for the GPU upload, else we get R / B swaped channels
                bitmapInfo.ColorType = SKColorType.Bgra8888;

                var bitmap = new SKBitmap (bitmapInfo);
                var result = codec.GetPixels (bitmapInfo, bitmap.GetPixels (out _));
                if (result != SKCodecResult.Success) {
                    bitmap.Dispose ();
                    throw new InvalidDataException($"Failed to decode bitmap from data with error: {result}");
                }

                return bitmap;
            }
        }

        public override void Dispose()
        {
            lock(disposeLock)
            {
                if (bitmap == null) return;

                SKBitmap? bitmapToDispose = bitmap;
                bitmap = null;
                bitmapToDispose?.Dispose();
            }
        }

        public override void Save(string filename)
        {
            bmp.Save(filename);
        }

        /// <summary>
        /// Retrives the ARGB value from given coordinate
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public override SKColor GetPixel(int x, int y)
        {
            return bmp.GetPixel(x, y);
        }

        /// <summary>
        /// Retrives the ARGB value from given coordinate using normalized coordinates (0..1)
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public override SKColor GetPixelRel(float x, float y)
        {
            return bmp.GetPixel((int)Math.Min(bmp.Width - 1, x * bmp.Width), (int)Math.Min(bmp.Height - 1, (y * bmp.Height)));
        }

        public override unsafe void MulAlpha(int alpha = 255)
        {
            var len = Width * Height;
            var af = alpha / 255f;
            var colp = (byte*)bmp.GetPixels().ToPointer();
            for (var i = 0; i < len; i++)
            {
                int a = colp[3];
                colp[0] = (byte)(colp[0] * af);
                colp[1] = (byte)(colp[1] * af);
                colp[2] = (byte)(colp[2] * af);

                colp[3] = (byte)(a * af);
                colp += 4;
            }
        }

        public override int[] GetPixelsTransformed(int rot = 0, int mulAlpha = 255)
        {
            int[] bmpPixels = new int[Width * Height];
            int width = bmp.Width;
            int height = bmp.Height;
            FastBitmap fastBitmap = new FastBitmap();
            fastBitmap.bmp = bmp;
            int stride = fastBitmap.Stride;
            switch (rot)
            {
                case 0:
                {
                    for (int y = 0; y < height; y++)
                    {
                        fastBitmap.GetPixelRow(width, y * stride, bmpPixels, y * width);
                    }

                    break;
                }
                case 90:
                {
                    for (int x = 0; x < width; x++)
                    {
                        int baseY = x * height;
                        for (int y = 0; y < height; y++)
                        {
                            bmpPixels[y + baseY] = fastBitmap.GetPixel(width - x - 1, y * stride);
                        }
                    }

                    break;
                }
                case 180:
                {
                    for (int y = 0; y < height; y++)
                    {
                        int baseX = y * width;
                        int yStride = (height - y - 1) * stride;
                        for (int x = 0; x < width; x++)
                        {
                            bmpPixels[x + baseX] = fastBitmap.GetPixel(width - x - 1, yStride);
                        }
                    }

                    break;
                }
                case 270:
                {
                    for (int x = 0; x < width; x++)
                    {
                        int baseY = x * height;
                        for (int y = 0; y < height; y++)
                        {
                            bmpPixels[y + baseY] = fastBitmap.GetPixel(x, (height - y - 1) * stride);
                        }
                    }

                    break;
                }
            }

            if (mulAlpha != 255)
            {
                var alpaP = mulAlpha / 255f;
                int clearAlpha = ~(0xff << 24);

                for (int i = 0; i < bmpPixels.Length; i++)
                {
                    var col = bmpPixels[i];
                    var curAlpha = (uint)col >> 24;
                    col &= clearAlpha;

                    bmpPixels[i] = col | ((int)(curAlpha * alpaP) << 24);
                }
            }

            return bmpPixels;
        }

        public override BitmapExternal CropTo(int newSize)
        {
            SKBitmap bmpPixels = new(newSize,newSize);
            int width = bmp.Width;
            int height = bmp.Height;
            int centerOffsetX = (width - newSize);
            int centerOffsetY = 0;
            int brown = (29 << 16) + (11 << 8) + 1;
            for (int x = 0; x < newSize; x++)
            {
                for (int y = 0; y < newSize; y++)
                {
                    var color = bmp.GetPixel(x + centerOffsetX, y + centerOffsetY);
                    int alpha = (int)((uint)color >> 24);
                    if (alpha < 255)
                    {
                        int rgb = (int)((uint)color & 0xFFFFFFu);
                        color = (uint)MathTools.ColorUtil.ColorOverlay(brown, rgb, alpha / 255f) | 0xFF000000u;
                    }
                    bmpPixels.SetPixel(x, y, color);
                }
            }

            return new BitmapExternal(bmpPixels);
        }


        /// <summary>
        /// Is set only in constructor and Dispose method
        /// </summary>
        private SKBitmap? bitmap;
        /// <summary>
        /// To prevent double diposing in concurrent scenarious
        /// </summary>
        private readonly Lock disposeLock = new();
    }
}
