using System.Drawing;
using System.Drawing.Drawing2D;
using static System.Math;
using static Slider.Methods.Math;

namespace Slider.Methods
{
  public class Drawing
  {
    #region Images
    public static Bitmap Transform(Image image, Rectangle srcRect, Rectangle destRect)
    {
      var bitmap = new Bitmap(destRect.Width, destRect.Height);
      using (var g = Graphics.FromImage(bitmap))
      {
        g.DrawImage(image, destRect, srcRect, GraphicsUnit.Pixel);
      }
      return bitmap;
    }

    public static Bitmap Resize(Image image, int width, int height)
    {
      var srcRect = new Rectangle(0, 0, image.Width, image.Height);
      var destRect = new Rectangle(0, 0, width, height);
      var destImage = new Bitmap(width, height);
      
      destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
      using (var g = Graphics.FromImage(destImage))
        g.DrawImage(image, destRect, srcRect, GraphicsUnit.Pixel);

      return destImage;
    }

    public static Bitmap Crop(Image image, Rectangle rect)
    {
      var bitmap = new Bitmap(rect.Width, rect.Height);
      using (Graphics g = Graphics.FromImage(bitmap))
        g.DrawImage(image, -rect.X, -rect.Y);
      return bitmap;
    }
    #endregion

    #region Colors
    public static Color Interpolate(Color StartColor, Color EndColor, float Ratio)
    {
      int A = (int)Math.Interpolate(StartColor.A, EndColor.A, Ratio);
      int R = (int)Math.Interpolate(StartColor.R, EndColor.R, Ratio);
      int G = (int)Math.Interpolate(StartColor.G, EndColor.G, Ratio);
      int B = (int)Math.Interpolate(StartColor.B, EndColor.B, Ratio);
      return Color.FromArgb(A, R, G, B);
    }

    public static Color WriteOut(Color color, int value)
    {
      int R = Clamp(color.R + value, 255, 0);
      int G = Clamp(color.G + value, 255, 0);
      int B = Clamp(color.B + value, 255, 0);
      return Color.FromArgb(color.A, R, G, B);
    }
    #endregion

    #region Shapes

    public static void DrawRoundRectangle(Graphics g, Pen pen, Rectangle rect, int cornerRadius)
    {
      using (GraphicsPath path = CreateRoundedRectanglePath(rect, cornerRadius))
      {
        g.DrawPath(pen, path);
      }
    }

    public static void FillRoundRectangle(Graphics g, Brush brush, Rectangle rect, int cornerRadius)
    {
      using (GraphicsPath path = CreateRoundedRectanglePath(rect, cornerRadius))
      {
        g.FillPath(brush, path);
      }
    }

    public static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int cornerRadius)
    {
      GraphicsPath roundedRect = new GraphicsPath();
      roundedRect.AddArc(rect.X, rect.Y, cornerRadius * 2, cornerRadius * 2, 180, 90);
      roundedRect.AddLine(rect.X + cornerRadius, rect.Y, rect.Right - cornerRadius * 2, rect.Y);
      roundedRect.AddArc(rect.X + rect.Width - cornerRadius * 2, rect.Y, cornerRadius * 2, cornerRadius * 2, 270, 90);
      roundedRect.AddLine(rect.Right, rect.Y + cornerRadius * 2, rect.Right, rect.Y + rect.Height - cornerRadius * 2);
      roundedRect.AddArc(rect.X + rect.Width - cornerRadius * 2, rect.Y + rect.Height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 0, 90);
      roundedRect.AddLine(rect.Right - cornerRadius * 2, rect.Bottom, rect.X + cornerRadius * 2, rect.Bottom);
      roundedRect.AddArc(rect.X, rect.Bottom - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 90, 90);
      roundedRect.AddLine(rect.X, rect.Bottom - cornerRadius * 2, rect.X, rect.Y + cornerRadius * 2);
      roundedRect.CloseFigure();
      return roundedRect;
    }

