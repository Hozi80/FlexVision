using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Emgu.CV;
using Emgu.CV.Structure;

namespace FlexVision
{
    public enum SaveConditionMode
    {
        Always,     // Minden ciklusban ment
        OnlyPass,   // Csak ha az eddigi lépések sikeresek (PASS)
        OnlyFail    // Csak hiba / selejt esetén ment (Archiválás)
    }

    public enum SaveImageFormat
    {
        Png,
        Jpg,
        Bmp
    }

    [Serializable]
    public class ImageSaveModule : BaseInspectionModule
    {
        [Browsable(false)]
        public override string PureName => "Save Image";

        [Browsable(false)]
        public override VisionModuleType ModuleType => VisionModuleType.ImageLoad;

        [Category("1. Mentési Beállítások")]
        [Description("A célmappa elérési útja, ahová a képek kerülnek.")]
        public string OutputFolder { get; set; } = @"C:\Test\SavedResults";

        [Category("1. Mentési Beállítások")]
        [Description("Fájlnév előtag (pl. Inspection, Defect, Overlay).")]
        public string FilePrefix { get; set; } = "Result";

        [Category("1. Mentési Beállítások")]
        [Description("Ha igaz, beilleszti az ImageLoad által kinyert szériaszámot a fájlnévbe.")]
        public bool IncludeSerialNumber { get; set; } = true;

        [Category("1. Mentési Beállítások")]
        [Description("Ha igaz, dátumot és pontos időt (ezredmásodperccel) fűz a fájlnévhez.")]
        public bool AppendTimestamp { get; set; } = true;

        [Category("1. Mentési Beállítások")]
        public SaveImageFormat Format { get; set; } = SaveImageFormat.Png;

        [Category("2. Feltételes Mentés")]
        [Description("Mikor történjen meg a képmentés?")]
        public SaveConditionMode Condition { get; set; } = SaveConditionMode.Always;

        [Category("2. Feltételes Mentés")]
        [Description("Ha igaz, kizárólag a teljes főképet menti el, az al-hurkok (sub-loop) kisképeit soha.")]
        public bool OnlyMainImage { get; set; } = true;

        [Category("3. Diagnosztika"), ReadOnly(true)]
        public string LastSavedPath { get; set; } = "-";

        public ImageSaveModule() { }

        public override void Process(InspectionContext context)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            this.Status = ModuleStatus.NotExecuted;

            try
            {
                // 1. SZIGORÚ VÉDELEM: Ha sub-loop (kiskép) kontextusban vagyunk, azonnal kilépünk!
                if (this.OnlyMainImage && context.Parent != null)
                {
                    sw.Stop();
                    this.Status = ModuleStatus.Success;
                    return;
                }

                // 2. Feltétel ellenőrzése (PASS / FAIL státusz az előző lépések alapján)
                // 2. Feltétel ellenőrzése (főág + al-hurkok összesített PASS / FAIL státusza alapján)
                bool pipelineOk = true;

                // Ha a futtató motor rögzített bármilyen al-hurok hibát a kontextusban:
                if (context.CustomData != null && context.CustomData.TryGetValue("AnyStepFailed", out object failObj) && failObj is bool anyFailed && anyFailed)
                {
                    pipelineOk = false;
                }

                // Ellenőrizzük a főág lépéseinek státuszát is
                if (pipelineOk && ReceptRegistry.ActiveSteps != null)
                {
                    foreach (var step in ReceptRegistry.ActiveSteps)
                    {
                        if (step == this) break;
                        if (step.Status == ModuleStatus.Failed)
                        {
                            pipelineOk = false;
                            break;
                        }
                    }
                }


                if (this.Condition == SaveConditionMode.OnlyPass && !pipelineOk)
                {
                    sw.Stop();
                    this.Status = ModuleStatus.Success;
                    return;
                }
                if (this.Condition == SaveConditionMode.OnlyFail && pipelineOk)
                {
                    sw.Stop();
                    this.Status = ModuleStatus.Success;
                    return;
                }

                // 3. Forráskép lekérése
                Image<Bgr, byte> imgToSave = null;
                if (string.IsNullOrEmpty(this.InputStepLink) || this.InputStepLink == "Default (Előző lépés)")
                {
                    imgToSave = context.SourceImage?.Copy();
                }
                else
                {
                    imgToSave = context.GetInputColorImage(this.InputStepLink);
                }

                if (imgToSave == null || imgToSave.Ptr == IntPtr.Zero)
                {
                    sw.Stop();
                    this.Status = ModuleStatus.Failed;
                    return;
                }

                using (imgToSave)
                {
                    // 4. MÁSODIK VÉDELEM: Ha a kép kisebb, mint a főág legelső (teljes) képe, akkor ez egy al-kép -> kihagyjuk!
                    if (this.OnlyMainImage && context.ColorHistory != null && context.ColorHistory.Count > 0)
                    {
                        var firstMainImg = context.ColorHistory.Values.FirstOrDefault();
                        if (firstMainImg != null && firstMainImg.Ptr != IntPtr.Zero)
                        {
                            if (imgToSave.Width < firstMainImg.Width || imgToSave.Height < firstMainImg.Height)
                            {
                                sw.Stop();
                                this.Status = ModuleStatus.Success;
                                return;
                            }
                        }
                    }

                    // 5. Mappa létrehozása és fájlnév összeállítása (szériaszámmal)
                    if (!Directory.Exists(this.OutputFolder))
                    {
                        Directory.CreateDirectory(this.OutputFolder);
                    }

                    string serialPart = "";
                    if (this.IncludeSerialNumber && context.CustomData != null && context.CustomData.TryGetValue("SerialNumber", out object snObj))
                    {
                        string rawSn = snObj?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(rawSn) && rawSn != "-" && rawSn != "FILE NOT FOUND" && rawSn != "INDEX_OUT_OF_RANGE")
                        {
                            string safeSn = SanitizeFileName(rawSn);
                            if (!string.IsNullOrEmpty(safeSn))
                            {
                                serialPart = $"_{safeSn}";
                            }
                        }
                    }

                    string ext = this.Format.ToString().ToLower();
                    string statusTag = pipelineOk ? "PASS" : "FAIL";
                    string timestamp = this.AppendTimestamp ? $"_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{statusTag}" : $"_{statusTag}";
                    string fileName = $"{this.FilePrefix}{serialPart}{timestamp}.{ext}";
                    string fullPath = Path.Combine(this.OutputFolder, fileName);

                    imgToSave.Save(fullPath);
                    this.LastSavedPath = fullPath;
                }

                sw.Stop();
                this.Status = ModuleStatus.Success;
            }
            catch (Exception ex)
            {
                sw.Stop();
                this.Status = ModuleStatus.Failed;
                Console.WriteLine($"[ImageSaveModule] Hiba: {ex.Message}");
            }
            finally
            {
                this.ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
            }
        }

        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
