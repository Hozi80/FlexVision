using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.Structure;
using RotationTemplateMatch;

namespace FlexVision
{
    // ====================================================================
    // --- INTELIGENS LEGÖRDÜLŐ MENÜ KONVERTER A FORRÁSVÁLASZTÁSHOZ ---
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
    public class TemplateMatchModule : IInspectionModule
    {
        // Az interfész által megkövetelt fix tulajdonságok
        public string Name => "03. Template Matching (Multi-Sablon)";
        public VisionModuleType ModuleType => VisionModuleType.BlobDetect;

        // --- 1. BEMENETI BEÁLLÍTÁSOK (RECEPTBEN MENTHETŐ ÉS PARAMÉTEREZHETŐ) ---
        [Category("1. Keresési Beállítások")]
        [Description("Bekapcsolja a forgatás-invariáns (Rotation) keresési módot.")]
        public bool UseRotation { get; set; } = true;

        [Category("1. Keresési Beállítások")]
        [Description("A keresési tartomány (Region of Interest) a főkép koordináta-rendszerében.")]
        public Rectangle ROI { get; set; } = new Rectangle(50, 50, 300, 200);

        // ÚJ GRÁF-ALAPÚ FORRÁSVÁLASZTÓ KAPCSOLÓ: A jobb oldali Properties rácson legördülő menüként jelenik meg
        [Category("1. Keresési Beállítások")]
        [TypeConverter(typeof(InputStepNameConverter))]
        [Description("Válaszd ki, hogy a sablonkereső melyik korábbi Pipeline lépés képén fusson le.")]
        public string InputStepLink { get; set; } = "Default (Előző lépés)";

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

        public void Process(InspectionContext context)
        {
            // 1. DINAMIKUS FORRÁSVÁLASZTÁS: Lekérjük a beállított korábbi lépés képét!
            Image<Bgr, byte> targetInputColor = context.GetInputColorImage(this.InputStepLink);

            // Biztonsági guardrail: ha nincs forráskép vagy egyetlen sablon sincs betanítva
            if (targetInputColor == null) return;
            if (Templates.Count == 0)
            {
                this.LastTestResult = "⚠️ NO TEMPLATE";
                this.TestResult = "⚠️ NO TEMPLATE";
                return;
            }

            try
            {
                // 2. Példányosítjuk a te jól bevált forgatási-számító motorodat
                RotationInvariantTemplateMatch MatchEngine = new RotationInvariantTemplateMatch();
                this.MatchParams.MaxPos = 6;
                MatchEngine.SetOptions(this.MatchParams, this.UseRotation);

                // Átváltunk tiszta Bitmap térbe a te gyári logikádhoz
                using (Bitmap sourceBmp = targetInputColor.ToBitmap())
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    List<TargetMatch> finalResults = null;
                    int successfulIdx = -1;

                    // 3. MULTI-TEMPLATE CASCADE FONTOSSÁGI SORRENDBEN
                    for (int i = 0; i < Templates.Count; i++)
                    {
                        // A keresést a választott képen futtatjuk le!
                        List<TargetMatch> currentResults = MatchEngine.MatchTemplate(Templates[i], sourceBmp);

                        // Ellenőrizzük a te gyári szabályrendszeredet a Score és Polaritás szűrésekkel
                        if (currentResults.Count > 0)
                        {
                            finalResults = currentResults;
                            successfulIdx = i;
                            break; // Megvan az első sikeresen illeszkedő sablon, leállítjuk a cascade-ot!
                        }

                        // Biztonsági mentés: ha egyetlen tökéletes sincs, az első sablon adatait mutatjuk
                        if (i == 0) finalResults = currentResults;
                    }
                    watch.Stop();

                    // 4. BELSŐ ADATOK ÉS ÉLŐ KIJELZŐK FRISSÍTÉSE A PROPERTYGRID REAPORTÁLÁSHOZ
                    this.LastExecutionTime = watch.ElapsedMilliseconds + " ms";
                    this.ExecutionTime = this.LastExecutionTime;

                    this.TargetFoundCount = finalResults != null ? finalResults.Count : 0;
                    this.LastMatches = finalResults ?? new List<TargetMatch>();
                    this.LastSuccessfulTemplateIndex = successfulIdx;

                    bool isPass = successfulIdx != -1;
                    this.LastTestResult = isPass ? "✅ PASS" : "❌ FAIL";
                    this.TestResult = this.LastTestResult;

                    // 5. NYILAK ÉS KERETEK RÁRAJZOLÁSA A MÓDOSÍTOTT KÉPRE
                    if (finalResults != null && finalResults.Count > 0)
                    {
                        // Megkérjük a motorodat, rajzolja fel az eredmény-nyilakat
                        Bitmap resultBmp = MatchEngine.DrawResults(sourceBmp, new Point(0, 0));

                        // Felülírjuk a lánc aktuális főképét a felnyilazott változattal, 
                        // így a rákövetkező modulok vagy a kijelző már ezt látják!
                        context.SourceImage?.Dispose();
                        context.SourceImage = new Image<Bgr, byte>(resultBmp);
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastTestResult = "💥 ERROR";
                this.TestResult = "💥 ERROR";
                Console.WriteLine("Multi-Template module runtime exception: " + ex.Message);
            }
        }
    }
}
