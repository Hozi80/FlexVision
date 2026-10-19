using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace FlexVision
{
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

        public Dictionary<string, Image<Bgr, byte>> ColorHistory { get; set; } = new Dictionary<string, Image<Bgr, byte>>();
        public Dictionary<string, Image<Gray, byte>> GrayHistory { get; set; } = new Dictionary<string, Image<Gray, byte>>();

        // 1. FÁZIS: KÖZPONTI GEOMETRIAI ÉS MÉRÉSI ADATTÁR (FEATURE STORE)
        public Dictionary<string, BaseFeature> Features { get; set; } = new Dictionary<string, BaseFeature>();

        public Dictionary<string, object> CustomData { get; set; } = new Dictionary<string, object>();

        public PointF GetPointFeature(string id) => Features.ContainsKey(id) && Features[id] is PointFeature pf ? pf.Position : PointF.Empty;
        public LineFeature GetLineFeature(string id) => Features.ContainsKey(id) && Features[id] is LineFeature lf ? lf : null;

        public Image<Bgr, byte> GetInputColorImage(string stepName)
        {
            if (string.IsNullOrEmpty(stepName) || !ColorHistory.ContainsKey(stepName) || stepName.StartsWith("Default"))
                return SourceImage;
            return ColorHistory[stepName];
        }

        public void Dispose()
        {
            if (SourceImage != null) { SourceImage.Dispose(); SourceImage = null; }
            if (WorkingMask != null) { WorkingMask.Dispose(); WorkingMask = null; }
            foreach (var img in ColorHistory.Values) img?.Dispose();
            foreach (var img in GrayHistory.Values) img?.Dispose();
            ColorHistory.Clear();
            GrayHistory.Clear();
            Features.Clear();
        }
    }

    // ====================================================================
    // --- FOLYAMATLÉPÉSEK INTERFÉSZE ---
    // ====================================================================
    public interface IInspectionModule
    {
        string Name { get; }
        VisionModuleType ModuleType { get; }
        void Process(InspectionContext context);
    }

    // ====================================================================
    // --- KÖZÖS IPARI MODUL ALAPOSZTÁLY (DINAMIKUS SORSZÁMOZÁSSAL) ---
    // ====================================================================
    [Serializable]
    public abstract class BaseInspectionModule : IInspectionModule
    {
        // ÚJ: Futásidőben itt tároljuk el, hogy a modul éppen hányadik a sorban
        [System.ComponentModel.Browsable(false)]
        public int StepIndex { get; set; } = 1;

        // Absztrakt tiszta név a sorszám nélkül (ezt adják meg az egyedi modulok)
        [System.ComponentModel.Browsable(false)]
        public abstract string PureName { get; }

        // AUTOMATIKUS DINAMIKUS NÉVGENERÁLÁS: Mindig a valós sorszámmal dekorálja ki a nevet!
        [System.ComponentModel.Browsable(false)]
        public string Name => $"{StepIndex:D2}. {PureName}";

        [System.ComponentModel.Browsable(false)]
        public abstract VisionModuleType ModuleType { get; }
        public abstract void Process(InspectionContext context);

        [Category("Common - Bemenet")]
        [TypeConverter(typeof(InputStepNameConverter))]
        [Description("Válaszd ki, hogy ez a modul melyik korábbi lépés képén fusson le.")]
        public string InputStepLink { get; set; } = "Default (Előző lépés)";

        [Category("Common - Offset Igazítás")]
        [TypeConverter(typeof(ReferenceFeatureConverter))]
        [Description("Válaszd ki azt a korábbi bázis-mérést, aminek az eltolódását ennek a modulnak követnie kell.")]
        public string ReferenceFeatureLink { get; set; } = "None (Fix koordináta, nincs követés)";

        [Category("Common - Névkezelés")]
        [Description("A modul egyedi eredmény-neve a központi adattárban. Ha üres, automatikusan generálódik.")]
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
        public string FilePath { get; set; } = @"C:\Test\wire.bmp";

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
        [Browsable(false)] public override string PureName => "Histogram Equalization (Kontraszt)";
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
        [Browsable(false)] public override string PureName => "Gamma Correction (Fényerő kiegyenlítés)";
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
        [Browsable(false)] public override string PureName => "Thresholding (Fix Küszöbölés)";
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
        [Browsable(false)] public override string PureName => "Box Filter (Átlagoló elmosás)";
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
        [Browsable(false)] public override string PureName => "Median Filter (Só-bors zajszűrés)";
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
        [Browsable(false)] public override string PureName => "Bilateral Filter (Élmegőrző simítás)";
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
        [Browsable(false)] public override string PureName => "Sobel Edge Detector (Gradiens élek)";
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
        [Browsable(false)] public override string PureName => "Laplace Filter (Másodrendű élek)";
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
        [Browsable(false)] public override string PureName => "Unsharp Masking (Élesítés)";
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
        [Browsable(false)] public override string PureName => "Morphological Operations (Alakzat-módosítás)";
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
        [Browsable(false)] public override string PureName => "Gaussian Blur (Zajszűrés)";
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
        [Browsable(false)] public override string PureName => "Canny Edge Detector (Élkereső)";
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
    // --- 06. MODUL: COLOR INRANGE FILTER (BGR / RGB SZÍNSZŰRŐ) ---
    // ====================================================================
    [Serializable]
    public class ColorInRangeModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Color InRange Filter (Színszűrés)";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        // BGR sávok alsó és felső határértékei a Pick Color funkcióhoz és a csúszkákhoz
        [Category("Module Specific - Alsó Színhatár (BGR)")] public byte MinBlue { get; set; } = 0;
        [Category("Module Specific - Alsó Színhatár (BGR)")] public byte MinGreen { get; set; } = 0;
        [Category("Module Specific - Alsó Színhatár (BGR)")] public byte MinRed { get; set; } = 0;

        [Category("Module Specific - Felső Színhatár (BGR)")] public byte MaxBlue { get; set; } = 255;
        [Category("Module Specific - Felső Színhatár (BGR)")] public byte MaxGreen { get; set; } = 255;
        [Category("Module Specific - Felső Színhatár (BGR)")] public byte MaxRed { get; set; } = 255;

        public ColorInRangeModule() { }

        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> inputImg = context.GetInputColorImage(this.InputStepLink);
            if (inputImg == null) return;

            Image<Gray, byte> binaryMask = new Image<Gray, byte>(inputImg.Width, inputImg.Height);

            // Definiáljuk az EmguCV színhatár-skálákat
            MCvScalar lowerBound = new MCvScalar(MinBlue, MinGreen, MinRed);
            MCvScalar upperBound = new MCvScalar(MaxBlue, MaxGreen, MaxRed);

            // Lefuttatjuk a színszűrést, ami a megadott BGR sáv közé eső pixeleket fehérré (255), a többit feketévé (0) teszi
            CvInvoke.InRange(inputImg, new ScalarArray(lowerBound), new ScalarArray(upperBound), binaryMask);

            if (context.WorkingMask != null) context.WorkingMask.Dispose();
            context.WorkingMask = binaryMask;
        }
    }


    // ====================================================================
    // --- MÓDOSÍTOTT OTSU SZUPER FÚZIÓS MODUL ---
    // ====================================================================
    [Serializable]
    public class OtsuSuperFusionModule : BaseInspectionModule
    {
        [System.ComponentModel.Browsable(false)]
        public override string PureName => "Otsu Super Fusion (2R + H + 2L)"; // Tisztított név

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
    // --- REFERENCIA VÁLASZTÓ LEGÖRDÜLŐ MENÜ CONVERTEREK ---
    // ====================================================================
    public class ReferenceFeatureConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            List<string> references = new List<string>
                {
                    "None (Fix koordináta, nincs követés)",
                    "Template_Matching_Multi_Sablon"
                };
            return new StandardValuesCollection(references);
        }
    }
}