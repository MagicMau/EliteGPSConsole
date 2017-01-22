using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace WindowsOCRTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string file = @"C:\Users\MElbers\OneDrive\Elite\App\Test1\20170121121957SSS.bmp";

            if (file == null)
            {
                Watch();
            }
            else
            {
                Test(file).Wait();
            }
        }

        static void Watch()
        {
            string screenshotPath = @"C:\Users\MElbers\OneDrive\Elite\App\Input";
            string failedPath = @"C:\Users\MElbers\OneDrive\Elite\App\Failure";

            var watcher = new PictureFolderWatcher(screenshotPath, failedPath);
            watcher.Start();

            Console.WriteLine("Observing... Press ENTER to quit");

            using (watcher.CoordinatesStream.Subscribe(new CoordinatesObserver()))
            {
                Console.ReadLine();
            }

            watcher.Stop();
        }

        static async Task Test(string path)
        {
            var reco = new CoordinateRecognizer();
            var coords = await reco.RecognizeAsync(path);
            if (coords == null)
            {
                System.Diagnostics.Trace.WriteLine("xoxoxox NO COORDINATES FOUND Q_Q");
            }
            else
            {
                System.Diagnostics.Trace.WriteLine($"xoxoxox COORDINATES FOUND o_O: {coords.ToString()}");
            }
        }
    }
}
