using System;
using System.Drawing;

namespace FlexVision
{
    public enum FeatureType { Point, Line, Rectangle, Circle, Angle }

    // Minden mérési eredmény közös őse
    [Serializable]
    public abstract class BaseFeature
    {
        public string FeatureId { get; set; }        // Egyedi név (pl. "Pad_01_Center", "Wire_Left_Edge")
        public string CreatedByModule { get; set; } // Melyik modul mérte ki (pl. "Template Matching")
        public abstract FeatureType Type { get; }
    }

    // 1. PONT FEATURE (pl. Hegesztési súlypontok, huzal végpontok)
    [Serializable]
    public class PointFeature : BaseFeature
    {
        public override FeatureType Type => FeatureType.Point;
        public PointF Position { get; set; }

        public PointFeature() { }
        public PointFeature(string id, string module, PointF pt)
        {
            this.FeatureId = id;
            this.CreatedByModule = module;
            this.Position = pt;
        }
    }

    [Serializable]
    public class LineFeature : BaseFeature
    {
        public override FeatureType Type => FeatureType.Line;
        public PointF StartPoint { get; set; }
        public PointF EndPoint { get; set; }

        // Pixelben mért hossz
        public double LengthInPixels => Math.Sqrt(Math.Pow(EndPoint.X - StartPoint.X, 2) + Math.Pow(EndPoint.Y - StartPoint.Y, 2));

        // ÚJ: AUTOMATIKUS MILLIMÉTER ÁTSZÁMÍTÁS A GLOBÁLIS SZORZÓ ALAPJÁN
        public double LengthInMm => LengthInPixels * SystemConfiguration.Instance.PixelToMmRatio;

        public LineFeature() { }
        public LineFeature(string id, string module, PointF p1, PointF p2)
        {
            this.FeatureId = id;
            this.CreatedByModule = module;
            this.StartPoint = p1;
            this.EndPoint = p2;
        }
    }


    // 3. TÉGLALAP FEATURE (pl. Detektált ROI-k, Blob határok)
    [Serializable]
    public class RectangleFeature : BaseFeature
    {
        public override FeatureType Type => FeatureType.Rectangle;
        public Rectangle Rect { get; set; }
        public double RotationAngle { get; set; } // Ha a sablon el van fordulva

        public RectangleFeature() { }
        public RectangleFeature(string id, string module, Rectangle r, double angle = 0)
        {
            this.FeatureId = id;
            this.CreatedByModule = module;
            this.Rect = r;
            this.RotationAngle = angle;
        }
    }
}
