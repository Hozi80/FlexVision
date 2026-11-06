using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.Structure;
using RotationTemplateMatch;

namespace FlexVision
{
    // ====================================================================
    // --- INTELLIGENS LEGÖRDÜLŐ MENÜ KONVERTER A FORRÁSVÁLASZTÁSHOZ ---
    // ====================================================================
    public class InputStepNameConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            List<string> stepNames = new List<string>
            {
                "Default (Előző lépés)",
                "01. Source Image Capture",
                "02. Otsu Super Fusion (2R + H + 2L)"
            };
            return new StandardValuesCollection(stepNames);
        }
    }

    // ====================================================================
    // --- MULTI-TEMPLATE SABLONKERESŐ PIPELINE FOLYAMATLÉPÉS MODUL ---
    // ====================================================================
    [Serializable]
    [XmlInclude(typeof(MatchParameters))] // Kötelező az XML recept-mentőnek
    public class TemplateMatchModule : BaseInspectionModule
    {
        // Az interfész által megkövetelt fix tulajdonságok
        // ÍGY MÓDOSÍTSD a TemplateMatchModule.cs-ben:
        [System.ComponentModel.Browsable(false)]
        public override string PureName => "Template Matching";


    //    public string Name => "03. Template Matching (Multi-Sablon)";
        public override VisionModuleType ModuleType => VisionModuleType.BlobDetect;

        // --- 1. BEMENETI BEÁLLÍTÁSOK (RECEPTBEN MENTHETŐ ÉS PARAMÉTEREZHETŐ) ---
        [Category("1. Keresési Beállítások")]
        [Description("Bekapcsolja a forgatás-invariáns (Rotation) keresési módot.")]
        public bool UseRotation { get; set; } = true;

        [Category("1. Keresési Beállítások")]
        [Description("A keresési tartomány (Region of Interest) a főkép koordináta-rendszerében.")]
        public Rectangle ROI { get; set; } = new Rectangle(50, 50, 300, 200);

        // GRÁF-ALAPÚ FORRÁSVÁLASZTÓ KAPCSOLÓ: A jobb oldali Properties rácson és az egyedi panelen legördülő menüként jelenik meg
        [Category("1. Keresési Beállítások")]
        [TypeConverter(typeof(InputStepNameConverter))]
        [Description("Válaszd ki, hogy a sablonkereső melyik korábbi Pipeline lépés képén fusson le.")]
        public string InputStepLink { get; set; } = "Default (Előző lépés)";

        // DINAMIKUS ÉS SZERKESZTHETŐ EGYEDI NÉVKEZELÉS (FEATURE ID COUPLING)
        private string _customFeatureName = "";

        [Category("1. Keresési Beállítások")]
        [Description("A modul által kinyert mérési eredmény egyedi azonosítója a központi adattárban. Ha üresen hagyod, a rendszer automatikusan generálja a lépés nevéből.")]
        public string CustomFeatureName
        {
            get { return _customFeatureName; }
            set { _customFeatureName = value; }
        }

        // Intelligens belső tulajdonság, ami eldönti a végleges kulcsnevet az InspectionContext.Features mentéskor
        [System.Xml.Serialization.XmlIgnore, Browsable(false)]
        public string EffectiveFeatureName
        {
            get
            {
                // Ha a mérnök beírt egy egyedi nevet a rácson (pl. "Felső_Hegesztő_Pad"), szigorúan azt használjuk
                if (!string.IsNullOrEmpty(_customFeatureName)) return _customFeatureName.Trim().Replace(" ", "_");

                // Különben automatikusan legeneráljuk a modul neve alapján, szóközök és írásjelek nélkül
                return this.Name.Replace(" ", "_").Replace(".", "").Replace("TL", "").Replace("(", "").Replace(")", "").Replace("-", "_");
            }
        }

        // A te bevált ipari paraméter-struktúrád
        [Category("2. Keresési Algoritmus Konfiguráció")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        [Description("A sablonkereső matematikai és polaritási finomhangolásai.")]
        public MatchParameters MatchParams { get; set; } = new MatchParameters();


        // --- 2. KIMENETI DIAGNOSZTIKAI MEZŐK (ÉLŐ, READ-ONLY RIAPORTÁLÁS) ---
        [Category("3. Mérési Eredmények"), ReadOnly(true)]
        [Description("A sablonkeresés valós futási ideje ezredmásodpercben.")]
        public string ExecutionTime { get; set; } = "0 ms";

        [Category("3. Mérési Eredmények"), ReadOnly(true)]
        [Description("A talált sablonok száma a kijelölt keresési zónában.")]
        public int TargetFoundCount { get; set; } = 0;

        [Category("3. Mérési Eredmények"), ReadOnly(true)]
        [Description("A mérés végső minősítése a MatchParameters alapján (PASS / FAIL).")]
        public string TestResult { get; set; } = "WAITING FOR TEST...";


        // --- 3. BELSŐ MEMÓRIATÁROLÓK (KIVÉVE AZ XML MENTÉSBŐL) ---
        [XmlIgnore, Browsable(false)]
        public List<Bitmap> Templates { get; set; } = new List<Bitmap>();
        // --- JAVÍTÁS: XML-Menthető Sablon Konverter Híd ---
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [XmlArray("SerializedTemplates"), Category("Belső")]
        public string[] SerializedTemplates
        {
            get
            {
                // Mentéskor a Bitmap képeket Base64-es string füzérré alakítjuk
                if (Templates == null || Templates.Count == 0) return new string[0];
                List<string> base64Images = new List<string>();
                foreach (var bmp in Templates)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); // Veszteségmentes PNG
                        base64Images.Add(Convert.ToBase64String(ms.ToArray()));
                    }
                }
                return base64Images.ToArray();
            }
            set
            {
                Templates.Clear();
                if (value == null) return;
                foreach (var base64 in value)
                {
                    if (string.IsNullOrEmpty(base64)) continue;
                    byte[] bytes = Convert.FromBase64String(base64);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        using (Bitmap rawBmp = new Bitmap(ms))
                        {
                            // --- JAVÍTÁS: KÉNYSZERÍTETT FORMÁTUMÚ RE-RENDERING ---
                            // Létrehozunk egy tiszta, garantáltan 24-bites RGB bitmapet az eredeti méretekkel
                            Bitmap purifiedBmp = new Bitmap(rawBmp.Width, rawBmp.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                            // Rárajzoljuk a betöltött nyers képet interpoláció (pixel-torzítás) NÉLKÜL
                            using (Graphics g = Graphics.FromImage(purifiedBmp))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                g.DrawImage(rawBmp, 0, 0, rawBmp.Width, rawBmp.Height);
                            }

                            // A megtisztított, OpenCV-barát bitmapet mentjük el a memóriába
                            Templates.Add(purifiedBmp);
                        }
                    }
                }
            }
        }




        [XmlIgnore, Browsable(false)]
        public int ActiveTemplateIndex { get; set; } = 0;

        [XmlIgnore, Browsable(false)]
        public string LastExecutionTime { get; set; } = "0 ms";

        [XmlIgnore, Browsable(false)]
        public string LastTestResult { get; set; } = "WAITING...";

        [XmlIgnore, Browsable(false)]
        public int LastSuccessfulTemplateIndex { get; set; } = -1;

        [XmlIgnore, Browsable(false)]
        public List<TargetMatch> LastMatches { get; set; } = new List<TargetMatch>();


        public TemplateMatchModule()
        {
            // Biztosítjuk a paraméterek meglétét indításkor
            if (this.MatchParams == null) this.MatchParams = new MatchParameters();

            // Ha létezik alapértelmezett tesztfájl, betöltjük első sablonnak
            if (System.IO.File.Exists(@"C:\Test\template.bmp"))
            {
                Templates.Add(new Bitmap(@"C:\Test\template.bmp"));
            }
        }


        [Category("Minőségellenőrzés"), Description("A képernyőn elvárt darabszám. Ha a rendszer kevesebb vagy több objektumot talál, a lépés HIBÁRA fut.")]
        public int ExpectedObjectCount
        {
            get { return this.MatchParams.MaxPos; }
            set { this.MatchParams.MaxPos = value; }
        }


        public override void Process(InspectionContext context)
        {
            // 1. Elindítjuk a nagypontosságú hardveres stopperórát a modul diagnosztikájához
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            this.Status = ModuleStatus.NotExecuted;

            // DINAMIKUS FORRÁSVÁLASZTÁS: Lekérjük a beállított korábbi lépés képét!
            Image<Bgr, byte> targetInputColor = context.GetInputColorImage(this.InputStepLink);

            // Biztonsági guardrail: ha nincs forráskép vagy egyetlen sablon sincs betanítva
            if (targetInputColor == null)
            {
                stopwatch.Stop();
                this.Status = ModuleStatus.Failed;
                return;
            }

            if (Templates.Count == 0)
            {
                stopwatch.Stop();
                this.LastTestResult = "⚠️ NO TEMPLATE";
                this.TestResult = "⚠️ NO TEMPLATE";
                this.Status = ModuleStatus.Warning;
                return;
            }

            try
            {
                // 2. Példányosítjuk a forgatási-számító motort
                RotationInvariantTemplateMatch MatchEngine = new RotationInvariantTemplateMatch();

                // Összekötjük az elvárt darabszámot a belső MaxPos keresési limittel (Pl. 6)
                MatchEngine.SetOptions(this.MatchParams, this.UseRotation);

                // Átváltunk tiszta Bitmap térbe a gyári logikához
                using (Bitmap sourceBmp = targetInputColor.ToBitmap())
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    List<TargetMatch> finalResults = null;
                    int successfulIdx = -1;

                    // 3. MULTI-TEMPLATE CASCADE FONTOSSÁGI SORRENDBEN (FOREACH / FOR LOOP CASCADE)
                    for (int i = 0; i < Templates.Count; i++)
                    {
                        // A keresést a választott képen futtatjuk le!
                        List<TargetMatch> currentResults = MatchEngine.MatchTemplate(Templates[i], sourceBmp);

                        // Ellenőrizzük a gyári szabályrendszert a Score és Polaritás szűrésekkel
                        if (currentResults.Count > 0)
                        {
                            finalResults = currentResults;
                            successfulIdx = i;
                            break; // Megvan az első sikeresen illeszkedő sablon, leállítjuk a cascade-ot!
                        }

                        // Biztonsági fék: ha egyetlen tökéletes sincs, az első sablon adatait mentjük el részeredménynek
                        if (i == 0) finalResults = currentResults;
                    }
                    watch.Stop();

                    // 4. BELSŐ ADATOK ÉS ÉLŐ KIJELZŐK FRISSÍTÉSE A PROPERTYGRID / CONFIG CONTROL RIAPORTÁLÁSHOZ
                    this.LastExecutionTime = watch.ElapsedMilliseconds + " ms";
                    this.ExecutionTime = this.LastExecutionTime;

                    this.TargetFoundCount = finalResults != null ? finalResults.Count : 0;
                    this.LastMatches = finalResults ?? new List<TargetMatch>();
                    this.LastSuccessfulTemplateIndex = successfulIdx;

                    // ====================================================================
                    // --- 5. LÉPÉS: MULTI-TARGET GENERÁLÁS AZ AL-PIPELINE CIKLUSHOZ ---
                    // ====================================================================
                    if (finalResults != null && finalResults.Count > 0)
                    {
                        // 1. Reseteljük a korábbi al-kontextusokat a biztonság kedvéért
                        if (context.SubContexts == null) context.SubContexts = new List<InspectionContext>();
                        context.SubContexts.Clear();

                        // 2. VÉGIGMEGYÜNK AZ ÖSSZES MEGTALÁLT IPARI TALÁLATON (PL. MINDEGYIK HEGESZTÉSI PONTON)
                        for (int t = 0; t < finalResults.Count; t++)
                        {
                            TargetMatch currentMatch = finalResults[t];

                            // Mentjük a ponton a központi Feature Store-ba egyedi sorszámozott névvel
                            string targetKey = $"{this.EffectiveFeatureName}_Target_{t + 1}";
                            context.Features[targetKey] = new PointFeature(targetKey, this.Name, currentMatch.CenterPoint);
                            string targetRotKey = $"{this.EffectiveFeatureName}_Target_{t + 1}.EdgeBased";

                            int tWidth = 200;
                            int tHeight = 200;

                            // Közvetlenül a listából olvassuk ki a méreteket, változó-generálás nélkül!
                            if (successfulIdx >= 0 && successfulIdx < Templates.Count)
                            {
                                tWidth = Templates[successfulIdx].Width;
                                tHeight = Templates[successfulIdx].Height;
                            }

                            // Létrehozzuk a téglalap feature-t a sablon valós méreteivel és a talált rotációs szöggel!
                            context.Features[targetRotKey] = new RectangleFeature(
                                targetRotKey,
                                this.Name,
                                new Rectangle((int)currentMatch.CenterPoint.X, (int)currentMatch.CenterPoint.Y, tWidth, tHeight),
                                currentMatch.Angle
                            );

                            // ====================================================================
                            // --- DINAMIKUS MERETVÉTEL A SIKERES SABLONBÓL ---
                            // ====================================================================
                            Rectangle subRoi = new Rectangle(
                                (int)(currentMatch.CenterPoint.X - (tWidth / 2)),
                                (int)(currentMatch.CenterPoint.Y - (tHeight / 2)),
                                tWidth,
                                tHeight
                            );

                            // Guardrail a kép szélei ellen
                            if (subRoi.X < 0) subRoi.X = 0;
                            if (subRoi.Y < 0) subRoi.Y = 0;
                            if (subRoi.X + subRoi.Width > sourceBmp.Width) subRoi.Width = sourceBmp.Width - subRoi.X;
                            if (subRoi.Y + subRoi.Height > sourceBmp.Height) subRoi.Height = sourceBmp.Height - subRoi.Y;

                            // 4. LÉTREHOZZUK AZ ÖNÁLLÓ AL-KÖRNYEZETET (SUB-CONTEXT)
                            InspectionContext subContext = new InspectionContext();

                            // Kivágjuk a parciális színes képet a főképből
                            Bitmap subBmp = CropImage(sourceBmp, subRoi);
                            subContext.SourceImage = new Image<Bgr, byte>(subBmp);
                            subContext.WorkingMask = subContext.SourceImage.Convert<Gray, byte>();

                            // Elmentjük az eltolás és szög adatokat az al-kontextus fejlécébe
                            subContext.LocalOffset = currentMatch.CenterPoint;
                            subContext.LocalAngle = currentMatch.Angle;

                            // --- JAVÍTÁS: AL-PIPELINE TÖRTÉNELEMMENTÉSI LÁNC HALADÓ INDÍTÁSA ---
                            // Éles üzemben és láncolásnál az al-pipeline saját magában is elmenti a nyers kezdőképet,
                            // így a mögé rakott színszűrő és Threshold modulok azonnal megtalálják ezt a képet!
                            subContext.ColorHistory[this.Name] = subContext.SourceImage.Copy();
                            subContext.GrayHistory[this.Name] = subContext.WorkingMask.Copy();

                            // Bedobjuk az al-kontextust a főlánc központi gyűjtőjébe
                            context.SubContexts.Add(subContext);
                            subBmp.Dispose();
                        }

                        // Kirajzoljuk a felnyilazott és összes kerettel ellátott ipari képet a képernyőre
                        Bitmap resultBmp = MatchEngine.DrawResults(sourceBmp, new Point(0, 0));
                        context.SourceImage?.Dispose();
                        context.SourceImage = new Image<Bgr, byte>(resultBmp);
                    }

                    // ====================================================================
                    // --- MINŐSÉGELLENŐRZŐ JAVÍTÁS (AUTOMATA SELEJT-RETESZ) ---
                    // ====================================================================
                    // Összevetjük az OpenCV által megtalált darabszámot a csúszkán konfigurált elvárással (MaxPos)
                    if (this.TargetFoundCount != this.ExpectedObjectCount)
                    {
                        // Ha 6 helyett csak 5 van a képen, a modul állapota és a GUI hibaállapotba billen (Piros X)
                        this.LastTestResult = "❌ FAIL (COUNT MISMATCH)";
                        this.TestResult = this.LastTestResult;
                        this.Status = ModuleStatus.Failed;
                    }
                    else
                    {
                        bool isPass = successfulIdx != -1;
                        this.LastTestResult = isPass ? "✅ PASS" : "❌ FAIL";
                        this.TestResult = this.LastTestResult;
                        this.Status = isPass ? ModuleStatus.Success : ModuleStatus.Failed;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastTestResult = "💥 ERROR";
                this.TestResult = "💥 ERROR";
                this.Status = ModuleStatus.Failed;
                Console.WriteLine("Multi-Template module runtime exception: " + ex.Message);
            }
            finally
            {
                // Megállítjuk a stopperórát, és rögzítjük a teljes futási időt ms-ben a ListBox kijelzéshez
                stopwatch.Stop();
                this.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
            }
        }




        static public Bitmap CropImage(Bitmap source, Rectangle section)
        {
            // An empty bitmap which will hold the cropped image
            //          if (section.Width > 1 && section.Height > 1)
            //           {
            try
            {

                Bitmap bmp = new Bitmap(section.Width, section.Height);

                Graphics g = Graphics.FromImage(bmp);

                // Draw the given area (section) of the source image
                // at location 0,0 on the empty bitmap (bmp)
                object bmplocker = new object();
                lock (bmplocker)
                {
                    g.DrawImage(source, 0, 0, section, GraphicsUnit.Pixel);
                }
                g.Dispose();
                return bmp;
            }
            catch
            {
                return null;
            }

            /*            }
                        else
                        {
                            return null;
                        }
            */
        }




    }
}