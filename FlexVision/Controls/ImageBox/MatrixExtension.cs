using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cyotek.Windows.Forms.Demo
{
    public static class MatrixExtension
    {
        public static PointF TransformPoint(this Matrix @this, PointF point)
        {
            var points = new[] { point };

            @this.TransformPoints(points);

            return points[0];
        }
    }
}
