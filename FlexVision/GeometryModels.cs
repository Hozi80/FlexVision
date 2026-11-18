using System;
using System.Drawing;
using System.Xml.Serialization;

namespace FlexVision
{
    public enum GeometryOperationType
    {
        DistancePointToPoint,
        DeltaXPointToPoint,
        DeltaYPointToPoint,
        DistancePointToLine,
        DistanceLineToLine,
        DeltaXLineToLine,
        DeltaYLineToLine,
        CenterlineLineToLine,
        IntersectionLineToLine,
        AngleLineToLine
    }

    [Serializable]
    public class GeometryMeasurementItem
    {
        public string Name { get; set; } = "Measure 1";
        public GeometryOperationType Operation { get; set; }
        public string Feature1Key { get; set; }
        public string Feature2Key { get; set; }

        public double NominalValue { get; set; } = 0.0;
        public double TolerancePlusMinus { get; set; } = 10.0;
        public bool CheckTolerance { get; set; } = false;

        // ÚJ: Manuális vagy automatikus eltolás a méretvonal közepéhez képest (receptben menthető!)
        public PointF LabelOffset { get; set; } = new PointF(0, 0);
    }

    public class GeometryEvaluatedResult
    {
        public GeometryMeasurementItem SourceRule { get; set; }
        public string DisplayName { get; set; }
        public GeometryOperationType Operation { get; set; }
        public double MeasuredValue { get; set; }
        public PointF CalculatedPoint { get; set; }
        public PointF DrawPoint1 { get; set; }
        public PointF DrawPoint2 { get; set; }
        public bool IsPass { get; set; }

        // ÚJ: A szöveg aktuális téglalapja a képen (egérrel való megfogáshoz)
        [XmlIgnore]
        public Rectangle LabelBounds { get; set; } = Rectangle.Empty;
    }

    public interface IInteractiveGeometryTool
    {
        void HandleMouseMove(Point imagePt);
        void HandleMouseClick(Point imagePt, Point screenPt);
        // ÚJ: Egér lenyomás és felengedés a feliratok húzásához
        void HandleMouseDown(Point imagePt);
        void HandleMouseUp(Point imagePt);
    }
}
