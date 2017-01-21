using AForge.Imaging;
using AForge.Imaging.Filters;
using System;
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
        private int[] orange = new[] { 255, 127, 0 };


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
                binaryBmp.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "binary.png"));
            }
            catch { }
#endif


            var blobCounter = new BlobCounter();
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
                var coordsY = blob.Rectangle.Y + blob.Rectangle.Height + 15;
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
                var ranges = CalculateRanges(orange);

                var colorFilter = new ColorFiltering();
                colorFilter.Red = ranges[0];
                colorFilter.Green = ranges[1];
                colorFilter.Blue = ranges[2];

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

        private AForge.IntRange[] CalculateRanges(int[] rgb)
        {
            // Assumption #1. Take the transformed color,
            // do R and B * 0.569 and G * 0.394. Make sure there is at least a gap of 25.

            var reds = CalculateColor(rgb[0], 0.569);
            var greens = CalculateColor(rgb[1], 0.394);
            var blues = CalculateColor(rgb[2], 0.569);
            
            return new AForge.IntRange[] { reds, greens, blues };
        }

        private AForge.IntRange CalculateColor(int color, double ratio)
        {
            // calc the minimum of the range
            int min = (int)Math.Round(color * ratio);

            // make sure there is at least a 25 gap between min and max
            int diff;
            while ((diff = Math.Abs(color - min)) < 25)
            {
                int fill = 25 - diff;

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
    }
}
