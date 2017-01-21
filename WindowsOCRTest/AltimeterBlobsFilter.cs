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

            // the corresponding blob will be 
            // - about 9.423 times higher than wide
            // - have a fullness of about 0.10125588697017268
            // - have an area of about 645
            // - have a center of gravity with Y ~ 0.5387755 * Height

            var heightRatio = blob.Rectangle.Height / (double)blob.Rectangle.Width;
            var fullness = blob.Fullness;
            var area = blob.Area;
            var cogYRatio = (blob.CenterOfGravity.Y - blob.Rectangle.Y) / blob.Rectangle.Height;

            // yes this can be optimized

            if (heightRatio < 7)
                return false;

            //if (fullness < 0.07 || fullness > 0.10)
            //    return false;

            if (area < 400 || area > 700)
                return false;

            if (cogYRatio < 0.51 || cogYRatio > 0.58)
                return false;

            return true;
        }
    }

}
