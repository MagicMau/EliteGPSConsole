using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsOCRTest
{
    public class Coordinates
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }


        public override string ToString()
        {
            return $"({Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        }

        public override int GetHashCode()
        {
            long h = Latitude.GetHashCode() + Longitude.GetHashCode();
            return (int)(h % int.MaxValue);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Coordinates);
        }

        public bool Equals(Coordinates that)
        {
            return Latitude == that.Latitude && Longitude == that.Longitude;
        }
    }
}
