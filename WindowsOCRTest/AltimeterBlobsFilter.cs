using AForge.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsOCRTest
{
    class AltimeterBlobsFilter : IBlobsFilter
    {
        public bool Check(Blob blob)
        {
            // we're looking for the altimer, the vertical line that tells us how high we are.
            // because just below that the coordinates are shown

            // find one that is at least 200 px high
            // is very narrow

            if (blob.Rectangle.Height < 175)
                return false;

            var ratio = (double)blob.Rectangle.Width / blob.Rectangle.Height;
            if (ratio > 0.15)
                return false;

            return true;
        }
    }

}
