using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using ZXing;
using ZXing.Common;

namespace FlexVision
{
    public enum CodeFormatSelection
    {
        AutoAll,
        DataMatrixOnly,
        QRCodeOnly,
        Barcodes1D
    }

    public enum CodeValidationMode
    {
        AnyNonEmpty,
        ContainsText,
        ExactMatchText,
        MatchFileSerial
    }

    [Serializable]
    public class CodeReaderModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "DataMatrix / QR Reader";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.CodeReader;

        [Category("1. Keresési Terület (ROI)")]
        public bool UseROI { get; set; } = false;

        [Category("1. Keresési Terület (ROI)")]
        public Rectangle ROI { get; set; } = Rectangle.Empty;

        [Category("2. Kódtípus és DPM (Lézergravír) Beállítások")]
        public CodeFormatSelection FormatSelection { get; set; } = CodeFormatSelection.DataMatrixOnly;

        [Category("2. Kódtípus és DPM (Lézergravír) Beállítások")]
        public bool EnableDpmEnhancement { get; set; } = true;

        [Category("2. Kódtípus és DPM (Lézergravír) Beállítások")]
        public int DpmBlurSize { get; set; } = 1;

        [Category("2. Kódtípus és DPM (Lézergravír) Beállítások")]
        public int DpmMorphSize { get; set; } = 1;

        [Category("2. Kódtípus és DPM (Lézergravír) Beállítások")]
        public bool TryInvertedAndMirrored { get; set; } = true;

        [Category("3. Tartalom Ellenőrzés (PASS/FAIL)")]
        public CodeValidationMode ValidationMode { get; set; } = CodeValidationMode.AnyNonEmpty;

        [Category("3. Tartalom Ellenőrzés (PASS/FAIL)")]
        public string ExpectedText { get; set; } = "";

        // --- FUTÁSIDEJŰ DIAGNOSZTIKAI EREDMÉNYEK ---
        [XmlIgnore, Browsable(false)] public string DecodedText { get; set; } = "";
        [XmlIgnore, Browsable(false)] public string DetectedFormat { get; set; } = "-";
        [XmlIgnore, Browsable(false)] public string UsedPreprocessStrategy { get; set; } = "-";
        [XmlIgnore, Browsable(false)] public string LastExecutionTime { get; set; } = "0 ms";
        [XmlIgnore, Browsable(false)] public string LastTestResult { get; set; } = "WAITING...";

        // ÚJ: A megtalált kód befoglaló téglalapja és a binarizált előnézeti kép
        [XmlIgnore, Browsable(false)] public Rectangle DetectedCodeBox { get; set; } = Rectangle.Empty;
        [XmlIgnore, Browsable(false)] public Bitmap LastDebugPreview { get; set; } = null;

        public CodeReaderModule() { }

        public override void Process(InspectionContext context)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            this.Status = ModuleStatus.NotExecuted;
            this.DetectedCodeBox = Rectangle.Empty;

            Image<Bgr, byte> inputColor = context.GetInputColorImage(this.InputStepLink);
            if (inputColor == null || inputColor.Ptr == IntPtr.Zero)
            {
                sw.Stop();
                this.Status = ModuleStatus.Failed;
                return;
            }

            Image<Bgr, byte> overlayOutput = inputColor.Copy();

