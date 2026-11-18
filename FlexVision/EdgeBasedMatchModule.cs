using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.Structure;
using AOI.Inspections.EdgeBasedTemplateDetection; // A te saját, beolvasott névtered!

namespace FlexVision
{
    [Serializable]
    public class EdgeBasedMatchModule : BaseInspectionModule
    {
        [Browsable(false)]
        public override string PureName => "Edge-Based Matching";

        [Browsable(false)]
        public override VisionModuleType ModuleType => VisionModuleType.BlobDetect;

        // --- PERZISZTENS MOTOR MEZŐK (Kivétel az XML mentésből - Tiszta RAM és GPU gyorsítótár) ---
        [XmlIgnore, Browsable(false)] public EdgeBasedTemplate _edgeEngine = null;
        [XmlIgnore, Browsable(false)] public EdgeBasedModelInfo360 Scaled { get; set; } = new EdgeBasedModelInfo360();
        [XmlIgnore, Browsable(false)] public EdgeBasedModelInfo360 Full { get; set; } = new EdgeBasedModelInfo360();

        // ====================================================================
        // --- RECEPTBEN MENTHETŐ IPARI PARAMÉTEREK (PROPERTYGRID + MENTÉS) ---
        // ====================================================================
        [Category("1. Keresési Beállítások")]
        [Description("Ha igaz, a keresés csak a kijelölt ROI területen belül fut le.")]
        public bool UseROI { get; set; } = false;

        [Category("1. Keresési Beállítások")]
        public Rectangle ROI { get; set; } = Rectangle.Empty;

        [Category("2. Éldetektálási Küszöbök")]
        public float LowThreshold { get; set; } = 130f;

        [Category("2. Éldetektálási Küszöbök")]
        public float HighThreshold { get; set; } = 255f;

        [Category("3. Algoritmus Finomhangolás")]
        public float MinScore { get; set; } = 0.75f;

        [Category("3. Algoritmus Finomhangolás")]
        public int MinEdgePoints { get; set; } = 50;

        [Category("3. Algoritmus Finomhangolás")]
        public int AngleDetectionRangeSimmetric { get; set; } = 180;

        [Category("3. Algoritmus Finomhangolás")]
        public int AccuracyLevel { get; set; } = 3;

        [Category("Minőségellenőrzés")]
        public int ExpectedObjectCount { get; set; } = 1;

        // --- KIMENETI DIAGNOSZTIKAI RIAPORTOK (READ-ONLY) ---
        [Category("4. Mérési Eredmények"), ReadOnly(true)] public string ExecutionTime { get; set; } = "0 ms";
        [Category("4. Mérési Eredmények"), ReadOnly(true)] public int TargetFoundCount { get; set; } = 0;
        [Category("4. Mérési Eredmények"), ReadOnly(true)] public string TestResult { get; set; } = "WAITING FOR TEST...";
        [Category("Diagnosztika - Hardver"), ReadOnly(true)] public string AccelerationEngine => HardwareAccelerator.IsOpenClAvailable ? $"OpenCL GPU ({HardwareAccelerator.HardwareName})" : "CPU Fallback";

        // --- BASE64 MULTI-TEMPLATE KÉPMENTŐ HÍD ---
        [XmlIgnore, Browsable(false)] public List<Bitmap> Templates { get; set; } = new List<Bitmap>();

