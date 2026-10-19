using System;
using System.ComponentModel;

public class SystemConfiguration
{
    private static SystemConfiguration _instance;

    // JAVÍTÁS: Klasszikus tulajdonság (Property) C# 7.3-as verzióhoz ??= helyett
    public static SystemConfiguration Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new SystemConfiguration();
            }
            return _instance;
        }
    }

    private SystemConfiguration() { }

    [Category("1. GIGÉ / USB3 KAMERA"), Description("A vizsgáló kamera egyedi IP címe.")]
    public string CameraIP { get; set; } = "192.168.1.120";

    [Category("1. GIGÉ / USB3 KAMERA"), Description("Kalibrációs tényező: 1 pixel mérete valós milliméterben (mm/pixel).")]
    public double PixelToMmRatio { get; set; } = 1; // Alapértelmezett mikroszkópos érték


    [Category("1. GIGÉ / USB3 KAMERA"), Description("Az expozíciós idő (shutter speed) mikroszekundumban.")]
    public int ExposureTimeUs { get; set; } = 4800;

    [Category("2. IPARI KOMMUNIKÁCIÓ (PLC)"), Description("A ModbusTCP vagy Profinet IO interfész címe.")]
    public string PlcAddress { get; set; } = "192.168.1.10";

    [Category("2. IPARI KOMMUNIKÁCIÓ (PLC)"), Description("A trigger jelet fogadó hardveres pin száma.")]
    public int InputTriggerPin { get; set; } = 2;

    [Category("3. MEGVILÁGÍTÁS VEZÉRLÉS"), Description("A LED stroboszkóp áramerőssége százalékban.")]
    public int LightIntensityPercent { get; set; } = 85;

    [Category("4. NYOMONKÖVETÉS (MES)"), Description("Aktiválja a mérési eredmények gyári SQL/REST küldését.")]
    public bool EnableMesReporting { get; set; } = true;

    [Category("4. NYOMONKÖVETÉS (MES)"), Description("A gyári szerver API végpontja.")]
    public string MesEndpointUrl { get; set; } = "http://factory.internal";
}
