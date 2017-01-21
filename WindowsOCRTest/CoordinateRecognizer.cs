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

        private Dictionary<int, OcrResult> rotations = new Dictionary<int, OcrResult>();

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

                // then, use OCR to extract the coordinates
                var firstResult = await RecognizeText(bmp);
                double? latitude = await ProcessLine(firstResult, bmp, 0, path);
                double? longitude = await ProcessLine(firstResult, bmp, 1, path);

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

        private async Task<double?> ProcessLine(OcrResult ocrResult, Bitmap bmp, int lineNr, string path)
        {
            // first see if we can find it on the first try
            double? value = await RecognizeLine(ocrResult, lineNr);

            if (value == null)
            {
                double textAngle = ocrResult.TextAngle ?? 0;

                if (textAngle == 0)
                    textAngle = 0.5;

                for (int attempt = 2; value == null && attempt > -2; attempt--)
                {
                    // try rotating it a bit
                    OcrResult rotatedResult;
                    if (!rotations.TryGetValue(attempt, out rotatedResult))
                    {
                        var rotatedBmp = await imageProcessor.RotateBitmap(bmp, attempt * textAngle);
#if DEBUG_PICS
                    try
                    {
                        rotatedBmp.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), $"rotated_{attempt}.png"));
                    }
                    catch { }
#endif

                        rotatedResult = await RecognizeText(rotatedBmp);
                        rotations[attempt] = rotatedResult;
                    }

                    value = await RecognizeLine(rotatedResult, lineNr);

                    if (value != null)
                        System.Diagnostics.Trace.WriteLine($"Found with attempt {attempt}");
                }
            }

            return value;
        }
        
        private async Task<OcrResult> RecognizeText(Bitmap bitmap)
        {
            var sbmp = await LoadExistingImage(bitmap);
            return await ocrEngine.RecognizeAsync(sbmp);
        }

        private Task<double?> RecognizeLine(OcrResult result, int lineNr)
        {
            return Task.Run(() =>
            {
                if (lineNr >= result.Lines.Count)
                    return default(double?);

                OcrLine line = result.Lines[lineNr];

                string text = line.Text
                    .Replace(',', '.')
                    .Replace('s', '5')
                    .Replace('S', '5')
                    .Replace('B', '8')
                    .Replace('~', '-')
                    .Replace(" ", string.Empty);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"[^0-9\.\-]", "0");

                // if no decimal point recognized, or if it's the last character recognized, we missed the decimal bit
                if (!text.Contains('.') || (text.IndexOf('.') == (text.Length - 1)))
                    return default(double?);

                double value;
                if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return value;

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