        // ====================================================================
        // --- JAVÍTÁS: DINAMIKUS ELŐNÉZETI KÉP GENERÁLÓ (BIZTONSÁGOS LUSTA-ALLOKÁCIÓVAL) ---
        // ====================================================================
        [XmlIgnore, Browsable(false)]
        public Bitmap GeneratedTemplatePreview
        {
            get
            {
                if (this.Templates == null || this.Templates.Count == 0)
                {
                    Bitmap emptyBmp = new Bitmap(120, 100);
                    using (Graphics g = Graphics.FromImage(emptyBmp))
                    {
                        g.Clear(Color.FromArgb(15, 15, 15)); // Üres sötét ipari háttér
                    }
                    return emptyBmp;
                }

                try
                {
                    // Ha a receptből való friss betöltés miatt még nincsenek kinyerve az 1D koordináták,
                    // ideiglenesen visszadobjuk a tiszta nyers képet a PropertyGrid-nek az összeomlás helyett
                    if (this.Scaled == null || this.Scaled.cordinatesarrayX360 == null || this.Scaled.cordinatesarrayX360.Length == 0)
                    {
                        return this.Templates[this.ActiveTemplateIndex];
                    }

                    int stepCount = this.Scaled.step360[0];
                    int basePosition = this.Scaled.positions[0];

                    if (stepCount <= 0) return this.Templates[this.ActiveTemplateIndex];

                    int w = this.Templates[this.ActiveTemplateIndex].Width;
                    int h = this.Templates[this.ActiveTemplateIndex].Height;

                    using (Image<Bgr, byte> canvas = new Image<Bgr, byte>(w, h, new Bgr(15, 15, 15)))
                    {
                        int offsetX = w / 2;
                        int offsetY = h / 2;

                        for (int a = 0; a < stepCount; a++)
                        {
                            int ptX = this.Scaled.cordinatesarrayY360[basePosition + a] + offsetX;
                            int ptY = this.Scaled.cordinatesarrayX360[basePosition + a] + offsetY;

                            if (ptX >= 0 && ptX < w && ptY >= 0 && ptY < h)
                            {
                                CvInvoke.Circle(canvas, new Point(ptX, ptY), 1, new Emgu.CV.Structure.MCvScalar(10, 240, 10), -1);
                            }
                        }
                        return canvas.ToBitmap();
                    }
                }
                catch
                {
                    return this.Templates[this.ActiveTemplateIndex];
                }
            }
        }

        [XmlIgnore, Browsable(false)] public int ActiveTemplateIndex { get; set; } = 0;
        [XmlIgnore, Browsable(false)] public string LastExecutionTime { get; set; } = "0 ms";
        [XmlIgnore, Browsable(false)] public string LastTestResult { get; set; } = "WAITING...";
        [XmlIgnore, Browsable(false)] public int LastSuccessfulTemplateIndex { get; set; } = -1;
        [XmlIgnore, Browsable(false)] public List<EdgeBasedShapeMatchingResult> LastMatches { get; set; } = new List<EdgeBasedShapeMatchingResult>();

