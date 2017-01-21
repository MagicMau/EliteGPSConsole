using System;

namespace WindowsOCRTest
{
    public class CoordinatesObserver : IObserver<Coordinates>
    {
        public void OnCompleted()
        {
            
        }

        public void OnError(Exception error)
        {
            Console.WriteLine($"ERROR: {error.Message}");
        }

        public void OnNext(Coordinates value)
        {
            Console.WriteLine($"SUCCESS: {value.ToString()}");
        }
    }
}