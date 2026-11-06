using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;

namespace FlexVision
{
    // ====================================================================
    // --- RELATÍV OFFSET ALAPÚ AL-PIPELINE GENERÁLÓ MODUL (FIXTURE LOOP) ---
    // ====================================================================
    [Serializable]
    public class SubRoiGeneratorModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "Sub-ROI Generator (Relatív Offset Követő)";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion;

        // --- DINAMIKUS IPARI BEÁLLÍTÁSOK ---
        [Category("1. Relatív Eltolás (Offset)"), Description("Y irányú eltolás pixelben a megtalált sablon középpontjához képest. (A huzalméréshez pl. -760 pixel a fel ugráshoz).")]
        public int OffsetY { get; set; } = -760;

        [Category("1. Relatív Eltolás (Offset)"), Description("X irányú eltolás pixelben a megtalált sablon középpontjához képest.")]
        public int OffsetX { get; set; } = 0;

        [Category("2. Új Lokális ROI Mérete"), Description("A kivágandó új mérési ablak szélessége pixelben.")]
        public int RoiWidth { get; set; } = 250;

        [Category("2. Új Lokális ROI Mérete"), Description("A kivágandó új mérési ablak magassága pixelben.")]
        public int RoiHeight { get; set; } = 200;

        public SubRoiGeneratorModule() { }

        public override void Process(InspectionContext context)
        {
            Image<Bgr, byte> baseGlobalColor = context.GetInputColorImage(this.InputStepLink);
            if (baseGlobalColor == null) return;

            if (context.SubContexts == null) context.SubContexts = new List<InspectionContext>();
            context.SubContexts.Clear();

            // 1. ALAPÉRTELMEZETT KIINDULÁSI ÉRTÉKEK (Ha nincs követés beállítva)
            // Tegyük fel, hogy a betanításkori elméleti Master pontunk a kép közepe volt (pl. 500, 500)
            PointF liveTargetCenter = new PointF(500, 500);
            double liveRotationAngle = 0.0;

            // --- A) X TENGELY KOORDINÁTA KINYERÉSE ---
            if (this.ReferenceXSrc != "None (Fix koordináta)" && !string.IsNullOrEmpty(this.ReferenceXSrc))
            {
                string normXKey = this.ReferenceXSrc.Replace("_", ".");
                foreach (var feat in context.Features)
                {
                    if (feat.Key.Replace("_", ".").Contains(normXKey) && feat.Value is PointFeature pf)
                    {
                        liveTargetCenter.X = pf.Position.X; // Csak az X értéket vesszük át!
                        break;
                    }
                }
            }

            // --- B) Y TENGELY KOORDINÁTA KINYERÉSE (ITT ADJUK HOZZÁ A RELATÍV ELTOLÁST!) ---
            if (this.ReferenceYSrc != "None (Fix koordináta)" && !string.IsNullOrEmpty(this.ReferenceYSrc))
            {
                string normYKey = this.ReferenceYSrc.Replace("_", ".");
                foreach (var feat in context.Features)
                {
                    if (feat.Key.Replace("_", ".").Contains(normYKey) && feat.Value is PointFeature pf)
                    {
                        liveTargetCenter.Y = pf.Position.Y; // Csak az Y értéket vesszük át!
                        break;
                    }
                }
            }

            // --- C) ROTÁCIÓ (SZÖG) KINYERÉSE ---
            if (this.ReferenceRotSrc != "None (Fix elfordulás)" && !string.IsNullOrEmpty(this.ReferenceRotSrc))
            {
                string normRotKey = this.ReferenceRotSrc.Replace("_", ".");
                foreach (var feat in context.Features)
                {
                    if (feat.Key.Replace("_", ".").Contains(normRotKey) && feat.Value is RectangleFeature rf)
                    {
                        liveRotationAngle = rf.RotationAngle;
                        break;
                    }
                }
            }

            // 2. FINOM MIKRONPONTOS KOMPOZIT OFFSET SZÁMÍTÁS
            // Összegyúrjuk a 3 különböző forrásból jövő adatot, és hozzáadjuk a te egyedi relatív ugrásodat!
            int finalX = (int)(liveTargetCenter.X + this.OffsetX - (this.RoiWidth / 2));
            int finalY = (int)(liveTargetCenter.Y + this.OffsetY - (this.RoiHeight / 2));

            Rectangle liveSubRoi = new Rectangle(finalX, finalY, this.RoiWidth, this.RoiHeight);

            // Guardrail a kép szélei ellen
            if (liveSubRoi.X < 0) liveSubRoi.X = 0;
            if (liveSubRoi.Y < 0) liveSubRoi.Y = 0;
            if (liveSubRoi.X + liveSubRoi.Width > baseGlobalColor.Width) liveSubRoi.Width = baseGlobalColor.Width - liveSubRoi.X;
            if (liveSubRoi.Y + liveSubRoi.Height > baseGlobalColor.Height) liveSubRoi.Height = baseGlobalColor.Height - liveSubRoi.Y;

            // 3. AL-KONTEXTUS (SUB-CONTEXT) GENERÁLÁS A PONTOSÍTOTT ADATOKKAL
            InspectionContext subContext = new InspectionContext();
            using (Bitmap globalBmp = baseGlobalColor.ToBitmap())
            {
                Bitmap croppedBmp = CropImage(globalBmp, liveSubRoi);
                subContext.SourceImage = new Image<Bgr, byte>(croppedBmp);
                subContext.WorkingMask = subContext.SourceImage.Convert<Gray, byte>();
                croppedBmp.Dispose();
            }

            // Átadjuk az eltolt al-fejlécnek a kompozit pozíciót és a pontos elfordulási szöget!
            subContext.LocalOffset = new PointF(liveSubRoi.X, liveSubRoi.Y);
            subContext.LocalAngle = liveRotationAngle;

            string roiKey = $"{this.EffectiveFeatureName}_Zone_1";
            context.Features[roiKey] = new RectangleFeature(roiKey, this.Name, liveSubRoi, liveRotationAngle);

            context.SubContexts.Add(subContext);
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