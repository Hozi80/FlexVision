using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace FlexVision
{
    [Serializable]
    public class GeometryMeasureModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Geometry & Overlay";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.EdgeDetect;

        public List<string> VisibleFeatureKeys { get; set; } = new List<string>();
        public List<GeometryMeasurementItem> Measurements { get; set; } = new List<GeometryMeasurementItem>();
        public bool UseMillimeters { get; set; } = false;
        public bool ExpandSubLoops { get; set; } = false; // Alapból csoportosítva (false) működik!

        // Futásidejű tárolók a UI és az egér-interakció számára
        [XmlIgnore, Browsable(false)]
        public Dictionary<string, List<BaseFeature>> GroupedFeatures { get; set; } = new Dictionary<string, List<BaseFeature>>();

        [XmlIgnore, Browsable(false)]
        public List<GeometryEvaluatedResult> EvaluatedResults { get; set; } = new List<GeometryEvaluatedResult>();

        [XmlIgnore, Browsable(false)] public string HoveredFeatureKey { get; set; } = null;
        [XmlIgnore, Browsable(false)] public string SelectedFeature1Key { get; set; } = null;

        public GeometryMeasureModule() { }

        public override void Process(InspectionContext context)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            this.Status = ModuleStatus.NotExecuted;

            // 1. Mindig a tiszta alapképre rajzolunk
            Image<Bgr, byte> baseImage = null;
            if (context.ColorHistory.Count > 0)
            {
                var firstKey = context.ColorHistory.Keys.First();
                baseImage = context.ColorHistory[firstKey].Copy();
            }
            else if (context.SourceImage != null)
            {
                baseImage = context.SourceImage.Copy();
            }

            if (baseImage == null)
            {
                this.Status = ModuleStatus.Failed;
                return;
            }

            // 2. Feature-ök összegyűjtése és transzformálása globális koordinátákra
            var grouped = new Dictionary<string, List<BaseFeature>>();
            var subDictionaries = new List<Dictionary<string, BaseFeature>>();
            var globalDict = new Dictionary<string, BaseFeature>();

            // Főág elemei
            foreach (var kvp in context.Features)
            {
                globalDict[kvp.Key] = kvp.Value;
                grouped[kvp.Key] = new List<BaseFeature> { kvp.Value };
            }

            // Al-pipeline (SubContexts) elemei
            if (context.SubContexts != null)
            {
                for (int s = 0; s < context.SubContexts.Count; s++)
                {
                    var sub = context.SubContexts[s];
                    var subMap = new Dictionary<string, BaseFeature>();

                    float subW = (sub.SourceImage != null) ? sub.SourceImage.Width : 0f;
                    float subH = (sub.SourceImage != null) ? sub.SourceImage.Height : 0f;
                    float shiftX = sub.LocalOffset.X - (subW / 2.0f);
                    float shiftY = sub.LocalOffset.Y - (subH / 2.0f);

                    foreach (var kvp in sub.Features)
                    {
                        BaseFeature transformed = null;
                        if (kvp.Value is PointFeature pf)
                            transformed = new PointFeature(kvp.Key, pf.CreatedByModule, new PointF(pf.Position.X + shiftX, pf.Position.Y + shiftY));
                        else if (kvp.Value is LineFeature lf)
                            transformed = new LineFeature(kvp.Key, lf.CreatedByModule,
                                new PointF(lf.StartPoint.X + shiftX, lf.StartPoint.Y + shiftY),
                                new PointF(lf.EndPoint.X + shiftX, lf.EndPoint.Y + shiftY));

                        if (transformed == null) continue;

                        if (this.ExpandSubLoops)
                        {
                            // Kiterjesztett mód: minden darab külön néven (Sub1_..., Sub2_...)
                            string expKey = $"Sub{s + 1}_{kvp.Key}";
                            transformed.FeatureId = expKey;
                            grouped[expKey] = new List<BaseFeature> { transformed };
                            subMap[expKey] = transformed;
                        }
                        else
                        {
                            // Csoportosított mód: közös kulcs alatt ("[Sub] ...") gyűjtjük mind a 6 példányt!
                            string grpKey = $"[Sub] {kvp.Key}";
                            transformed.FeatureId = grpKey;
                            if (!grouped.ContainsKey(grpKey)) grouped[grpKey] = new List<BaseFeature>();
                            grouped[grpKey].Add(transformed);
                            subMap[grpKey] = transformed;
                        }
                    }
                    subDictionaries.Add(subMap);
                }
            }

            this.GroupedFeatures = grouped;
            this.EvaluatedResults.Clear();



            // 3. Mérések kiértékelése (Csoportosítva vagy egyenként)
            List<Rectangle> placedLabelRects = new List<Rectangle>();


            bool allPass = true;
            double scale = this.UseMillimeters ? SystemConfiguration.Instance.PixelToMmRatio : 1.0;
            string unit = this.UseMillimeters ? "mm" : "px";

            foreach (var m in this.Measurements)
            {
                bool isSubMeasure = !this.ExpandSubLoops && (m.Feature1Key.StartsWith("[Sub] ") || m.Feature2Key.StartsWith("[Sub] "));

                if (isSubMeasure && subDictionaries.Count > 0)
                {
                    // Automatikusan végrehajtjuk az összes sub-loop darabon!
                    for (int s = 0; s < subDictionaries.Count; s++)
                    {
                        var sDict = subDictionaries[s];
                        BaseFeature f1 = sDict.ContainsKey(m.Feature1Key) ? sDict[m.Feature1Key] : (globalDict.ContainsKey(m.Feature1Key) ? globalDict[m.Feature1Key] : null);
                        BaseFeature f2 = sDict.ContainsKey(m.Feature2Key) ? sDict[m.Feature2Key] : (globalDict.ContainsKey(m.Feature2Key) ? globalDict[m.Feature2Key] : null);

                        if (f1 == null || f2 == null)
                        {
                            allPass = false;
                            continue;
                        }

                        var eval = EvaluateSingle(m, $"{m.Name} [#{s + 1}]", f1, f2, sDict, grouped, scale);
                        this.EvaluatedResults.Add(eval);
                        if (m.CheckTolerance && !eval.IsPass) allPass = false;
                      //  DrawMeasurement(baseImage, eval, f1, f2, unit);
                        DrawMeasurement(baseImage, eval, f1, f2, unit, placedLabelRects);
                    }
                }
                else
                {
                    // Egyedi mérés (főágon vagy kiterjesztett módban)
                    BaseFeature f1 = grouped.ContainsKey(m.Feature1Key) && grouped[m.Feature1Key].Count > 0 ? grouped[m.Feature1Key].First() : null;
                    BaseFeature f2 = grouped.ContainsKey(m.Feature2Key) && grouped[m.Feature2Key].Count > 0 ? grouped[m.Feature2Key].First() : null;

                    if (f1 == null || f2 == null)
                    {
                        allPass = false;
                        continue;
                    }

                    var eval = EvaluateSingle(m, m.Name, f1, f2, globalDict, grouped, scale);
                    this.EvaluatedResults.Add(eval);
                    if (m.CheckTolerance && !eval.IsPass) allPass = false;
                    //    DrawMeasurement(baseImage, eval, f1, f2, unit);
                    DrawMeasurement(baseImage, eval, f1, f2, unit, placedLabelRects);
                }
            }

            // 4. Bekapcsolt, Hover és Kijelölt elemek kirajzolása (csoportosítva egyszerre mind a 6-on!)
            foreach (var kvp in grouped)
            {
                bool isVisible = this.VisibleFeatureKeys.Contains(kvp.Key);
                bool isHovered = (kvp.Key == this.HoveredFeatureKey);
                bool isSelected1 = (kvp.Key == this.SelectedFeature1Key);

                if (!isVisible && !isHovered && !isSelected1) continue;

                MCvScalar color = new MCvScalar(255, 200, 0); // Alap: Ciánkék
                int thickness = 2;

                if (isSelected1) { color = new MCvScalar(255, 0, 255); thickness = 3; } // Magenta
                else if (isHovered) { color = new MCvScalar(0, 255, 255); thickness = 3; } // Sárga

                foreach (var feat in kvp.Value)
                {
                    if (feat is PointFeature ptFeat)
                    {
                        CvInvoke.Circle(baseImage, Point.Round(ptFeat.Position), 5, color, thickness);
                    }
                    else if (feat is LineFeature lineFeat)
                    {
                        CvInvoke.Line(baseImage, Point.Round(lineFeat.StartPoint), Point.Round(lineFeat.EndPoint), color, thickness);
                    }
                }
            }

            context.SourceImage?.Dispose();
            context.SourceImage = baseImage;

            sw.Stop();
            this.ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
            this.Status = allPass ? ModuleStatus.Success : ModuleStatus.Failed;
            this.Status = ModuleStatus.Success;
        }

        private GeometryEvaluatedResult EvaluateSingle(GeometryMeasurementItem m, string dispName, BaseFeature f1, BaseFeature f2,
            Dictionary<string, BaseFeature> localScope, Dictionary<string, List<BaseFeature>> globalGrouped, double scale)
        {
            var res = new GeometryEvaluatedResult
            {
                SourceRule = m,
                DisplayName = dispName,
                Operation = m.Operation
            };

            if (m.Operation == GeometryOperationType.DistancePointToPoint && f1 is PointFeature p1 && f2 is PointFeature p2)
            {
                double distPx = Math.Sqrt(Math.Pow(p2.Position.X - p1.Position.X, 2) + Math.Pow(p2.Position.Y - p1.Position.Y, 2));
                res.MeasuredValue = distPx * scale;
                res.DrawPoint1 = p1.Position;
                res.DrawPoint2 = p2.Position;
                res.CalculatedPoint = new PointF((p1.Position.X + p2.Position.X) / 2f, (p1.Position.Y + p2.Position.Y) / 2f);
            }
            else if (m.Operation == GeometryOperationType.DeltaXPointToPoint && f1 is PointFeature px1 && f2 is PointFeature px2)
            {
                res.MeasuredValue = Math.Abs(px2.Position.X - px1.Position.X) * scale;
                res.DrawPoint1 = px1.Position;
                res.DrawPoint2 = new PointF(px2.Position.X, px1.Position.Y);
                res.CalculatedPoint = new PointF((res.DrawPoint1.X + res.DrawPoint2.X) / 2f, px1.Position.Y);
            }
            else if (m.Operation == GeometryOperationType.DeltaYPointToPoint && f1 is PointFeature py1 && f2 is PointFeature py2)
            {
                res.MeasuredValue = Math.Abs(py2.Position.Y - py1.Position.Y) * scale;
                res.DrawPoint1 = py1.Position;
                res.DrawPoint2 = new PointF(py1.Position.X, py2.Position.Y);
                res.CalculatedPoint = new PointF(py1.Position.X, (res.DrawPoint1.Y + res.DrawPoint2.Y) / 2f);
            }
            else if (m.Operation == GeometryOperationType.DistancePointToLine)
            {
                PointFeature pt = (f1 as PointFeature) ?? (f2 as PointFeature);
                LineFeature ln = (f1 as LineFeature) ?? (f2 as LineFeature);
                if (pt != null && ln != null)
                {
                    PointF proj = ProjectPointOnLine(pt.Position, ln.StartPoint, ln.EndPoint);
                    double distPx = Math.Sqrt(Math.Pow(pt.Position.X - proj.X, 2) + Math.Pow(pt.Position.Y - proj.Y, 2));
                    res.MeasuredValue = distPx * scale;
                    res.DrawPoint1 = pt.Position;
                    res.DrawPoint2 = proj;
                    res.CalculatedPoint = new PointF((pt.Position.X + proj.X) / 2f, (pt.Position.Y + proj.Y) / 2f);
                }
            }
            else if (m.Operation == GeometryOperationType.DistanceLineToLine && f1 is LineFeature dl1 && f2 is LineFeature dl2)
            {
                // Az 1. szakasz felezőpontját merőlegesen vetítjük a 2. egyenesre
                PointF mid1 = new PointF((dl1.StartPoint.X + dl1.EndPoint.X) / 2f, (dl1.StartPoint.Y + dl1.EndPoint.Y) / 2f);
                PointF projOn2 = ProjectPointOnLine(mid1, dl2.StartPoint, dl2.EndPoint);

                double distPx = Math.Sqrt(Math.Pow(mid1.X - projOn2.X, 2) + Math.Pow(mid1.Y - projOn2.Y, 2));
                res.MeasuredValue = distPx * scale;
                res.DrawPoint1 = mid1;
                res.DrawPoint2 = projOn2;
                res.CalculatedPoint = new PointF((mid1.X + projOn2.X) / 2f, (mid1.Y + projOn2.Y) / 2f);
            }
            else if (m.Operation == GeometryOperationType.DeltaXLineToLine && f1 is LineFeature dxl1 && f2 is LineFeature dxl2)
            {
                PointF mid1 = new PointF((dxl1.StartPoint.X + dxl1.EndPoint.X) / 2f, (dxl1.StartPoint.Y + dxl1.EndPoint.Y) / 2f);
                PointF mid2 = new PointF((dxl2.StartPoint.X + dxl2.EndPoint.X) / 2f, (dxl2.StartPoint.Y + dxl2.EndPoint.Y) / 2f);
                res.MeasuredValue = Math.Abs(mid2.X - mid1.X) * scale;
                res.DrawPoint1 = mid1;
                res.DrawPoint2 = new PointF(mid2.X, mid1.Y);
                res.CalculatedPoint = new PointF((mid1.X + mid2.X) / 2f, mid1.Y);
            }
            else if (m.Operation == GeometryOperationType.DeltaYLineToLine && f1 is LineFeature dyl1 && f2 is LineFeature dyl2)
            {
                PointF mid1 = new PointF((dyl1.StartPoint.X + dyl1.EndPoint.X) / 2f, (dyl1.StartPoint.Y + dyl1.EndPoint.Y) / 2f);
                PointF mid2 = new PointF((dyl2.StartPoint.X + dyl2.EndPoint.X) / 2f, (dyl2.StartPoint.Y + dyl2.EndPoint.Y) / 2f);
                res.MeasuredValue = Math.Abs(mid2.Y - mid1.Y) * scale;
                res.DrawPoint1 = mid1;
                res.DrawPoint2 = new PointF(mid1.X, mid2.Y);
                res.CalculatedPoint = new PointF(mid1.X, (mid1.Y + mid2.Y) / 2f);
            }
            else if (m.Operation == GeometryOperationType.CenterlineLineToLine && f1 is LineFeature cl1 && f2 is LineFeature cl2)
            {
                // Orientáció-helyes felezőszakasz létrehozása két vonal között
                double dSame = Math.Pow(cl1.StartPoint.X - cl2.StartPoint.X, 2) + Math.Pow(cl1.StartPoint.Y - cl2.StartPoint.Y, 2);
                double dFlip = Math.Pow(cl1.StartPoint.X - cl2.EndPoint.X, 2) + Math.Pow(cl1.StartPoint.Y - cl2.EndPoint.Y, 2);

                PointF cStart = dSame <= dFlip
                    ? new PointF((cl1.StartPoint.X + cl2.StartPoint.X) / 2f, (cl1.StartPoint.Y + cl2.StartPoint.Y) / 2f)
                    : new PointF((cl1.StartPoint.X + cl2.EndPoint.X) / 2f, (cl1.StartPoint.Y + cl2.EndPoint.Y) / 2f);

                PointF cEnd = dSame <= dFlip
                    ? new PointF((cl1.EndPoint.X + cl2.EndPoint.X) / 2f, (cl1.EndPoint.Y + cl2.EndPoint.Y) / 2f)
                    : new PointF((cl1.EndPoint.X + cl2.StartPoint.X) / 2f, (cl1.EndPoint.Y + cl2.StartPoint.Y) / 2f);

                res.DrawPoint1 = cStart;
                res.DrawPoint2 = cEnd;
                res.CalculatedPoint = new PointF((cStart.X + cEnd.X) / 2f, (cStart.Y + cEnd.Y) / 2f);
                res.MeasuredValue = 0;

                string centerLineKey = this.ExpandSubLoops ? $"{this.EffectiveFeatureName}_{m.Name}" : $"[Sub] {this.EffectiveFeatureName}_{m.Name}";
                var newLine = new LineFeature(centerLineKey, this.Name, cStart, cEnd);
                localScope[centerLineKey] = newLine;
                if (!globalGrouped.ContainsKey(centerLineKey)) globalGrouped[centerLineKey] = new List<BaseFeature>();
                globalGrouped[centerLineKey].Add(newLine);
            }
            else if (m.Operation == GeometryOperationType.IntersectionLineToLine && f1 is LineFeature l1 && f2 is LineFeature l2)
            {
                PointF inter = GetLineIntersection(l1.StartPoint, l1.EndPoint, l2.StartPoint, l2.EndPoint);
                res.CalculatedPoint = inter;
                res.MeasuredValue = 0;

                string interKey = this.ExpandSubLoops ? $"{this.EffectiveFeatureName}_{m.Name}" : $"[Sub] {this.EffectiveFeatureName}_{m.Name}";
                var newPt = new PointFeature(interKey, this.Name, inter);
                localScope[interKey] = newPt;
                if (!globalGrouped.ContainsKey(interKey)) globalGrouped[interKey] = new List<BaseFeature>();
                globalGrouped[interKey].Add(newPt);
            }
            else if (m.Operation == GeometryOperationType.AngleLineToLine && f1 is LineFeature al1 && f2 is LineFeature al2)
            {
                double ang1 = Math.Atan2(al1.EndPoint.Y - al1.StartPoint.Y, al1.EndPoint.X - al1.StartPoint.X);
                double ang2 = Math.Atan2(al2.EndPoint.Y - al2.StartPoint.Y, al2.EndPoint.X - al2.StartPoint.X);
                double diffDeg = Math.Abs((ang1 - ang2) * 180.0 / Math.PI);
                if (diffDeg > 180) diffDeg = 360 - diffDeg;
                res.MeasuredValue = diffDeg;
                res.CalculatedPoint = GetLineIntersection(al1.StartPoint, al1.EndPoint, al2.StartPoint, al2.EndPoint);
            }

            res.IsPass = !m.CheckTolerance || (Math.Abs(res.MeasuredValue - m.NominalValue) <= m.TolerancePlusMinus);
            return res;
        }

        private void DrawMeasurement(Image<Bgr, byte> img, GeometryEvaluatedResult eval, BaseFeature f1, BaseFeature f2, string unit, List<Rectangle> occupiedRects)
        {
            MCvScalar drawColor = eval.IsPass ? new MCvScalar(0, 255, 0) : new MCvScalar(0, 0, 255);

            if (eval.Operation == GeometryOperationType.CenterlineLineToLine)
            {
                CvInvoke.Line(img, Point.Round(eval.DrawPoint1), Point.Round(eval.DrawPoint2), new MCvScalar(255, 255, 0), 2);
                PlaceSmartLabel(img, eval, eval.DisplayName, eval.CalculatedPoint, new MCvScalar(255, 255, 0), occupiedRects);
                return;
            }

            if (eval.Operation == GeometryOperationType.IntersectionLineToLine)
            {
                Point pt = Point.Round(eval.CalculatedPoint);
                CvInvoke.Line(img, new Point(pt.X - 8, pt.Y), new Point(pt.X + 8, pt.Y), new MCvScalar(0, 255, 255), 2);
                CvInvoke.Line(img, new Point(pt.X, pt.Y - 8), new Point(pt.X, pt.Y + 8), new MCvScalar(0, 255, 255), 2);
                PlaceSmartLabel(img, eval, eval.DisplayName, eval.CalculatedPoint, new MCvScalar(0, 255, 255), occupiedRects);
                return;
            }

            if (eval.Operation == GeometryOperationType.AngleLineToLine)
            {
                Point pt = Point.Round(eval.CalculatedPoint);
                CvInvoke.Circle(img, pt, 6, drawColor, 2);
                PlaceSmartLabel(img, eval, $"{eval.MeasuredValue:F2} deg", eval.CalculatedPoint, drawColor, occupiedRects);
                return;
            }

            // Távolságmérések (Line-to-Line, Point-to-Line, Point-to-Point)
            if (eval.DrawPoint1 != PointF.Empty && eval.DrawPoint2 != PointF.Empty)
            {
                Point p1 = Point.Round(eval.DrawPoint1);
                Point p2 = Point.Round(eval.DrawPoint2);
                CvInvoke.Line(img, p1, p2, drawColor, 2);
                CvInvoke.Circle(img, p1, 3, drawColor, -1);
                CvInvoke.Circle(img, p2, 3, drawColor, -1);

                string text = $"{eval.MeasuredValue:F2} {unit}";
                PlaceSmartLabel(img, eval, text, eval.CalculatedPoint, drawColor, occupiedRects);
            }
        }

        private void PlaceSmartLabel(Image<Bgr, byte> img, GeometryEvaluatedResult eval, string text, PointF anchorPt, MCvScalar color, List<Rectangle> occupiedRects)
        {
            int baseline = 0;
            Size textSize = CvInvoke.GetTextSize(text, FontFace.HersheySimplex, 0.45, 1, ref baseline);
            int pad = 3;
            int boxW = textSize.Width + pad * 2;
            int boxH = textSize.Height + baseline + pad * 2;

            PointF offset = eval.SourceRule.LabelOffset;
            Rectangle finalBox = Rectangle.Empty;

            // 1. Ha a felhasználó manuálisan elhúzta a feliratot, pontosan oda tesszük!
            if (Math.Abs(offset.X) > 0.5f || Math.Abs(offset.Y) > 0.5f)
            {
                int x = (int)(anchorPt.X + offset.X - boxW / 2f);
                int y = (int)(anchorPt.Y + offset.Y - boxH / 2f);
                finalBox = new Rectangle(x, y, boxW, boxH);
            }
            else
            {
                // 2. Automatikus ütközéskerülő keresés 8 irányban, növekvő sugarakkal
                PointF[] dirs = {
            new PointF(0, -1),   // Fent
            new PointF(0, 1),    // Lent
            new PointF(1, 0),    // Jobbra
            new PointF(-1, 0),   // Balra
            new PointF(0.7f, -0.7f),  // Jobbra fent
            new PointF(-0.7f, -0.7f), // Balra fent
            new PointF(0.7f, 0.7f),   // Jobbra lent
            new PointF(-0.7f, 0.7f)   // Balra lent
        };

                bool placed = false;
                for (int radius = 18; radius <= 140; radius += 16)
                {
                    foreach (var d in dirs)
                    {
                        int candX = (int)(anchorPt.X + d.X * radius - boxW / 2f);
                        int candY = (int)(anchorPt.Y + d.Y * radius - boxH / 2f);

                        // Képszél védelem
                        candX = Math.Max(2, Math.Min(img.Width - boxW - 2, candX));
                        candY = Math.Max(2, Math.Min(img.Height - boxH - 2, candY));

                        Rectangle candRect = new Rectangle(candX, candY, boxW, boxH);

                        bool overlaps = false;
                        foreach (var occ in occupiedRects)
                        {
                            if (occ.IntersectsWith(candRect))
                            {
                                overlaps = true;
                                break;
                            }
                        }

                        if (!overlaps)
                        {
                            finalBox = candRect;
                            placed = true;
                            break;
                        }
                    }
                    if (placed) break;
                }

                if (!placed)
                {
                    finalBox = new Rectangle((int)anchorPt.X + 6, (int)anchorPt.Y - boxH - 6, boxW, boxH);
                }
            }

            occupiedRects.Add(finalBox);
            eval.LabelBounds = finalBox;

            // Mutatóvonal (Leader line) a mérési ponttól a szövegdoboz közepéig
            Point boxCenter = new Point(finalBox.X + finalBox.Width / 2, finalBox.Y + finalBox.Height / 2);
            CvInvoke.Line(img, Point.Round(anchorPt), boxCenter, color, 1);

            // Sötét háttérszalag a kiváló olvashatóságért
            CvInvoke.Rectangle(img, finalBox, new MCvScalar(20, 20, 20), -1);
            CvInvoke.Rectangle(img, finalBox, color, 1);

            // Szöveg kirajzolása
            Point textOrg = new Point(finalBox.X + pad, finalBox.Y + textSize.Height + pad);
            CvInvoke.PutText(img, text, textOrg, FontFace.HersheySimplex, 0.45, color, 1);
        }

        private PointF ProjectPointOnLine(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f) return a;
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            return new PointF(a.X + t * dx, a.Y + t * dy);
        }
        private PointF GetLineIntersection(PointF a1, PointF a2, PointF b1, PointF b2)
        {
            float denom = (a1.X - a2.X) * (b1.Y - b2.Y) - (a1.Y - a2.Y) * (b1.X - b2.X);
            if (Math.Abs(denom) < 1e-5f) return new PointF((a1.X + b1.X) / 2f, (a1.Y + b1.Y) / 2f);
            float t = ((a1.X - b1.X) * (b1.Y - b2.Y) - (a1.Y - b1.Y) * (b1.X - b2.X)) / denom;
            return new PointF(a1.X + t * (a2.X - a1.X), a1.Y + t * (a2.Y - a1.Y));
        }
    }
}

