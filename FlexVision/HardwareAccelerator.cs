using System;
using Emgu.CV;
using AOI.Devices.OpenCl;

namespace FlexVision
{
    public static class HardwareAccelerator
    {
        public static ComputeContext OpenClContext { get; private set; }
        public static bool IsOpenClAvailable { get; private set; } = false;
        public static string HardwareName { get; private set; } = "CPU (No Acceleration)";

        public static void Initialize()
        {
            try
            {
                Console.WriteLine("[FlexVision] Initializing Hardware Acceleration...");

                // 1. Emgu CV beépített OpenCL gyorsításának aktiválása
                if (CvInvoke.HaveOpenCL)
                {
                    CvInvoke.UseOpenCL = true;
                    Console.WriteLine("[FlexVision] Emgu CV OpenCL support activated.");
                }

                // 2. A te egyedi OpenCL.Net alapú ComputeContext-ed inicializálása
                OpenClContext = new ComputeContext();
                OpenClContext.initialize();

                if (OpenClContext.ImageSupported())
                {
                    IsOpenClAvailable = true;
                    HardwareName = OpenClContext.GPUDeviceName;
                    Console.WriteLine($"[FlexVision] OpenCL GPU acceleration successfully binded to: {HardwareName}");
                }
                else
                {
                    Console.WriteLine("[FlexVision] GPU initialized but Image Support is missing in OpenCL device.");
                }
            }
            catch (Exception ex)
            {
                IsOpenClAvailable = false;
                HardwareName = "CPU (Fallback Mode)";
                Console.WriteLine("[FlexVision] OpenCL Initialization failed. Falling back to CPU. Details: " + ex.Message);
            }
        }
    }
}
