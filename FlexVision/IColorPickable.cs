using System.Drawing;
using Emgu.CV.Structure;

namespace FlexVision
{
    public interface IColorPickable
    {
        // Ezen keresztül kapja meg a konfigurációs pult a kattintott pixel adatait
        void HandlePickedColor(Bgr bgrColor, Point pixelLocation);
    }
}
