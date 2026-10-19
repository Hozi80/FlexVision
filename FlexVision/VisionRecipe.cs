using RotationTemplateMatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace FlexVision // Biztosítsd, hogy ez megegyezzen a többi fájl névterével!
{
    // --- KRITIKUS JAVÍTÁS C# 7.3 XML SOROSÍTÁSHOZ ---
    // Az XmlInclude attribútumokkal kötelezően megtanítjuk az XML motornak, 
    // hogy a Pipeline listában milyen egyedi modulosztályok fordulhatnak elő!
    [XmlInclude(typeof(ImageLoadModule))]
    [XmlInclude(typeof(OtsuSuperFusionModule))]
    [XmlInclude(typeof(TemplateMatchModule))] // ÚJ: Megtanítjuk az XML-nek a sablonkereső osztályt!
    [XmlInclude(typeof(MatchParameters))]   // ÚJ: Megtanítjuk az XML-nek a belső paraméter-osztályt!

    [Serializable]
    public class VisionRecipe
    {
        public string RecipeName { get; set; } = "Uj_Recept";

        // C# 7.3 kompatibilis, tiszta objektum-lista az interfész helyett a biztos mentéshez
        public List<object> SerializedSteps { get; set; } = new List<object>();

        public VisionRecipe() { } // Kötelező üres konstruktor az XML-hez

        // --- RECEPT MENTÉSE FÁJLBA ---
        public static void SaveToFile(string path, List<IInspectionModule> steps)
        {
            VisionRecipe recipe = new VisionRecipe();

            // Átmásoljuk a modulokat a sorosítható listába
            foreach (var step in steps)
            {
                recipe.SerializedSteps.Add(step);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(VisionRecipe));
            using (StreamWriter writer = new StreamWriter(path))
            {
                serializer.Serialize(writer, recipe);
            }
        }

        // --- RECEPT BETÖLTÉSE FÁJLÍTÓL ---
        public static List<IInspectionModule> LoadFromFile(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(VisionRecipe));
            using (StreamReader reader = new StreamReader(path))
            {
                VisionRecipe recipe = (VisionRecipe)serializer.Deserialize(reader);
                List<IInspectionModule> steps = new List<IInspectionModule>();

                // A betöltött objektumokat biztonságosan visszaforgatjuk az interfész alapú Pipeline-ba
                foreach (var stepObj in recipe.SerializedSteps)
                {
                    if (stepObj is IInspectionModule module)
                    {
                        steps.Add(module);
                    }
                }
                return steps;
            }
        }
    }
}
