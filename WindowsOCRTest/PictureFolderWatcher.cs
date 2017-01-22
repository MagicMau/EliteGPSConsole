using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WindowsOCRTest
{
    public class PictureFolderWatcher : FileSystemWatcher
    {
        private CoordinateRecognizer recognizer = new CoordinateRecognizer();
        private ConcurrentQueue<string> screenshots = new ConcurrentQueue<string>();
        private AutoResetEvent screenshotAvailable = new AutoResetEvent(false);
        private Thread recognizerThread = null;
        private string failedPath;

        private Subject<Coordinates> coordinateStream = new Subject<Coordinates>();

        public PictureFolderWatcher(string path, string failedPath)
        {
            if (!Directory.Exists(path))
                return; // if the path doesn't exist, we will not watch it either.

            Directory.CreateDirectory(failedPath);
            this.failedPath = failedPath;

            Filter = "*.bmp";
            NotifyFilter = NotifyFilters.FileName;
            try
            {
                Path = path;
            }
            catch (Exception)
            {

            }
        }

        public IObservable<Coordinates> CoordinatesStream { get { return coordinateStream; } }

        public void Start()
        {
            if (EnableRaisingEvents)
                return; // already watching

            // start a thread to recognize at a lower priority
            if (recognizerThread == null)
            {
                recognizerThread = new Thread(RecognizerThread)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "Coordinate Recognizer Thread"
                };
                recognizerThread.Start();
            }

            Created += PictureFolderWatcher_Created;
            Changed += PictureFolderWatcher_Changed;

            EnableRaisingEvents = true;
        }

        private void PictureFolderWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            Recognize(e.FullPath);
        }

        public void Stop()
        {
            EnableRaisingEvents = false;
            if (!(recognizerThread?.Join(1000) ?? true))
                recognizerThread.Abort();
        }

        private void PictureFolderWatcher_Created(object sender, FileSystemEventArgs e)
        {
            Recognize(e.FullPath);
        }

        private void Recognize(string path)
        {
            screenshots.Enqueue(path);
            screenshotAvailable.Set();
        }

        private void RecognizerThread()
        {
            while (EnableRaisingEvents)
            {
                while (EnableRaisingEvents && !screenshotAvailable.WaitOne(500))
                    ; // wait for a screenshot to become available

                string path;
                while (screenshots.TryDequeue(out path))
                {
                    try
                    {
                        var task = recognizer.RecognizeAsync(path);
                        task.Wait();

                        var coordinates = task.Result;
                        if (coordinates == null)
                        {
                            // recognition failed, move picture to failed path
                            string fileName = System.IO.Path.GetFileName(path);
                            string ext = System.IO.Path.GetExtension(path);
                            string destPath = System.IO.Path.Combine(failedPath, DateTime.Now.ToString("yyyyMMddHHmmssfff") + ext);

                            bool isSuccess = false;
                            while (!isSuccess)
                            {
                                try
                                {
                                    File.Move(path, destPath);
                                    isSuccess = true;
                                }
                                catch (Exception e)
                                {
                                    System.Diagnostics.Trace.WriteLine($"Exception moving {e.Message}");
                                    Thread.Sleep(500);
                                }
                            }
                        }
                        else
                        {
                            // add to the stream
                            coordinateStream.OnNext(coordinates);
                        }
                    }
                    catch (Exception e)
                    {
                        System.Diagnostics.Trace.WriteLine($"An exception in the coordinate recognizer: {e.Message}");
                    }
                }
            }
        }
    }
}
