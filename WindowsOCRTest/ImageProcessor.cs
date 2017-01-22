using AForge.Imaging;
using AForge.Imaging.Filters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Ocr;

namespace WindowsOCRTest
{
    public class ImageProcessor
    {
        private Color orange = Color.FromArgb(255, 245, 100, 10);

        private static System.Drawing.Imaging.ColorMatrix greenColorMatrix = new System.Drawing.Imaging.ColorMatrix(
            new float[][]
            {
                new float[] {0f, 1f, 0f, 0, 0},
                new float[] {1f, 1f, .51f, 0, 0},
                new float[] {1f, 0f, 1f, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            });

        private static System.Drawing.Imaging.ColorMatrix noColorMatrix = new System.Drawing.Imaging.ColorMatrix(
            new float[][]
            {
                new float[] {1f, 0f, 0f, 0, 0},
                new float[] {0f, 1f, 0f, 0, 0},
                new float[] {0f, 0f, 1f, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            });

        private System.Drawing.Imaging.ColorMatrix colorMatrix = noColorMatrix;

        public Bitmap MakeBinary(Bitmap bitmap, int threshold)
        {
            var filter = new FiltersSequence(new IFilter[]
            {
                Grayscale.CommonAlgorithms.BT709,
                new Threshold(threshold)
            });
            return filter.Apply(bitmap);
        }

        public Task<Bitmap> FindCoordinateBox(string path)
        {
            return Task.Run(async () =>
            {
                // load the screenshot into memory
                var bmp = AForge.Imaging.Image.FromFile(path);

                Bitmap coordsBmp = null;

                // find the altimeter blob in the shot
                var blob = FindAltimeterBlob(bmp, 0x80, path);

                if (blob == null)
                    blob = FindAltimeterBlob(bmp, 0x70, path);

                if (blob == null)
                    blob = FindAltimeterBlob(bmp, 0x60, path);

                if (blob != null)
                {
                    // based on the altimeter blob find and crop the coordinates from the screenshot
                    coordsBmp = await CropCoordinatesBox(bmp, blob, path);
                }

                // done
                return coordsBmp;
            });
        }

        public Task<Bitmap> RotateBitmap(Bitmap bitmap, double angle)
        {
            return Task.Run(() => new RotateNearestNeighbor(angle).Apply(bitmap));
        }

        private Blob FindAltimeterBlob(Bitmap bitmap, int threshold, string path)
        {
            // make it binary (black and white)
            var binaryBmp = MakeBinary(bitmap, threshold);

#if DEBUG_PICS
            try
            {
                binaryBmp.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), $"binary_{threshold}.png"));
            }
            catch { }
#endif


            var blobCounter = new BlobCounter();
            blobCounter.ObjectsOrder = ObjectsOrder.YX;
            blobCounter.BlobsFilter = new AltimeterBlobsFilter();
            blobCounter.FilterBlobs = true;
            blobCounter.ProcessImage(binaryBmp);

            var blobs = blobCounter.GetObjectsInformation();

            if (blobs == null || blobs.Length == 0)
                return null;

            return blobs.Last();
        }

        private Task<Bitmap> CropCoordinatesBox(Bitmap bitmap, Blob blob, string path)
        {
            return Task.Run(() =>
            {
                // now, save the area just below this blob as coords.bmp
                var coordsY = blob.Rectangle.Y + blob.Rectangle.Height + 5;
                var coordsHeight = 65;
                var coordsX = blob.Rectangle.X - 15;
                var coordsWidth = 105;

                var cropRect = new Rectangle(coordsX, coordsY, coordsWidth, coordsHeight);

                bitmap = bitmap.Clone(cropRect, bitmap.PixelFormat);

                // add a 5 pixel black border to aid the OCR
                var borderedBmp = new Bitmap(bitmap.Width + 10, bitmap.Height + 10, bitmap.PixelFormat);
                using (var g = Graphics.FromImage(borderedBmp))
                    g.DrawImage(bitmap, new Point(5, 5));

#if DEBUG_PICS
                    try
                    {
                    borderedBmp.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "coords_orig.png"));
                    }
                    catch { }
#endif

                // extract colors and calculate RGB ranges to filter on
                var ranges = CalculateRanges(orange, colorMatrix);

                var minColor = ranges[0];
                var maxColor = ranges[1];

#if DEBUG_PICS
                try
                {
                    var clrBmp = new Bitmap(150, 50, bitmap.PixelFormat);
                    using (Graphics g = Graphics.FromImage(clrBmp))
                    using (var minBrush = new SolidBrush(minColor))
                    using (var colorBrush = new SolidBrush(GetTransformedColor(orange, colorMatrix)))
                    using (var maxBrush = new SolidBrush(maxColor))
                    {
                        g.FillRectangle(minBrush, new Rectangle(0, 0, 50, 50));
                        g.FillRectangle(colorBrush, new Rectangle(50, 0, 50, 50));
                        g.FillRectangle(maxBrush, new RectangleF(100, 0, 50, 50));
                    }

                    clrBmp.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "colors_min_max_to_filter.png"));
                }
                catch { }