        // Pehelykönnyű Base64 híd az XML sorosító számára (Csak a tiszta mester-képeket menti el)
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [XmlArray("SerializedEdgeTemplates")]
        public string[] SerializedEdgeTemplates
        {
            get
            {
                if (Templates == null || Templates.Count == 0) return new string[0];
                List<string> base64Images = new List<string>();
                foreach (var bmp in Templates)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        base64Images.Add(Convert.ToBase64String(ms.ToArray()));
                    }
                }
                return base64Images.ToArray();
            }
            set
            {
                Templates.Clear(); if (value == null) return;
                foreach (var base64 in value)
                {
                    if (string.IsNullOrEmpty(base64)) continue;
                    byte[] bytes = Convert.FromBase64String(base64);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        using (Bitmap rawBmp = new Bitmap(ms))
                        {
                            Bitmap purifiedBmp = new Bitmap(rawBmp.Width, rawBmp.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                            using (Graphics g = Graphics.FromImage(purifiedBmp))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                                g.DrawImage(rawBmp, 0, 0, rawBmp.Width, rawBmp.Height);
                            }
                            Templates.Add(purifiedBmp);
                        }
                    }
                }
            }
        }

        public EdgeBasedMatchModule() { }

        // ====================================================================
        // --- PROCESS AUTOMATA KÉPFELDOLGOZÓ CIKLUS ---
        // ====================================================================
        public override void Process(InspectionContext context)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            this.Status = ModuleStatus.NotExecuted;

            // 1. Biztonságos képbeolvasás a pipeline láncolatból
            Image<Bgr, byte> targetInputColor = context.GetInputColorImage(this.InputStepLink);
            if (targetInputColor == null) { stopwatch.Stop(); this.Status = ModuleStatus.Failed; return; }

            // 2. ENGINE EGYSZERI BINDELÉSE (LAZY LOADING)
            // 2. ENGINE EGYSZERI BINDELÉSE (LAZY LOADING)
            if (_edgeEngine == null)
            {
                _edgeEngine = new EdgeBasedTemplate(HardwareAccelerator.OpenClContext);
            }

            // ====================================================================
            // --- JAVÍTÁS: GYÁRI CSŐVEZETÉKES AUTOMATA GPU ÉLESÍTŐ RETESZ ---
            // ====================================================================
            if (this.Templates != null && this.Templates.Count > 0)
            {
                // Ha a recept betöltése után a belső unmanaged motor vagy az OpenCL pufferek üresek, 
                // az első képfeldolgozási körben azonnal újra-tanítjuk a GPU-t a mesterképből!
                if (_edgeEngine.Models == null || _edgeEngine.Scaled.edgeMagnitude360 == null)
                {
                    Console.WriteLine($"[FlexVision] {this.Name} -> Recept betöltve. OpenCL GPU struktúrák automata újraépítése a mesterképből...");

                    _edgeEngine.lowThreshold = this.LowThreshold;
                    _edgeEngine.highThreshold = this.HighThreshold;
                    _edgeEngine.minScore = this.MinScore;
                    _edgeEngine.minEdgepoints = this.MinEdgePoints;
                    _edgeEngine.AngleDetectionRangeSimmetric = this.AngleDetectionRangeSimmetric;
                    _edgeEngine.Accuracy = this.AccuracyLevel;
                    
                    using (Image<Gray, float> tempGray = new Image<Gray, float>(this.Templates[this.ActiveTemplateIndex]))
                    {
             //           tempGray.Save(@"c:\Test\BondTemplate.png");
                        _edgeEngine.TrainTemplate(tempGray);
                    }

                    // Elmentjük az aktuális OpenCL leírókat a modul belső RAM mezőibe is
                    this.Scaled = _edgeEngine.Scaled;
                    this.Full = _edgeEngine.Full;
                }
            }
            else
            {
                // ====================================================================
                // --- IPARI MENEKÜLÉSI KAPU: HA A MODUL MÉG SZŰZ (NINCS BETANÍTVA) ---
                // ====================================================================
                // Ha a listában még nincs kép, nem engedjük rá a FindModel-re,
                // így megvédjük a szoftvert a NullReferenceException összeomlástól!
                stopwatch.Stop();
                this.LastExecutionTime = "0 ms";
                this.TargetFoundCount = 0;
                this.LastTestResult = "⚠️ WAITING FOR TEACH...";
                this.TestResult = this.LastTestResult;
                this.Status = ModuleStatus.Warning; // Sárga figyelmeztető státusz a listában
                return; // !!! KRITIKUS JAVÍTÁS: BIZTONSÁGOS KILÉPÉS, NEM ENGEDJÜK LEJJEBB A KÓDOT !!!
            }

            // Végső hardveres retesz: ha valami unmanaged hiba miatt mégis null maradt a Models
            if (_edgeEngine.Models == null)
            {
                stopwatch.Stop();
                this.LastTestResult = "⚠️ NO MODEL TRAINED";
                this.TestResult = this.LastTestResult;
                this.Status = ModuleStatus.Warning;
                return; // !!! BIZTONSÁGOS KILÉPÉS RETESZ !!!
            }

            try
            {
                // Élő paraméter szinkronizáció futásidőben a mérés előtt
                _edgeEngine.lowThreshold = this.LowThreshold;
                _edgeEngine.highThreshold = this.HighThreshold;
                _edgeEngine.minScore = this.MinScore;
                _edgeEngine.minEdgepoints = this.MinEdgePoints;
                _edgeEngine.AngleDetectionRangeSimmetric = this.AngleDetectionRangeSimmetric;
                _edgeEngine.ObjectCount = this.ExpectedObjectCount;
                _edgeEngine.Accuracy = this.AccuracyLevel;
                using (Image<Gray, float> fullSourceGray = targetInputColor.Convert<Gray, float>())
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();

                    // --- 2 PIXELES KERETVÉDELEM A KÉPSZÉLI FANTOM ÉLEK ELLEN ---
                    int borderMargin = 2;
                    Rectangle safeBounds = new Rectangle(
                        borderMargin,
                        borderMargin,
                        Math.Max(10, fullSourceGray.Width - (borderMargin * 2)),
                        Math.Max(10, fullSourceGray.Height - (borderMargin * 2))
                    );

                    Rectangle activeSearchArea = safeBounds;
                    bool isRoiActive = this.UseROI && this.ROI != Rectangle.Empty && this.ROI.Width > 10 && this.ROI.Height > 10;

                    if (isRoiActive)
                    {
                        // Ha a felhasználó rajzolt ROI-t, azt is a biztonságos belső kereten belül tartjuk
                        activeSearchArea = Rectangle.Intersect(safeBounds, this.ROI);
                    }

                    // Kivágjuk a tiszta keresési területet (akár kézi ROI, akár a 2px-lel szűkített teljes kép)
                    fullSourceGray.ROI = activeSearchArea;
                    Image<Gray, float> searchImage = fullSourceGray.Copy();
                    fullSourceGray.ROI = Rectangle.Empty;

                    List<EdgeBasedShapeMatchingResult> finalResults = null;
                    try
                    {
                        finalResults = _edgeEngine.FindModel(searchImage);
                    }
                    finally
                    {
                        searchImage?.Dispose();
                    }

                    // Mivel a 2 pixeles keretlevágás miatt a kép origója mindig eltolódik,
                    // a koordináta-visszatolást MINDIG lefuttatjuk!
                    if (finalResults != null)
                    {
                        for (int m = 0; m < finalResults.Count; m++)
                        {
                            var match = finalResults[m];
                            match.X += activeSearchArea.Y; // Sor (Y) eltolás
                            match.Y += activeSearchArea.X; // Oszlop (X) eltolás
                            finalResults[m] = match;
                        }
                    }

                    watch.Stop();
                    // ... (innen folytatódik a kódod változatlanul) ...


                    this.LastExecutionTime = watch.ElapsedMilliseconds + " ms";
                    this.ExecutionTime = this.LastExecutionTime;
                    this.TargetFoundCount = finalResults != null ? finalResults.Count : 0;
                    this.LastMatches = finalResults ?? new List<EdgeBasedShapeMatchingResult>();
                    this.LastExecutionTime = watch.ElapsedMilliseconds + " ms";
                    this.ExecutionTime = this.LastExecutionTime;
                    this.TargetFoundCount = finalResults != null ? finalResults.Count : 0;
                    this.LastMatches = finalResults ?? new List<EdgeBasedShapeMatchingResult>();

                    // --- ÚJ: Eredmények elmentése az aktuális sub-context saját tárhelyébe ---
                    context.CustomData[this.Name + "_Matches"] = new List<EdgeBasedShapeMatchingResult>(this.LastMatches);
                    context.CustomData[this.Name + "_Time"] = this.LastExecutionTime;


                    // --- SUB-PIPELINE KONTEXTUSOK DINAMIKUS GENERÁLÁSA (CSAK HA NEM VAGYUNK MÁR EGY HUROKBAN!) ---
                    if (finalResults != null && finalResults.Count > 0)
                    {
                        // Kiválasztjuk az Input ComboBox szerinti képet háttérnek
                        Image<Bgr, byte> processedInputImage = context.GetInputColorImage(this.InputStepLink);
                        Image<Bgr, byte> overlayOutput = (processedInputImage != null && processedInputImage.Ptr != IntPtr.Zero)
                            ? processedInputImage.Copy()
                            : targetInputColor.Copy();

                        for (int t = 0; t < finalResults.Count; t++)
                        {
                            EdgeBasedShapeMatchingResult currentMatch = finalResults[t];
                            _edgeEngine.ImageWithResult(overlayOutput, currentMatch);

                            // Pont és téglalap rögzítése a Feature Store-ba
                            string targetKey = $"{this.EffectiveFeatureName}Target{t + 1}";
                            PointF matchCenter = new PointF((float)currentMatch.Y, (float)currentMatch.X);
                            context.Features[targetKey] = new PointFeature(targetKey, this.Name, matchCenter);

                            string targetRotKey = $"{this.EffectiveFeatureName}Target{t + 1}.EdgeBased";
                            
                            int tWidth = Templates.Count > 0 ? Templates[0].Width : 100;
                            int tHeight = Templates.Count > 0 ? Templates[0].Height : 100;
                            context.Features[targetRotKey] = new RectangleFeature(targetRotKey, this.Name, new Rectangle((int)matchCenter.X, (int)matchCenter.Y, tWidth, tHeight), currentMatch.Angle);

                            // --- ÚJ: ELFORGATOTT TÉGLALAP (ROTATEDRECT) 4 OLDALÁNAK BONTÁSA LINEFEATURE-RE ---

                            SizeF TemplateSize = _edgeEngine.GetTemplateSize();
                            // --- ÚJ: ELFORGATOTT TÉGLALAP 4 OLDALÁNAK FIX GEOMETRIAI RENDEZÉSE ---
                            RotatedRect rRect = new RotatedRect(matchCenter, TemplateSize, (float)currentMatch.Angle);
                            PointF[] corners = CvInvoke.BoxPoints(rRect);

                            // Összeállítjuk a 4 szakaszt a felezőpontjukkal együtt
                            var rawEdges = new List<(PointF p1, PointF p2, PointF mid)>();
                            for (int c = 0; c < 4; c++)
                            {
                                PointF p1 = corners[c];
                                PointF p2 = corners[(c + 1) % 4];
                                PointF mid = new PointF((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f);
                                rawEdges.Add((p1, p2, mid));
                            }

                            // Kiszámoljuk mind a 4 él irányát a téglalap középpontjához képest (-180° .. +180°)
                            // -90° = Fent (Top), 0° = Jobbra (Right), +90° = Lent (Bottom), ±180° = Balra (Left)
                            var topEdge = rawEdges[0];
                            var rightEdge = rawEdges[0];
                            var bottomEdge = rawEdges[0];
                            var leftEdge = rawEdges[0];

                            foreach (var edge in rawEdges)
                            {
                                if (edge.mid.Y < topEdge.mid.Y) topEdge = edge;    // Legkisebb Y = Felső él
                                if (edge.mid.X > rightEdge.mid.X) rightEdge = edge;  // Legnagyobb X = Jobb él
                                if (edge.mid.Y > bottomEdge.mid.Y) bottomEdge = edge; // Legnagyobb Y = Alsó él
                                if (edge.mid.X < leftEdge.mid.X) leftEdge = edge;   // Legkisebb X = Bal él
                            }

                            context.Features[$"{targetKey}_Edge1_Top"] = new LineFeature($"{targetKey}_Edge1_Top", this.Name, topEdge.p1, topEdge.p2);
                            context.Features[$"{targetKey}_Edge2_Right"] = new LineFeature($"{targetKey}_Edge2_Right", this.Name, rightEdge.p1, rightEdge.p2);
                            context.Features[$"{targetKey}_Edge3_Bottom"] = new LineFeature($"{targetKey}_Edge3_Bottom", this.Name, bottomEdge.p1, bottomEdge.p2);
                            context.Features[$"{targetKey}_Edge4_Left"] = new LineFeature($"{targetKey}_Edge4_Left", this.Name, leftEdge.p1, leftEdge.p2);

                        }

                        // JAVÍTÁS: Csak akkor nyitunk új al-hurkot (SubContexts), ha a FŐÁGON vagyunk (context.Parent == null)!
                        // Ha már egy TemplateMatch al-képén belül futunk, NEM nyitunk új hurkot, hogy a következő Color Filter 
                        // ugyanebben az al-képben tudjon dolgozni!
                        if (context.Parent == null)
                        {
                            if (context.SubContexts == null) context.SubContexts = new List<InspectionContext>();
                            context.SubContexts.Clear();

                            for (int t = 0; t < finalResults.Count; t++)
                            {
                                EdgeBasedShapeMatchingResult currentMatch = finalResults[t];
                                int tWidth = Templates.Count > 0 ? Templates[0].Width : 100;
                                int tHeight = Templates.Count > 0 ? Templates[0].Height : 100;

                                Rectangle subRoi = new Rectangle((int)(currentMatch.Y - (tWidth / 2)), (int)(currentMatch.X - (tHeight / 2)), tWidth, tHeight);
                                if (subRoi.X < 0) subRoi.X = 0;
                                if (subRoi.Y < 0) subRoi.Y = 0;
                                if (subRoi.X + subRoi.Width > targetInputColor.Width) subRoi.Width = targetInputColor.Width - subRoi.X;
                                if (subRoi.Y + subRoi.Height > targetInputColor.Height) subRoi.Height = targetInputColor.Height - subRoi.Y;

                                InspectionContext subContext = new InspectionContext();
                                using (Bitmap globalBmp = targetInputColor.ToBitmap())
                                {
                                    Bitmap subBmp = CropImage(globalBmp, subRoi);
                                    subContext.SourceImage = new Image<Bgr, byte>(subBmp);
                                    subContext.WorkingMask = subContext.SourceImage.Convert<Gray, byte>();
                                    subBmp.Dispose();
                                }
                                subContext.LocalOffset = new PointF((float)currentMatch.Y, (float)currentMatch.X);
                                subContext.LocalAngle = currentMatch.Angle;

                                subContext.ColorHistory[this.Name] = subContext.SourceImage.Copy();
                                subContext.GrayHistory[this.Name] = subContext.WorkingMask.Copy();
                                context.SubContexts.Add(subContext);
                            }
                        }

                        // Kimentjük a kész, ráégetett találati kontúros képet a kontextusba
                        context.SourceImage?.Dispose();
                        context.SourceImage = overlayOutput;
                    }

                    // AUTOMATA MINŐSÉGI RETESZELÉS (SELEJT KEZELÉS)
                    this.LastTestResult = (this.TargetFoundCount == this.ExpectedObjectCount) ? "✅ PASS" : "❌ FAIL";
                    this.TestResult = this.LastTestResult;
                    this.Status = (this.TargetFoundCount == this.ExpectedObjectCount) ? ModuleStatus.Success : ModuleStatus.Failed;
                }
            }
            catch (Exception ex)
            {
                this.LastTestResult = "💥 ERROR";
                this.TestResult = this.LastTestResult;
                this.Status = ModuleStatus.Failed;
                Console.WriteLine("Process failure: " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                this.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
            }
        }
        private Bitmap CropImage(Bitmap s, Rectangle sec)
        {
            try { Bitmap b = new Bitmap(sec.Width, sec.Height); using (Graphics g = Graphics.FromImage(b)) lock (s) g.DrawImage(s, 0, 0, sec, GraphicsUnit.Pixel); return b; }
            catch { return null; }
        }
    }
}