            try
            {
                Rectangle searchBox = new Rectangle(0, 0, inputColor.Width, inputColor.Height);
                if (this.UseROI && this.ROI != Rectangle.Empty && this.ROI.Width > 10 && this.ROI.Height > 10)
                {
                    searchBox = Rectangle.Intersect(searchBox, this.ROI);
                }

                inputColor.ROI = searchBox;
                using (Image<Bgr, byte> roiColor = inputColor.Copy())
                using (Image<Gray, byte> roiGray = roiColor.Convert<Gray, byte>())
                {
                    inputColor.ROI = Rectangle.Empty;

                    // 1. Megkeressük a kód textúrájának helyét a ROI-n belül (DPM Lokalizátor fallbackhez)
                    Rectangle localCandidateBox = LocateDpmCandidateBox(roiGray);
                    if (localCandidateBox != Rectangle.Empty)
                    {
                        this.DetectedCodeBox = new Rectangle(
                            searchBox.X + localCandidateBox.X,
                            searchBox.Y + localCandidateBox.Y,
                            localCandidateBox.Width,
                            localCandidateBox.Height);
                    }

                    BarcodeReader reader = CreateConfiguredReader(pureBarcode: false);
                    BarcodeReader pureReader = CreateConfiguredReader(pureBarcode: true);

                    Result decodeResult = null;
                    string winningStrategy = "None";
                    int winScale = 1;
                    int winBorder = 0;
                    Point subOffset = searchBox.Location;

                    // 2. Előállítjuk a kontrasztjavított variációkat (Scale és Border adatokkal együtt)
                    var strategies = BuildDpmCandidateImages(roiGray);

                    if (strategies.Count > 3)
                    {
                        LastDebugPreview?.Dispose();
                        LastDebugPreview = strategies[3].Image.ToBitmap();
                    }


                    foreach (var strat in strategies)
                    {
                        using (strat.Image)
                        {
                            if (decodeResult == null)
                            {
                                using (Bitmap bmp = strat.Image.ToBitmap())
                                {
                                    decodeResult = reader.Decode(bmp);
                                    if (decodeResult != null)
                                    {
                                        winningStrategy = strat.Name;
                                        winScale = strat.Scale;
                                        winBorder = strat.Border;
                                        subOffset = searchBox.Location;
                                    }
                                    else if (this.FormatSelection == CodeFormatSelection.DataMatrixOnly)
                                    {
                                        decodeResult = pureReader.Decode(bmp);
                                        if (decodeResult != null)
                                        {
                                            winningStrategy = strat.Name + " (Pure)";
                                            winScale = strat.Scale;
                                            winBorder = strat.Border;
                                            subOffset = searchBox.Location;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 3. Ha még nem sikerült és a lokalizátor talált egy szűkebb négyzetet, megpróbáljuk csak azt kivágva is
                    if (decodeResult == null && localCandidateBox != Rectangle.Empty && localCandidateBox.Width > 20 && localCandidateBox.Height > 20)
                    {
                        roiGray.ROI = localCandidateBox;
                        using (Image<Gray, byte> tightCode = roiGray.Copy())
                        {
                            roiGray.ROI = Rectangle.Empty;
                            var tightStrategies = BuildDpmCandidateImages(tightCode);
                            foreach (var strat in tightStrategies)
                            {
                                using (strat.Image)
                                {
                                    if (decodeResult == null)
                                    {
                                        using (Bitmap bmp = strat.Image.ToBitmap())
                                        {
                                            decodeResult = reader.Decode(bmp) ?? pureReader.Decode(bmp);
                                            if (decodeResult != null)
                                            {
                                                winningStrategy = "TightCrop + " + strat.Name;
                                                winScale = strat.Scale;
                                                winBorder = strat.Border;
                                                subOffset = new Point(searchBox.X + localCandidateBox.X, searchBox.Y + localCandidateBox.Y);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 4. Kiértékelés és pontos visszavetített sarokpontok kirajzolása
                    if (decodeResult != null && !string.IsNullOrEmpty(decodeResult.Text))
                    {
                        this.DecodedText = decodeResult.Text.Trim();
                        this.DetectedFormat = decodeResult.BarcodeFormat.ToString();
                        this.UsedPreprocessStrategy = winningStrategy;

                        bool isValid = ValidateDecodedText(this.DecodedText, context);
                        this.LastTestResult = isValid ? "✅ PASS" : "❌ FAIL (MISMATCH)";
                        this.Status = isValid ? ModuleStatus.Success : ModuleStatus.Failed;

                        context.CustomData["DecodedCode"] = this.DecodedText;
                        context.CustomData[this.Name + "_Code"] = this.DecodedText;

                        if (!context.CustomData.ContainsKey("SerialNumber") || context.CustomData["SerialNumber"]?.ToString() == "-")
                        {
                            context.CustomData["SerialNumber"] = this.DecodedText;
                        }

                        MCvScalar drawColor = isValid ? new MCvScalar(0, 255, 0) : new MCvScalar(0, 165, 255);
                        Point textAnchor = searchBox.Location;

                        if (decodeResult.ResultPoints != null && decodeResult.ResultPoints.Length >= 3)
                        {
                            Point[] exactCorners = new Point[decodeResult.ResultPoints.Length];
                            int minX = int.MaxValue, minY = int.MaxValue;
                            float sumX = 0, sumY = 0;

                            for (int p = 0; p < decodeResult.ResultPoints.Length; p++)
                            {
                                float realX = subOffset.X + (decodeResult.ResultPoints[p].X - winBorder) / winScale;
                                float realY = subOffset.Y + (decodeResult.ResultPoints[p].Y - winBorder) / winScale;
                                exactCorners[p] = new Point((int)Math.Round(realX), (int)Math.Round(realY));

                                sumX += realX;
                                sumY += realY;
                                if (exactCorners[p].X < minX) minX = exactCorners[p].X;
                                if (exactCorners[p].Y < minY) minY = exactCorners[p].Y;
                            }

                            // Zárt kontúr rajzolása pontosan a DataMatrix cellahatáraira
                            for (int p = 0; p < exactCorners.Length; p++)
                            {
                                CvInvoke.Line(overlayOutput, exactCorners[p], exactCorners[(p + 1) % exactCorners.Length], drawColor, 2);
                                CvInvoke.Circle(overlayOutput, exactCorners[p], 3, new MCvScalar(0, 255, 255), -1);
                            }

                            textAnchor = new Point(minX, minY);

                            // Kód középpontjának rögzítése a Feature Store-ba geometriai mérésekhez
                            PointF codeCenter = new PointF(sumX / exactCorners.Length, sumY / exactCorners.Length);
                            context.Features[$"{this.EffectiveFeatureName}_Center"] = new PointFeature($"{this.EffectiveFeatureName}_Center", this.Name, codeCenter);
                        }
                        else
                        {
                            Rectangle fallbackBox = (this.DetectedCodeBox != Rectangle.Empty) ? this.DetectedCodeBox : searchBox;
                            CvInvoke.Rectangle(overlayOutput, fallbackBox, drawColor, 2);
                            textAnchor = fallbackBox.Location;
                        }

                        Point textPos = new Point(textAnchor.X, Math.Max(20, textAnchor.Y - 8));
                        CvInvoke.PutText(overlayOutput, $"DMC: {this.DecodedText}", textPos, FontFace.HersheySimplex, 0.55, drawColor, 2);
                    }
                    else
                    {
                        this.DecodedText = "";
                        this.DetectedFormat = "-";
                        this.UsedPreprocessStrategy = "Failed";
                        this.LastTestResult = "❌ NO READ";
                        this.Status = ModuleStatus.Failed;

                        Rectangle failBox = (this.DetectedCodeBox != Rectangle.Empty) ? this.DetectedCodeBox : searchBox;
                        CvInvoke.Rectangle(overlayOutput, failBox, new MCvScalar(0, 0, 255), 2);
                        Point textPos = new Point(failBox.X, Math.Max(20, failBox.Y - 8));
                        string label = (this.DetectedCodeBox != Rectangle.Empty) ? "CODE LOCATED (NO READ)" : "NO CODE FOUND";
                        CvInvoke.PutText(overlayOutput, label, textPos, FontFace.HersheySimplex, 0.5, new MCvScalar(0, 0, 255), 2);
                    }
                }

                sw.Stop();
                this.LastExecutionTime = $"{sw.Elapsed.TotalMilliseconds:F1} ms";

                context.CustomData[this.Name + "_Code"] = this.DecodedText;
                context.CustomData[this.Name + "_Format"] = this.DetectedFormat;
                context.CustomData[this.Name + "_Strategy"] = this.UsedPreprocessStrategy;
                context.CustomData[this.Name + "_Time"] = this.LastExecutionTime;
                context.CustomData[this.Name + "_Status"] = this.LastTestResult;
                context.SourceImage?.Dispose();
                context.SourceImage = overlayOutput;
            }
            catch (Exception ex)
            {
                sw.Stop();
                overlayOutput?.Dispose();
                this.LastTestResult = "💥 ERROR";
                this.Status = ModuleStatus.Failed;
                Console.WriteLine($"[CodeReaderModule] Hiba: {ex.Message}");
            }
            finally
            {
                inputColor?.Dispose();
                this.ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
            }
        }

        // ====================================================================
        // --- DPM LOKALIZÁTOR: MEGTALÁLJA A KÓD NÉGYZETÉT A TEXTÚRA ALAPJÁN ---
        // ====================================================================
        private Rectangle LocateDpmCandidateBox(Image<Gray, byte> gray)
        {
            try
            {
                // Sobel élgradiens + morfológiai összemosás a sűrű DataMatrix cellák megtalálásához
                using (Image<Gray, float> gradX = gray.Sobel(1, 0, 3))
                using (Image<Gray, float> gradY = gray.Sobel(0, 1, 3))
                using (Image<Gray, byte> absX = gradX.ConvertScale<byte>(1, 0))
                using (Image<Gray, byte> absY = gradY.ConvertScale<byte>(1, 0))
                using (Image<Gray, byte> grad = new Image<Gray, byte>(gray.Size))
                using (Image<Gray, byte> thresh = new Image<Gray, byte>(gray.Size))
                using (Image<Gray, byte> closed = new Image<Gray, byte>(gray.Size))
                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                using (Mat hierarchy = new Mat())
                {
                    CvInvoke.AddWeighted(absX, 0.5, absY, 0.5, 0, grad);
                    CvInvoke.Threshold(grad, thresh, 0, 255, ThresholdType.Otsu | ThresholdType.Binary);
                    // Összemossuk a kód belsejében lévő sűrű pontokat egyetlen tömör négyzetté
                    using (Mat kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(9, 9), new Point(-1, -1)))
                    {
                        CvInvoke.MorphologyEx(thresh, closed, MorphOp.Close, kernel, new Point(-1, -1), 2, BorderType.Reflect, new MCvScalar());
                    }
                    CvInvoke.FindContours(closed, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);
                    Rectangle bestSquare = Rectangle.Empty;
                    double bestScore = 0;
                    for (int i = 0; i < contours.Size; i++)
                    {
                        Rectangle r = CvInvoke.BoundingRectangle(contours[i]);
                        if (r.Width < 25 || r.Height < 25) continue;
                        double aspect = (double)r.Width / r.Height;
                        // A DataMatrix négyzetes vagy 2:1 téglalap alakú (0.45 .. 2.2)
                        if (aspect >= 0.45 && aspect <= 2.2)
                        {
                            double area = r.Width * r.Height;
                            if (area > bestScore)
                            {
                                bestScore = area;
                                bestSquare = r;
                            }
                        }
                    }
                    if (bestSquare != Rectangle.Empty)
                    {
                        // Hagyunk 10% csendes zónát (Quiet Zone) a kód körül a ZXing számára!
                        int padX = Math.Max(6, (int)(bestSquare.Width * 0.12));
                        int padY = Math.Max(6, (int)(bestSquare.Height * 0.12));
                        int x = Math.Max(0, bestSquare.X - padX);
                        int y = Math.Max(0, bestSquare.Y - padY);
                        int w = Math.Min(gray.Width - x, bestSquare.Width + padX * 2);
                        int h = Math.Min(gray.Height - y, bestSquare.Height + padY * 2);
                        return new Rectangle(x, y, w, h);
                    }
                }
            }
            catch { }
            return Rectangle.Empty;
        }
        private BarcodeReader CreateConfiguredReader(bool pureBarcode)
        {
            var hints = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = this.TryInvertedAndMirrored,
                PureBarcode = pureBarcode
            };
            switch (this.FormatSelection)
            {
                case CodeFormatSelection.DataMatrixOnly:
                    hints.PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.DATA_MATRIX };
                    break;
                case CodeFormatSelection.QRCodeOnly:
                    hints.PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE };
                    break;
                case CodeFormatSelection.Barcodes1D:
                    hints.PossibleFormats = new List<BarcodeFormat> {
            BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.EAN_13, BarcodeFormat.ITF
        };
                    break;
                case CodeFormatSelection.AutoAll:
                default:
                    hints.PossibleFormats = new List<BarcodeFormat> {
            BarcodeFormat.DATA_MATRIX, BarcodeFormat.QR_CODE, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39
        };
                    break;
            }

            return new BarcodeReader { AutoRotate = true, Options = hints };
        }
        private List<(string Name, Image<Gray, byte> Image, int Scale, int Border)> BuildDpmCandidateImages(Image<Gray, byte> gray)
        {
            var list = new List<(string, Image<Gray, byte>, int, int)>();

            // 1. Nyers kép (Scale = 1, Border = 0)
            list.Add(("Raw Grayscale", gray.Copy(), 1, 0));

            if (!this.EnableDpmEnhancement) return list;

            int targetMinDim = 180;
            int scale = 1;
            if (gray.Width < targetMinDim || gray.Height < targetMinDim)
            {
                scale = Math.Max(2, (int)Math.Ceiling((double)targetMinDim / Math.Min(gray.Width, gray.Height)));
                scale = Math.Min(scale, 4);
            }

            Image<Gray, byte> working = scale > 1
                ? gray.Resize(gray.Width * scale, gray.Height * scale, Inter.Linear)
                : gray.Copy();

            Image<Gray, byte> smooth = working.SmoothGaussian(3);

            Image<Gray, byte> clahe = new Image<Gray, byte>(smooth.Size);
            CvInvoke.CLAHE(smooth, 2.0, new Size(6, 6), clahe);
            list.Add(("Soft CLAHE", AddQuietZoneBorder(clahe, 20), scale, 20));

            int[] blockSizes = { 15, 25, 35 };
            foreach (int bSize in blockSizes)
            {
                Image<Gray, byte> adapt = new Image<Gray, byte>(clahe.Size);
                CvInvoke.AdaptiveThreshold(clahe, adapt, 255, AdaptiveThresholdType.GaussianC, ThresholdType.Binary, bSize, 2);

                if (this.DpmMorphSize > 0)
                {
                    using (Mat k3 = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(3, 3), new Point(-1, -1)))
                    {
                        CvInvoke.MorphologyEx(adapt, adapt, MorphOp.Close, k3, new Point(-1, -1), 1, BorderType.Reflect, new MCvScalar());
                    }
                }

                Image<Gray, byte> adaptInv = adapt.Not();

                list.Add(($"AdaptiveInv(B{bSize})", AddQuietZoneBorder(adaptInv, 24), scale, 24));
                list.Add(($"Adaptive(B{bSize})", AddQuietZoneBorder(adapt, 24), scale, 24));

                adapt.Dispose();
                adaptInv.Dispose();
            }

            Image<Gray, byte> otsu = new Image<Gray, byte>(clahe.Size);
            CvInvoke.Threshold(clahe, otsu, 0, 255, ThresholdType.Otsu | ThresholdType.BinaryInv);
            list.Add(("Otsu Inverted", AddQuietZoneBorder(otsu, 24), scale, 24));

            working.Dispose();
            smooth.Dispose();
            clahe.Dispose();
            otsu.Dispose();

            return list;
        }


        // Fehér "Csendes Zóna" (Quiet Zone) keret hozzáadása a kép köré, hogy a ZXing Finder Pattern detektora biztosan megtalálja a széleket!
        private Image<Gray, byte> AddQuietZoneBorder(Image<Gray, byte> src, int borderPx)
        {
            Image<Gray, byte> padded = new Image<Gray, byte>(src.Width + borderPx * 2, src.Height + borderPx * 2, new Gray(255));
            padded.ROI = new Rectangle(borderPx, borderPx, src.Width, src.Height);
            src.CopyTo(padded);
            padded.ROI = Rectangle.Empty;
            return padded;
        }

        private bool ValidateDecodedText(string decoded, InspectionContext context)
        {
            switch (this.ValidationMode)
            {
                case CodeValidationMode.ContainsText:
                    return !string.IsNullOrEmpty(this.ExpectedText) && decoded.Contains(this.ExpectedText);
                case CodeValidationMode.ExactMatchText:
                    return string.Equals(decoded, this.ExpectedText, StringComparison.OrdinalIgnoreCase);
                case CodeValidationMode.MatchFileSerial:
                    if (context.CustomData != null && context.CustomData.TryGetValue("SerialNumber", out object snObj))
                    {
                        string fileSn = snObj?.ToString() ?? "";
                        return !string.IsNullOrEmpty(fileSn) && string.Equals(decoded, fileSn, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                default:
                    return !string.IsNullOrWhiteSpace(decoded);
            }
        }
    }
}