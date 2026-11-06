using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System.Linq;

namespace FlexVision
{


    // --- ÚJ: IPARI MODUL STÁTUSZ ENUM ---
    public enum ModuleStatus
    {
        NotExecuted, // Még nem futott le
        Success,     // Hiba nélkül lefutott
        Warning,     // Lefutott, de pl. határértéken mozog az adat
        Failed       // Hibára futott (pl. hiányzó alkatrész)
    }

    // ====================================================================
    // --- GLOBÁLIS PIPELINE ENUMOK ÉS TÍPUSOK ---
    // ====================================================================
    public enum VisionModuleType
    {
        ImageLoad,
        OtsuFusion,
        BlobDetect
    }

    // ====================================================================
    // --- ADATKÖRNYEZET (INSPECTION CONTEXT) MÉRÉSI ADATTÁRRAL ---
    // ====================================================================

    public class InspectionContext : IDisposable
    {
        public Image<Bgr, byte> SourceImage { get; set; }
        public Image<Gray, byte> WorkingMask { get; set; }
        public PointF LocalOffset { get; set; } = new PointF(0, 0);
        public double LocalAngle { get; set; } = 0.0;

        // --- JAVÍTÁS: Független, szálbiztos unmanaged képtörténelem tároló ---
        public Dictionary<string, Image<Bgr, byte>> ColorHistory { get; set; } = new Dictionary<string, Image<Bgr, byte>>();
        public Dictionary<string, Image<Gray, byte>> GrayHistory { get; set; } = new Dictionary<string, Image<Gray, byte>>();

        public Dictionary<string, BaseFeature> Features { get; set; } = new Dictionary<string, BaseFeature>();
        public Dictionary<string, object> CustomData { get; set; } = new Dictionary<string, object>();
        public List<InspectionContext> SubContexts { get; set; } = new List<InspectionContext>();

        public Image<Bgr, byte> GetInputColorImage(string stepLink)
        {
            // AUTOMATIKUS FALLBACK LOGIKA: Ha előző lépést kérünk, kiszedjük a legutoljára elmentett érvényes képet
            if (string.IsNullOrEmpty(stepLink) || stepLink == "Default (Előző lépés)")
            {
                if (ColorHistory.Count > 0)
                {
                    var lastKey = ColorHistory.Keys.Last();
                    if (ColorHistory[lastKey] != null && ColorHistory[lastKey].Ptr != IntPtr.Zero)
                        return ColorHistory[lastKey].Copy(); // .Copy() = Tiszta, független C++ mély másolat!
                }
                return null;
            }

            // Ha konkrét lépésnevet keresünk (pl. ComboBox kijelölés alapján)
            if (ColorHistory.ContainsKey(stepLink) && ColorHistory[stepLink] != null && ColorHistory[stepLink].Ptr != IntPtr.Zero)
            {
                return ColorHistory[stepLink].Copy();
            }
            return null;
        }

        public Image<Gray, byte> GetInputGrayImage(string stepLink)
        {
            if (string.IsNullOrEmpty(stepLink) || stepLink == "Default (Előző lépés)")
            {
                if (GrayHistory.Count > 0)
                {
                    var lastKey = GrayHistory.Keys.Last();
                    if (GrayHistory[lastKey] != null && GrayHistory[lastKey].Ptr != IntPtr.Zero)
                        return GrayHistory[lastKey].Copy();
                }
                return null;
            }

            if (GrayHistory.ContainsKey(stepLink) && GrayHistory[stepLink] != null && GrayHistory[stepLink].Ptr != IntPtr.Zero)
            {
                return GrayHistory[stepLink].Copy();
            }
            return null;
        }

        public void Dispose()
        {
            SourceImage?.Dispose();
            WorkingMask?.Dispose();

            // Biztonságos unmanaged erőforrás-felszabadítás
            foreach (var img in ColorHistory.Values) img?.Dispose();
            foreach (var img in GrayHistory.Values) img?.Dispose();

            ColorHistory.Clear();
            GrayHistory.Clear();

            if (SubContexts != null)
            {
                foreach (var sub in SubContexts) sub.Dispose();
                SubContexts.Clear();
            }
        }
    }

