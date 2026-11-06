using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Slider.Methods
{
  public class System
  {
    /*	TimeIt Example:
    *	1. 
    *	Code code = () => { string s = "Your Codes"; };
    *	TimeIt(code); 
    *	2.
    *	TimeIt( () => { string s = "Your Codes"; } );
    */
    public delegate void Code();
    public static void TimeIt(Code code)
    {
      Stopwatch sw = new Stopwatch();
      sw.Reset();
      sw.Start();
      code.Invoke();

      sw.Stop();
      string result = sw.Elapsed.TotalMilliseconds.ToString();
      Console.WriteLine(result);
    }

    /*	GetAllControls Exmaple:	
		 *	List<Control> AllControls = GetAllControls(Form); 
		 */
    public static List<Control> GetAllControls(Form form)
    {
      return GetAllControls(ToList(form.Controls));
    }
    public static List<Control> ToList(Control.ControlCollection controls)
    {
      List<Control> controlList = new List<Control>();
      foreach (Control control in controls)
        controlList.Add(control);
      return controlList;
    }
    public static List<Control> GetAllControls(List<Control> inputList)
    {
      List<Control> outputList = new List<Control>(inputList);
      IEnumerable<Control> containers = from control in inputList
                                        where
              control is GroupBox |
              control is TabControl |
              control is Panel |
              control is FlowLayoutPanel |
              control is TableLayoutPanel |
              control is ContainerControl
                                        select control;


      foreach (Control container in containers)
      {
        outputList.AddRange(GetAllControls(ToList(container.Controls)));
      }
      return outputList;
    }

    public static TextFormatFlags GetTextFormatFlags(ContentAlignment textAlign)
    {
      TextFormatFlags controlFlags = TextFormatFlags.EndEllipsis;

      switch (textAlign)
      {
        case ContentAlignment.TopLeft:
          controlFlags |= TextFormatFlags.Top | TextFormatFlags.Left;
          break;
        case ContentAlignment.TopCenter:
          controlFlags |= TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
          break;
        case ContentAlignment.TopRight:
          controlFlags |= TextFormatFlags.Top | TextFormatFlags.Right;
          break;
        case ContentAlignment.MiddleLeft:
          controlFlags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
          break;
        case ContentAlignment.MiddleCenter:
          controlFlags |= TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
          break;
        case ContentAlignment.MiddleRight:
          controlFlags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
          break;
        case ContentAlignment.BottomLeft:
          controlFlags |= TextFormatFlags.Bottom | TextFormatFlags.Left;
          break;
        case ContentAlignment.BottomCenter:
          controlFlags |= TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
          break;
        case ContentAlignment.BottomRight:
          controlFlags |= TextFormatFlags.Bottom | TextFormatFlags.Right;
          break;
      }

      return controlFlags;
    }
  }
}
