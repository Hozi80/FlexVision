using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FlexVision
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UserSession.Instance.Login("Developer", UserRole.Administrator);
            HardwareAccelerator.Initialize();
            Application.Run(new MainVisionForm()); // Ez indítja az új indusztriális keretrendszert
        }
    }

}