    // ====================================================================
    // --- FOLYAMATLÉPÉSEK INTERFÉSZE ---
    // ====================================================================
    public interface IInspectionModule
    {
        string Name { get; }
        VisionModuleType ModuleType { get; }
        ModuleStatus Status { get; }
        double ProcessingTimeMs { get; }

        void Process(InspectionContext context);
    }

    // ====================================================================
    // --- KÖZÖS IPARI MODUL ALAPOSZTÁLY (GOLYÓÁLLÓ FIXTURE RÉTEG) ---
    // ====================================================================
    [Serializable]
    public abstract class BaseInspectionModule : IInspectionModule
    {
        [System.ComponentModel.Browsable(false)]
        public int StepIndex { get; set; } = 1;

        [System.ComponentModel.Browsable(false)]
        public abstract string PureName { get; }

        [System.ComponentModel.Browsable(false)]
        public string Name => $"{StepIndex:D2}. {PureName}";

        [System.ComponentModel.Browsable(false)]
        public abstract VisionModuleType ModuleType { get; }

        [Category("Diagnosztika"), Description("A modul legutóbbi futásának állapota."), ReadOnly(true)]
        public ModuleStatus Status { get; set; } = ModuleStatus.NotExecuted;

        [Category("Diagnosztika"), Description("A modul futási ideje ezredmásodpercben (ms)."), ReadOnly(true)]
        public double ProcessingTimeMs { get; set; } = 0.0;

        public abstract void Process(InspectionContext context);

        // --- COMMON PARAMÉTEREK A COMBOBOXOK SZÁMÁRA (EZ HIÁNYZOTT!) ---
        [Category("Common - Bemenet")]
        public string InputStepLink { get; set; } = "Default (Előző lépés)";

        [Category("Common - Offset (X Tengely)")]
        public string ReferenceXSrc { get; set; } = "None (Fix koordináta)";

        [Category("Common - Offset (Y Tengely)")]
        public string ReferenceYSrc { get; set; } = "None (Fix koordináta)";

        [Category("Common - Offset (Rotáció)")]
        public string ReferenceRotSrc { get; set; } = "None (Fix elfordulás)";

        [Category("Common - Névkezelés")]
        public string CustomFeatureName { get; set; } = "";

