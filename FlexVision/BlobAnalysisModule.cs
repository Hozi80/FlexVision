using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace FlexVision
{
    public class BlobResult
    {
        public int Index { get; set; }
        public double Area { get; set; }
        public double Circularity { get; set; }
        public double AspectRatio { get; set; }
        public double Perimeter { get; set; }
        public PointF Center { get; set; }
        public Rectangle BoundingBox { get; set; }
    }

    [Serializable]
    public class BlobAnalysisModule : BaseInspectionModule
    {
        [Browsable(false)]
        public override string PureName => "Blob Analysis";

        [Browsable(false)]
        public override VisionModuleType ModuleType => VisionModuleType.BlobDetect;

        // ====================================================================
        // --- RECEPTBEN MENTHETŐ IPARI PARAMÉTEREK ---
        // ====================================================================
        [Category("1. Keresési Terület (ROI)")]
        public bool UseROI { get; set; } = false;

        [Category("1. Keresési Terület (ROI)")]
        public Rectangle ROI { get; set; } = Rectangle.Empty;

        [Category("2. Folt Szűrési Feltételek")]
        public bool SearchWhiteBlobs { get; set; } = true;

        [Category("2. Folt Szűrési Feltételek")]
        public bool SearchBlackBlobs { get; set; } = true;

        // Area (Mindig aktív alapszűrő)
        [Category("2. Folt Szűrési Feltételek")]
        public double MinArea { get; set; } = 100.0;

        [Category("2. Folt Szűrési Feltételek")]
        public double MaxArea { get; set; } = 500000.0;

        // Circularity (Kapcsolható)
        [Category("2. Folt Szűrési Feltételek")]
        public bool UseCircularityFilter { get; set; } = false;

        [Category("2. Folt Szűrési Feltételek")]
        public double MinCircularity { get; set; } = 0.0;

        [Category("2. Folt Szűrési Feltételek")]
        public double MaxCircularity { get; set; } = 1.0;

        // Aspect Ratio (Kapcsolható: Szélesség / Magasság)
        [Category("2. Folt Szűrési Feltételek")]
        public bool UseAspectRatioFilter { get; set; } = false;

        [Category("2. Folt Szűrési Feltételek")]
        public double MinAspectRatio { get; set; } = 0.0;

        [Category("2. Folt Szűrési Feltételek")]
        public double MaxAspectRatio { get; set; } = 100.0;

        // Perimeter (Kapcsolható: Kerület px)
        [Category("2. Folt Szűrési Feltételek")]
        public bool UsePerimeterFilter { get; set; } = false;

        [Category("2. Folt Szűrési Feltételek")]
        public double MinPerimeter { get; set; } = 0.0;

        [Category("2. Folt Szűrési Feltételek")]
        public double MaxPerimeter { get; set; } = 100000.0;

        [Category("3. Minőségellenőrzés és Hurok")]
        public int ExpectedBlobCount { get; set; } = 1;

        [Category("3. Minőségellenőrzés és Hurok")]
        public bool CreateSubContexts { get; set; } = false;

        // --- KIMENETI DIAGNOSZTIKAI RIAPORTOK ---
        [Category("4. Mérési Eredmények"), ReadOnly(true)]
        public string ExecutionTime { get; set; } = "0 ms";

        [Category("4. Mérési Eredmények"), ReadOnly(true)]
        public int FoundBlobCount { get; set; } = 0;

        [Category("4. Mérési Eredmények"), ReadOnly(true)]
        public string TestResult { get; set; } = "WAITING...";

        // --- FUTÁSIDEJŰ EREDMÉNYTÁROLÓK ---
        [XmlIgnore, Browsable(false)]
        public List<BlobResult> LastBlobs { get; set; } = new List<BlobResult>();

        [XmlIgnore, Browsable(false)]
        public string LastExecutionTime { get; set; } = "0 ms";

        [XmlIgnore, Browsable(false)]
        public string LastTestResult { get; set; } = "WAITING...";

        public BlobAnalysisModule() { }

        public override void Process(InspectionContext context)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            this.Status = ModuleStatus.NotExecuted;

            Image<Gray, byte> inputMask = context.GetInputGrayImage(this.InputStepLink);
            if (inputMask == null)
            {
                Image<Bgr, byte> colorFallback = context.GetInputColorImage(this.InputStepLink);
                if (colorFallback != null)
                {
                    inputMask = colorFallback.Convert<Gray, byte>();
                    colorFallback.Dispose();
                }
            }

            if (inputMask == null)
            {
                stopwatch.Stop();
                this.Status = ModuleStatus.Failed;
                return;
            }

            Image<Bgr, byte> baseColor = context.GetInputColorImage(this.InputStepLink);
            Image<Bgr, byte> overlayOutput = baseColor != null ? baseColor.Copy() : inputMask.Convert<Bgr, byte>();
            baseColor?.Dispose();

            List<BlobResult> validBlobs = new List<BlobResult>();

            try
            {
                Rectangle activeArea = new Rectangle(0, 0, inputMask.Width, inputMask.Height);
                bool isRoiActive = this.UseROI && this.ROI != Rectangle.Empty && this.ROI.Width > 5 && this.ROI.Height > 5;

                if (isRoiActive)
                {
                    activeArea = Rectangle.Intersect(activeArea, this.ROI);
                }

                inputMask.ROI = activeArea;

                using (Image<Gray, byte> workingCopy = inputMask.Copy())
                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                using (Mat hierarchy = new Mat())
                {
                    inputMask.ROI = Rectangle.Empty;
                    CvInvoke.FindContours(workingCopy, contours, hierarchy, RetrType.List, ChainApproxMethod.ChainApproxSimple);

                    int blobIdx = 1;
                    for (int i = 0; i < contours.Size; i++)
                    {
                        using (VectorOfPoint contour = contours[i])
                        {
                            // 1. TERÜLET SZŰRÉS (Mindig aktív)
                            double area = CvInvoke.ContourArea(contour);
                            if (area < this.MinArea || area > this.MaxArea) continue;

                            // 2. OLDALARÁNY (ASPECT RATIO) SZŰRÉS (Opcionális)
                            Rectangle localRect = CvInvoke.BoundingRectangle(contour);
                            double aspectRatio = localRect.Height > 0 ? (double)localRect.Width / localRect.Height : 0.0;
                            if (this.UseAspectRatioFilter)
                            {
                                if (aspectRatio < this.MinAspectRatio || aspectRatio > this.MaxAspectRatio) continue;
                            }

                            // 3. KERÜLET ÉS KÖRSZERŰSÉG SZŰRÉS (Csak akkor számoljuk az ArcLength-t, ha legalább az egyik aktív!)
                            double perimeter = 0.0;
                            double circularity = 0.0;
                            if (this.UsePerimeterFilter || this.UseCircularityFilter)
                            {
                                perimeter = CvInvoke.ArcLength(contour, true);
                                if (this.UsePerimeterFilter)
                                {
                                    if (perimeter < this.MinPerimeter || perimeter > this.MaxPerimeter) continue;
                                }

                                if (this.UseCircularityFilter)
                                {
                                    circularity = (perimeter > 0) ? Math.Min(1.0, (4.0 * Math.PI * area) / (perimeter * perimeter)) : 0.0;
                                    if (circularity < this.MinCircularity || circularity > this.MaxCircularity) continue;
                                }
                            }

                            // 4. SZÍN (POLARITÁS) SZŰRÉS (Csak ha nem mindkettő engedélyezett)
                            if (!this.SearchWhiteBlobs || !this.SearchBlackBlobs)
                            {
                                using (Image<Gray, byte> tempContourMask = new Image<Gray, byte>(workingCopy.Size))
                                {
                                    tempContourMask.SetZero();
                                    CvInvoke.DrawContours(tempContourMask, contours, i, new MCvScalar(255), -1);

                                    MCvScalar meanIntensity = CvInvoke.Mean(workingCopy, tempContourMask);
                                    bool isWhite = meanIntensity.V0 >= 128;
                                    bool isBlack = meanIntensity.V0 < 128;

                                    if (isWhite && !this.SearchWhiteBlobs) continue;
                                    if (isBlack && !this.SearchBlackBlobs) continue;
                                }
                            }

                            // 5. SÚLYPONT ÉS POZÍCIÓ RÖGZÍTÉSE
                            Moments moments = CvInvoke.Moments(contour);
                            float cx = moments.M00 != 0 ? (float)(moments.M10 / moments.M00) : 0f;
                            float cy = moments.M00 != 0 ? (float)(moments.M01 / moments.M00) : 0f;

                            PointF globalCenter = new PointF(cx + activeArea.X, cy + activeArea.Y);
                            Rectangle globalRect = new Rectangle(
                                localRect.X + activeArea.X,
                                localRect.Y + activeArea.Y,
                                localRect.Width,
                                localRect.Height
                            );

                            // Ha a diagnosztikai listába a kerület/körszerűség még nem volt kiszámolva, a megtartott kevés találatra kiszámoljuk
                            if (perimeter == 0.0)
                            {
                                perimeter = CvInvoke.ArcLength(contour, true);
                                circularity = (perimeter > 0) ? Math.Min(1.0, (4.0 * Math.PI * area) / (perimeter * perimeter)) : 0.0;
                            }
                            validBlobs.Add(new BlobResult
                            {
                                Index = blobIdx,
                                Area = area,
                                Circularity = circularity,
                                AspectRatio = aspectRatio,
                                Perimeter = perimeter,
                                Center = globalCenter,
                                BoundingBox = globalRect
                            });
                            // Overlay rajzolás
                            Point[] shiftedPoints = new Point[contour.Size];
                            for (int p = 0; p < contour.Size; p++)
                            {
                                shiftedPoints[p] = new Point(contour[p].X + activeArea.X, contour[p].Y + activeArea.Y);
                            }
                            using (VectorOfPoint shiftedContour = new VectorOfPoint(shiftedPoints))
                            {
                                CvInvoke.Polylines(overlayOutput, shiftedContour, true, new MCvScalar(0, 255, 0), 2);
                            }
                            CvInvoke.Rectangle(overlayOutput, globalRect, new MCvScalar(255, 200, 0), 1);
                            Point centerPt = Point.Round(globalCenter);
                            CvInvoke.Line(overlayOutput, new Point(centerPt.X - 6, centerPt.Y), new Point(centerPt.X + 6, centerPt.Y), new MCvScalar(0, 0, 255), 2);
                            CvInvoke.Line(overlayOutput, new Point(centerPt.X, centerPt.Y - 6), new Point(centerPt.X, centerPt.Y + 6), new MCvScalar(0, 0, 255), 2);
                            // Feature Store mentés (Súlypont + 4 oldalvonal)
                            string featKey = $"{this.EffectiveFeatureName}Blob{blobIdx}";
                            context.Features[featKey] = new PointFeature(featKey, this.Name, globalCenter);
                            PointF tl = new PointF(globalRect.Left, globalRect.Top);
                            PointF tr = new PointF(globalRect.Right, globalRect.Top);
                            PointF br = new PointF(globalRect.Right, globalRect.Bottom);
                            PointF bl = new PointF(globalRect.Left, globalRect.Bottom);
                            context.Features[$"{featKey}_Top"] = new LineFeature($"{featKey}_Top", this.Name, tl, tr);
                            context.Features[$"{featKey}_Right"] = new LineFeature($"{featKey}_Right", this.Name, tr, br);
                            context.Features[$"{featKey}_Bottom"] = new LineFeature($"{featKey}_Bottom", this.Name, br, bl);
                            context.Features[$"{featKey}_Left"] = new LineFeature($"{featKey}_Left", this.Name, bl, tl);
                            blobIdx++;
                        }
                    }
                }
                // Sub-Pipeline generálás
                if (this.CreateSubContexts && validBlobs.Count > 0)
                {
                    if (context.SubContexts == null) context.SubContexts = new List<InspectionContext>();
                    context.SubContexts.Clear();
                    using (Bitmap globalBmp = overlayOutput.ToBitmap())
                    {
                        foreach (var blob in validBlobs)
                        {
                            InspectionContext subCtx = new InspectionContext();
                            Bitmap subBmp = CropImage(globalBmp, blob.BoundingBox);
                            if (subBmp != null)
                            {
                                subCtx.SourceImage = new Image<Bgr, byte>(subBmp);
                                subCtx.WorkingMask = subCtx.SourceImage.Convert<Gray, byte>();
                                subBmp.Dispose();
                            }
                            subCtx.LocalOffset = blob.Center;
                            subCtx.ColorHistory[this.Name] = subCtx.SourceImage.Copy();
                            subCtx.GrayHistory[this.Name] = subCtx.WorkingMask.Copy();
                            context.SubContexts.Add(subCtx);
                        }
                    }
                }
                stopwatch.Stop();
                this.LastBlobs = validBlobs;
                this.FoundBlobCount = validBlobs.Count;
                this.LastExecutionTime = $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms";
                this.ExecutionTime = this.LastExecutionTime;
                bool isPass = (this.FoundBlobCount == this.ExpectedBlobCount);
                this.LastTestResult = isPass ? "✅ PASS" : "❌ FAIL";
                this.TestResult = this.LastTestResult;
                this.Status = isPass ? ModuleStatus.Success : ModuleStatus.Failed;
                context.CustomData[this.Name + "_Blobs"] = new List<BlobResult>(validBlobs);
                context.CustomData[this.Name + "_Time"] = this.LastExecutionTime;
                context.CustomData[this.Name + "_Status"] = this.LastTestResult;
                context.SourceImage?.Dispose();
                context.SourceImage = overlayOutput;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                overlayOutput?.Dispose();
                this.LastTestResult = "💥 ERROR";
                this.TestResult = this.LastTestResult;
                this.Status = ModuleStatus.Failed;
                Console.WriteLine($"[BlobAnalysis] Hiba: {ex.Message}");
            }
            finally
            {
                inputMask?.Dispose();
                this.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
            }
        }
        private Bitmap CropImage(Bitmap s, Rectangle sec)
        {
            try
            {
                Bitmap b = new Bitmap(sec.Width, sec.Height);
                using (Graphics g = Graphics.FromImage(b))
                {
                    lock (s) g.DrawImage(s, 0, 0, sec, GraphicsUnit.Pixel);
                }
                return b;
            }
            catch { return null; }
        }
    }
}