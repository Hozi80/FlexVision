

using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RotationTemplateMatch
{

    public class RotationInvariantTemplateMatch
    {
        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern int LoadTemplateFromBitmap(IntPtr FTME, byte[] img, long data_len);

        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern void LoadTestImageFromBitmap(IntPtr FTME, byte[] img, long data_len);

        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern IntPtr Create_FTME();

        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern int Match(IntPtr FTME);

        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern IntPtr GetResultImage(IntPtr FTME, out int Rows, out int Cols,out int Ch, out int Step,out int Depth);
        // unsigned char* GetResultImage(FTME* a_pObject, int& Rows, int& Cols, int& Ch)

        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern void Dispose_FTME(IntPtr FTME);

        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern void Set(IntPtr FTME, double score, double overlap, double Angle, int maxpos, int reducearea, bool smid, bool subpixel, bool first, bool CheckPolarity, int polarityCheckRange, int polarityCheckSides);


        [DllImport(@"FTME.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern int GetResultsData(IntPtr FTME,out IntPtr Score, out IntPtr Angle, out IntPtr CenterX, out IntPtr CenterY,  out IntPtr LBX, out IntPtr LBY, out IntPtr RBX, out IntPtr RBY, out IntPtr LTX, out IntPtr LTY, out IntPtr RTX, out IntPtr RTY);
        

        IntPtr Ftme_Ptr = new IntPtr(0);
        int MaxPos = 1;
        double MaxOverlap = 0;
        double Score = 0.75;
        double ToleranceAngle = 180;
        int MinReduceArea = 256;
        bool SubPixelEstimation = true;
        bool UseSMID = true;
        bool FirstTarget = false;
        public List<TargetMatch> MatchResults = new List<TargetMatch>();
        public long LastMatchTime = 0;
        Size TemplateSize = new Size();
        Size TestedSize = new Size();
        bool PolarityCheck = false;
        int PolaritycheckRange = 0;
        int PolaritycheckSide = 1;


        public RotationInvariantTemplateMatch()
        {
            Ftme_Ptr = Create_FTME();
            UpdateSettings();
        }


        public void Dispose()
        {

            Dispose_FTME(Ftme_Ptr);

        }


/*
        public void SetOptions(TemplateCompareSettings options)
        {
            this.MaxPos = 1;
            this.MaxOverlap = 0.3;
            //this.Score = options.Score;
            this.Score = 0;
            this.ToleranceAngle = options.MaxAngle;
            this.MinReduceArea = 256;
            this.SubPixelEstimation = true;
            this.UseSMID = true;
            this.FirstTarget = false;
            this.PolarityCheck = options.PolarityCheck;
            this.PolaritycheckRange = options.PolaritySideRotation;
            this.PolaritycheckSide = options.PolaritySide;

        }
*/


        public void SetOptions(MatchParameters options, bool userotation)
        {
            this.MaxPos = options.MaxPos;
            this.MaxOverlap = options.MaxOverlap;
            this.Score = options.Score;

            if (userotation)
            this.ToleranceAngle = options.ToleranceAngle;
            else
                this.ToleranceAngle = 0;

            this.MinReduceArea = options.MinReduceArea;
            this.SubPixelEstimation = options.SubPixelEstimation;
            this.UseSMID = options.UseSMID;
            this.FirstTarget = options.FirstTarget;
            this.PolarityCheck = options.PolarityCheck;
            this.PolaritycheckRange = options.PolaritycheckRange;
            this.PolaritycheckSide = options.PolaritycheckSide;

        }


        public  Bitmap  DrawResults(Bitmap tested)
        {
            Image<Bgr, byte> src = new Image<Bgr, byte>(tested);

 
            for (int i = 0; i < MatchResults.Count; i++)
            {
                PointF ptLT = new PointF(MatchResults[i].LeftTop.X , MatchResults[i].LeftTop.Y);
                PointF ptLB = new PointF(MatchResults[i].LeftBottom.X, MatchResults[i].LeftBottom.Y);
                PointF ptRB = new PointF(MatchResults[i].RightBottom.X, MatchResults[i].RightBottom.Y);
                PointF ptRT = new PointF(MatchResults[i].RightTop.X, MatchResults[i].RightTop.Y);
                PointF ptC = new PointF(MatchResults[i].CenterPoint.X, MatchResults[i].CenterPoint.Y);

                src.Draw(new LineSegment2DF( ptLT, ptLB), new Bgr(40, 255, 0),1,Emgu.CV.CvEnum.LineType.AntiAlias);
                src.Draw(new LineSegment2DF(ptLB, ptRB), new Bgr(40, 255, 0), 1, Emgu.CV.CvEnum.LineType.AntiAlias);
                src.Draw(new LineSegment2DF(ptRB, ptRT), new Bgr(40, 255, 0), 1, Emgu.CV.CvEnum.LineType.AntiAlias);
                src.Draw(new LineSegment2DF(ptRT, ptLT), new Bgr(40, 255, 0), 1, Emgu.CV.CvEnum.LineType.AntiAlias);

                src.Draw(new Cross2DF(ptC,6,6), new Bgr(0, 0, 255), 1);
        //        Console.WriteLine(ptC.ToString());
                PointF ptDis1, ptDis2;
                float cols = src.Cols;
                float rows = src.Rows;

                if (cols > rows)
                {
                    ptDis1 = new PointF( (ptLB.X - ptLT.X) / 3 , (ptLB.Y - ptLT.Y) / 3);
                    ptDis2 = new PointF((ptRT.X - ptLT.X) / 3 * (rows / (float)cols), (ptRT.Y - ptLT.Y) / 3 * (rows / (float)cols));
                    //(ptRT - ptLT) / 3 * (rows / (float)cols);
                }
                else
                {
                    ptDis1 = new PointF((ptLB.X - ptLT.X) / 3 * (cols / (float)rows), (ptLB.Y - ptLT.Y) / 3 * (cols / (float)rows));
                    //(ptLB - ptLT) / 3 * (cols / (float)rows);
                    ptDis2 = new PointF((ptRT.X - ptLT.X) / 3, (ptRT.Y - ptLT.Y) / 3);
                    //(ptRT - ptLT) / 3;
                }

                int CornerThickness = 1;

                src.Draw(new LineSegment2DF(ptLT, new PointF(ptLT.X + ptDis1.X / 2, ptLT.Y + ptDis1.Y / 2)), new Bgr(0, 0, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);
                src.Draw(new LineSegment2DF(ptLT, new PointF(ptLT.X + ptDis2.X / 2, ptLT.Y + ptDis2.Y / 2)), new Bgr(0, 0, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);

                src.Draw(new LineSegment2DF(ptRT, new PointF(ptRT.X + ptDis1.X / 2, ptRT.Y + ptDis1.Y / 2)), new Bgr(0, 255, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);
                src.Draw(new LineSegment2DF(ptRT, new PointF(ptRT.X - ptDis2.X / 2, ptRT.Y - ptDis2.Y / 2)), new Bgr(0, 255, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);

                src.Draw(new LineSegment2DF(ptRB, new PointF(ptRB.X - ptDis1.X / 2, ptRB.Y - ptDis1.Y / 2)), new Bgr(0, 255, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);
                src.Draw(new LineSegment2DF(ptRB, new PointF(ptRB.X - ptDis2.X / 2, ptRB.Y - ptDis2.Y / 2)), new Bgr(0, 255, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);

                src.Draw(new LineSegment2DF(ptLB, new PointF(ptLB.X - ptDis1.X / 2, ptLB.Y - ptDis1.Y / 2)), new Bgr(0, 255, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);
                src.Draw(new LineSegment2DF(ptLB, new PointF(ptLB.X + ptDis2.X / 2, ptLB.Y + ptDis2.Y / 2)), new Bgr(0, 255, 255), CornerThickness, Emgu.CV.CvEnum.LineType.AntiAlias);

                /*     line(matColorSrc, ptLT, ptLT + ptDis1 / 2, colorRed, 1, CV_AA);
                     line(matColorSrc, ptLT, ptLT + ptDis2 / 2, colorRed, 1, CV_AA);

                     line(matColorSrc, ptRT, ptRT + ptDis1 / 2, colorYellow, 1, CV_AA);
                     line(matColorSrc, ptRT, ptRT - ptDis2 / 2, colorYellow, 1, CV_AA);

                     line(matColorSrc, ptRB, ptRB - ptDis1 / 2, colorYellow, 1, CV_AA);
                     line(matColorSrc, ptRB, ptRB - ptDis2 / 2, colorYellow, 1, CV_AA);
                     line(matColorSrc, ptLB, ptLB - ptDis1 / 2, colorYellow, 1, CV_AA);
                     line(matColorSrc, ptLB, ptLB + ptDis2 / 2, colorYellow, 1, CV_AA);
                  */
                //

                //            Point LeftMid((ptLT.x +ptLB.x) / 2, (ptLT.y + ptLB.y) / 2);
                //       Point RightMid((ptRT.x +ptRB.x) / 2, (ptRT.y + ptRB.y) / 2);
                //       Point TopMid((ptLT.x +ptRT.x) / 2, (ptLT.y + ptRT.y) / 2);
                //       Point BottomMid((ptLB.x +ptRB.x) / 2, (ptLB.y + ptRB.y) / 2);



                //  DrawMarkCross(matColorSrc, ptC.x, ptC.y, 5, colorRed, 1);

            }
            return src.ToBitmap();
        }
        


    public void GetResults()
        {
            MatchResults.Clear();
      //  List<TargetMatch> res = new List<TargetMatch>();
            
            IntPtr s;
            IntPtr a;
            IntPtr x;
            IntPtr y;
            IntPtr LBX;
            IntPtr LBY;
            IntPtr RBX;
            IntPtr RBY;
            IntPtr LTX;
            IntPtr LTY; 
            IntPtr RTX;
            IntPtr RTY;

            int pcs = GetResultsData(Ftme_Ptr, out s, out a, out x, out y, out LBX, out LBY, out RBX, out RBY, out LTX, out LTY, out RTX, out RTY);

            double[] Scores = new double[pcs];
            double[] Angles = new double[pcs];
            double[] CenterX = new double[pcs];
            double[] CenterY = new double[pcs];

            double[] lbx = new double[pcs];
            double[] lby = new double[pcs];
            double[] rbx = new double[pcs];
            double[] rby = new double[pcs];
            double[] ltx = new double[pcs];
            double[] lty = new double[pcs];
            double[] rtx = new double[pcs];
            double[] rty = new double[pcs];

            Marshal.Copy(s, Scores, 0, pcs);
            Marshal.Copy(a, Angles, 0, pcs);
            Marshal.Copy(x, CenterX, 0, pcs);
            Marshal.Copy(y, CenterY, 0, pcs);
            Marshal.Copy(LBX,lbx, 0, pcs);
            Marshal.Copy(LBY, lby, 0, pcs);
            Marshal.Copy(RBX, rbx, 0, pcs);
            Marshal.Copy(RBY, rby, 0, pcs);
            Marshal.Copy(LTX, ltx, 0, pcs);
            Marshal.Copy(LTY, lty, 0, pcs);
            Marshal.Copy(RTX, rtx, 0, pcs);
            Marshal.Copy(RTY, rty, 0, pcs);

            for (int i = 0; i < pcs; i++)
            {
                PointF C =  new PointF((float)CenterX[i], (float)CenterY[i] );
                PointF LT = new PointF((float)ltx[i], (float)lty[i] );
                PointF LB = new PointF((float)lbx[i], (float)lby[i] );
                PointF RT = new PointF((float)rtx[i], (float)rty[i] );
                PointF RB = new PointF((float)rbx[i], (float)rby[i] );

                MatchResults.Add(new TargetMatch(C,LT,RT, LB, RB, Angles[i], Scores[i]));

          //      Console.WriteLine(Scores[i] + " / " + Angles[i] + " --- " + CenterX[i] + " ; " + CenterY[i]);
            }


          //  return res;
        }


        void UpdateSettings()
        {
            Set(Ftme_Ptr, Score, MaxOverlap, ToleranceAngle, MaxPos, MinReduceArea, UseSMID, SubPixelEstimation, FirstTarget, PolarityCheck, PolaritycheckRange, PolaritycheckSide);
        }





        void LoadTemplate(Bitmap templateimage)
        {
            //   int len;
   
        //    ImageFormat fmt = ImageFormat.Png; // new ImageFormat(templateimage.RawFormat.Guid);
            ImageFormat fmt = new ImageFormat(templateimage.RawFormat.Guid);
            var imageCodecInfo = ImageCodecInfo.GetImageEncoders().FirstOrDefault(codec => codec.FormatID == templateimage.RawFormat.Guid);
            //this is for situations, where the image is not read from disk, and is stored in the memort(e.g. image comes from a camera or snapshot)
            if (imageCodecInfo == null)
            {
                fmt = ImageFormat.Jpeg;
            }




            using (MemoryStream ms = new MemoryStream())
            {
                templateimage.Save(ms, fmt);
                byte[] image_byte_array = ms.ToArray();
              LoadTemplateFromBitmap(Ftme_Ptr, image_byte_array, ms.Length);
            }

        }

        void LoadTested(Bitmap testedimage)
        {
            //     int len;
//            ImageFormat fmt = ImageFormat.Png;
            ImageFormat fmt = new ImageFormat(testedimage.RawFormat.Guid);
            var imageCodecInfo = ImageCodecInfo.GetImageEncoders().FirstOrDefault(codec => codec.FormatID == testedimage.RawFormat.Guid);
            //this is for situations, where the image is not read from disk, and is stored in the memort(e.g. image comes from a camera or snapshot)
            if (imageCodecInfo == null)
            {
                fmt = ImageFormat.Jpeg;
            }




            using (MemoryStream ms = new MemoryStream())
            {
                testedimage.Save(ms, fmt);
                byte[] image_byte_array = ms.ToArray();
                LoadTestImageFromBitmap(Ftme_Ptr, image_byte_array, ms.Length);
            }

        }


         Bitmap GetResultsImage()
        {
            int c;
            int r;
            int ch;
            int st;
            int de;
            IntPtr imagePtr = GetResultImage(Ftme_Ptr, out r, out c, out ch, out st, out de);
            Console.WriteLine(c + " x " + r + " / " + imagePtr + " ----- " + st + " -> " + de);

            Bitmap ResultBitmap = new Bitmap(c, r,  st, PixelFormat.Format24bppRgb, imagePtr);
   //         Bitmap ResultBitmap = new Bitmap(100,100);
            return ResultBitmap;
        }


         Mat ShowResults()
        {

            int c;
            int r;
            int ch;
            int st;
            int de;
            IntPtr imagePtr = GetResultImage(Ftme_Ptr, out r, out c, out ch, out st, out de);
    //        Console.WriteLine(c + " x " + r + " / " + imagePtr + " ----- " + st + " -> " + de);
            Image<Bgr, byte> Imres = new Image<Bgr, byte>(c, r, st, imagePtr);

        //   Imres.
            //     Mat resultMat = new Mat();
            //  Mat resultMat = new Mat(r, c, Emgu.CV.CvEnum.DepthType.Cv8U, ch, imagePtr,st); 
            //         CvInvoke.ima
            // = new Mat(r,c,Emgu.CV.CvEnum.DepthType. );
            //       byte[] data = new byte[r * st];
            //       Marshal.Copy(imagePtr, data,0, r * st);


            //       CvInvoke.Imdecode(data,Emgu.CV.CvEnum.ImreadModes.AnyColor, resultMat);
            return Imres.Mat; 
         //   return resultMat;
        }


        /*
        public ListViewItem[] GetListViewItems(Templates TemplateConfig, Size imagesize)
        {
            ListViewItem[] itms = new ListViewItem[MatchResults.Count];
                
            PointF Match_point = new PointF();
            Point offset;
            for (int i = 0; i < MatchResults.Count; i++)
            {

                Match_point.X = MatchResults[i].CenterPoint.X + TemplateConfig.ROI.Location.X;
                Match_point.Y = MatchResults[i].CenterPoint.Y + TemplateConfig.ROI.Location.Y;
                offset = new Point((int)(Match_point.X - imagesize.Width / 2 + TemplateConfig.OffsetPointFromCenter.X), (int)(Match_point.Y - imagesize.Height / 2 + TemplateConfig.OffsetPointFromCenter.Y));
                string[] arr = new string[6];
               

                //Add first item
                arr[0] = i.ToString();
                arr[1] = Math.Round(MatchResults[i].Score * 100, 2).ToString();
                arr[2] = offset.X.ToString();
                arr[3] = offset.Y.ToString();
                arr[4] = Math.Round(MatchResults[i].Angle, 1).ToString() + "°";
                itms[i] = new ListViewItem(arr);
               
            }

            return itms;
        }
 */

        public Bitmap DrawResults(Bitmap image, Point offset)
        {
            PointF Match_point = new PointF();
            Image<Bgr, byte> Match_result = new Image<Bgr, byte>(image);
            for (int i = 0; i < MatchResults.Count; i++)
            {
                Match_point.X = MatchResults[i].CenterPoint.X + offset.X;
                Match_point.Y = MatchResults[i].CenterPoint.Y + offset.Y;
                RotatedRect respos = new RotatedRect(Match_point, TemplateSize, (float)MatchResults[i].Angle);
                Point[] vertices = Array.ConvertAll(respos.GetVertices(), Point.Round);
                Match_result.Draw(vertices, new Bgr(45, 255, 45), 2);
            }


            return Match_result.ToBitmap();
            }



        public Image<Bgr, byte> DrawResults(Image<Bgr, byte> image, Point offset)
        {
            PointF Match_point = new PointF();
            for (int i = 0; i < MatchResults.Count; i++)
            {
                Match_point.X = MatchResults[i].CenterPoint.X + offset.X;
                Match_point.Y = MatchResults[i].CenterPoint.Y + offset.Y;
                RotatedRect respos = new RotatedRect(Match_point, TemplateSize, (float)MatchResults[i].Angle);
                Point[] vertices = Array.ConvertAll(respos.GetVertices(), Point.Round);
                image.Draw(vertices, new Bgr(45, 255, 45), 2);
            }


            return image;
        }

        public RotatedRect GetRotatedRectangle()
        {
            PointF Match_point = new PointF();
            RotatedRect respos = new RotatedRect();
            if ( MatchResults.Count > 0)
            {
                Match_point.X = MatchResults[0].CenterPoint.X;
                Match_point.Y = MatchResults[0].CenterPoint.Y;
                respos = new RotatedRect(Match_point, TemplateSize, (float)MatchResults[0].Angle);
               
            }


            return respos;
        }

        /*
        public bool GetTestResult(Templates TemplateConfig)
        {
            bool res = false;
            if (MatchResults.Count > 0)
            {
                if (TemplateConfig.QuantityCheck)
                {
                    if (MatchResults.Count >= TemplateConfig.MinQuantity && MatchResults.Count <= TemplateConfig.MaxQuantity)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
               else if (TemplateConfig.matchParameters.PolarityCheck)
                {
                    if (MatchResults[0].Angle < 45 && MatchResults[0].Angle > -45)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }



                }
                else
                {
                    return true;
                }

            }
            else
            {
                return false;
            }





            return res;
        }
        */

        /*
        public string GetTestResult(TemplateCompareSettings TemplateConfig )
        {
            bool res = true;
            string ErrorString = "";

            if (MatchResults.Count > 0)
            {

                if (MatchResults[0].Score < TemplateConfig.Score)
                {
                        ErrorString = ErrorString + "Missing;";
                        res = false;
                    TemplateConfig.Offset_x = 0;
                    TemplateConfig.Offset_y = 0;
                    TemplateConfig.Offset_angle = 0;
                    TemplateConfig.ErrorString = ErrorString;
                    TemplateConfig.ResultScore = Math.Round(MatchResults[0].Score, 2);
                    return ErrorString;

                }



                if (TemplateConfig.PolarityCheck)
                {
                    if (TemplateConfig.IgnorePolarity == false)
                    {
                        if (MatchResults[0].Angle > 45 || MatchResults[0].Angle < -45)
                        {
                            ErrorString = ErrorString + "Polarity;";
                            res = false;
                        }
                    }
                    else
                    {


                    }

                }

                
                float Offset_X = MatchResults[0].CenterPoint.X - TestedSize.Width / 2;
                float Offset_Y = MatchResults[0].CenterPoint.Y - TestedSize.Height / 2;
                TemplateConfig.Offset_x = Math.Round(Offset_X, 2);
                TemplateConfig.Offset_y = Math.Round(Offset_Y, 2);
                TemplateConfig.Offset_angle = Math.Round(MatchResults[0].Angle, 2);
                TemplateConfig.ResultScore = Math.Round(MatchResults[0].Score, 2);

                if (Offset_X  > TemplateConfig.X_tolerance || Offset_X < -TemplateConfig.X_tolerance)
                {
                    ErrorString = ErrorString + "X tolerance;";
                    res = false;
                }


                if (Offset_Y > TemplateConfig.Y_tolerance || Offset_Y < -TemplateConfig.Y_tolerance)
                {
                    ErrorString = ErrorString + "Y tolerance;";
                    res = false;
                }


                if (MatchResults[0].Angle > TemplateConfig.Rotation_Tolerance || MatchResults[0].Angle < -TemplateConfig.Rotation_Tolerance)
                {
                    ErrorString = ErrorString + "Angle tolerance;";
                    res = false;
                }

            }
            else
            {
                TemplateConfig.Offset_x = 0;
                TemplateConfig.Offset_y = 0;
                TemplateConfig.Offset_angle = 0;
                TemplateConfig.ResultScore = 0;
                ErrorString = ErrorString + "Missing;";
                res = false;
            }



            TemplateConfig.ErrorString = ErrorString;

            return ErrorString;
        }
        */


        public List<TargetMatch> MatchTemplate(Bitmap Template, Bitmap Tested)
        {
       //     Template.Save(@"C:\debug\template.bmp");
       //     Tested.Save(@"C:\debug\tested.bmp");
            try
            {
                TemplateSize = Template.Size;
                TestedSize = Tested.Size;
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                //  Jedec_tested.SelectNone();

                //         Console.WriteLine("Start");
                LoadTemplate(Template);
         //       Console.WriteLine("Load template");
                LoadTested(Tested);
         //       Console.WriteLine("Load tested");
                UpdateSettings();
         //       Console.WriteLine("Update settings");
                Match(Ftme_Ptr);
         //       Console.WriteLine("Match");
                GetResults();
                //       Console.WriteLine("Get res");
                stopwatch.Stop();
                LastMatchTime = stopwatch.ElapsedMilliseconds;
            }
            catch
            {

            }

            return MatchResults;



        }


    }


    public class MatchParameters
    {
        public int MaxPos = 1;
        public double MaxOverlap = 0.3;
        public double Score = 0.8;
        public double ToleranceAngle = 180;
        public int MinReduceArea = 256;
        public bool SubPixelEstimation = true;
        public bool UseSMID = true;
        public bool FirstTarget = false;
        public bool PolarityCheck = false;
        public int PolaritycheckRange = 5;
        public int PolaritycheckSide = 1;

        public MatchParameters()
        {

        }

        public MatchParameters(int PozCnt, double overlap, double scorelimit, double angletolerance, int redicearea, bool subpix, bool smid, bool firsttarget, bool polCheck, int polRange, int polSide)
        {
            this.MaxPos = PozCnt;
            this.MaxOverlap = overlap;
            this.Score = scorelimit;
            this.ToleranceAngle = angletolerance;
            this.MinReduceArea = redicearea;
            this.SubPixelEstimation = subpix;
            this.UseSMID = smid;
            this.FirstTarget = firsttarget;
            this.PolarityCheck = polCheck;
            this.PolaritycheckRange = polRange;
            this.PolaritycheckSide = polSide;
        }

        public MatchParameters( double scorelimit, double angletolerance,  bool polCheck, int polRange, int polSide)
        {
            this.MaxPos = 1;
            this.MaxOverlap = 0.3;
            this.Score = scorelimit;
            this.ToleranceAngle = angletolerance;
            this.MinReduceArea = 256;
            this.SubPixelEstimation = true;
            this.UseSMID = true;
            this.FirstTarget = false;
            this.PolarityCheck = polCheck;
            this.PolaritycheckRange = polRange;
            this.PolaritycheckSide = polSide;
        }



    }


    public class TargetMatch
    {
        public PointF CenterPoint = new PointF();
        public PointF LeftTop = new PointF();
        public PointF RightTop = new PointF();
        public PointF LeftBottom = new PointF();
        public PointF RightBottom = new PointF();
        public double Angle = 0;
        public double Score = 0;

        public TargetMatch()
            {

            }

        public TargetMatch(PointF C, PointF LT, PointF RT, PointF LB, PointF RB, double angle, double score)
        {
            this.Angle = angle;
            this.Score = score;
            this.CenterPoint = new PointF(C.X, C.Y);
            this.LeftBottom = new PointF(LB.X, LB.Y);
            this.LeftTop = new PointF(LT.X, LT.Y);
            this.RightBottom = new PointF(RB.X, RB.Y);
            this.RightTop = new PointF(RT.X, RT.Y);

        }
    }




}