        [System.ComponentModel.Browsable(false)]
        public string EffectiveFeatureName
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomFeatureName)) return CustomFeatureName.Trim().Replace(" ", "_");
                return this.Name.Replace(" ", "_").Replace(".", "").Replace("(", "").Replace(")", "").Replace("-", "_");
            }
        }
    }
    // ====================================================================
    // --- MÓDOSÍTOTT IMAGE LOAD MODUL ---
    // ====================================================================
    [Serializable]
    public class ImageLoadModule : BaseInspectionModule
    {
        [System.ComponentModel.Browsable(false)]
        public override string PureName => "Source Image Capture"; // Tisztított név sorszám nélkül

        [System.ComponentModel.Browsable(false)]
        public override VisionModuleType ModuleType => VisionModuleType.ImageLoad;

        [Category("Module Specific - Fájl Beállítások"), Description("A vizsgálandó kép abszolút elérési útja.")]
        public string FilePath { get; set; } = @"C:\Test\boundtest3.bmp";

        public ImageLoadModule() { }

        public override void Process(InspectionContext context)
        {
            if (System.IO.File.Exists(FilePath))
            {
                context.SourceImage = new Image<Bgr, byte>(FilePath);
                context.WorkingMask = context.SourceImage.Convert<Gray, byte>();
            }
        }
    }


    // ====================================================================
    // 1. CSOPORT: KÉPJAVÍTÁS A TÉRBELI TARTOMÁNYBAN (PIXEL-ALAPÚ MŰVELETEK)
    // ====================================================================
    [Serializable]
    public class HistogramEqualizationModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Histogram Equalization";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        public HistogramEqualizationModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            using (Image<Gray, byte> gray = inputImg.Convert<Gray, byte>())
            {
                Image<Gray, byte> equalized = new Image<Gray, byte>(gray.Width, gray.Height);
                CvInvoke.EqualizeHist(gray, equalized);
                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = equalized;
            }
        }
    }

    [Serializable]
    public class GammaCorrectionModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Gamma Correction";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Gamma"), Description("Gamma érték. < 1.0 sötétít, > 1.0 világosítja a részleteket.")]
        public double Gamma { get; set; } = 1.2;
        public GammaCorrectionModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            byte[] lut = new byte[256];
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)Math.Min(255, Math.Max(0, Math.Pow(i / 255.0, 1.0 / Gamma) * 255.0));
            Image<Bgr, byte> result = inputImg.Clone();
            using (Mat lutMat = new Mat(1, 256, DepthType.Cv8U, 1))
            {
                lutMat.SetTo(lut);
                CvInvoke.LUT(inputImg, lutMat, result);
            }
            context.SourceImage?.Dispose(); context.SourceImage = result;
        }
    }

    [Serializable]
    public class SimpleThresholdModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Thresholding";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Küszöb"), Description("Fix fényerő határérték (0 - 255).")]
        public double ThresholdValue { get; set; } = 128;
        public SimpleThresholdModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            using (Image<Gray, byte> gray = inputImg.Convert<Gray, byte>())
            {
                Image<Gray, byte> binary = new Image<Gray, byte>(gray.Width, gray.Height);
                CvInvoke.Threshold(gray, binary, ThresholdValue, 255, ThresholdType.Binary);
                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = binary;
            }
        }
    }

    // ====================================================================
    // 2. CSOPORT: SIMÍTÓ (ZAJCSÖKKENTŐ) SZŰRŐK
    // ====================================================================
    [Serializable]
    public class BoxFilterModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Box Filter";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Kernel"), Description("Ablakméret.")] public int KernelSize { get; set; } = 5;
        public BoxFilterModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            Image<Bgr, byte> result = new Image<Bgr, byte>(inputImg.Width, inputImg.Height);
            CvInvoke.Blur(inputImg, result, new Size(KernelSize, KernelSize), new Point(-1, -1));
            context.SourceImage?.Dispose(); context.SourceImage = result;
        }
    }

    [Serializable]
    public class MedianFilterModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Median Filter";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Kernel"), Description("Ablakméret (Páratlan pozitív szám).")] public int KernelSize { get; set; } = 5;
        public MedianFilterModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            int k = KernelSize % 2 == 0 ? KernelSize + 1 : KernelSize;
            Image<Bgr, byte> result = new Image<Bgr, byte>(inputImg.Width, inputImg.Height);
            CvInvoke.MedianBlur(inputImg, result, k);
            context.SourceImage?.Dispose(); context.SourceImage = result;
        }
    }

    [Serializable]
    public class BilateralFilterModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Bilateral Filter";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Bilateral"), Description("Pixel környezet átmérője.")] public int Diameter { get; set; } = 9;
        [Category("Bilateral"), Description("Színtartomány szórása.")] public double SigmaColor { get; set; } = 75;
        [Category("Bilateral"), Description("Térbeli távolság szórása.")] public double SigmaSpace { get; set; } = 75;
        public BilateralFilterModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            Image<Bgr, byte> result = new Image<Bgr, byte>(inputImg.Width, inputImg.Height);
            CvInvoke.BilateralFilter(inputImg, result, Diameter, SigmaColor, SigmaSpace);
            context.SourceImage?.Dispose(); context.SourceImage = result;
        }
    }

    // ====================================================================
    // 3. CSOPORT: ÉLESÍTŐ ÉS ÉLKERESŐ SZŰRŐK
    // ====================================================================
    [Serializable]
    public class SobelFilterModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Sobel Detector";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Sobel"), Description("X irányú deriválás.")] public int Dx { get; set; } = 1;
        [Category("Sobel"), Description("Y irányú deriválás.")] public int Dy { get; set; } = 0;
        [Category("Sobel"), Description("Kernel méret (1, 3, 5, 7).")] public int KernelSize { get; set; } = 3;
        public SobelFilterModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            using (Image<Gray, byte> gray = inputImg.Convert<Gray, byte>())
            {
                Image<Gray, float> grad = new Image<Gray, float>(gray.Width, gray.Height);
                CvInvoke.Sobel(gray, grad, DepthType.Cv32F, Dx, Dy, KernelSize);
                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = grad.Convert<Gray, byte>();
            }
        }
    }

    [Serializable]
    public class LaplacianFilterModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Laplace Filter";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Laplace"), Description("Aperture ablakméret (Páratlan szám).")] public int ApertureSize { get; set; } = 3;
        public LaplacianFilterModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            using (Image<Gray, byte> gray = inputImg.Convert<Gray, byte>())
            {
                Image<Gray, float> lap = new Image<Gray, float>(gray.Width, gray.Height);
                CvInvoke.Laplacian(gray, lap, DepthType.Cv32F, ApertureSize);
                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = lap.Convert<Gray, byte>();
            }
        }
    }


    [Serializable]
    public class UnsharpMaskingModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Unsharp Masking";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Élesítés"), Description("Elmosási sugár.")] public int BlurSize { get; set; } = 5;
        [Category("Élesítés"), Description("Élesítési súlyszorzó.")] public double Weight { get; set; } = 1.5;
        public UnsharpMaskingModule() { }
        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;
            int k = BlurSize % 2 == 0 ? BlurSize + 1 : BlurSize;
            using (Image<Bgr, byte> blurred = new Image<Bgr, byte>(inputImg.Width, inputImg.Height))
            {
                CvInvoke.GaussianBlur(inputImg, blurred, new Size(k, k), 0);
                Image<Bgr, byte> sharp = new Image<Bgr, byte>(inputImg.Width, inputImg.Height);
                CvInvoke.AddWeighted(inputImg, 1.0 + Weight, blurred, -Weight, 0, sharp);
                context.SourceImage?.Dispose(); context.SourceImage = sharp;
            }
        }
    }

    // ====================================================================
    // 4. CSOPORT: MORFOLÓGIAI MŰVELETEK (OPENING, CLOSING, DILATE)
    // ====================================================================
    [Serializable]
    public class AdvancedMorphologyModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Morphological Operations";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;
        [Category("Morfológia"), Description("Művelet típusa: Erode, Dilate, Open, Close.")]
        public MorphOp Operation { get; set; } = MorphOp.Open;
        [Category("Morfológia"), Description("Strukturáló elem mérete (Mérőháló ablak).")]
        public int ElementSize { get; set; } = 3;
        [Category("Morfológia"), Description("Iterációk száma.")]
        public int Iterations { get; set; } = 1;
        public AdvancedMorphologyModule() { }
        public override void Process(InspectionContext context)
        {
            if (context.WorkingMask == null) return;
            Image<Gray, byte> result = context.WorkingMask.Clone();
            using (Mat element = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(ElementSize, ElementSize), new Point(-1, -1)))
            {
                CvInvoke.MorphologyEx(context.WorkingMask, result, Operation, element, new Point(-1, -1), Iterations, BorderType.Constant, new MCvScalar());
            }
            context.WorkingMask.Dispose(); context.WorkingMask = result;
        }
    }




    // ====================================================================
    // --- 04. MODUL: GAUSSIAN BLUR (GAUSS ELMOSÁS SZŰRŐ) ---
    // ====================================================================
    [Serializable]
    public class GaussianBlurModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Gaussian Blur";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        [Category("Module Specific - Szűrő Beállítások")]
        [Description("A Gauss-szűrő ablakmérete pixelben. Szigorúan PÁRATLAN számnak kell lennie! (Pl. 3, 5, 7, 15)")]
        public int KernelSize { get; set; } = 5;

        [Category("Module Specific - Szűrő Beállítások")]
        [Description("A Gauss-eloszlás szórása (Sigma) X irányban. Ha 0, a rendszer a kernel méretéből számolja ki automatikusan.")]
        public double SigmaX { get; set; } = 1.5;

        public GaussianBlurModule() { }

        public override void Process(InspectionContext context)
        {
            // Lekérjük a gráf szerint kiválasztott színes forrásképet
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;

            // Biztonsági korlát: a kernel mérete csak páratlan pozitív szám lehet
            int kSize = KernelSize;
            if (kSize % 2 == 0) kSize++;
            if (kSize < 1) kSize = 1;

            // Létrehozunk egy azonos méretű kimeneti képet
            Image<Bgr, byte> blurredImg = new Image<Bgr, byte>(inputImg.Width, inputImg.Height);

            // EmguCV Gauss-szűrés végrehajtása
            CvInvoke.GaussianBlur(inputImg, blurredImg, new Size(kSize, kSize), SigmaX);

            // Frissítjük a lánc aktuális színes képét, a WorkingMask-ba pedig áttesszük a szürke változatát
            context.SourceImage?.Dispose();
            context.SourceImage = blurredImg;

            if (context.WorkingMask != null) context.WorkingMask.Dispose();
            context.WorkingMask = blurredImg.Convert<Gray, byte>();
        }
    }

    // ====================================================================
    // --- 05. MODUL: CANNY EDGE DETECTOR (ÉLKERESŐ SZŰRŐ) ---
    // ====================================================================
    [Serializable]
    public class CannyEdgeModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Canny Edge Detector";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        [Category("Module Specific - Canny Beállítások")]
        [Description("Az élek összekötéséhez használt alsó hiszterézis küszöbérték.")]
        public double LowThreshold { get; set; } = 50.0;

        [Category("Module Specific - Canny Beállítások")]
        [Description("Az erős élek detektálásához használt felső küszöbérték.")]
        public double HighThreshold { get; set; } = 150.0;

        [Category("Module Specific - Canny Beállítások")]
        [Description("A Sobel operátor ablakmérete (Aperture size). Értéke szigorúan 3, 5 vagy 7 lehet.")]
        public int ApertureSize { get; set; } = 3;

        public CannyEdgeModule() { }

        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;

            // A Canny szigorúan szürkeárnyalatos képet vár bemenetként
            using (Image<Gray, byte> grayInput = inputImg.Convert<Gray, byte>())
            {
                Image<Gray, byte> edges = new Image<Gray, byte>(inputImg.Width, inputImg.Height);

                // EmguCV Canny algoritmus futtatása
                CvInvoke.Canny(grayInput, edges, LowThreshold, HighThreshold, ApertureSize);

                // Mivel a Canny egy tiszta bináris maszkot ad vissza, felülírjuk vele a WorkingMask-ot
                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = edges;
            }
        }
    }


    // ====================================================================
    // --- 05. CSOPORT: LOKÁLIS ALKALMAZKODÓ KÜSZÖBÖLÉS (ADAPTIVE THRESHOLD) ---
    // ====================================================================
    [Serializable]
    public class AdaptiveThresholdModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Adaptive Threshold";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        [Category("Adaptive Threshold"), Description("A lokális ablakméret pixelben, amin belül a fényerő-átlagot számoljuk (Páratlan szám, pl. 3, 11, 21, 55).")]
        public int BlockSize { get; set; } = 11;

        [Category("Adaptive Threshold"), Description("Konstans érték, amit levonunk a kiszámolt átlagból (finomhangolás a zajok ellen).")]
        public double ConstantC { get; set; } = 2.0;

        public AdaptiveThresholdModule() { }

        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;

            int bSize = BlockSize;
            if (bSize % 2 == 0) bSize++;
            if (bSize < 3) bSize = 3;

            using (Image<Gray, byte> gray = inputImg.Convert<Gray, byte>())
            {
                Image<Gray, byte> binary = new Image<Gray, byte>(gray.Width, gray.Height);

                CvInvoke.AdaptiveThreshold(
                    gray,
                    binary,
                    255,
                    AdaptiveThresholdType.MeanC,
                    ThresholdType.Binary,
                    bSize,
                    ConstantC
                );

                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = binary;
            }
        }
    }



    public enum ColorSpaceType { BGR, HSV, LAB }

    [Serializable]
    public class ColorInRangeModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Color Filter";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        [System.ComponentModel.Browsable(false)]
        public bool IsFilterActivated { get; set; } = false;

        [Category("Module Specific - Színtér"), Description("A szűréshez használt ipari színtér (BGR, HSV vagy LAB).")]
        public ColorSpaceType ActiveColorSpace { get; set; } = ColorSpaceType.BGR;

        [Category("Module Specific - Csatorna Korlátok")] public byte MinCh1 { get; set; } = 100;
        [Category("Module Specific - Csatorna Korlátok")] public byte MaxCh1 { get; set; } = 150;
        [Category("Module Specific - Csatorna Korlátok")] public byte MinCh2 { get; set; } = 100;
        [Category("Module Specific - Csatorna Korlátok")] public byte MaxCh2 { get; set; } = 150;
        [Category("Module Specific - Csatorna Korlátok")] public byte MinCh3 { get; set; } = 100;
        [Category("Module Specific - Csatorna Korlátok")] public byte MaxCh3 { get; set; } = 150;

        [Category("Module Specific - Overlay Vizuál")] public byte OverlayBlue { get; set; } = 0;
        [Category("Module Specific - Overlay Vizuál")] public byte OverlayGreen { get; set; } = 255;
        [Category("Module Specific - Overlay Vizuál")] public byte OverlayRed { get; set; } = 255;

        [Category("Module Specific - Szűrés"), Description("Ha igaz, a kimeneti bináris maszk megfordul (a fekete és fehér pixelek felcserélődnek).")]
        public bool IsInverted { get; set; } = false;

        // ====================================================================
        // --- ÚJ RECEPT PARAMÉTER: CSAK MASZK TOVÁBBKÜLDÉSE ---
        // ====================================================================
        [Category("Module Specific - Szűrés"), Description("Ha be van kapcsolva, a modul csak a tiszta fekete-fehér bináris maszkot küldi tovább a következő moduloknak az overlay helyett.")]
        public bool OnlyMask { get; set; } = false;

        public ColorInRangeModule() { }

        public override void Process(InspectionContext context)
        {
            Console.WriteLine("ColorInRange Process Start");

            // 1. Biztonságos képbeolvasás a javított láncból vagy az al-pipeline-ból
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null)
            {
                if (context.SourceImage != null && context.SourceImage.Ptr != IntPtr.Zero)
                    inputImg = context.SourceImage.Copy();
            }

            // Golyóálló hardveres mutató-ellenőrzés
            if (inputImg == null || inputImg.Ptr == System.IntPtr.Zero) return;

            // --- 2. FÁZIS: INAKTÍV ÁLLAPOT KEZELÉSE (TISZTA FALLBACK OVERLAY) ---
            if (!IsFilterActivated)
            {
                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = inputImg.Convert<Gray, byte>();

                context.SourceImage?.Dispose();
                context.SourceImage = inputImg.Copy();
                inputImg.Dispose();
                return;
            }

            // --- 3. FÁZIS: AKTÍV ADAPTÍV SZŰRÉS ---
            using (Image<Gray, byte> binaryMask = new Image<Gray, byte>(inputImg.Size))
            {
                // Garantált 8-bites csatornaillesztés az unmanaged OpenCV C++ mag felé
                MCvScalar lowerBound = new MCvScalar(MinCh1, MinCh2, MinCh3);
                MCvScalar upperBound = new MCvScalar(MaxCh1, MaxCh2, MaxCh3);

                if (ActiveColorSpace == ColorSpaceType.BGR)
                {
                    CvInvoke.InRange(inputImg, new ScalarArray(lowerBound), new ScalarArray(upperBound), binaryMask);
                }
                else
                {
                    using (Mat convertedMat = new Mat())
                    {
                        if (ActiveColorSpace == ColorSpaceType.HSV)
                        {
                            CvInvoke.CvtColor(inputImg, convertedMat, ColorConversion.Bgr2Hsv);
                        }
                        else if (ActiveColorSpace == ColorSpaceType.LAB)
                        {
                            CvInvoke.CvtColor(inputImg, convertedMat, ColorConversion.Bgr2Lab);
                        }

                        CvInvoke.InRange(convertedMat, new ScalarArray(lowerBound), new ScalarArray(upperBound), binaryMask);
                    }
                }

                // ====================================================================
                // --- INTELIGENS KIMENETI MASZK INVERTÁLÓ RETESZ (BITWISE NOT) ---
                // ====================================================================
                if (IsInverted)
                {
                    CvInvoke.BitwiseNot(binaryMask, binaryMask);
                }

                // Mentjük a kiszámolt maszkot a láncoláshoz a rákövetkező Threshold/Edge modulnak
                if (context.WorkingMask != null) context.WorkingMask.Dispose();
                context.WorkingMask = binaryMask.Copy();

                // Debug mentés a lemezre ellenőrzéshez
                binaryMask.Save(@"C:\Test\binarymask.bmp");

                // Diagnosztikai pixelszámlálás a Console ablakba
                int pixels = CvInvoke.CountNonZero(binaryMask);
                Console.WriteLine($"Active ColorSpace: {ActiveColorSpace} | Inverted: {IsInverted} | Real Mask pixels: {pixels}");

                // ====================================================================
                // --- JAVÍTÁS: ÚJ KIMENETI STRATÉGIA VÁLASZTÓ REKESZ ---
                // ====================================================================
                if (this.OnlyMask)
                {
                    // --- STRATÉGIA [A]: ONLY MASK ÜZEMMÓD ---
                    // Konvertáljuk a bináris maszkot 3-csatornás BGR képpé (tiszta fekete-fehér kép lesz)
                    // Ez kötelező, mert a context.SourceImage-nek 3-csatornás RGB/BGR formátumúnak kell lennie a UI felé!
                    Image<Bgr, byte> pureBinaryOutput = binaryMask.Convert<Bgr, byte>();

                    context.SourceImage?.Dispose();
                    context.SourceImage = pureBinaryOutput.Copy();

                    // Frissítjük a színes előzménytárat is a következő modulok számára
                    context.ColorHistory[this.Name] = pureBinaryOutput.Copy();
                    pureBinaryOutput.Dispose();
                }
                else
                {
                    // --- STRATÉGIA [B]: STANDARD OVERLAY ÜZEMMÓD ---
                    using (Image<Bgr, byte> colorMask = new Image<Bgr, byte>(inputImg.Size))
                    using (Image<Bgr, byte> overlayOutput = new Image<Bgr, byte>(inputImg.Size))
                    {
                        colorMask.SetValue(new MCvScalar(OverlayBlue, OverlayGreen, OverlayRed));

                        using (Image<Bgr, byte> maskedYellow = new Image<Bgr, byte>(inputImg.Size))
                        {
                            CvInvoke.BitwiseAnd(colorMask, colorMask, maskedYellow, binaryMask);
                            CvInvoke.AddWeighted(inputImg, 1, maskedYellow, 1, 0, overlayOutput);
                        }

                        context.SourceImage?.Dispose();
                        context.SourceImage = overlayOutput.Copy();
                        context.ColorHistory[this.Name] = overlayOutput.Copy();
                    }
                }
            }

            inputImg.Dispose();
            Console.WriteLine("ColorInRange Process End");
        }
    }



    // ====================================================================
    // --- MÓDOSÍTOTT OTSU SZUPER FÚZIÓS MODUL ---
    // ====================================================================
    [Serializable]
    public class OtsuSuperFusionModule : BaseInspectionModule
    {
        [System.ComponentModel.Browsable(false)]
        public override string PureName => "Otsu Filter"; // Tisztított név

        [System.ComponentModel.Browsable(false)]
        public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        [Category("Module Specific - 1. Elmosás")] public int BlurSize { get; set; } = 15;
        [Category("Module Specific - 2. Morfológia")] public bool EnableErode { get; set; } = true;
        [Category("Module Specific - 2. Morfológia")] public int ErodeIterations { get; set; } = 1;
        [Category("Module Specific - 3. Terület")] public Rectangle MasterROI { get; set; } = new Rectangle(10, 10, 300, 200);

        [System.ComponentModel.Browsable(false)]
        public PointF MasterBasePoint { get; set; } = new PointF(0, 0);

        public OtsuSuperFusionModule() { }

        public override void Process(InspectionContext context)
        {
            // ... (A fúziós és binarizálási logikád változatlanul fut itt tovább) ...
            if (context.SourceImage == null) return;
            // [A korábbi eltolás-követő OpenCV kódod változatlan marad...]
        }
    }
    // ====================================================================
    // --- INTELIGENS, DINAMIKUS HIERARCHIKUS POZÍCIÓ-REFERENCIA KONVERTER ---
    // ====================================================================
    public class ReferenceFeatureConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            List<string> dynamicReferences = new List<string>
            {
                "None (Fix koordináta, nincs követés)"
            };

            try
            {
                // Windows Forms trükk: A PropertyGrid környezetéből (context) vissza tudunk nyúlni a fő felülethez!
                if (context != null && context.Instance != null)
                {
                    // Lekérjük a jelenleg érvényes receptet a memóriából.
                    // Ehhez feltételezzük, hogy a PipelineEditorControl-on keresztül elérhetjük az aktuális lépéseket.
                    // Hogy golyóálló legyen, egy belső statikus hivatkozáson vagy a felületi struktúrán keresünk:

                    // Keressük a Pipeline listát a memóriában a kódolt láncon keresztül
                    List<IInspectionModule> activeSteps = null;

                    // Megpróbáljuk megkeresni a fő szerkesztőt a vizuális fában
                    Control targetControl = context.Container as Control;
                    if (targetControl == null && context.Instance is BaseInspectionModule mod)
                    {
                        // Ha közvetlenül a modulból hívták meg, megpróbáljuk elérni a globális statikus tárat
                        activeSteps = ReceptRegistry.ActiveSteps;
                    }

                    if (activeSteps != null)
                    {
                        foreach (var step in activeSteps)
                        {
                            if (step is BaseInspectionModule bMod)
                            {
                                string baseKey = bMod.EffectiveFeatureName;

                                // 1. Szint: Maga a Főmodul (pl. "TemplateMatch")
                                dynamicReferences.Add(baseKey);

                                // 2. Szint: Dinamikus al-ágak automatikus generálása a modul típusa szerint!
                                // Ha ez egy sablonkereső, akkor a Bond-welding logikád alapján felkínáljuk az al-ágait:
                                if (step is TemplateMatchModule)
                                {
                                    dynamicReferences.Add($"{baseKey}.EdgeBased");
                                    dynamicReferences.Add($"{baseKey}.EdgeBased.Edge1");
                                    dynamicReferences.Add($"{baseKey}.EdgeBased.Edge2");
                                    dynamicReferences.Add($"{baseKey}.EdgeBased.WireEnd");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Fallback gyári teszt-struktúra a betöltés idejére, amíg a vizuális fa még nem épült fel
                        dynamicReferences.Add("02_Template_Matching_Multi_Sablon");
                        dynamicReferences.Add("02_Template_Matching_Multi_Sablon.EdgeBased");
                        dynamicReferences.Add("02_Template_Matching_Multi_Sablon.EdgeBased.Edge1");
                        dynamicReferences.Add("02_Template_Matching_Multi_Sablon.EdgeBased.Edge2");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Dinamikus referencia generálási hiba: " + ex.Message);
            }

            return new StandardValuesCollection(dynamicReferences);
        }
    }

    // Egy parányi globális regiszter, hogy a konverter a felülettől függetlenül is mindig lássa az aktuális lépéseket
    public static class ReceptRegistry
    {
        public static List<IInspectionModule> ActiveSteps { get; set; } = new List<IInspectionModule>();
    }

}