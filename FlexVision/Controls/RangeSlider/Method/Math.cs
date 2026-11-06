using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Math;

namespace Slider.Methods
{
  public class Math
  {
    public static int LinConvert(int value1, int max1, int min1, int max2, int min2)
    {
      float r = (float)(max2 - min2) / (max1 - min1);
      return (int)Round(min2 + (value1 - min1) * r);
    }

    public static float LinConvert(float value1, float max1, float min1, float max2, float min2)
    {
      float r = (max2 - min2) / (max1 - min1);
      return (min2 + (value1 - min1) * r);
    }

    public static double LinConvert(double value1, double max1, double min1, double max2, double min2)
    {
      double r = (max2 - min2) / (max1 - min1);
      return (min2 + (value1 - min1) * r);
    }

    public static float Interpolate(int StartValue, int EndValue, float Ratio)
    {
      return StartValue + Ratio * (EndValue - StartValue);
    }

    public static int Clamp(float value, int Max, int Min)
    {
      if (Min > Max)
        Swap(ref Max, ref Min);

      if (value > Max)
        return Max;
      else if (value < Min)
        return Min;
      else
        return (int)value;
    }

    public static void Swap<T>(ref T x, ref T y)
    {
      T tmp = x;
      x = y;
      y = tmp;
    }

    public static bool IsIn(int value, int Max, int Min)
    {
      if (Min > Max)
        Swap(ref Max, ref Min);
      return (value <= Max && value >= Min) ? true : false;
    }

    public static bool IsIn(float value, int Max, int Min)
    {
      if (Min > Max)
        Swap(ref Max, ref Min);
      return (value <= Max && value >= Min) ? true : false;
    }

    public static bool IsIn(float value, int Max, int Min, bool excludeBoundary)
    {
      if (!excludeBoundary)
        return IsIn(value, Max, Min);
      else
      {
        if (Min > Max)
          Swap(ref Max, ref Min);
        return (value < Max && value > Min) ? true : false;
      }
    }

    public static double LogBase(double Base, double num)
    {
      return Log(num) / Log(Base);
    }
  }
}
