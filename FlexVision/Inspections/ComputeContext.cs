using System;
using System.Collections.Generic;

using System.Runtime.Serialization;

using OpenCL.Net;

namespace AOI.Devices.OpenCl
{
   public class ComputeContext
    {
        [Serializable]
        public class OpenClError : Exception
        {
            // Constructors
            public OpenClError()
                : base("OpenClError")
            { }

            // Ensure Exception is Serializable
            protected OpenClError(SerializationInfo info, StreamingContext ctxt)
                : base(info, ctxt)
            { }
        }



        public void initialize()
        {
            ErrorCode errorCode;


            // http://www.codeproject.com/Articles/502829/GPGPU-image-processing-basics-using-OpenCL-NET
            // license  The Code Project Open License (CPOL)
            // snip ===

            OpenCL.Net.Platform[] platforms = Cl.GetPlatformIDs(out errorCode);
            if (errorCode != ErrorCode.Success)
            {
                throw new OpenClError();
            }

            List<OpenCL.Net.Device> devicesList = new List<OpenCL.Net.Device>();


            foreach (OpenCL.Net.Platform platform in platforms)
            {
                string platformName = Cl.GetPlatformInfo(platform, OpenCL.Net.PlatformInfo.Name, out errorCode).ToString();
                if (errorCode != ErrorCode.Success)
                {
                    throw new OpenClError();
                }

                Console.WriteLine("Platform: " + platformName);

                //We will be looking only for GPU devices
                foreach (OpenCL.Net.Device device in Cl.GetDeviceIDs(platform, OpenCL.Net.DeviceType.Gpu, out errorCode))
                {
                    if (errorCode != ErrorCode.Success)
                    {
                        throw new OpenClError();
                    }

                    Console.WriteLine("Device: " + device.ToString());
                    devicesList.Add(device);
                }
            }

            if (devicesList.Count <= 0)
            {
                Console.WriteLine("No devices found.");
                throw new OpenClError();
            }

            if (devicesList.Count > 1)
                device = devicesList[1];
            else
                device = devicesList[0];

            InfoBuffer Dv = Cl.GetDeviceInfo(devicesList[0], DeviceInfo.Name, out errorCode);
            if (errorCode != ErrorCode.Success)
            {
                throw new OpenClError();
            }

            this.GPUDeviceName = Dv.ToString();

            // end snip ===


            context = Cl.CreateContext(null, 1, new Device[] { device }, null, IntPtr.Zero, out errorCode);
            if (errorCode != ErrorCode.Success)
            {
                throw new OpenClError();
            }

            commandQueue = Cl.CreateCommandQueue(context, device, CommandQueueProperties.OutOfOrderExecModeEnable, out errorCode);
            if (errorCode != ErrorCode.Success)
            {
                throw new OpenClError();
            }
        }

        public bool ImageSupported()
        {
            ErrorCode errorCode;
            if (Cl.GetDeviceInfo( device, DeviceInfo.ImageSupport, out errorCode).CastTo<Bool>() == Bool.False)
            {
                return false;
            }
            else
            {
                return true;
            }

        }




    public static void throwErrorIfNotSuccessfull(ErrorCode errorCode, string message)
        {
            if (errorCode != ErrorCode.Success)
            {
               
                throw new OpenClError();
            }
        }

        public OpenCL.Net.Context context;
        public OpenCL.Net.CommandQueue commandQueue;
        public OpenCL.Net.Device device;
        public string GPUDeviceName = "None";
    }
}