using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace WindowsOCRTest
{
    class Program
    {
        static void Main(string[] args)
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
    }
}