    public static Point[] ResizePolygon(Point[] inputPoints, float ratio)
    {
      PointF centroid = GetCentroid(inputPoints);
      Point[] OutputPoints = new Point[inputPoints.Length];
      PointF vector;
      double normal;
      for (int i = 0; i < inputPoints.Length; i++)
      {
        vector = new PointF(inputPoints[i].X - centroid.X,
                            inputPoints[i].Y - centroid.Y);
        normal = GetNormal(vector);
        vector.X = (float)(vector.X / normal);
        vector.Y = (float)(vector.Y / normal);

        OutputPoints[i].X = (int)(centroid.X + vector.X * ratio);
        OutputPoints[i].Y = (int)(centroid.Y + vector.Y * ratio);
      }
      return OutputPoints;
    }

    public static PointF[] ResizePolygon(PointF[] inputPoints, float width)
    {
      PointF centroid = GetCentroid(inputPoints);
      PointF[] OutputPoints = new PointF[inputPoints.Length];
      PointF vector;
      double normal;
      for (int i = 0; i < inputPoints.Length; i++)
      {
        vector = new PointF(inputPoints[i].X - centroid.X,
                            inputPoints[i].Y - centroid.Y);
        normal = GetNormal(vector);
        vector.X = (float)(vector.X / normal);
        vector.Y = (float)(vector.Y / normal);

        OutputPoints[i].X = inputPoints[i].X + vector.X * width;
        OutputPoints[i].Y = inputPoints[i].Y + vector.Y * width;
      }
      return OutputPoints;
    }
    #endregion

    #region Geometry
    public static Rectangle ShiftRect(Rectangle rect, Point vector)
    {
      return new Rectangle(rect.X + vector.X, rect.Y + vector.Y, rect.Width, rect.Height);
    }

    public static PointF[] ShiftPolygon(PointF[] points, Point vector)
    {
      for (int i = 0; i < points.Length; i++)
      {
        points[i].X += vector.X;
        points[i].Y += vector.Y;
      }
      return points;
    }

    public static double GetNormal(PointF vector)
    {
      return Sqrt(Pow(vector.X, 2) + Pow(vector.Y, 2));
    }

    public static PointF GetCentroid(Point[] points)
    {
      return GetCentroid(Point2PointF(points));
    }

    public static PointF GetCentroid(PointF[] points)
    {
      PointF centroid = new PointF(0, 0);
      foreach (PointF point in points)
      {
        centroid.X += point.X;
        centroid.Y += point.Y;
      }
      centroid.X /= points.Length;
      centroid.Y /= points.Length;
      return centroid;
    }

    public static PointF Point2PointF(Point point)
    {
      return new PointF(point.X, point.Y);
    }

    public static PointF[] Point2PointF(Point[] points)
    {
      PointF[] output = new PointF[points.Length];
      for (int i = 0; i < points.Length; i++)
      {
        output[i].X = points[i].X;
        output[i].Y = points[i].Y;
      }
      return output;
    }
    #endregion

    #region Shadows
    public static void DrawRoundShadow(Graphics g, Rectangle rect, int width)
    {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      Color color = Color.FromArgb(0, 0, 0, 0);
      int penWidth = 3;
      Point shift = new Point(width / 2, width / 2);
      rect = ShiftRect(rect, shift);
      using (Pen pen = new Pen(color, penWidth))
      {
        for (int i = -width; i < width; i++)
        {
          pen.Color = Color.FromArgb((byte)((50 / width) * (width - i)), color);
          g.DrawEllipse(pen, new Rectangle(rect.X - i, rect.Y - i, rect.Width + 2 * i, rect.Height + 2 * i));
        }
      }
    }

    public static void DrawPolygonShadow(Graphics g, Point[] points, int width)
    {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      Color color = Color.FromArgb(0, 0, 0, 0);
      int penWidth = 3;
      PointF[] pointFs = Point2PointF(points);
      Point shift = new Point(width / 3, width / 3);
      pointFs = ShiftPolygon(pointFs, shift);
      using (Pen pen = new Pen(color, penWidth))
      {
        for (int i = -width; i < width; i++)
        {
          pen.Color = Color.FromArgb((byte)((50 / width) * (width - i)), color);
          g.DrawPolygon(pen, ResizePolygon(pointFs, i));
        }
      }
    }
    #endregion
  }
}
