using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;

namespace FlexVision // Biztosítsd, hogy ez megegyezzen a Form és a Control névterével!
{
    // --- 1. LÉPÉS: AZ ENUM DEFINÍCIÓ ---
    public enum VisionModuleType
    {
        ImageLoad,
        OtsuFusion,
        BlobDetect
    }

    // --- 2. LÉPÉS: ADATKÖRNYEZET (CONTEXT) ---
    public class InspectionContext : IDisposable
    {
        // A legfrissebb színes és szürke munkaképek (Alapértelmezett lánchoz)
        public Image<Bgr, byte> SourceImage { get; set; }
        public Image<Gray, byte> WorkingMask { get; set; }

        // ÚJ: A Pipeline történeti memóriatára! 
        // Ide mentünk el minden egyes lépés után egy-egy teljes másolatot a képállapotról.
        public Dictionary<string, Image<Bgr, byte>> ColorHistory { get; set; } = new Dictionary<string, Image<Bgr, byte>>();
        public Dictionary<string, Image<Gray, byte>> GrayHistory { get; set; } = new Dictionary<string, Image<Gray, byte>>();

        public Dictionary<string, object> CustomData { get; set; } = new Dictionary<string, object>();

        // ÚJ SEGÉDFÜGGVÉNY: Ezzel a modulok biztonságosan lekérhetik a számukra beállított forrásképet
        public Image<Bgr, byte> GetInputColorImage(string stepName)
        {
            if (string.IsNullOrEmpty(stepName) || !ColorHistory.ContainsKey(stepName))
                return SourceImage; // Alapértelmezett fallback, ha nincs egyedi forrás beállítva
            return ColorHistory[stepName];
        }

        public void Dispose()
        {
            SourceImage?.Dispose(); SourceImage = null;
            WorkingMask?.Dispose(); WorkingMask = null;
            foreach (var img in ColorHistory.Values) img?.Dispose();
            foreach (var img in GrayHistory.Values) img?.Dispose();
            ColorHistory.Clear();
            GrayHistory.Clear();
        }
    }


    // --- 3. LÉPÉS: AZ ALAP INTERFÉSZ ---
    public interface IInspectionModule
    {
        string Name { get; }
        VisionModuleType ModuleType { get; }
        void Process(InspectionContext context);
    }

    // --- 4. LÉPÉS: KÉPBETÖLTŐ MODUL ---
    [Serializable]
    public class ImageLoadModule : IInspectionModule
    {
        public string Name => "01. Source Image Capture";
        public VisionModuleType ModuleType => VisionModuleType.ImageLoad;

        [Category("Fájl Beállítások"), Description("A vizsgálandó kép abszolút elérési útja.")]
        public string FilePath { get; set; } = @"C:\Test\BoundTest3.bmp";

        public ImageLoadModule() { } // XML-hez szükséges üres konstruktor

        public void Process(InspectionContext context)
        {
            if (System.IO.File.Exists(FilePath))
            {
                context.SourceImage = new Image<Bgr, byte>(FilePath);
                context.WorkingMask = context.SourceImage.Convert<Gray, byte>();
            }
        }
    }

    // --- 5. LÉPÉS: OTSU SZUPER-FÚZIÓS MODUL ---
    [Serializable]
    public class OtsuSuperFusionModule : IInspectionModule
    {
        public string Name => "02. Otsu Super Fusion (2R + H + 2L)";
        public VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        [Category("1. Elmosás")] public int BlurSize { get; set; } = 15;
        [Category("2. Morfológia")] public bool EnableErode { get; set; } = true;
        [Category("2. Morfológia")] public int ErodeIterations { get; set; } = 1;

        public OtsuSuperFusionModule() { } // XML-hez szükséges üres konstruktor

        public void Process(InspectionContext context)
        {
            if (context.SourceImage == null) return;

            int w = context.SourceImage.Width;
            int h = context.SourceImage.Height;

            using (Image<Hsv, byte> hsvImage = context.SourceImage.Convert<Hsv, byte>())
            using (Image<Lab, byte> labImage = context.SourceImage.Convert<Lab, byte>())
            using (Image<Gray, byte> chR = new Image<Gray, byte>(w, h))
            using (Image<Gray, byte> chH = new Image<Gray, byte>(w, h))
            using (Image<Gray, byte> chL = new Image<Gray, byte>(w, h))
            {
                CvInvoke.ExtractChannel(context.SourceImage.Mat, chR.Mat, 2);
                CvInvoke.ExtractChannel(hsvImage.Mat, chH.Mat, 0);
                CvInvoke.ExtractChannel(labImage.Mat, chL.Mat, 0);

                using (Image<Gray, float> fR = chR.Convert<Gray, float>())
                using (Image<Gray, float> fH = chH.Convert<Gray, float>())
                using (Image<Gray, float> fL = chL.Convert<Gray, float>())
                using (Image<Gray, float> fSumRL = new Image<Gray, float>(w, h))
                using (Image<Gray, float> fTmpRH = new Image<Gray, float>(w, h))
                using (Image<Gray, float> fSumRHL = new Image<Gray, float>(w, h))
                using (Image<Gray, float> fSuperFloat = new Image<Gray, float>(w, h))
                using (Image<Gray, byte> byteFusion = new Image<Gray, byte>(w, h))
                {
                    CvInvoke.Add(fR, fL, fSumRL);
                    CvInvoke.Add(fR, fH, fTmpRH);
                    CvInvoke.Add(fTmpRH, fL, fSumRHL);
                    CvInvoke.Add(fSumRL, fSumRHL, fSuperFloat);

                    fSuperFloat.Mat.ConvertTo(byteFusion.Mat, Emgu.CV.CvEnum.DepthType.Cv8U, 0.2, 0);

                    using (Image<Gray, byte> blurMask = new Image<Gray, byte>(w, h))
                    {
                        CvInvoke.GaussianBlur(byteFusion, blurMask, new Size(BlurSize, BlurSize), 3);

                        Image<Gray, byte> finalBinaryMask = new Image<Gray, byte>(w, h);
                        CvInvoke.Threshold(blurMask, finalBinaryMask, 0.0, 255.0, Emgu.CV.CvEnum.ThresholdType.Binary | Emgu.CV.CvEnum.ThresholdType.Otsu);

                        if (EnableErode)
                        {
                            using (Mat element = CvInvoke.GetStructuringElement(Emgu.CV.CvEnum.ElementShape.Rectangle, new Size(3, 3), new Point(-1, -1)))
                            {
                                CvInvoke.MorphologyEx(finalBinaryMask, finalBinaryMask, Emgu.CV.CvEnum.MorphOp.Erode, element, new Point(-1, -1), ErodeIterations, Emgu.CV.CvEnum.BorderType.Constant, new MCvScalar());
                            }
                        }

                        if (context.WorkingMask != null) context.WorkingMask.Dispose();
                        context.WorkingMask = finalBinaryMask;
                    }
                }
            }
        }
    }
}
