using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace WindowsOCRTest
{
    public class CoordinateRecognizer
    {
        private readonly OcrEngine ocrEngine;
        private readonly ImageProcessor imageProcessor;

        public CoordinateRecognizer()
        {
            ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));

            if (ocrEngine == null)
            {
                System.Diagnostics.Trace.WriteLine($"No OCR engine created");
            }

            imageProcessor = new ImageProcessor();
        }

        public async Task<Coordinates> RecognizeAsync(string path)
        {
            try
            {
                // first, find the coordinates in the screenshot
                var bmp = await imageProcessor.FindCoordinateBox(path);
                if (bmp == null)
                {
                    System.Diagnostics.Trace.WriteLine("Coordinates not found on screenshot");
                    return null;
                }

                var lines = imageProcessor.ExtractLines(bmp, path);
                if (lines.Length != 2)
                {
                    System.Diagnostics.Trace.WriteLine($"Found {lines.Length} lines, that's not right");
                    return null;
                }

                // then, use OCR to extract the coordinates
                double? latitude = await ProcessLine(lines[0], path);
                double? longitude = await ProcessLine(lines[1], path);

                if (latitude.HasValue && longitude.HasValue)
                    return new Coordinates { Latitude = latitude.Value, Longitude = longitude.Value };

                System.Diagnostics.Trace.WriteLine($"Coordinates not recognized, got lat = {latitude} and long = {longitude}");
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine($"Exception while recognizing: {e.Message}");
            }

            return null;
        }

        private async Task<double?> ProcessLine(Bitmap bmp, string path)
        {
            int bestDigitCount = 0;
            double? bestGuess = null;

            for (int attempt = 5; attempt > -6; attempt--)
            {
                // try rotating it a bit
                var rotatedBmp = await imageProcessor.RotateBitmap(bmp, attempt);
#if DEBUG_PICS
                try
                {
                    rotatedBmp.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), $"rotated_{attempt}.png"));
                }
                catch { }
#endif

                var rotatedResult = await RecognizeText(rotatedBmp);
                // count the number of digits recognized
                int digitCount = CountDigits(rotatedResult.Text);
                double? guess = await RecognizeLine(rotatedResult);
                if (digitCount >= bestDigitCount && guess.HasValue)
                {
                    if (bestDigitCount == digitCount && bestGuess < 0 && guess > 0)
                        continue; // prefer a negative match

                    bestGuess = guess;
                    bestDigitCount = digitCount;
                    System.Diagnostics.Trace.WriteLine($"New best guess at attempt {attempt} with {digitCount} digits: {guess}");
                }

                //if (thisDigitCount > 3 && guess.HasValue)
                //    return guess; // 4 or more digits? we've got a winner!
            }

            return bestGuess;
        }

        private int CountDigits(string input)
        {
            input = System.Text.RegularExpressions.Regex.Replace(input, @"[^0-9\-Øø]", string.Empty);
            return input.Length;
        }
        
        private async Task<OcrResult> RecognizeText(Bitmap bitmap)
        {
            var sbmp = await LoadExistingImage(bitmap);
            return await ocrEngine.RecognizeAsync(sbmp);
        }

        private Task<double?> RecognizeLine(OcrResult result)
        {
            return Task.Run(() =>
            {
                System.Diagnostics.Trace.WriteLine($"Found: {result.Text}");

                string text = result.Text
                    .Replace(',', '.')
                    .Replace('s', '5')
                    .Replace('S', '5')
                    .Replace('e', '8')
                    .Replace('B', '8')
                    .Replace('Ø', '0')
                    .Replace('€', '6')
                    .Replace('ø', '0')
                    .Replace('~', '-')
                    .Replace('—', '-')
                    .Replace('_', '-')
                    .Replace(" ", string.Empty);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"[^0-9\.\-]", "0");

                // if no decimal point recognized, or if it's the last character recognized, we missed the decimal bit
                if (!text.Contains('.') || (text.IndexOf('.') == (text.Length - 1)))
                    return default(double?);

                double value;
                if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    long l = (long)(value * 10000);
                    return l / 10000.0; // only keep 4 digits
                }

                return default(double?);
            });
        }

        private async Task<SoftwareBitmap> LoadExistingImage(Bitmap image)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                image.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                ms.Position = 0;
                var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                return bitmap;
            }
        }
    }
}