#endif


                var colorFilter = new ColorFiltering();
                colorFilter.Red = new AForge.IntRange(minColor.R, maxColor.R);
                colorFilter.Green = new AForge.IntRange(minColor.G, maxColor.G);
                colorFilter.Blue = new AForge.IntRange(minColor.B, maxColor.B);

                var filter = new FiltersSequence(new IFilter[]
                {
                new ResizeBicubic(borderedBmp.Width * 12, borderedBmp.Height * 12),
                colorFilter
                });

                bitmap = filter.Apply(borderedBmp);

#if DEBUG_PICS
                try
                {
                    bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "coords_filtered.png"));
                }
                catch { }
#endif

                return bitmap;
            });
        }

        public Task<double> FindTextAngle(Bitmap bmp)
        {
            // for each X, find the lowest non-black Y.
            return Task.Run(() =>
            {
                int width = bmp.Width, height = bmp.Height;

                int minY = int.MaxValue, minYx = int.MaxValue;

                // find the highest point where there is a non black pixel
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        var color = bmp.GetPixel(x, y);
                        if (color.R + color.G + color.B > 0)
                        {
                            if (y < minY)
                            {
                                minY = y;
                                minYx = x;
                            }
                            break;
                        }
                    }
                }

                // now find the lowest point where there is a non black pixel
                // within a 10% screenheight margin
                int threshold = height / 10;

                int maxY = 0, maxYx = 0;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        var color = bmp.GetPixel(x, y);
                        if (color.R + color.G + color.B > 0)
                        {
                            if (y > maxY && y < (minY + threshold))
                            {
                                maxY = y;
                                maxYx = x;
                            }
                            break;
                        }
                    }
                }

                // did we find something useful?
                if (minY == maxY)
                    return 0;

                // are we going up or down?
                int leftY, leftX, rightY, rightX;
                if (maxYx > minYx)
                {
                    leftY = minY;
                    leftX = minYx;
                    rightY = maxY;
                    rightX = maxYx;
                }
                else
                {
                    leftY = maxY;
                    leftX = maxYx;
                    rightY = minY;
                    rightX = minYx;
                }

                // OK, we now have two points, let's calculate the angle between them
                double deltaY = rightY - leftY;
                double deltaX = rightX - leftX;

                double angle = Math.Atan2(deltaY, deltaX);

                return angle;
            });
        }

        private Color[] CalculateRanges(Color color, System.Drawing.Imaging.ColorMatrix colorMatrix)
        {
            // Assumption #1. Take the transformed color,
            // do R and B * 0.569 and G * 0.394. Make sure there is at least a gap of 25.

            color = GetTransformedColor(color, colorMatrix);

            var reds = CalculateColor(color.R, 0.569);
            var greens = CalculateColor(color.G, 0.394);
            var blues = CalculateColor(color.B, 0.569);

            int minRed = Math.Min(reds.Min, color.R);
            int maxRed = Math.Max(reds.Max, color.R);
            int minGreen = Math.Min(greens.Min, color.G);
            int maxGreen = Math.Max(greens.Max, color.G);
            int minBlue = Math.Min(blues.Min, color.B);
            int maxBlue = Math.Max(blues.Max, color.B);

            Color minColor = Color.FromArgb(255, minRed, minGreen, minBlue);
            Color maxColor = Color.FromArgb(255, maxRed, maxGreen, maxBlue);

            var minHue = Math.Min(color.GetHue(), Math.Min(minColor.GetHue(), maxColor.GetHue()));
            var maxHue = Math.Max(color.GetHue(), Math.Max(minColor.GetHue(), maxColor.GetHue()));

            var minSaturation = Math.Min(color.GetSaturation(), Math.Min(minColor.GetSaturation(), maxColor.GetSaturation()));
            var maxSaturation = Math.Max(color.GetSaturation(), Math.Max(minColor.GetSaturation(), maxColor.GetSaturation()));

            var minBrightness = Math.Min(color.GetBrightness(), Math.Min(minColor.GetBrightness(), maxColor.GetBrightness()));
            var maxBrightness = Math.Max(color.GetBrightness(), Math.Max(minColor.GetBrightness(), maxColor.GetBrightness()));

            return new Color[]
            {
                FromAhsb(255, minHue, minSaturation, minBrightness),
                FromAhsb(255, maxHue, maxSaturation, maxBrightness),
            };
        }

        private AForge.IntRange CalculateColor(int color, double ratio)
        {
            // calc the minimum of the range
            int min = (int)Math.Round(color * ratio);

            // make sure there is at least a 'minGap' gap between min and max
            int minGap = 30;
            int diff;
            while ((diff = Math.Abs(color - min)) < minGap)
            {
                int fill = minGap - diff;

                if (min == 0 && color == 0)
                {
                    color = fill;
                }
                else if (min > 0 && color < 255)
                {
                    int halfFill = fill / 2;
                    min = Math.Max(0, min - (fill - halfFill));
                    color = Math.Min(0, color + halfFill);
                }
                else if (min > 0)
                    min = Math.Max(0, min - fill);
                else if (color < 255)
                    color = Math.Min(0, color + fill);
            }

            return new AForge.IntRange(min, color);
        }


        /// http://stackoverflow.com/a/4106615/3131828
        /// <summary>
        /// Creates a Color from alpha, hue, saturation and brightness.
        /// </summary>
        /// <param name="alpha">The alpha channel value.</param>
        /// <param name="hue">The hue value.</param>
        /// <param name="saturation">The saturation value.</param>
        /// <param name="brightness">The brightness value.</param>
        /// <returns>A Color with the given values.</returns>
        public static Color FromAhsb(int alpha, float hue, float saturation, float brightness)
        {
            if (0 > alpha
                || 255 < alpha)
            {
                throw new ArgumentOutOfRangeException(
                    "alpha",
                    alpha,
                    "Value must be within a range of 0 - 255.");
            }

            if (0f > hue
                || 360f < hue)
            {
                throw new ArgumentOutOfRangeException(
                    "hue",
                    hue,
                    "Value must be within a range of 0 - 360.");
            }

            if (0f > saturation
                || 1f < saturation)
            {
                throw new ArgumentOutOfRangeException(
                    "saturation",
                    saturation,
                    "Value must be within a range of 0 - 1.");
            }

            if (0f > brightness
                || 1f < brightness)
            {
                throw new ArgumentOutOfRangeException(
                    "brightness",
                    brightness,
                    "Value must be within a range of 0 - 1.");
            }

            if (0 == saturation)
            {
                return Color.FromArgb(
                                    alpha,
                                    Convert.ToInt32(brightness * 255),
                                    Convert.ToInt32(brightness * 255),
                                    Convert.ToInt32(brightness * 255));
            }

            float fMax, fMid, fMin;
            int iSextant, iMax, iMid, iMin;

            if (0.5 < brightness)
            {
                fMax = brightness - (brightness * saturation) + saturation;
                fMin = brightness + (brightness * saturation) - saturation;
            }
            else
            {
                fMax = brightness + (brightness * saturation);
                fMin = brightness - (brightness * saturation);
            }

            iSextant = (int)Math.Floor(hue / 60f);
            if (300f <= hue)
            {
                hue -= 360f;
            }

            hue /= 60f;
            hue -= 2f * (float)Math.Floor(((iSextant + 1f) % 6f) / 2f);
            if (0 == iSextant % 2)
            {
                fMid = (hue * (fMax - fMin)) + fMin;
            }
            else
            {
                fMid = fMin - (hue * (fMax - fMin));
            }

            iMax = Convert.ToInt32(fMax * 255);
            iMid = Convert.ToInt32(fMid * 255);
            iMin = Convert.ToInt32(fMin * 255);

            switch (iSextant)
            {
                case 1:
                    return Color.FromArgb(alpha, iMid, iMax, iMin);
                case 2:
                    return Color.FromArgb(alpha, iMin, iMax, iMid);
                case 3:
                    return Color.FromArgb(alpha, iMin, iMid, iMax);
                case 4:
                    return Color.FromArgb(alpha, iMid, iMin, iMax);
                case 5:
                    return Color.FromArgb(alpha, iMax, iMin, iMid);
                default:
                    return Color.FromArgb(alpha, iMax, iMid, iMin);
            }
        }

        /// <summary>
        /// Use the BlobCounter to extract lines from the bitmap
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public Bitmap[] ExtractLines(Bitmap input, string path)
        {
            var binaryBmp = MakeBinary(input, 0x10);
#if DEBUG_PICS
            try
            {
                binaryBmp.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), $"binary_lines.png"));
            }
            catch { }
