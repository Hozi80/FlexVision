using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace FlexVision
{

    public enum CaliperMeasurementMode
    {
        SingleEdge, // Egyedi élek keresése
        EdgePair    // Élpár keresése adott távolságra
    }

    public class CaliperEdgeResult
    {
        public int Index { get; set; }
        public PointF Position { get; set; }
        public PointF Edge1Position { get; set; }
        public PointF Edge2Position { get; set; }
        public double PairDistance { get; set; }
        public double Contrast { get; set; }      // Valódi átlagos kontraszt (0-255)
        public double Edge1Contrast { get; set; } // 1. él kontrasztja
        public double Edge2Contrast { get; set; } // 2. él kontrasztja
        public double PairScore { get; set; }     // Belső rangsorolási pontszám (Straddle + Kontraszt)
        public string Polarity { get; set; }
        public bool IsPair { get; set; }
    }

    public enum CaliperDirection
    {
        LeftToRight,
        RightToLeft,
        TopToBottom,
        BottomToTop
    }

    public enum EdgePolarity
    {
        Any,
        DarkToLight, // Sötétből világosba (+)
        LightToDark  // Világosból sötétbe (-)
    }

    public enum EdgeSelectionMode
    {
        FirstEdge,
        LastEdge,
        StrongestEdge,
        AllEdges
    }

    public enum CaliperColorChannel
    {
        AutoBest,
        Grayscale,
        Blue,
        Green,
        Red,
        Hue,        // HSV - Színárnyalat
        Saturation, // HSV - Telítettség
        Value,      // HSV - Fényerő
        Lab_L,      // CIE Lab - Világosság
        Lab_A,      // CIE Lab - Zöld-Vörös tengely (A te képeden ez brutális kontrasztot ad!)
        Lab_B       // CIE Lab - Kék-Sárga tengely
    }



    [Serializable]
    public class EdgeCaliperModule : BaseInspectionModule
    {
        [Browsable(false)]
        public override string PureName => "1D Edge Caliper";

        [Browsable(false)]
        public override VisionModuleType ModuleType => VisionModuleType.EdgeDetect;

        // ====================================================================
        // --- RECEPTBEN MENTHETŐ IPARI PARAMÉTEREK ---
        // ====================================================================
        [Category("1. Mérőablak (Caliper ROI)")]
        public bool UseROI { get; set; } = true;

        [Category("1. Mérőablak (Caliper ROI)")]
        public Rectangle ROI { get; set; } = new Rectangle(100, 100, 200, 60);

        [Category("2. Élkeresési Beállítások")]
        public CaliperMeasurementMode MeasurementMode { get; set; } = CaliperMeasurementMode.SingleEdge;

        [Category("2. Élkeresési Beállítások")]
        [Description("Elvárt távolság a két él között pixelben (EdgePair módban).")]
        public double ExpectedPairWidth { get; set; } = 50.0;

        [Category("2. Élkeresési Beállítások")]
        [Description("Megengedett eltérés az elvárt távolságtól pixelben (+/-).")]
        public double PairWidthTolerance { get; set; } = 10.0;

        [Category("2. Élkeresési Beállítások")]
        [Description("Élpár esetén a 2. él polaritása (pl. ha az 1. Dark->Light, a 2. gyakran Light->Dark).")]
        public EdgePolarity PairSecondPolarity { get; set; } = EdgePolarity.Any;

        [Category("2. Élkeresési Beállítások")]
        [Description("Straddle: Előnyben részesíti azokat az élpárokat, amelyek közvetlenül a mérőablak középpontja körül helyezkednek el.")]
        public bool PreferStraddleCenter { get; set; } = true;


        [Category("2. Élkeresési Beállítások")]
        [Description("Színcsatorna kiválasztása a kontraszt maximalizálásához.")]
        public CaliperColorChannel ColorChannel { get; set; } = CaliperColorChannel.AutoBest;


        [Category("2. Élkeresési Beállítások")]
        public CaliperDirection Direction { get; set; } = CaliperDirection.LeftToRight;

        [Category("2. Élkeresési Beállítások")]
        public EdgePolarity Polarity { get; set; } = EdgePolarity.Any;

        [Category("2. Élkeresési Beállítások")]
        public EdgeSelectionMode SelectionMode { get; set; } = EdgeSelectionMode.FirstEdge;

        [Category("2. Élkeresési Beállítások")]
        [Description("Minimális kontrasztugrás (0 - 255 között).")]
        public double ContrastThreshold { get; set; } = 30.0;

        [Category("2. Élkeresési Beállítások")]
        [Description("1D Gauss simítás szűrőmérete a zaj csökkentéséhez (páratlan szám: 1, 3, 5, 7).")]
        public int FilterHalfWidth { get; set; } = 2;

        [Category("3. Minőségellenőrzés")]
        public int ExpectedEdgeCount { get; set; } = 1;

        // --- KIMENETI DIAGNOSZTIKA ---
        [Category("4. Mérési Eredmények"), ReadOnly(true)]
        public string ExecutionTime { get; set; } = "0 ms";

        [Category("4. Mérési Eredmények"), ReadOnly(true)]
        public int FoundEdgeCount { get; set; } = 0;

        [Category("4. Mérési Eredmények"), ReadOnly(true)]
        public string TestResult { get; set; } = "WAITING...";

        // --- FUTÁSIDEJŰ EREDMÉNYEK ---
        [XmlIgnore, Browsable(false)]
        public List<CaliperEdgeResult> LastEdges { get; set; } = new List<CaliperEdgeResult>();

        [XmlIgnore, Browsable(false)]
        public string LastExecutionTime { get; set; } = "0 ms";

        [XmlIgnore, Browsable(false)]
        public string LastTestResult { get; set; } = "WAITING...";

        [XmlIgnore, Browsable(false)]
        public Image<Bgr, byte> LastCleanInput { get; set; }


        [XmlIgnore, Browsable(false)]
        public List<int> SelectedEdgeIndices { get; set; } = new List<int>();

        [XmlIgnore, Browsable(false)]
        public string LastUsedChannel { get; set; } = "-";

        public EdgeCaliperModule() { }



        public static Image<Gray, byte> ExtractChannelImage(Image<Bgr, byte> bgrImg, CaliperColorChannel channel)
        {
            switch (channel)
            {
                case CaliperColorChannel.Blue:
                case CaliperColorChannel.Green:
                case CaliperColorChannel.Red:
                    {
                        Image<Gray, byte>[] ch = bgrImg.Split();
                        int idx = (channel == CaliperColorChannel.Blue) ? 0 : (channel == CaliperColorChannel.Green ? 1 : 2);
                        Image<Gray, byte> res = ch[idx].Clone();
                        for (int i = 0; i < 3; i++) ch[i].Dispose();
                        return res;
                    }
                case CaliperColorChannel.Hue:
                case CaliperColorChannel.Saturation:
                case CaliperColorChannel.Value:
                    {
                        using (Image<Hsv, byte> hsv = bgrImg.Convert<Hsv, byte>())
                        {
                            Image<Gray, byte>[] ch = hsv.Split();
                            int idx = (channel == CaliperColorChannel.Hue) ? 0 : (channel == CaliperColorChannel.Saturation ? 1 : 2);
                            Image<Gray, byte> res = ch[idx].Clone();
                            for (int i = 0; i < 3; i++) ch[i].Dispose();
                            return res;
                        }
                    }
                case CaliperColorChannel.Lab_L:
                case CaliperColorChannel.Lab_A:
                case CaliperColorChannel.Lab_B:
                    {
                        using (Image<Lab, byte> lab = bgrImg.Convert<Lab, byte>())
                        {
                            Image<Gray, byte>[] ch = lab.Split();
                            int idx = (channel == CaliperColorChannel.Lab_L) ? 0 : (channel == CaliperColorChannel.Lab_A ? 1 : 2);
                            Image<Gray, byte> res = ch[idx].Clone();
                            for (int i = 0; i < 3; i++) ch[i].Dispose();
                            return res;
                        }
                    }
                case CaliperColorChannel.Grayscale:
                default:
                    return bgrImg.Convert<Gray, byte>();
            }
        }


        public override void Process(InspectionContext context)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            this.Status = ModuleStatus.NotExecuted;

            Image<Bgr, byte> inputColor = context.GetInputColorImage(this.InputStepLink);
            if (inputColor == null) { stopwatch.Stop(); this.Status = ModuleStatus.Failed; return; }

            // Elmentjük a tiszta bemeneti képet a Mátrix ablaknak, még mielőtt bármit rárajzolnánk
            this.LastCleanInput?.Dispose();
            this.LastCleanInput = inputColor.Copy();

            Image<Bgr, byte> overlayOutput = inputColor.Copy();
            List<CaliperEdgeResult> detectedEdges = new List<CaliperEdgeResult>();

            try
            {
                Rectangle searchBox = new Rectangle(0, 0, inputColor.Width, inputColor.Height);
                if (this.UseROI && this.ROI != Rectangle.Empty && this.ROI.Width > 4 && this.ROI.Height > 4)
                {
                    searchBox = Rectangle.Intersect(searchBox, this.ROI);
                }

                if (searchBox.Width < 5 || searchBox.Height < 5)
                {
                    stopwatch.Stop();
                    this.Status = ModuleStatus.Failed;
                    return;
                }

                // EdgePair módban ideiglenesen minden polaritást begyűjtünk a párosításhoz
                EdgePolarity savedPol = this.Polarity;
                if (this.MeasurementMode == CaliperMeasurementMode.EdgePair)
                {
                    this.Polarity = EdgePolarity.Any;
                }

                try
                {
                    inputColor.ROI = searchBox;
                    using (Image<Bgr, byte> roiColor = inputColor.Copy())
                    {
                        inputColor.ROI = Rectangle.Empty;

                        if (this.ColorChannel == CaliperColorChannel.AutoBest)
                        {
                            List<CaliperEdgeResult> bestCandidates = new List<CaliperEdgeResult>();
                            double highestScore = double.MinValue;
                            CaliperColorChannel winningChannel = CaliperColorChannel.Grayscale;
                            bool foundAny = false;

                            CaliperColorChannel[] allChannels = {
                        CaliperColorChannel.Blue, CaliperColorChannel.Green, CaliperColorChannel.Red,
                        CaliperColorChannel.Hue, CaliperColorChannel.Saturation, CaliperColorChannel.Value,
                        CaliperColorChannel.Lab_L, CaliperColorChannel.Lab_A, CaliperColorChannel.Lab_B
                    };

                            foreach (var ch in allChannels)
                            {
                                using (Image<Gray, byte> chImg = ExtractChannelImage(roiColor, ch))
                                {
                                    double[] prof = Extract1DProfile(chImg, this.Direction);
                                    double[] smooth = SmoothProfile(prof, this.FilterHalfWidth);
                                    var cands = FindSubPixelEdges(smooth, searchBox, this.Direction);

                                    if (this.MeasurementMode == CaliperMeasurementMode.SingleEdge)
                                    {
                                        foreach (var edge in cands)
                                        {
                                            if (edge.Contrast > highestScore)
                                            {
                                                highestScore = edge.Contrast;
                                                bestCandidates = cands;
                                                winningChannel = ch;
                                                foundAny = true;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // EdgePair módban a Straddle + Kontraszt pontszám (PairScore) dönti el a győztes csatornát
                                        this.Polarity = savedPol;
                                        var pairs = FindAndScoreEdgePairs(cands, searchBox);
                                        this.Polarity = EdgePolarity.Any;

                                        foreach (var pair in pairs)
                                        {
                                            if (pair.PairScore > highestScore)
                                            {
                                                highestScore = pair.PairScore;
                                                bestCandidates = pairs;
                                                winningChannel = ch;
                                                foundAny = true;
                                            }
                                        }
                                    }
                                }
                            }

                            this.LastUsedChannel = foundAny ? $"Auto ({winningChannel})" : "Auto (None)";

                            if (this.MeasurementMode == CaliperMeasurementMode.SingleEdge)
                                detectedEdges = FilterCandidates(bestCandidates);
                            else
                                detectedEdges = bestCandidates;
                        }
                        else
                        {
                            this.LastUsedChannel = this.ColorChannel.ToString();

                            using (Image<Gray, byte> selectedGray = ExtractChannelImage(roiColor, this.ColorChannel))
                            {
                                double[] profile = Extract1DProfile(selectedGray, this.Direction);
                                double[] smoothed = SmoothProfile(profile, this.FilterHalfWidth);
                                List<CaliperEdgeResult> rawCandidates = FindSubPixelEdges(smoothed, searchBox, this.Direction);

                                if (this.MeasurementMode == CaliperMeasurementMode.SingleEdge)
                                {
                                    detectedEdges = FilterCandidates(rawCandidates);
                                }
                                else
                                {
                                    this.Polarity = savedPol;
                                    detectedEdges = FindAndScoreEdgePairs(rawCandidates, searchBox);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    this.Polarity = savedPol;
                }

                // Vizuális overlay rajzolása és Feature-ök rögzítése
                DrawCaliperOverlay(overlayOutput, searchBox, detectedEdges);

                bool isHoriz = (this.Direction == CaliperDirection.LeftToRight || this.Direction == CaliperDirection.RightToLeft);

                for (int i = 0; i < detectedEdges.Count; i++)
                {
                    var edge = detectedEdges[i];
                    edge.Index = i + 1;

                    if (edge.IsPair)
                    {
                        string centerKey = $"{this.EffectiveFeatureName}_PairCenter_{edge.Index}";
                        string e1Key = $"{this.EffectiveFeatureName}_PairEdge1_{edge.Index}";
                        string e2Key = $"{this.EffectiveFeatureName}_PairEdge2_{edge.Index}";

                        context.Features[centerKey] = new PointFeature(centerKey, this.Name, edge.Position);
                        context.Features[e1Key] = new PointFeature(e1Key, this.Name, edge.Edge1Position);
                        context.Features[e2Key] = new PointFeature(e2Key, this.Name, edge.Edge2Position);

                        // Élvonalak mentése a geometriai mérésekhez
                        PointF l1Start = isHoriz ? new PointF(edge.Edge1Position.X, searchBox.Y) : new PointF(searchBox.X, edge.Edge1Position.Y);
                        PointF l1End = isHoriz ? new PointF(edge.Edge1Position.X, searchBox.Bottom) : new PointF(searchBox.Right, edge.Edge1Position.Y);
                        context.Features[$"{this.EffectiveFeatureName}_PairLine1_{edge.Index}"] = new LineFeature($"{this.EffectiveFeatureName}_PairLine1_{edge.Index}", this.Name, l1Start, l1End);

                        PointF l2Start = isHoriz ? new PointF(edge.Edge2Position.X, searchBox.Y) : new PointF(searchBox.X, edge.Edge2Position.Y);
                        PointF l2End = isHoriz ? new PointF(edge.Edge2Position.X, searchBox.Bottom) : new PointF(searchBox.Right, edge.Edge2Position.Y);
                        context.Features[$"{this.EffectiveFeatureName}_PairLine2_{edge.Index}"] = new LineFeature($"{this.EffectiveFeatureName}_PairLine2_{edge.Index}", this.Name, l2Start, l2End);
                    }
                    else
                    {
                        string featKey = $"{this.EffectiveFeatureName}_Edge_{edge.Index}";
                        context.Features[featKey] = new PointFeature(featKey, this.Name, edge.Position);

                        PointF lStart = isHoriz ? new PointF(edge.Position.X, searchBox.Y) : new PointF(searchBox.X, edge.Position.Y);
                        PointF lEnd = isHoriz ? new PointF(edge.Position.X, searchBox.Bottom) : new PointF(searchBox.Right, edge.Position.Y);
                        context.Features[$"{this.EffectiveFeatureName}_Line_{edge.Index}"] = new LineFeature($"{this.EffectiveFeatureName}_Line_{edge.Index}", this.Name, lStart, lEnd);
                    }
                }


                stopwatch.Stop();
                this.LastEdges = detectedEdges;
                this.FoundEdgeCount = detectedEdges.Count;
                this.LastExecutionTime = $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms";
                this.ExecutionTime = this.LastExecutionTime;

                bool isPass = (this.FoundEdgeCount == this.ExpectedEdgeCount);
                this.LastTestResult = isPass ? "✅ PASS" : "❌ FAIL";
                this.TestResult = this.LastTestResult;
                this.Status = isPass ? ModuleStatus.Success : ModuleStatus.Failed;

                // Sub-loop szinkronizációhoz mentés
                context.CustomData[this.Name + "_Edges"] = new List<CaliperEdgeResult>(detectedEdges);
                context.CustomData[this.Name + "_Time"] = this.LastExecutionTime;
                context.CustomData[this.Name + "_Status"] = this.LastTestResult;
                context.CustomData[this.Name + "_Channel"] = this.LastUsedChannel;

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
                Console.WriteLine($"[EdgeCaliper] Hiba: {ex.Message}");
            }
            finally
            {
                inputColor?.Dispose();
                this.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private double[] Extract1DProfile(Image<Gray, byte> roiGray, CaliperDirection dir)
        {
            int w = roiGray.Width;
            int h = roiGray.Height;
            byte[,,] data = roiGray.Data;

            if (dir == CaliperDirection.LeftToRight || dir == CaliperDirection.RightToLeft)
            {
                double[] profile = new double[w];
                for (int x = 0; x < w; x++)
                {
                    double sum = 0;
                    for (int y = 0; y < h; y++) sum += data[y, x, 0];
                    int targetIdx = (dir == CaliperDirection.LeftToRight) ? x : (w - 1 - x);
                    profile[targetIdx] = sum / h;
                }
                return profile;
            }
            else
            {
                double[] profile = new double[h];
                for (int y = 0; y < h; y++)
                {
                    double sum = 0;
                    for (int x = 0; x < w; x++) sum += data[y, x, 0];
                    int targetIdx = (dir == CaliperDirection.TopToBottom) ? y : (h - 1 - y);
                    profile[targetIdx] = sum / w;
                }
                return profile;
            }
        }

        private double[] SmoothProfile(double[] src, int halfWidth)
        {
            if (halfWidth <= 0) return src;
            int len = src.Length;
            double[] dst = new double[len];

            for (int i = 0; i < len; i++)
            {
                double sum = 0;
                int count = 0;
                for (int k = -halfWidth; k <= halfWidth; k++)
                {
                    int idx = i + k;
                    if (idx >= 0 && idx < len)
                    {
                        sum += src[idx];
                        count++;
                    }
                }
                dst[i] = sum / count;
            }
            return dst;
        }

        private List<CaliperEdgeResult> FindSubPixelEdges(double[] profile, Rectangle box, CaliperDirection dir)
        {
            List<CaliperEdgeResult> results = new List<CaliperEdgeResult>();
            int len = profile.Length;
            if (len < 4) return results;

            // 1. Első derivált számítása (gradiens)
            double[] grad = new double[len];
            for (int i = 1; i < len - 1; i++)
            {
                grad[i] = (profile[i + 1] - profile[i - 1]) * 0.5;
            }

            // 2. Lokális szélsőértékek keresése és parabolikus sub-pixel interpoláció
            for (int i = 2; i < len - 2; i++)
            {
                double gPrev = Math.Abs(grad[i - 1]);
                double gCurr = Math.Abs(grad[i]);
                double gNext = Math.Abs(grad[i + 1]);
                if (gCurr >= this.ContrastThreshold && gCurr > gPrev && gCurr >= gNext)
                {
                    bool isDarkToLight = grad[i] > 0;
                    if (this.Polarity == EdgePolarity.DarkToLight && !isDarkToLight) continue;
                    if (this.Polarity == EdgePolarity.LightToDark && isDarkToLight) continue;
                    // Parabolikus csúcs-eltolás (-0.5 .. +0.5 pixel között)
                    double denom = 2.0 * (gPrev - 2.0 * gCurr + gNext);
                    double subOffset = (Math.Abs(denom) > 1e-5) ? (gPrev - gNext) / denom : 0.0;
                    double exactPos = i + subOffset;
                    PointF worldPt = Map1DToWorld(exactPos, box, dir);
                    results.Add(new CaliperEdgeResult
                    {
                        Position = worldPt,
                        Contrast = gCurr,
                        Polarity = isDarkToLight ? "Dark -> Light" : "Light -> Dark"
                    });
                }
            }
            return results;
        }
        private PointF Map1DToWorld(double pos1D, Rectangle box, CaliperDirection dir)
        {
            float midX = box.X + (box.Width / 2.0f);
            float midY = box.Y + (box.Height / 2.0f);
            switch (dir)
            {
                case CaliperDirection.LeftToRight:
                    return new PointF((float)(box.X + pos1D), midY);
                case CaliperDirection.RightToLeft:
                    return new PointF((float)(box.Right - 1 - pos1D), midY);
                case CaliperDirection.TopToBottom:
                    return new PointF(midX, (float)(box.Y + pos1D));
                case CaliperDirection.BottomToTop:
                    return new PointF(midX, (float)(box.Bottom - 1 - pos1D));
                default:
                    return new PointF(midX, midY);
            }
        }
        private List<CaliperEdgeResult> FilterCandidates(List<CaliperEdgeResult> candidates)
        {
            if (candidates.Count == 0) return candidates;
            switch (this.SelectionMode)
            {
                case EdgeSelectionMode.FirstEdge:
                    return new List<CaliperEdgeResult> { candidates[0] };
                case EdgeSelectionMode.LastEdge:
                    return new List<CaliperEdgeResult> { candidates[candidates.Count - 1] };
                case EdgeSelectionMode.StrongestEdge:
                    CaliperEdgeResult best = candidates[0];
                    foreach (var c in candidates)
                    {
                        if (c.Contrast > best.Contrast) best = c;
                    }
                    return new List<CaliperEdgeResult> { best };
                case EdgeSelectionMode.AllEdges:
                default:
                    return candidates;
            }
        }


        private List<CaliperEdgeResult> FindAndScoreEdgePairs(List<CaliperEdgeResult> rawEdges, Rectangle box)
        {
            List<CaliperEdgeResult> validPairs = new List<CaliperEdgeResult>();
            if (rawEdges.Count < 2) return validPairs;

            PointF boxCenter = new PointF(box.X + box.Width / 2.0f, box.Y + box.Height / 2.0f);
            double maxPossibleDist = Math.Max(box.Width, box.Height) / 2.0;

            // Minden lehetséges élpárt megvizsgálunk (i < j)
            for (int i = 0; i < rawEdges.Count - 1; i++)
            {
                var e1 = rawEdges[i];
                bool e1DarkToLight = e1.Polarity == "Dark -> Light";
                if (this.Polarity == EdgePolarity.DarkToLight && !e1DarkToLight) continue;
                if (this.Polarity == EdgePolarity.LightToDark && e1DarkToLight) continue;

                for (int j = i + 1; j < rawEdges.Count; j++)
                {
                    var e2 = rawEdges[j];
                    bool e2DarkToLight = e2.Polarity == "Dark -> Light";
                    if (this.PairSecondPolarity == EdgePolarity.DarkToLight && !e2DarkToLight) continue;
                    if (this.PairSecondPolarity == EdgePolarity.LightToDark && e2DarkToLight) continue;

                    // Euklideszi távolság a két él között
                    double dx = e2.Position.X - e1.Position.X;
                    double dy = e2.Position.Y - e1.Position.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    // Távolság tolerancia ellenőrzése
                    double widthError = Math.Abs(dist - this.ExpectedPairWidth);
                    if (widthError <= this.PairWidthTolerance)
                    {
                        PointF pairCenter = new PointF((e1.Position.X + e2.Position.X) / 2.0f, (e1.Position.Y + e2.Position.Y) / 2.0f);
                        double avgContrast = (e1.Contrast + e2.Contrast) / 2.0;

                        // Straddle büntetés számítása (mennyire van távol a pár közepe a ROI közepétől)
                        double centerOffset = Math.Sqrt(Math.Pow(pairCenter.X - boxCenter.X, 2) + Math.Pow(pairCenter.Y - boxCenter.Y, 2));

                        // Pontszám: Kontraszt + szélesség pontosság - Straddle középpont eltérés
                        double score = avgContrast - (widthError * 2.0);
                        if (this.PreferStraddleCenter)
                        {
                            // Erős súlyozás, ha a ROI közepe a két él közé esik
                            score -= (centerOffset / Math.Max(1.0, maxPossibleDist)) * 150.0;
                        }

                        validPairs.Add(new CaliperEdgeResult
                        {
                            IsPair = true,
                            Position = pairCenter,
                            Edge1Position = e1.Position,
                            Edge2Position = e2.Position,
                            PairDistance = dist,
                            Contrast = avgContrast,          // Valódi átlagos kontraszt!
                            Edge1Contrast = e1.Contrast,
                            Edge2Contrast = e2.Contrast,
                            PairScore = score,               // Rangsorolási pontszám (Straddle + Kontraszt)
                            Polarity = $"{(e1DarkToLight ? "+" : "-")} / {(e2DarkToLight ? "+" : "-")}"
                        });

                    }
                }
            }

            if (validPairs.Count == 0) return validPairs;

            // Kiválasztási mód alkalmazása a talált párokra
            switch (this.SelectionMode)
            {
                case EdgeSelectionMode.FirstEdge:
                    return new List<CaliperEdgeResult> { validPairs[0] };
                case EdgeSelectionMode.LastEdge:
                    return new List<CaliperEdgeResult> { validPairs[validPairs.Count - 1] };
                case EdgeSelectionMode.StrongestEdge:
                    // Itt a legmagasabb Straddle + Kontraszt pontszámú (PairScore) párt választja ki!
                    CaliperEdgeResult best = validPairs[0];
                    foreach (var p in validPairs)
                    {
                        if (p.PairScore > best.PairScore) best = p;
                    }
                    return new List<CaliperEdgeResult> { best };
                case EdgeSelectionMode.AllEdges:
                default:
                    return validPairs;
            }
        }



        private void DrawCaliperOverlay(Image<Bgr, byte> img, Rectangle box, List<CaliperEdgeResult> edges)
        {
            CvInvoke.Rectangle(img, box, new MCvScalar(255, 200, 0), 1);

            Point pStart = Point.Empty, pEnd = Point.Empty;
            int midX = box.X + box.Width / 2;
            int midY = box.Y + box.Height / 2;

            switch (this.Direction)
            {
                case CaliperDirection.LeftToRight: pStart = new Point(box.X + 4, midY); pEnd = new Point(box.Right - 4, midY); break;
                case CaliperDirection.RightToLeft: pStart = new Point(box.Right - 4, midY); pEnd = new Point(box.X + 4, midY); break;
                case CaliperDirection.TopToBottom: pStart = new Point(midX, box.Y + 4); pEnd = new Point(midX, box.Bottom - 4); break;
                case CaliperDirection.BottomToTop: pStart = new Point(midX, box.Bottom - 4); pEnd = new Point(midX, box.Y + 4); break;
            }

            CvInvoke.ArrowedLine(img, pStart, pEnd, new MCvScalar(255, 200, 0), 1);

            bool isHorizontal = (this.Direction == CaliperDirection.LeftToRight || this.Direction == CaliperDirection.RightToLeft);

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                bool isSelected = (this.SelectedEdgeIndices != null && this.SelectedEdgeIndices.Contains(i));

                // Kijelölt sor: élénk Magenta (255, 0, 255) és 3px vastagság; alap: Neon zöld (0, 255, 0) és 2px
                MCvScalar lineColor = isSelected ? new MCvScalar(255, 0, 255) : new MCvScalar(0, 255, 0);
                int thickness = isSelected ? 3 : 2;

                if (edge.IsPair)
                {
                    Point pt1 = Point.Round(edge.Edge1Position);
                    Point pt2 = Point.Round(edge.Edge2Position);
                    Point ptCenter = Point.Round(edge.Position);

                    if (isHorizontal)
                    {
                        CvInvoke.Line(img, new Point(pt1.X, box.Y), new Point(pt1.X, box.Bottom), lineColor, thickness);
                        CvInvoke.Line(img, new Point(pt2.X, box.Y), new Point(pt2.X, box.Bottom), lineColor, thickness);
                    }
                    else
                    {
                        CvInvoke.Line(img, new Point(box.X, pt1.Y), new Point(box.Right, pt1.Y), lineColor, thickness);
                        CvInvoke.Line(img, new Point(box.X, pt2.Y), new Point(box.Right, pt2.Y), lineColor, thickness);
                    }

                    // Összekötő sárga mérővonal a két él között
                    CvInvoke.Line(img, pt1, pt2, new MCvScalar(0, 255, 255), 1);

                    // Felezőpont (Center) piros szálkeresztje
                    CvInvoke.Line(img, new Point(ptCenter.X - 6, ptCenter.Y), new Point(ptCenter.X + 6, ptCenter.Y), new MCvScalar(0, 0, 255), 2);
                    CvInvoke.Line(img, new Point(ptCenter.X, ptCenter.Y - 6), new Point(ptCenter.X, ptCenter.Y + 6), new MCvScalar(0, 0, 255), 2);
                }
                else
                {
                    Point pt = Point.Round(edge.Position);
                    if (isHorizontal)
                        CvInvoke.Line(img, new Point(pt.X, box.Y), new Point(pt.X, box.Bottom), lineColor, thickness);
                    else
                        CvInvoke.Line(img, new Point(box.X, pt.Y), new Point(box.Right, pt.Y), lineColor, thickness);

                    CvInvoke.Line(img, new Point(pt.X - 5, pt.Y), new Point(pt.X + 5, pt.Y), new MCvScalar(0, 0, 255), 2);
                    CvInvoke.Line(img, new Point(pt.X, pt.Y - 5), new Point(pt.X, pt.Y + 5), new MCvScalar(0, 0, 255), 2);
                }
            }
        }

    }
}