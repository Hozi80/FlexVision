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
    [XmlInclude(typeof(EdgeBasedModelInfo360))]
    public class EdgeBasedMatchModule : BaseInspectionModule
    {
        [Browsable(false)]
        public override string PureName => "Edge-Based Matching";

        [Browsable(false)]
        public override VisionModuleType ModuleType => VisionModuleType.BlobDetect;

        // --- PERZISZTENS MOTOR MEZŐK (Kivétel az XML mentésből) ---
        [XmlIgnore, Browsable(false)] public EdgeBasedTemplate _edgeEngine = null;
        [XmlIgnore, Browsable(false)] public EdgeBasedModelInfo360 Scaled { get; set; } = new EdgeBasedModelInfo360();
        [XmlIgnore, Browsable(false)] public EdgeBasedModelInfo360 Full { get; set; } = new EdgeBasedModelInfo360();

        // ====================================================================
        // --- 1. JAVÍTÁS: A TELJES OPENCL ADATSTRUKTÚRA BEÁGYAZÁSA A VREC-BE ---
        // ====================================================================
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [XmlElement("SerializedScaledModel")]
        public string SerializedScaledModel
        {
            get => ConvertModelToString(this.Scaled);
            set => this.Scaled = ConvertStringToModel(value);
        }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [XmlElement("SerializedFullModel")]
        public string SerializedFullModel
        {
            get => ConvertModelToString(this.Full);
            set => this.Full = ConvertStringToModel(value);
        }

        // ====================================================================
        // --- RECEPTBEN MENTHETŐ IPARI PARAMÉTEREK (PROPERTYGRID + MENTÉS) ---
        // ====================================================================
        [Category("1. Keresési Beállítások")]
        public Rectangle ROI { get; set; } = new Rectangle(50, 50, 300, 200);

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
        // --- JAVÍTÁS: DINAMIKUS ELŐNÉZETI KÉP GENERÁLÓ AZ UNMANAGED ADATOKBÓL ---
        // ====================================================================
        [XmlIgnore, Browsable(false)]
        public Bitmap GeneratedTemplatePreview
        {
            get
            {
                // 1. Guardrail védelem: Ha még nincs betanított sablon, az alapértelmezett első képet adjuk vissza
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
                    // Ha a belső struktúra vagy a recept üres, visszaadjuk a sima nyers képet
                    if (this.Scaled == null || this.Scaled.cordinatesarrayX360 == null || this.Scaled.cordinatesarrayX360.Length == 0)
                    {
                        return this.Templates[this.ActiveTemplateIndex];
                    }

                    // 2. Kérünk egy mintát a te 0 fokos alapmodelltömbödből (Shiju rács)
                    int stepCount = this.Scaled.step360[0];
                    int basePosition = this.Scaled.positions[0];

                    if (stepCount <= 0) return this.Templates[this.ActiveTemplateIndex];

                    // Kiszámoljuk a mintakép szélességét és magasságát
                    int w = this.Templates[this.ActiveTemplateIndex].Width;
                    int h = this.Templates[this.ActiveTemplateIndex].Height;

                    // Létrehozunk egy tiszta fekete unmanaged Emgu CV képet a kontúr overlay-hez
                    using (Image<Bgr, byte> canvas = new Image<Bgr, byte>(w, h, new Bgr(15, 15, 15)))
                    {
                        int offsetX = w / 2;
                        int offsetY = h / 2;

                        // 3. Végigmegyünk a pontokon és egy tiszta neon-sárga/zöld kontúrt rajzolunk
                        for (int a = 0; a < stepCount; a++)
                        {
                            // Shiju-féle lokális koordináták visszaszámítása a kép középpontjához képest:
                            // cordinatesarrayY360 = Oszlop eltolás (vízszintes/X a képernyőn)
                            // cordinatesarrayX360 = Sor eltolás (függőleges/Y a képernyőn)
                            int ptX = this.Scaled.cordinatesarrayY360[basePosition + a] + offsetX;
                            int ptY = this.Scaled.cordinatesarrayX360[basePosition + a] + offsetY;

                            if (ptX >= 0 && ptX < w && ptY >= 0 && ptY < h)
                            {
                                // Apró, tűpontos pontok felrajzolása élsimítással
                                CvInvoke.Circle(canvas, new Point(ptX, ptY), 1, new Emgu.CV.Structure.MCvScalar(10, 240, 10), -1);
                            }
                        }

                        // Visszaadjuk a memóriából kinyert tiszta kontúrképet a PropertyGrid-nek vagy a kis PictureBox-nak
                        return canvas.ToBitmap();
                    }
                }
                catch
                {
                    // Ha bármi hiba történik (pl. inicializálatlan unmanaged pointer), biztonsági fallback a nyers kép
                    return this.Templates[this.ActiveTemplateIndex];
                }
            }
        }


        [XmlIgnore, Browsable(false)] public int ActiveTemplateIndex { get; set; } = 0;
        [XmlIgnore, Browsable(false)] public string LastExecutionTime { get; set; } = "0 ms";
        [XmlIgnore, Browsable(false)] public string LastTestResult { get; set; } = "WAITING...";
        [XmlIgnore, Browsable(false)] public int LastSuccessfulTemplateIndex { get; set; } = -1;
        [XmlIgnore, Browsable(false)] public List<EdgeBasedShapeMatchingResult> LastMatches { get; set; } = new List<EdgeBasedShapeMatchingResult>();

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
            if (_edgeEngine == null)
            {
                _edgeEngine = new EdgeBasedTemplate(HardwareAccelerator.OpenClContext);
            }

            // ====================================================================
            // --- GYÁRI INTEGRÁCIÓS JAVÍTÁS: RECEPT AUTOMATA RE-INICIALIZÁCIÓS RETESZ ---
            // ====================================================================
            if (this.Scaled != null && this.Scaled.edgeMagnitude360 != null && this.Scaled.edgeMagnitude360.Length > 0)
            {
                // Ha az OpenCL pufferek még nincsenek feltöltve a videókártyára, áttöltjük az XML receptből
                if (_edgeEngine.Scaled.edgeMagnitude360 == null || _edgeEngine.Scaled.edgeMagnitude360.Length == 0)
                {
                    _edgeEngine.Scaled = this.Scaled;
                    _edgeEngine.Full = this.Full;
                    _edgeEngine.CopyTemplateDataToGPU(_edgeEngine.Scaled); // Feltöltjük a videókártyát
                }

                // --- GOLYÓÁLLÓ KAPOCS: Ha a Models[] tömb null (mert most töltöttük be a receptet fájlból),
                // meghívjuk a te gyári TrainTemplate függvényedet a receptben tárolt képpel,
                // így a motor belső unmanaged változói bitre pontosan felépülnek az éles mérés előtt! ---
                if (_edgeEngine.Models == null && this.Templates != null && this.Templates.Count > 0)
                {
                    Console.WriteLine($"[FlexVision] {this.Name} -> Belső Models[] struktúra újjáépítése a gyári TrainTemplate-tel...");

                    // Szinkronizáljuk a pult paramétereit a motorral a biztonságos inicializáláshoz
                    _edgeEngine.lowThreshold = this.LowThreshold;
                    _edgeEngine.highThreshold = this.HighThreshold;
                    _edgeEngine.minScore = this.MinScore;
                    _edgeEngine.minEdgepoints = this.MinEdgePoints;
                    _edgeEngine.AngleDetectionRangeSimmetric = this.AngleDetectionRangeSimmetric;
                    _edgeEngine.Accuracy = this.AccuracyLevel;

                    using (Image<Gray, float> tempGray = new Image<Gray, float>(this.Templates[0]))
                    {
                        // A te saját gyári rutinod: legenerálja a 360 fokos listákat a RAM-ban,
                        // így a blankingRadius számítások megkapják a szükséges méret-adatokat!
                        _edgeEngine.TrainTemplate(tempGray);
                    }
                }
            }
            else
            {
                stopwatch.Stop();
                this.LastTestResult = "⚠️ NO MODEL TRAINED";
                this.TestResult = this.LastTestResult;
                this.Status = ModuleStatus.Warning;
                return;
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

                using (Image<Gray, float> sourceGray = targetInputColor.Convert<Gray, float>())
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();

                    sourceGray.Save(@"C:\Test\source.png");
                    // ÉLES OPENCL ALAPÚ MINTAKERESÉS FUTTATÁSA
                    List<EdgeBasedShapeMatchingResult> finalResults = _edgeEngine.FindModel(sourceGray);

                    watch.Stop();

                    this.LastExecutionTime = watch.ElapsedMilliseconds + " ms";
                    this.ExecutionTime = this.LastExecutionTime;
                    this.TargetFoundCount = finalResults != null ? finalResults.Count : 0;
                    this.LastMatches = finalResults ?? new List<EdgeBasedShapeMatchingResult>();

                    // --- SUB-PIPELINE KONTEXTUSOK DINAMIKUS GENERÁLÁSA (FIXTURE LOOP) ---
                    if (finalResults != null && finalResults.Count > 0)
                    {
                        if (context.SubContexts == null) context.SubContexts = new List<InspectionContext>();
                        context.SubContexts.Clear();

                        Image<Bgr, byte> overlayOutput = targetInputColor.Copy();

                        for (int t = 0; t < finalResults.Count; t++)
                        {
                            EdgeBasedShapeMatchingResult currentMatch = finalResults[t];

                            _edgeEngine.ImageWithResult(overlayOutput, currentMatch);
                            // Pont rögzítése a központi Feature Store-ba (Tengely-korrigált Shiju rács szerint)
                            string targetKey = $"{this.EffectiveFeatureName}_Target_{t + 1}";
                            context.Features[targetKey] = new PointFeature(targetKey, this.Name, new PointF((float)currentMatch.Y, (float)currentMatch.X));

                            // Téglalap rögzítése a követő ablakokhoz
                            string targetRotKey = $"{this.EffectiveFeatureName}_Target_{t + 1}.EdgeBased";
                            int tWidth = Templates.Count > 0 ? Templates[0].Width : 100;
                            int tHeight = Templates.Count > 0 ? Templates[0].Height : 100;

                            context.Features[targetRotKey] = new RectangleFeature(targetRotKey, this.Name, new Rectangle((int)currentMatch.Y, (int)currentMatch.X, tWidth, tHeight), currentMatch.Angle);

                            // Al-kép ROI ablak kiszámítása és kivágása
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

                            // Al-pipeline történelem beégetése a rákövetkező belső moduloknak
                            subContext.ColorHistory[this.Name] = subContext.SourceImage.Copy();
                            subContext.GrayHistory[this.Name] = subContext.WorkingMask.Copy();

                            context.SubContexts.Add(subContext);
                        }

                        // GRAFIKUS OVERLAY KIRAJZOLÁSA A SAJÁT ENGINE METÓDUSODDAL
                        
//                        _edgeEngine.ImageWithResult_xyChange(overlayOutput); // Neon zöld kontúrkereszt beégetése
                         // Neon zöld kontúrkereszt beégetése

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
        // ====================================================================
        // --- INTERN SEGÉDFÜGGVÉNYEK: ADATTÖMBÖK TEXT STRINGGÉ ALAKÍTÁSA ---
        // ====================================================================
        private string ConvertModelToString(EdgeBasedModelInfo360 model)
        {
            if (model == null || model.edgeMagnitude360 == null) return "";
            using (StringWriter sw = new StringWriter())
            {
                sw.WriteLine(model.noOfCordinates360);
                sw.WriteLine(string.Join(",", model.cordinatesarrayX360));
                sw.WriteLine(string.Join(",", model.cordinatesarrayY360));
                sw.WriteLine(string.Join(",", model.edgeMagnitude360));
                sw.WriteLine(string.Join(",", model.edgeDerivativeX360));
                sw.WriteLine(string.Join(",", model.edgeDerivativeY360));
                sw.WriteLine(string.Join(",", model.step360));
                sw.WriteLine(string.Join(",", model.positions));
                return sw.ToString();
            }
        }
        private EdgeBasedModelInfo360 ConvertStringToModel(string src)
        {
            if (string.IsNullOrEmpty(src)) return new EdgeBasedModelInfo360();
            try
            {
                using (StringReader sr = new StringReader(src))
                {
                    EdgeBasedModelInfo360 m = new EdgeBasedModelInfo360();
                    m.noOfCordinates360 = int.Parse(sr.ReadLine());
                    m.cordinatesarrayX360 = Array.ConvertAll(sr.ReadLine().Split(','), int.Parse);
                    m.cordinatesarrayY360 = Array.ConvertAll(sr.ReadLine().Split(','), int.Parse);
                    m.edgeMagnitude360 = Array.ConvertAll(sr.ReadLine().Split(','), float.Parse);
                    m.edgeDerivativeX360 = Array.ConvertAll(sr.ReadLine().Split(','), float.Parse);
                    m.edgeDerivativeY360 = Array.ConvertAll(sr.ReadLine().Split(','), float.Parse);
                    m.step360 = Array.ConvertAll(sr.ReadLine().Split(','), int.Parse);
                    m.positions = Array.ConvertAll(sr.ReadLine().Split(','), int.Parse);
                    m.normMinScore360 = new float[360]; m.normGreediness360 = new float[360];
                    for (int i = 0; i < 360; i++)
                    {
                        m.normMinScore360[i] = this.MinScore;
                        m.normGreediness360[i] = (float)((1 - 0.8f * this.MinScore) / (1 - 0.8f));
                    }
                    return m;
                }
            }
            catch { return new EdgeBasedModelInfo360(); }
        }
        private Bitmap CropImage(Bitmap s, Rectangle sec)
        {
            try { Bitmap b = new Bitmap(sec.Width, sec.Height); using (Graphics g = Graphics.FromImage(b)) lock (s) g.DrawImage(s, 0, 0, sec, GraphicsUnit.Pixel); return b; }
            catch { return null; }
        }
    }
}