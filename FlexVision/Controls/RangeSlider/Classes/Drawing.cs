using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Slider.Classes
{
  public sealed class ShadeColors
  {
    private static int ThumbNormal = 120;
    private static int ThumbFocus = ThumbNormal - 40;
    private static int ThumbPressed = ThumbFocus - 60;
    private static int ThumbDisabled = ThumbNormal + 50;
    private static int BarNormal = ThumbNormal + 50;
    private static int BarFocus = ThumbFocus + 50;
    private static int BarPressed = BarFocus;
    private static int BarDisabled = BarNormal + 50;
    public sealed class Thumb
    {
      public static Color Normal = Color.FromArgb(ThumbNormal, ThumbNormal, ThumbNormal);
      public static Color Focus = Color.FromArgb(ThumbFocus, ThumbFocus, ThumbFocus);
      public static Color Pressed = Color.FromArgb(ThumbPressed, ThumbPressed, ThumbPressed);
      public static Color Disabled = Color.FromArgb(ThumbDisabled, ThumbDisabled, ThumbDisabled);
    }
    public sealed class Bar
    {
      public static Color Normal = Color.FromArgb(BarNormal, BarNormal, BarNormal);
      public static Color Focus = Color.FromArgb(BarFocus, BarFocus, BarFocus);
      public static Color Pressed = Color.FromArgb(BarPressed, BarPressed, BarPressed);
      public static Color Disabled = Color.FromArgb(BarDisabled, BarDisabled, BarDisabled);
    }
    public sealed class Text
    {
      public static Color Normal = Color.FromArgb(0, 0, 0);
      public static Color Focus = Color.FromArgb(255, 255, 255);
      public static Color Pressed = Color.FromArgb(255, 255, 255);
      public static Color Disabled = Color.FromArgb(255, 255, 255);
    }
  }

  public enum EMyColors
  {
    Blue,
    Green,
    Red,
    Black,
    White,
    Silver,
    Lime,
    Teal,
    Orange,
    Brown,
    Pink,
    Magenta,
    Purple,
    Yellow
  }

  public sealed class MyColors
  {
    public static Color Blue = Color.FromArgb(67, 133, 246);
    public static Color Green = Color.FromArgb(52, 168, 85);
    public static Color Red = Color.FromArgb(234, 66, 53);
    public static Color Black = Color.FromArgb(0, 0, 0);
    public static Color White = Color.FromArgb(255, 255, 255);
    public static Color Silver = Color.FromArgb(85, 85, 85);
    public static Color Lime = Color.FromArgb(142, 188, 0);
    public static Color Teal = Color.FromArgb(0, 170, 173);
    public static Color Orange = Color.FromArgb(243, 119, 53);
    public static Color Brown = Color.FromArgb(165, 81, 0);
    public static Color Pink = Color.FromArgb(231, 113, 189);
    public static Color Magenta = Color.FromArgb(255, 0, 148);
    public static Color Purple = Color.FromArgb(124, 65, 153);
    public static Color Yellow = Color.FromArgb(255, 196, 37);
  }

  public class PixelImage : ICloneable
  {
    private Bitmap _Bitmap;
    public Bitmap Bitmap
    {
      get
            {
                return _Bitmap;
            }
      set
      {
        _Bitmap = value;
        Bitmap2Pixel();
      }
    }
    private byte[] _Pixel;
    public byte[] Pixel
    {
      get
            { return _Pixel; }
      set
      {
        _Pixel = value;
        Pixel2Bitmap();
      }
    }
    public int Byte = 4; //Format32bppArgb

    public int Stride => Width * Byte;  //Format32bppArgb
    public int Width => _Bitmap.Width;
    public int Height => _Bitmap.Height;
    public Size Size => _Bitmap.Size;
    public static PixelFormat PixelFormat = PixelFormat.Format32bppArgb;

    #region Constructors
    public PixelImage()
    {

    }

    public PixelImage(Bitmap bitmap)
    {
      this.Bitmap = new Bitmap(bitmap); 
    }

    public PixelImage(Size size)
    {
      _Bitmap = new Bitmap(size.Width, size.Height); 
    }

    public PixelImage(byte[] pixel, Size size)
    {
      _Bitmap = new Bitmap(size.Width, size.Height); 
      this.Pixel = pixel; 
    }

    public object Clone()
    {
      return new PixelImage()
      {
        _Bitmap = (Bitmap)this._Bitmap.Clone(),
        _Pixel = (byte[])this._Pixel.Clone(),
        Byte = this.Byte
      };
    } 
    #endregion

    #region Private Methods
    private void Pixel2Bitmap()
    {
      BitmapData bitmapData = _Bitmap.LockBits(
      new Rectangle(0, 0, _Bitmap.Width, _Bitmap.Height),
      ImageLockMode.ReadOnly,
      PixelFormat);


      Marshal.Copy(_Pixel, 0, bitmapData.Scan0, _Pixel.Length);

    
      _Bitmap.UnlockBits(bitmapData);
    }

    private void Bitmap2Pixel()
    {

      BitmapData bitmapData = _Bitmap.LockBits(
        new Rectangle(0, 0, _Bitmap.Width, _Bitmap.Height),
        ImageLockMode.ReadOnly,
        PixelFormat);

    
      _Pixel = new byte[bitmapData.Stride * _Bitmap.Height];

    
      Marshal.Copy(bitmapData.Scan0, _Pixel, 0, _Pixel.Length);

    
      _Bitmap.UnlockBits(bitmapData); 

    }
    #endregion
  }

  public enum HVOrientation
  {
    Horizontal,
    Vertical,
  }

  public enum UDLROrientation
  {
    Down,
    Right,
    Up,
    Left
  }
}