#endif


            var blobCounter = new BlobCounter();
            blobCounter.ObjectsOrder = ObjectsOrder.YX;
            blobCounter.ProcessImage(binaryBmp);

            var blobs = blobCounter.GetObjectsInformation();

            if (blobs.Length == 0)
            {
                System.Diagnostics.Trace.WriteLine("No lines extracted");
                return new Bitmap[0];
            }

            List<Rectangle> lines = new List<Rectangle>();

            Rectangle rect = blobs[0].Rectangle;

            foreach (var blob in blobs)
            {
                if (blob.Rectangle.Top > (rect.Top + rect.Height))
                {
                    // we started a new line apparently
                    if (rect.Width > 600 && rect.Height > 100)
                        lines.Add(rect);

                    rect = blob.Rectangle;
                }
                else
                {
                    // still on the same line, update our rectangle accordingly to make it a bounding box
                    int curRight = rect.Right;
                    int curBottom = rect.Bottom;

                    rect.X = Math.Min(rect.X, blob.Rectangle.X);
                    //rect.Y = Math.Min(rect.Y, blob.Rectangle.Y); Y never changes due to sort order ;)
                    rect.Width = Math.Max(curRight, blob.Rectangle.Right) - rect.X;
                    rect.Height = Math.Max(curBottom, blob.Rectangle.Bottom) - rect.Y;
                }
            }

            System.Diagnostics.Trace.WriteLine($"Detected {lines.Count} lines, extracting to separate bitmaps now");

            Bitmap[] linesBmps = new Bitmap[lines.Count];

            int lineNr = 0;
            foreach (var lineRect in lines)
            {
                var bitmap = new Bitmap(lineRect.Width + 100, lineRect.Height + 100, input.PixelFormat);
                using (var g = Graphics.FromImage(bitmap))
                    g.DrawImage(input, 50f, 50f, lineRect, GraphicsUnit.Pixel);

                //bitmap = MakeBinary(bitmap, 0x10);

#if DEBUG_PICS
                try
                {
                    bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), $"line_{lineNr}.png"));
                }
                catch { }
#endif

                linesBmps[lineNr++] = bitmap;
            }

            return linesBmps;
        }

        private ConcurrentDictionary<Color, Color> transformedColors = new ConcurrentDictionary<Color, Color>();

        public Color GetTransformedColor(Color color, System.Drawing.Imaging.ColorMatrix matrix)
        {
            Color transformedColor;

            if (transformedColors.TryGetValue(color, out transformedColor))
                return transformedColor;

            var bitmap = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var imgAttribs = new System.Drawing.Imaging.ImageAttributes();
            imgAttribs.SetColorMatrix(matrix);

            using (var g = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, new Rectangle(0, 0, 1, 1));
            }

            var transformedBitmap = new Bitmap(1, 1, bitmap.PixelFormat);
            using (var g = Graphics.FromImage(transformedBitmap))
                g.DrawImage(bitmap, new Rectangle(0, 0, 1, 1), 0, 0, 1, 1, GraphicsUnit.Pixel, imgAttribs);

            transformedColor = transformedBitmap.GetPixel(0, 0);
            transformedColors.TryAdd(color, transformedColor);

            return transformedColor;
        }

    }
}
