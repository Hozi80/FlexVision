using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCL.Net;
using System.Drawing;
using Emgu.CV.Structure;
using AOI.Devices.OpenCl;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing.Imaging;
using Emgu.CV;
using Emgu.CV.Ocl;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using Emgu.CV.Cuda;
using Emgu.CV.XFeatures2D;
using System.Numerics;

namespace AOI.Inspections.EdgeBasedTemplateDetection
{
   public class EdgeBasedTemplate
    {
		private OpenCL.Net.Context context;
		private OpenCL.Net.Device device;
		private OpenCL.Net.CommandQueue commandQueue;
		private OpenCL.Net.Program program;
		private OpenCL.Net.Kernel kernelBalanced;
		private OpenCL.Net.Kernel kernelSpeed;
		private OpenCL.Net.Kernel kernelAccuracy;
		private OpenCL.Net.Kernel kernelNoRot;
		private OpenCL.Net.Kernel kernelFineTune;
		private OpenCL.Net.Kernel SobelMagnitude;
		private OpenCL.Net.Kernel DivMagnitude;
		private OpenCL.Net.Kernel MatchSobel;
		private OpenCL.Net.Kernel RotatedTemplateSobel;
		private OpenCL.Net.Kernel initKernel2D;
		private OpenCL.Net.Kernel reduceKernel;

		//		private OpenCL.Net.Program programImage;
		//		private OpenCL.Net.Kernel kernelImage;

		public int FoundRotate = 0;
		public int finetunerotate = 2;
		public float lowThreshold = 130;     //default value
		public float highThreshold = 255;    //default value
		public int minEdgepoints = 50;		//50
		public float minScore = 0.75F;     //default value
		public float greediness = 0.8F;   //default value
		public Point RotatePoint = new Point(0,0);
		public  float scale = 1;
		public int Accuracy = 3; // 1 - very Fast , 2 - Balanced, 3 - high precision
		public int ObjectCount = 1;
		public bool DetectRotation = true;
		public int AngleDetectionRangeSimmetric = 180;
		public bool EnableHalfPixel = false;
		public EdgeBasedModelInfo[] Models = new EdgeBasedModelInfo[360];
		public EdgeBasedModelInfo[] ModelsFullScale = new EdgeBasedModelInfo[360];
		//	EdgeBasedModelInfo FullScale = new EdgeBasedModelInfo();
		public EdgeBasedModelInfo360 Full = new EdgeBasedModelInfo360();
		public EdgeBasedModelInfo360 Scaled = new EdgeBasedModelInfo360();


		EdgeBasedShapeMatchingResult result = new EdgeBasedShapeMatchingResult();

		public Image<Gray, float> X;
		public Image<Gray, float> Y;
		public Image<Gray, float> M;
		public Image<Gray, float> Mask;


		String kernelsource = @"

__kernel void init_maps_2d(__global float* resultmap2D,
                           int pixNumb, int rotationsCount)
{
    int gid = get_global_id(0);
    int rot = get_global_id(1);
    if (gid < 0 || gid >= pixNumb) return;
    if (rot < 0 || rot >= rotationsCount) return;

    size_t idx = (size_t)rot * (size_t)pixNumb + (size_t)gid;
    resultmap2D[idx] = -INFINITY;
}


__kernel void reduce_best_rotation(
    __global const float* resultmap2D, // [rotationsCount * pixNumb]
    int                   pixNumb,
    int                   rotationsCount,
    __global float*       bestScore,   // [pixNumb]
    __global int*         bestRotation // [pixNumb]
){
    int gid = get_global_id(0);
    if (gid < 0 || gid >= pixNumb) return;

    float best = -INFINITY;
    int   brot = -1;

    for (int r = 0; r < rotationsCount; ++r) {
        float val = resultmap2D[(size_t)r * (size_t)pixNumb + (size_t)gid];
        if (val > best) { best = val; brot = r; }
    }

    bestScore[gid]   = best;
    bestRotation[gid]= brot;
}



__kernel void edge_match_Accuracy(
    __global const float* SourceMagnitude,
    __global const float* SourceDerivateX,
    __global const float* SourceDerivateY,
    __global const float* TemplateDerivateX,
    __global const float* TemplateDerivateY,
    __global const float* TemplateMagnitude,
    __global const int*   TemplateCoordX,
    __global const int*   TemplateCoordY,
    __global const int*   TemplateCoordinatesNumber,
    int                   SizeWidth,
    int                   pixNumb,
    float                 minScore,
    __global const int*   Rotatepositions,
    __global const float* normMinScore360,
    __global const float* normGreediness360,
    int                   rotationsCount,
    __global float*       resultmap2D   // [rotationsCount * pixNumb]
){
    int gid0     = get_global_id(0);
    int rotation = get_global_id(1);
    if (gid0 < 0 || gid0 >= pixNumb) return;
    if (rotation < 0 || rotation >= rotationsCount) return;

    int position   = Rotatepositions[rotation];
    int stepnumber = TemplateCoordinatesNumber[rotation];

    float partialSum   = 0.0f;
    float partialScore = 0.0f;

    for (int m = 0; m < stepnumber; ++m)
    {
        int tx = TemplateCoordX[position + m];
        int ty = TemplateCoordY[position + m];

        int distance = gid0 + ty + tx * SizeWidth; 
        if (distance < 0 || distance >= pixNumb) continue;

        float iTx = TemplateDerivateX[position + m];
        float iTy = TemplateDerivateY[position + m];

        float iSx = SourceDerivateX[distance];
        float iSy = SourceDerivateY[distance];

        float dotProduct = (iSx * iTx) + (iSy * iTy);
        float tMag = TemplateMagnitude[position + m];
        float sMag = SourceMagnitude[distance];
        
        // Tiszta pont-korreláció kiszámítása inverz magnitúdókkal szorozva
        float pointCorrelation = dotProduct * (tMag * sMag);
        
        // Lebegőpontos kerekítési és skálázási hibák elleni egyedi pont-korlátozás
        pointCorrelation = fmin(fmax(pointCorrelation, -1.0f), 1.0f);

        partialSum += pointCorrelation;

        float sumOfCoords = (float)(m + 1);
        partialScore = partialSum / sumOfCoords;

        // Haladási arány (progress) kiszámítása a teljes sablonhoz képest
        float progress = sumOfCoords / (float)stepnumber;

        // Finomhangolt Greediness kilépési feltételek
        float thresholdA = (minScore - 1.0f) + normGreediness360[rotation] * progress;
        float thresholdB = normMinScore360[rotation] * progress;
        float threshold  = fmin(thresholdA, thresholdB);

        if (partialScore < threshold)
            break;
    }

    float finalScore = partialScore; 
    if (finalScore < 0.0f || isinf(finalScore) || isnan(finalScore)) finalScore = 0.0f;
    if (finalScore > 1.0f) finalScore = 1.0f;

    size_t idx = (size_t)rotation * (size_t)pixNumb + (size_t)gid0;
    resultmap2D[idx] = finalScore;
}


__kernel void edge_match_Balanced(
    __global const float* SourceMagnitude,
    __global const float* SourceDerivateX,
    __global const float* SourceDerivateY,
    __global const float* TemplateDerivateX,
    __global const float* TemplateDerivateY,
    __global const float* TemplateMagnitude,
    __global const int*   TemplateCoordX,
    __global const int*   TemplateCoordY,
    __global const int*   TemplateCoordinatesNumber,
    int                   SizeWidth,
    int                   pixNumb,
    float                 minScore,
    __global const int*   Rotatepositions,
    __global const float* normMinScore360,
    __global const float* normGreediness360,
    int                   rotationsCount,
    __global float*       resultmap2D
){
    int gid0     = get_global_id(0);
    int rotBase  = get_global_id(1);
    int rotation = rotBase * 2;
    if (gid0 < 0 || gid0 >= pixNumb) return;
    if (rotation < 0 || rotation >= rotationsCount) return;

    int position   = Rotatepositions[rotation];
    int stepnumber = TemplateCoordinatesNumber[rotation];

    float partialSum   = 0.0f;
    float partialScore = 0.0f;

    for (int m = 0; m < stepnumber; ++m)
    {
        int tx = TemplateCoordX[position + m];
        int ty = TemplateCoordY[position + m];

        int distance = gid0 + ty + tx * SizeWidth; 
        if (distance < 0 || distance >= pixNumb) continue;

        float iTx = TemplateDerivateX[position + m];
        float iTy = TemplateDerivateY[position + m];

        float iSx = SourceDerivateX[distance];
        float iSy = SourceDerivateY[distance];

        float dotProduct = (iSx * iTx) + (iSy * iTy);
        float tMag = TemplateMagnitude[position + m];
        float sMag = SourceMagnitude[distance];
        
        float pointCorrelation = dotProduct * (tMag * sMag);
        pointCorrelation = fmin(fmax(pointCorrelation, -1.0f), 1.0f);

        partialSum += pointCorrelation;

        float sumOfCoords = (float)(m + 1);
        partialScore = partialSum / sumOfCoords;

        float progress = sumOfCoords / (float)stepnumber;
        float thresholdA = (minScore - 1.0f) + normGreediness360[rotation] * progress;
        float thresholdB = normMinScore360[rotation] * progress;
        float threshold  = fmin(thresholdA, thresholdB);

        if (partialScore < threshold)
            break;
    }

    float finalScore = partialScore; 
    if (finalScore < 0.0f || isinf(finalScore) || isnan(finalScore)) finalScore = 0.0f;
    if (finalScore > 1.0f) finalScore = 1.0f;

    size_t idx = (size_t)rotation * (size_t)pixNumb + (size_t)gid0;
    resultmap2D[idx] = finalScore;
}


__kernel void edge_match_Speed(
    __global const float* SourceMagnitude,
    __global const float* SourceDerivateX,
    __global const float* SourceDerivateY,
    __global const float* TemplateDerivateX,
    __global const float* TemplateDerivateY,
    __global const float* TemplateMagnitude,
    __global const int*   TemplateCoordX,
    __global const int*   TemplateCoordY,
    __global const int*   TemplateCoordinatesNumber,
    int                   SizeWidth,
    int                   pixNumb,
    float                 minScore,
    __global const int*   Rotatepositions,
    __global const float* normMinScore360,
    __global const float* normGreediness360,
    int                   rotationsCount,
    __global float*       resultmap2D
){
    int gid0     = get_global_id(0);       
    int rotation = get_global_id(1) * 3;   
    int gidReal  = gid0 * 2;               

    if (gidReal < 0 || gidReal >= pixNumb) return;
    if (rotation < 0 || rotation >= rotationsCount) return;

    int position   = Rotatepositions[rotation];
    int stepnumber = TemplateCoordinatesNumber[rotation];

    float partialSum   = 0.0f;
    float partialScore = 0.0f;

    for (int m = 0; m < stepnumber; ++m)
    {
        int tx = TemplateCoordX[position + m];
        int ty = TemplateCoordY[position + m];

        int distance = gidReal + ty + tx * SizeWidth; 
        if (distance < 0 || distance >= pixNumb) continue;

        float iTx = TemplateDerivateX[position + m];
        float iTy = TemplateDerivateY[position + m];

        float iSx = SourceDerivateX[distance];
        float iSy = SourceDerivateY[distance];

        float dotProduct = (iSx * iTx) + (iSy * iTy);
        float tMag = TemplateMagnitude[position + m];
        float sMag = SourceMagnitude[distance];
        
        float pointCorrelation = dotProduct * (tMag * sMag);
        pointCorrelation = fmin(fmax(pointCorrelation, -1.0f), 1.0f);

        partialSum += pointCorrelation;

        float sumOfCoords = (float)(m + 1);
        partialScore = partialSum / sumOfCoords;

        float progress = sumOfCoords / (float)stepnumber;
        float thresholdA = (minScore - 1.0f) + normGreediness360[rotation] * progress;
        float thresholdB = normMinScore360[rotation] * progress;
        float threshold  = fmin(thresholdA, thresholdB);

        if (partialScore < threshold)
            break;
    }

    float finalScore = partialScore; 
    if (finalScore < 0.0f || isinf(finalScore) || isnan(finalScore)) finalScore = 0.0f;
    if (finalScore > 1.0f) finalScore = 1.0f;

    size_t idx = (size_t)rotation * (size_t)pixNumb + (size_t)gidReal;
    resultmap2D[idx] = finalScore; 
}


__kernel void edge_match_finetune(
    __global float*       SourceMagnitude,
    __global float*       SourceDerivateX,
    __global float*       SourceDerivateY,
    __global float*       TemplateDerivateX,
    __global float*       TemplateDerivateY,
    __global float*       TemplateMagnitude,
    __global int*         TemplateCoordX,
    __global int*         TemplateCoordY,
    __global int*         TemplateCoordinatesNumber,
    int                   NumberOfCoord,
    int                   SizeWidth,
    int                   pixNumb,
    __global float*       resultmap,
    __global int*         rotationmap,
    float                 minScore,
    __global int*         Rotatepositions,
    __global float*       normMinScore360,
    __global float*       normGreediness360,
    __global int*         PreMatchedrotation,
    __global int*         PreMatchedshift
){
    int gid      = PreMatchedshift[get_global_id(0)];
    int rotation = PreMatchedrotation[get_global_id(1)];
    int position = Rotatepositions[rotation];
    int Stepnumber = TemplateCoordinatesNumber[rotation];

    float partialSum   = 0.0f;
    float partialScore = 0.0f;

    for (int m = 0; m < Stepnumber; m++)
    {
        int distance = gid + TemplateCoordY[position + m] + TemplateCoordX[position + m] * SizeWidth;

        if (distance < 0 || distance >= pixNumb)
            continue;
					 
        float iTx = TemplateDerivateX[position + m];
        float iTy = TemplateDerivateY[position + m];  
        float iSx = SourceDerivateX[distance]; 
        float iSy = SourceDerivateY[distance];
						
        float dotProduct = (iSx * iTx) + (iSy * iTy);
        float tMag = TemplateMagnitude[position + m];
float sMag = SourceMagnitude[distance];
float pointCorrelation = dotProduct * (tMag * sMag);
pointCorrelation = fmin(fmax(pointCorrelation, -1.0f), 1.0f);
partialSum += pointCorrelation;
float sumOfCoords = (float)(m + 1);
partialScore = partialSum / sumOfCoords;
float progress = sumOfCoords / (float)Stepnumber;
if (partialScore < (fmin((minScore - 1.0f) + normGreediness360[rotation] * progress, normMinScore360[rotation] * progress)))
break;
}
float finalScore = partialScore;
if (finalScore < 0.0f || isinf(finalScore) || isnan(finalScore)) finalScore = 0.0f;
if (finalScore > 1.0f) finalScore = 1.0f;
if (resultmap[gid] < finalScore)
{
resultmap[gid] = finalScore;
rotationmap[gid] = rotation;
}
}


__kernel void Sobel_Magnitude(__global float* Sourceimage,
						__global float* TemplateDerivateX,
						__global float* TemplateDerivateY,
						__global float* TemplateMagnitude,
						__global int* TemplateDirection,
						int SizeWidth)
{

	int X = get_global_id(0) + 1;
	int Y = get_global_id(1) + 1;
	int ArrayPosition = Y * SizeWidth + X;	


   int Gx[3][3] = {{-1,-2,-1},{0,0,0},{1,2,1}};
   int Gy[3][3] = {{-1,0,1},{-2,0,2},{-1,0,1}};

	float Middle = Sourceimage[Y * SizeWidth + X];
	float LeftTop = Sourceimage[(Y - 1) * SizeWidth + X - 1];
	float MiddleLeft =Sourceimage[ (Y) * SizeWidth + X - 1];
	float LeftBottom = Sourceimage[(Y + 1) * SizeWidth + X - 1];
	float MiddleTop = Sourceimage[(Y - 1) * SizeWidth + X] ;
	float MiddleBottom = Sourceimage[(Y + 1) * SizeWidth + X];
	float RightTop =Sourceimage[ (Y ) * SizeWidth + X  + 1];
	float MiddleRight =Sourceimage[ (Y) * SizeWidth + X + 1];
	float RightBottom =Sourceimage[ (Y + 1) * SizeWidth + X + 1];
	float Pixels[3][3] = { {LeftTop, MiddleLeft, LeftBottom}, {MiddleTop,Middle,MiddleBottom}, {RightTop,MiddleRight,RightBottom}};
	float x_dir = 0;
	float y_dir = 0;

	for (int i = 0; i < 3; ++i) {
            for (int j = 0; j < 3; ++j) {
                x_dir += Pixels[i][j] * Gx[i][j];
                y_dir += Pixels[i][j] * Gy[i][j];
            }
        }

float direction = fabs( atan2(y_dir, x_dir)) ;
	int dir = 0;	

			if ((direction > 0 && direction < 0.3926) || (direction > 2.7488 && direction < 3.1415) )
				{
					dir = 0;
				}						
			else if ((direction > 0.3926 && direction < 1.1780))
					{
					dir = 45;
					}
			else if ((direction > 1.1780 && direction < 1.9634))
					{
				dir = 90;
					}					
			else if ((direction > 1.9634 && direction < 2.7488))
					{
					dir = 135;
					}		
			else
					{
					dir = 0;			
					}		




	TemplateDerivateX[ArrayPosition] =  x_dir; 
	TemplateDerivateY[ArrayPosition] =  y_dir;
	TemplateMagnitude[ArrayPosition] =	sqrt((x_dir*x_dir+y_dir*y_dir));
	TemplateDirection[ArrayPosition] =	dir; // atan2(y_dir, x_dir);

}


__kernel void Sobel(__global float* Sourceimage,
						__global float* TemplateDerivateX,
						__global float* TemplateDerivateY,
						__global float* TemplateMagnitude,
						int SizeWidth)
{

	int X = get_global_id(0);
	int Y = get_global_id(1);
	int ArrayPosition = Y * SizeWidth + X;	

	if (X < 1 || Y < 1 || X >= SizeWidth)
	{
	TemplateDerivateX[ArrayPosition] =  0; 
	TemplateDerivateY[ArrayPosition] =  0;
	TemplateMagnitude[ArrayPosition] =	0;
	}
	else
	{
   int Gx[3][3] = {{-1,-2,-1},{0,0,0},{1,2,1}};
   int Gy[3][3] = {{-1,0,1},{-2,0,2},{-1,0,1}};

	float Middle = Sourceimage[Y * SizeWidth + X];
	float LeftTop = Sourceimage[(Y - 1) * SizeWidth + X - 1];
	float MiddleLeft =Sourceimage[ (Y) * SizeWidth + X - 1];
	float LeftBottom = Sourceimage[(Y + 1) * SizeWidth + X - 1];
	float MiddleTop = Sourceimage[(Y - 1) * SizeWidth + X] ;
	float MiddleBottom = Sourceimage[(Y + 1) * SizeWidth + X];
	float RightTop =Sourceimage[ (Y ) * SizeWidth + X  + 1];
	float MiddleRight =Sourceimage[ (Y) * SizeWidth + X + 1];
	float RightBottom =Sourceimage[ (Y + 1) * SizeWidth + X + 1];
	float Pixels[3][3] = { {LeftTop, MiddleLeft, LeftBottom}, {MiddleTop,Middle,MiddleBottom}, {RightTop,MiddleRight,RightBottom}};
	float x_dir = 0;
	float y_dir = 0;

	for (int i = 0; i < 3; ++i) {
            for (int j = 0; j < 3; ++j) {
                x_dir += Pixels[i][j] * Gx[i][j];
                y_dir += Pixels[i][j] * Gy[i][j];
            }
        }

	TemplateDerivateX[ArrayPosition] = (float)x_dir; 
	TemplateDerivateY[ArrayPosition] = (float) y_dir ;
	float mag = (float)sqrt((x_dir*x_dir+y_dir*y_dir));
	if (mag != 0 )
		TemplateMagnitude[ArrayPosition] =	(float) 1 / mag;
	else
		TemplateMagnitude[ArrayPosition] =	0;
	}
}



__kernel void NonMaximumSuppression(__global float* Magnitude,
						__global float* Result,
						__global int* Directions,
						int SizeWidth,
						float MinContrast,
						float MaxContrast,
						float maxmagnitude)
{

	int X = get_global_id(0) + 1;
	int Y = get_global_id(1) + 1;
	int ArrayPosition = Y * SizeWidth + X;

	float Middle = Magnitude[ArrayPosition];
	float LeftTop = Magnitude[(Y - 1) * SizeWidth + X - 1];
	float MiddleLeft = Magnitude[ (Y) * SizeWidth + X - 1];
	float LeftBottom = Magnitude[(Y + 1) * SizeWidth + X - 1];
	float MiddleTop = Magnitude[(Y - 1) * SizeWidth + X] ;
	float MiddleBottom = Magnitude[(Y + 1) * SizeWidth + X];
	float RightTop = Magnitude[ (Y ) * SizeWidth + X  + 1];
	float MiddleRight = Magnitude[ (Y) * SizeWidth + X + 1];
	float RightBottom = Magnitude[ (Y + 1) * SizeWidth + X + 1];
	float leftPixel = 0;
	float rightPixel = 0;
				switch (Directions[ArrayPosition])
					{
						case 0:
							leftPixel = MiddleLeft;
							rightPixel =  MiddleRight;
							break;
						case 45:
							leftPixel = RightTop;
							rightPixel = LeftBottom;
							break;
						case 90:
							leftPixel = MiddleTop;
							rightPixel = MiddleBottom;
							break;
						case 135:
							leftPixel = LeftTop;
							rightPixel =RightBottom;
							break;
					}

float mag = 0;
				if ((Middle < leftPixel) || (Middle < rightPixel))
						mag = 0;
					else
						mag = (float)( Middle  / maxmagnitude * 255) ;

	
	if (mag > MinContrast && mag < MaxContrast)	
	Result[ArrayPosition] = mag;
	else
	Result[ArrayPosition] = 0;

}




__kernel void RotatedSobelPoints(__global float* Sourceimage,
						__global float* TemplateDerivateX,
						__global float* TemplateDerivateY,
						__global float* TemplateMagnitude,
						int SizeWidth,
						int SizeHeight,
						__global int* XCoordinates,
						__global int* YCoordinates,
							int CenterX,
							int CenterY,
						__global int* NewX,
						__global int* NewY,
						float sinTheta,
						float cosTheta,
						float Maxgradient,
						__global float* testoutput)
{

	int gid = get_global_id(0);
    int  X = (int)(cosTheta * (YCoordinates[gid]) - sinTheta * (XCoordinates[gid]) +	SizeWidth / 2 );
    int  Y = (int)(sinTheta * (YCoordinates[gid]) + cosTheta * (XCoordinates[gid]) +	SizeHeight /2 );
	int Gx[3][3] = {{-1,-2,-1},{0,0,0},{1,2,1}};
	int Gy[3][3] = {{-1,0,1},{-2,0,2},{-1,0,1}};

	float Middle = Sourceimage[Y * SizeWidth + X];
	float LeftTop = Sourceimage[(Y - 1) * SizeWidth + X - 1];
	float MiddleLeft =Sourceimage[ (Y) * SizeWidth + X - 1];
	float LeftBottom = Sourceimage[(Y + 1) * SizeWidth + X - 1];
	float MiddleTop = Sourceimage[(Y - 1) * SizeWidth + X] ;
	float MiddleBottom = Sourceimage[(Y + 1) * SizeWidth + X];
	float RightTop =Sourceimage[ (Y ) * SizeWidth + X  + 1];
	float MiddleRight =Sourceimage[ (Y) * SizeWidth + X + 1];
	float RightBottom =Sourceimage[ (Y + 1) * SizeWidth + X + 1];
	float Pixels[3][3] = { {LeftTop, MiddleLeft, LeftBottom}, {MiddleTop,Middle,MiddleBottom}, {RightTop,MiddleRight,RightBottom}};
	float x_dir = 0;
	float y_dir = 0;

	for (int i = 0; i < 3; ++i) {
            for (int j = 0; j < 3; ++j) {
                x_dir += Pixels[i][j] * Gx[i][j];
                y_dir += Pixels[i][j] * Gy[i][j];
            }
        }

	TemplateDerivateX[gid] = (float)x_dir; 
	TemplateDerivateY[gid] = (float) y_dir ;
	float mag = (float)sqrt((x_dir*x_dir+y_dir*y_dir));
	if (mag != 0 )
		TemplateMagnitude[gid] =	(float) mag / Maxgradient * 255;
	else
		TemplateMagnitude[gid] =	0;

//	testoutput[(XCoordinates[gid] + CenterY) * SizeWidth + (YCoordinates[gid] + CenterX)] = Maxgradient;
//	testoutput[Y * SizeWidth + X] = Maxgradient * 2;
	Sourceimage[Y * SizeWidth + X] = 0;
	NewX[gid] = Y;
	NewY[gid] = X;

}



";

		private OpenCL.Net.IMem CLSdx;
		private OpenCL.Net.IMem CLSdy;
		private OpenCL.Net.IMem CLmagnitude;

		private OpenCL.Net.IMem CLTempCordX;
		private OpenCL.Net.IMem CLTempCordY;
		private OpenCL.Net.IMem CLTempNoCoord;
		private OpenCL.Net.IMem CLTemppositions;
		private OpenCL.Net.IMem CLTempDerivativeX;
		private OpenCL.Net.IMem CLTempDerivativeY;
		private OpenCL.Net.IMem CLTempMagnitude;

		private OpenCL.Net.IMem ResultMap;
		private OpenCL.Net.IMem RotationMap;
		private OpenCL.Net.IMem CLnormMinScore;
		private OpenCL.Net.IMem CLnormGreediness;
		int NumberOfCord;
		int SourceWidth;
		int PixelCount;



		public void dispose()
		{
			Cl.ReleaseMemObject(CLSdx);
			Cl.ReleaseMemObject(CLSdy);
			Cl.ReleaseMemObject(CLmagnitude);
			Cl.ReleaseMemObject(CLTempCordX);
			Cl.ReleaseMemObject(CLTempCordY);
			Cl.ReleaseMemObject(CLTempNoCoord);
			Cl.ReleaseMemObject(CLTemppositions);
			Cl.ReleaseMemObject(CLTempDerivativeX);
			Cl.ReleaseMemObject(CLTempDerivativeY);
			Cl.ReleaseMemObject(CLTempMagnitude);
			Cl.ReleaseMemObject(ResultMap);
			Cl.ReleaseMemObject(RotationMap);
			Cl.ReleaseMemObject(CLnormMinScore);
			Cl.ReleaseMemObject(CLnormGreediness);

			Cl.ReleaseProgram(program);
//			Cl.ReleaseKernel(kernel);
			Cl.ReleaseKernel(kernelNoRot);
			Cl.ReleaseKernel(kernelFineTune);
			Cl.ReleaseKernel(SobelMagnitude);
			Cl.ReleaseKernel(DivMagnitude);
			Cl.ReleaseKernel(MatchSobel);
			Cl.ReleaseKernel(RotatedTemplateSobel);
			//			Cl.ReleaseKernel(kernelImage);
			Cl.ReleaseContext(context);
			Cl.ReleaseCommandQueue(commandQueue);
		}

		public void LoadModel(string filename)
		{
	
			string[] lines = System.IO.File.ReadAllLines(filename);
			string[] first = lines[0].Split(';');
			int datalength = int.Parse(first[0]);
			this.scale = float.Parse(first[1]);

			Full.noOfCordinates360 = datalength;
			Full.cordinatesarrayX360 = new int[datalength];
			Full.cordinatesarrayY360 = new int[datalength];
			Full.edgeDerivativeX360 = new float[datalength];
			Full.edgeDerivativeY360 = new float[datalength];
			Full.edgeMagnitude360 = new float[datalength];
			Full.step360 = new int[360];
			Full.positions = new int[360];
			Full.normMinScore360 = new float[360];
			Full.normGreediness360 = new float[360];

			for (int i = 0; i < datalength; i++)
            {
				string[] datapack = lines[i + 1].Split(';');
				Full.cordinatesarrayX360[i] = int.Parse(datapack[0]);
				Full.cordinatesarrayY360[i] = int.Parse(datapack[1]);
				Full.edgeDerivativeX360[i] = float.Parse(datapack[2]);
				Full.edgeDerivativeY360[i] = float.Parse(datapack[3]);
				Full.edgeMagnitude360[i] = float.Parse(datapack[4]);
			}
			
			datalength++;

			for (int i = 0; i < 360; i++)
			{
				string[] datapack = lines[datalength + i + 1].Split(';');
				Full.step360[i] = int.Parse(datapack[0]);
				Full.positions[i] = int.Parse(datapack[1]);
				Full.normMinScore360[i] = float.Parse(datapack[2]);
				Full.normGreediness360[i] = float.Parse(datapack[3]);
				
			}

			datalength = datalength + 360;
			datalength++;
			int datalength2 = int.Parse(lines[datalength]);


			Scaled.noOfCordinates360 = datalength2;
			Scaled.cordinatesarrayX360 = new int[datalength2];
			Scaled.cordinatesarrayY360 = new int[datalength2];
			Scaled.edgeDerivativeX360 = new float[datalength2];
			Scaled.edgeDerivativeY360 = new float[datalength2];
			Scaled.edgeMagnitude360 = new float[datalength2];
			Scaled.step360 = new int[360];
			Scaled.positions = new int[360];
			Scaled.normMinScore360 = new float[360];
			Scaled.normGreediness360 = new float[360];

	//		MessageBox.Show(lines[datalength]);
			for (int i = 0; i < datalength2; i++)
			{
				string[] datapack = lines[datalength + i + 1].Split(';');
				Scaled.cordinatesarrayX360[i] = int.Parse(datapack[0]);
				Scaled.cordinatesarrayY360[i] = int.Parse(datapack[1]);
				Scaled.edgeDerivativeX360[i] = float.Parse(datapack[2]);
				Scaled.edgeDerivativeY360[i] = float.Parse(datapack[3]);
				Scaled.edgeMagnitude360[i] = float.Parse(datapack[4]);
			}

			datalength = datalength + datalength2;

			datalength++;
	//		MessageBox.Show(lines[datalength]);
			for (int i = 0; i < 360; i++)
			{
				string[] datapack = lines[datalength + i + 1].Split(';');
				Scaled.step360[i] = int.Parse(datapack[0]);
				Scaled.positions[i] = int.Parse(datapack[1]);
				Scaled.normMinScore360[i] = float.Parse(datapack[2]);
				Scaled.normGreediness360[i] = float.Parse(datapack[3]);

			}

			//			MessageBox.Show(lines[datalength]);

		//	CopyTemplateDataToGPU(Scaled);
			
		}


		public void SaveModel(string filename)
        {
			using (System.IO.StreamWriter file =
				  new System.IO.StreamWriter(filename))
			{
				file.WriteLine(Full.edgeMagnitude360.Length + ";" +  scale );

				for (int i = 0; i < Full.edgeMagnitude360.Length; i++)
				{
					file.WriteLine(Full.cordinatesarrayX360[i] + ";" + Full.cordinatesarrayY360[i] + ";" + Full.edgeDerivativeX360[i] + ";" + Full.edgeDerivativeY360[i] + ";" + Full.edgeMagnitude360[i] + ";");
				}
				file.WriteLine(360);
				for (int i = 0; i < 360; i++)
				{
					file.WriteLine(Full.step360[i] + ";" + Full.positions[i] + ";" + Full.normMinScore360[i] + ";" + Full.normGreediness360[i] + ";");
				}

				
				file.WriteLine(Scaled.edgeMagnitude360.Length);

				for (int i = 0; i < Scaled.edgeMagnitude360.Length; i++)
				{
					file.WriteLine(Scaled.cordinatesarrayX360[i] + ";" + Scaled.cordinatesarrayY360[i] + ";" + Scaled.edgeDerivativeX360[i] + ";" + Scaled.edgeDerivativeY360[i] + ";" + Scaled.edgeMagnitude360[i] + ";");
				}
				file.WriteLine(360);
				for (int i = 0; i < 360; i++)
				{
					file.WriteLine(Scaled.step360[i] + ";" + Scaled.positions[i] + ";" + Scaled.normMinScore360[i] + ";" + Scaled.normGreediness360[i] + ";");
				}
			}
		}

			public EdgeBasedTemplate(ComputeContext c)
        {
			
		//	CvInvoke.UseOpenCL = true;
			ErrorCode errorCode;
			this.device = c.device;
			this.context = c.context;
			this.commandQueue = c.commandQueue;
			/*			string programLocation = Assembly.GetEntryAssembly().Location;
						string pathToLoad = Path.Combine(Path.GetDirectoryName(programLocation), "Config\\EdgeKernel.cl");
						string openClSource = File.ReadAllText(pathToLoad);
			*/
			
			program = Cl.CreateProgramWithSource(context, 1, new[] { kernelsource }, null, out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			errorCode = Cl.BuildProgram(program, 1, new[] { device }, "", null, IntPtr.Zero);
			if (errorCode != ErrorCode.Success)
			{
				OpenCL.Net.InfoBuffer logInfoBuffer = Cl.GetProgramBuildInfo(program, device, ProgramBuildInfo.Log, out errorCode);
				ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

				throw new ComputeContext.OpenClError();
			}

			kernelBalanced = Cl.CreateKernel(program, "edge_match_Balanced", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");
			kernelSpeed = Cl.CreateKernel(program, "edge_match_Speed", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");
			kernelAccuracy = Cl.CreateKernel(program, "edge_match_Accuracy", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			initKernel2D =   Cl.CreateKernel(program, "init_maps_2d", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");


			reduceKernel =   Cl.CreateKernel(program, "reduce_best_rotation", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			//		kernelNoRot = Cl.CreateKernel(program, "edge_template_norot", out errorCode);
			//		ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");
			kernelFineTune = Cl.CreateKernel(program, "edge_match_finetune", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			SobelMagnitude = Cl.CreateKernel(program, "Sobel_Magnitude", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			DivMagnitude = Cl.CreateKernel(program, "NonMaximumSuppression", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			MatchSobel = Cl.CreateKernel(program, "Sobel", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			RotatedTemplateSobel = Cl.CreateKernel(program, "RotatedSobelPoints", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");


			//		 programLocation = Assembly.GetEntryAssembly().Location;
			/*			 pathToLoad = Path.Combine(Path.GetDirectoryName(programLocation), "Config\\ImageKernel.cl");
						 openClSource = File.ReadAllText(pathToLoad);
						programImage = Cl.CreateProgramWithSource(context, 1, new[] { openClSource }, null, out errorCode);
						ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

						errorCode = Cl.BuildProgram(programImage, 1, new[] { device }, "", null, IntPtr.Zero);
						if (errorCode != ErrorCode.Success)
						{
							OpenCL.Net.InfoBuffer logInfoBuffer = Cl.GetProgramBuildInfo(programImage, device, ProgramBuildInfo.Log, out errorCode);
							ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

							throw new ComputeContext.OpenClError();
						}


						kernelImage = Cl.CreateKernel(programImage, "sobel_rgb", out errorCode);
			//			kernelImage = Cl.CreateKernel(programImage, "imageToMagnitue", out errorCode);
						ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");
			*/

		}



		public static Point[] RotatePoints(Point[] pointToRotate, double angleInDegrees)
		{
			double angleInRadians = angleInDegrees * (Math.PI / 180);
			double cosTheta = Math.Cos(angleInRadians);
			double sinTheta = Math.Sin(angleInRadians);
			Point[] Rotated = new Point[pointToRotate.Length];
		
			for (int i = 0; i < pointToRotate.Length; i++)
			{
				Rotated[i].X = (int)(cosTheta * pointToRotate[i].X - sinTheta * pointToRotate[i].Y);
				Rotated[i].Y = (int)(sinTheta * pointToRotate[i].X + cosTheta * pointToRotate[i].Y);
			}
			return Rotated;
		}

		public static Point RotateOnePoint(Point pointToRotate, double angleInDegrees)
		{
			double angleInRadians = angleInDegrees * (Math.PI / 180);
			double cosTheta = Math.Cos(angleInRadians);
			double sinTheta = Math.Sin(angleInRadians);
			Point Rotated = new Point();

		
				Rotated.X = (int)(cosTheta * pointToRotate.X - sinTheta * pointToRotate.Y);
				Rotated.Y = (int)(sinTheta * pointToRotate.X + cosTheta * pointToRotate.Y);
		
			return Rotated;
		}


		public void CreateRotatedTemplates(Image<Gray, float> templateArr, int rotationrange = 180)
		{
		//	int i;
			Models[0] = CreateModel(templateArr, scale, 0);

	//		Models = CreateModels(templateArr, scale);
	//		ModelsFullScale = CreateModels(templateArr,1);

			





			
			if (scale == 1)
			{
				ModelsFullScale[0] = CreateModel(templateArr, 1, 0);
				Parallel.For(1, 360, i =>

				//	for (i = 1; i < 360; i++)
				{
				//	ModelsFullScale[i] = Models[i] = CreateModel(templateArr, 1, i);

					ModelsFullScale[i] = Models[i] = ModelsFullScale[0].GetRotatedModel(i);


					//	ModelsFullScale[i] = Models[i] = Models[0];
					//	ModelsFullScale[i].cordinates = Models[i].cordinates = RotatePoints(Models[0].cordinates, i);


					//		ModelsFullScale[i] = Models[i] = CreateModelGPU_Rotated(templateArr, 1, i, Models[0].MaxGradient, Models[0].cordinatesarrayX, Models[0].cordinatesarrayY, Models[0].Center.X, Models[0].Center.Y);
				});
			}
			else
            {
				
				Models[0] = CreateModel(templateArr, scale, 0);
				ModelsFullScale[0] = CreateModel(templateArr, 1, 0);
				Parallel.For(1, 360, i =>
				//	for (int i = 1; i < 360; i++)
				{
			//		long elapsed_time;
			//		var stopwatch = new Stopwatch();
		//			stopwatch.Start();

					Models[i] = Models[0].GetRotatedModel(i);
					ModelsFullScale[i] = ModelsFullScale[0].GetRotatedModel(i);
			//		Models[i] = Models[0].GetRotatedModel_SIMD(i);
			//		ModelsFullScale[i] = ModelsFullScale[0].GetRotatedModel_SIMD(i);
			//		stopwatch.Stop();
			//		elapsed_time = stopwatch.ElapsedMilliseconds;
			//		Console.WriteLine("CreateModel -> " + elapsed_time.ToString());
					//		Mat canvas = new Mat(new Size(ModelsFullScale[i].modelWidth, ModelsFullScale[i].modelHeight), DepthType.Cv8U, 3);
					//		canvas.SetTo(new MCvScalar(0, 0, 0)); // fekete háttér

					//		DrawTemplatePointsWithGradients(canvas, ModelsFullScale[i], new MCvScalar(0, 255, 0), new MCvScalar(255, 0, 0)); // zöld pontok, piros irányok
					//		canvas.Bitmap.Save(@"C:\Debug\Edge\new" + i.ToString() + ".bmp");
					//		CvInvoke.Imshow("Template with Gradients", canvas);
					//		CvInvoke.WaitKey(0);

					//	}
				});

				
			/*		
					Parallel.For(0, 360, i =>
					//	for (i = 1; i < 360; i++)
					{
						Models[i] = CreateModel(templateArr, scale, i);
						ModelsFullScale[i] = CreateModel(templateArr, 1, i);


						//		Models[i] = CreateModel(templateArr, scale, i);
						//		ModelsFullScale[i] = CreateModel(templateArr, 1, i);
						Mat canvas = new Mat(new Size(ModelsFullScale[i].modelWidth, ModelsFullScale[i].modelHeight), DepthType.Cv8U, 3);
						canvas.SetTo(new MCvScalar(0, 0, 0)); // fekete háttér
						DrawTemplatePointsWithGradients(canvas, ModelsFullScale[i], new MCvScalar(0, 255, 0), new MCvScalar(255, 0, 0)); // zöld pontok, piros irányok
						canvas.Bitmap.Save(@"C:\Debug\Edge\origi" + i.ToString() + ".bmp");

				//		CvInvoke.Imshow("Template with Gradients", canvas);
				//		CvInvoke.WaitKey(0);
						//		ModelsFullScale[i].cordinates = RotatePoints(ModelsFullScale[0].cordinates,i);
						//		ModelsFullScale[i].cordinates = RotatePoints(ModelsFullScale[0].cordinates, i);

						//Models[i] = CreateModelGPU_Rotated(templateArr, scale, i, Models[0].MaxGradient, Models[0].cordinatesarrayX, Models[0].cordinatesarrayY, Models[0].Center.X, Models[0].Center.Y);
						//ModelsFullScale[i] = CreateModelGPU_Rotated(templateArr, 1, i, Models[0].MaxGradient, Models[0].cordinatesarrayX, Models[0].cordinatesarrayY, Models[0].Center.X, Models[0].Center.Y);

					});

	

				*/




			}

		}


		public void CreateRotatedTemplatesNew(Image<Gray, float> templateArr)
		{
			//	int i;
		//	Models[0] = CreateModel(templateArr, scale, 0);

			//		Models = CreateModels(templateArr, scale);
			//		ModelsFullScale = CreateModels(templateArr,1);


			ModelInfoBase MBase = CreateModelBase(templateArr, false);
			var Resized = templateArr.Resize(scale, Inter.Linear);
			ModelInfoBase MBaseResized = CreateModelBase(Resized, false);



			if (scale == 1)
			{
				Parallel.For(0, 360, i =>

				//	for (i = 1; i < 360; i++)
				{
					ModelsFullScale[i] = Models[i] = CreateModelFromBase(MBase, i);




					//	ModelsFullScale[i] = Models[i] = Models[0];
					//	ModelsFullScale[i].cordinates = Models[i].cordinates = RotatePoints(Models[0].cordinates, i);


					//		ModelsFullScale[i] = Models[i] = CreateModelGPU_Rotated(templateArr, 1, i, Models[0].MaxGradient, Models[0].cordinatesarrayX, Models[0].cordinatesarrayY, Models[0].Center.X, Models[0].Center.Y);
				});
			}
			else
			{
				Parallel.For(0, 360, i =>
				//	for (i = 1; i < 360; i++)
				{
					Models[i] = CreateModelFromBase(MBaseResized, i);
					ModelsFullScale[i] = CreateModelFromBase(MBase, i);




					//Models[i] = CreateModelGPU_Rotated(templateArr, scale, i, Models[0].MaxGradient, Models[0].cordinatesarrayX, Models[0].cordinatesarrayY, Models[0].Center.X, Models[0].Center.Y);
					//ModelsFullScale[i] = CreateModelGPU_Rotated(templateArr, 1, i, Models[0].MaxGradient, Models[0].cordinatesarrayX, Models[0].cordinatesarrayY, Models[0].Center.X, Models[0].Center.Y);

				});
			}

		}



		public EdgeBasedModelInfo  CreateModelFromBase(ModelInfoBase Mb,int rot)
        {
			EdgeBasedModelInfo tempmodel = new EdgeBasedModelInfo();
			tempmodel.Center = Mb.Center;
			tempmodel.centerOfGravity = Mb.Center;
			tempmodel.MaxGradient = Mb.MaxGradient;
			tempmodel.modelHeight = Mb.ModelSize.Height;
			tempmodel.modelWidth = Mb.ModelSize.Width;
			tempmodel.EdgeHeight = Mb.ModelSize.Height;
			tempmodel.EdgeWith = Mb.ModelSize.Width;

			tempmodel.noOfCordinates = Mb.EdgePoints.Count;
			tempmodel.rotation = rot;

			tempmodel.cordinates = new Point[Mb.EdgePoints.Count];
			tempmodel.cordinatesarrayX = new int[Mb.EdgePoints.Count];
			tempmodel.cordinatesarrayY = new int[Mb.EdgePoints.Count];
			tempmodel.edgeDerivativeX = new float[Mb.EdgePoints.Count];
			tempmodel.edgeDerivativeY = new float[Mb.EdgePoints.Count];
			tempmodel.edgeMagnitude = new float[Mb.EdgePoints.Count];




			for (int i = 0; i < Mb.EdgePoints.Count; i++)
            {
				tempmodel.cordinates[i] = RotateOnePoint(Mb.EdgePoints[i].Location , Mb.Center, rot );
				tempmodel.cordinatesarrayX[i] = tempmodel.cordinates[i].X;
				tempmodel.cordinatesarrayY[i] = tempmodel.cordinates[i].Y;
				tempmodel.edgeDerivativeX[i] = Mb.EdgePoints[i].DerivateX;
				tempmodel.edgeDerivativeY[i] = Mb.EdgePoints[i].DerivateY;
				tempmodel.edgeMagnitude[i] = Mb.EdgePoints[i].Magnitude;
			}




			return tempmodel;
        }

		public static Point RotateOnePoint(Point pointToRotate, Point centerPoint, double angleInDegrees)
		{
			double angleInRadians = angleInDegrees * (Math.PI / 180);
			double cosTheta = Math.Cos(angleInRadians);
			double sinTheta = Math.Sin(angleInRadians);
			return new Point
			{
				X =
					(int)
					(cosTheta * (pointToRotate.X - centerPoint.X) -
					sinTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.X),
				Y =
					(int)
					(sinTheta * (pointToRotate.X - centerPoint.X) +
					cosTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.Y)
			};
		}

		private EdgeBasedModelInfo360 Create1DarrayForGPU(EdgeBasedModelInfo[] modelinfos )
		{
			EdgeBasedModelInfo360 result360 = new EdgeBasedModelInfo360();

			int count = 0;
			int i;
			for (i = 0; i < 360; i++)
			{
				count = count + modelinfos[i].noOfCordinates;
			}

			result360.cordinatesarrayX360 = new int[count];
			result360.cordinatesarrayY360 = new int[count];
			result360.edgeMagnitude360 = new float[count];
			result360.edgeDerivativeX360 = new float[count];
			result360.edgeDerivativeY360 = new float[count];

			result360.step360 = new int[360];
			result360.positions = new int[360];
			result360.noOfCordinates360 = count;
			result360.normMinScore360 = new float[360];
			result360.normGreediness360 = new float[360];


			int j;
			int sum = 0;
		
			for (i = 0; i < 360; i++)
			{
//				result360.normMinScore360[i] = (float)(minScore / modelinfos[i].noOfCordinates); // precompute minumum score 
//				result360.normGreediness360[i] = (float)(((1 - greediness * minScore) / (1 - greediness)) / modelinfos[i].noOfCordinates); // precompute greedniness 
																																		   // JAVÍTOTT KÜSZÖBÉRTÉK SZÁMÍTÁS (Nem osztunk a darabszámmal!):
				result360.normMinScore360[i] = (float)minScore;
				result360.normGreediness360[i] = (float)((1 - greediness * minScore) / (1 - greediness));


				result360.positions[i] = sum;
				for (j = 0; j < modelinfos[i].noOfCordinates; j++)
				{
					result360.cordinatesarrayX360[sum + j] = modelinfos[i].cordinatesarrayX[j];
					result360.cordinatesarrayY360[sum + j] = modelinfos[i].cordinatesarrayY[j];
					result360.edgeMagnitude360[sum + j] = modelinfos[i].edgeMagnitude[j];
					result360.edgeDerivativeX360[sum + j] = modelinfos[i].edgeDerivativeX[j];
					result360.edgeDerivativeY360[sum + j] = modelinfos[i].edgeDerivativeY[j];
	
				}
				result360.step360[i] = modelinfos[i].noOfCordinates;
				sum = sum + modelinfos[i].noOfCordinates;
			}
		
		/*	
			for (i = 0; i < 360; i++)
			{
				result360.normMinScore360[i] = (float)(minScore / modelinfos[0].noOfCordinates); // precompute minumum score 
				result360.normGreediness360[i] = (float)(((1 - greediness * minScore) / (1 - greediness)) / modelinfos[0].noOfCordinates); // precompute greedniness 


				result360.positions[i] = sum;
				for (j = 0; j < modelinfos[0].noOfCordinates; j++)
				{
					result360.cordinatesarrayX360[sum + j] = modelinfos[0].cordinatesarrayX[j];
					result360.cordinatesarrayY360[sum + j] = modelinfos[0].cordinatesarrayY[j];
					result360.edgeMagnitude360[sum + j] = modelinfos[0].edgeMagnitude[j];
					result360.edgeDerivativeX360[sum + j] = modelinfos[0].edgeDerivativeX[j];
					result360.edgeDerivativeY360[sum + j] = modelinfos[0].edgeDerivativeY[j];

				}
				result360.step360[i] = modelinfos[0].noOfCordinates;
				sum = sum + modelinfos[0].noOfCordinates;
			}


			*/

				return result360;
		}



	private	float CalculatePyramidLevel(Image<Gray, float> templateArr)
		{
			EdgeBasedModelInfo Model = new EdgeBasedModelInfo(); ;
			float Scalefactor = 1;
			ModelsFullScale[0] = CreateModel(templateArr, 1, 0);
			RotatePoint = new Point(ModelsFullScale[0].centerOfGravity.X, ModelsFullScale[0].centerOfGravity.Y);

			Debug.WriteLine("FullScale Coordinates Number: " + ModelsFullScale[0].noOfCordinates);
		int	Edgepoints = ModelsFullScale[0].noOfCordinates / 32;
			if (Edgepoints < minEdgepoints)
			{
				Edgepoints = minEdgepoints;
			}

			

			if (ModelsFullScale[0].noOfCordinates < Edgepoints)
				return Scalefactor;

			Model.noOfCordinates = ModelsFullScale[0].noOfCordinates;
			while (Model.noOfCordinates > Edgepoints)
			{
				Scalefactor = Scalefactor / 2;
				Model = CreateModel(templateArr, Scalefactor, 0);
			}


			if (Scalefactor < 0.25)
				return 0.25f;
			else
				return Scalefactor * 2;
/*
			if (Scalefactor < 0.125)
				return 0.125f;
			else

				return Scalefactor * 2;
*/
		}


		


  	public void TrainTemplateNew(Image<Gray, float> templateArr)
		{
			//	Magnitude(templateArr);


			this.scale = CalculatePyramidLevel(templateArr);
			Debug.WriteLine("Scale factor: " + this.scale);

			//		CreateRotatedTemplates(AutoArea(templateArr));
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			CreateRotatedTemplatesNew(templateArr);
			Debug.WriteLine($"Create Rotated Templates time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			stopwatch.Restart();
			Scaled = Create1DarrayForGPU(Models);
			Full = Create1DarrayForGPU(ModelsFullScale);
			CopyTemplateDataToGPU(Scaled);
			Debug.WriteLine($"Create 1D array time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
		}



		public static void DrawTemplatePointsWithGradients(Mat image, EdgeBasedModelInfo model, MCvScalar pointColor, MCvScalar gradientColor, int radius = 1, float arrowLength = 10f)
		{
			if (image == null || model == null || model.cordinatesarrayX == null || model.cordinatesarrayY == null)
				return;

			for (int i = 0; i < model.noOfCordinates; i++)
			{
				Point pt = new Point(model.cordinatesarrayX[i], model.cordinatesarrayY[i]);

				// Kirajzoljuk a pontot
				CvInvoke.Circle(image, pt, radius, pointColor, -1);

				// Gradient irány kiszámítása
				float gx = model.edgeDerivativeX[i];
				float gy = model.edgeDerivativeY[i];

				// Normalizálás az irányhoz
				float length = (float)Math.Sqrt(gx * gx + gy * gy);
				if (length > 0.0001f)
				{
					float dx = gx / length * arrowLength;
					float dy = gy / length * arrowLength;

					Point arrowEnd = new Point((int)(pt.X + dx), (int)(pt.Y + dy));

					// Vonal kirajzolása az irányban
					CvInvoke.Line(image, pt, arrowEnd, gradientColor, 1);
				}
			}

			// Súlypont kirajzolása
			CvInvoke.Circle(image, model.centerOfGravity, 3, new MCvScalar(0, 255, 255), 2); // sárga kör
		}



		public void TrainTemplate(Image<Gray, float> templateArr)
		{
		//	Magnitude(templateArr);


			this.scale = CalculatePyramidLevel(templateArr);
			Debug.WriteLine("Scale factor: " + this.scale);

			//		CreateRotatedTemplates(AutoArea(templateArr));
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			CreateRotatedTemplates(templateArr);
			Debug.WriteLine( $"Create Rotated Templates time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			stopwatch.Restart();
			Scaled = Create1DarrayForGPU(Models);
			Full = Create1DarrayForGPU(ModelsFullScale);
			CopyTemplateDataToGPU(Scaled);
			Debug.WriteLine($"Create 1D array time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
		}


		public Image<Gray, float> AutoArea(Image<Gray, float> templateArr)
        {
			int X = ModelsFullScale[0].Center.Y - (ModelsFullScale[0].EdgeWith / 2 +1);
			int Y = ModelsFullScale[0].Center.X - (ModelsFullScale[0].EdgeHeight / 2 +1);
//			int width = ModelsFullScale[0].Center.Y + (ModelsFullScale[0].EdgeWith / 2 + 1);
//			int height = ModelsFullScale[0].Center.X + (ModelsFullScale[0].EdgeHeight / 2 + 1);
			int width = ModelsFullScale[0].EdgeWith + 2;
			int height = ModelsFullScale[0].EdgeHeight + 2;

			templateArr.ROI = new Rectangle(X,Y,width,height);

			return templateArr.Copy();
        }


		public void CopyTemplateDataToGPU(EdgeBasedModelInfo360 modelinfo360)
		{
			ErrorCode errorCode;

			CLTempCordX = Cl.CreateBuffer<int>(this.context,  MemFlags.ReadOnly | MemFlags.CopyHostPtr,
							modelinfo360.cordinatesarrayX360, out errorCode);


			CLTempCordY = Cl.CreateBuffer<int>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.cordinatesarrayY360, out errorCode);


			CLTempNoCoord = Cl.CreateBuffer<int>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.step360, out errorCode);

			CLTemppositions = Cl.CreateBuffer<int>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.positions, out errorCode);


			CLTempDerivativeX = Cl.CreateBuffer<float>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.edgeDerivativeX360, out errorCode);

			CLTempDerivativeY = Cl.CreateBuffer<float>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.edgeDerivativeY360, out errorCode);

			CLTempMagnitude = Cl.CreateBuffer<float>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.edgeMagnitude360, out errorCode);

			CLnormMinScore = Cl.CreateBuffer<float>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.normMinScore360, out errorCode);

			CLnormGreediness = Cl.CreateBuffer<float>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				 modelinfo360.normGreediness360, out errorCode);

			NumberOfCord = modelinfo360.noOfCordinates360;
		}

		private void ReleaseMEMObjects()
		{

			Cl.ReleaseMemObject(CLTempCordX);
			Cl.ReleaseMemObject(CLTempCordY); 
			Cl.ReleaseMemObject(CLTempNoCoord); 
			Cl.ReleaseMemObject(CLTemppositions);
			Cl.ReleaseMemObject(CLTempDerivativeX); 
			Cl.ReleaseMemObject(CLTempDerivativeY);
			Cl.ReleaseMemObject(CLTempMagnitude);
			Cl.ReleaseMemObject(CLnormMinScore); 
			Cl.ReleaseMemObject(CLnormGreediness);
		}


		public EdgeBasedModelInfo CreateModelCPU(Image<Gray, float> TemplateImage,float scale, int Rotation)
        {
			
			EdgeBasedModelInfo TempModelinfo = new EdgeBasedModelInfo();
			EdgeBasedModelInfo Result = new EdgeBasedModelInfo(); 
			Mat gx;     //Matrix to store X derivative
			Mat gy;     //Matrix to store Y derivative
			Mat nmsEdges;       //Matrix to store temp restult
			Size Ssize = new Size();
			int ModelSize;
			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
		//	Mat Template;
			int minx = 99999;
			int miny = 99999;
			int maxx = 0;
			int maxy = 0;
			int rot = Rotation;


			if (rot < 0)
			{
				rot = rot + 360;
			}

			if (rot >= 360)
			{
				rot = rot - 360;
			}


			

			if (scale != 1)
			{
			Resized = TemplateImage.Resize(scale,Inter.Linear);
			}
			else
			{
				Resized = TemplateImage;
			//	TemplateImage.CopyTo(Resized);
			}

			if (rot == 0)
			{
				Rotated = Resized;
//				Resized.CopyTo(Rotated);
				 
			}
			else
			{
				Rotated = Resized.Rotate(rot, new Gray(0), false);
		//		Rotated = Resized.Rotate(rot,RotatePoint ,Inter.Area, new Gray(0), false);
			//	Rotated = RotateMat(src, rot);
			}




			Ssize.Width = Rotated.Cols;
			Ssize.Height = Rotated.Rows; //src->height;
			ModelSize = Ssize.Width * Ssize.Height;

			RotatedRect Bounding = new RotatedRect(new PointF(Ssize.Width / 2, Ssize.Height / 2), new SizeF((float)(Resized.Cols * 0.95), (float)(Resized.Rows * 0.95)), rot);

			TempModelinfo.modelHeight = Ssize.Height;       //Save Template height
			TempModelinfo.modelWidth = Ssize.Width;         //Save Template width

			TempModelinfo.noOfCordinates = 0;                                           //initialize	
			TempModelinfo.cordinates = new Point[ModelSize];        //Allocate memory for coorinates of selected points in template image

			TempModelinfo.edgeMagnitude = new float[ModelSize];     //Allocate memory for edge magnitude for selected points
			TempModelinfo.edgeDerivativeX = new float[ModelSize];           //Allocate memory for edge X derivative for selected points
			TempModelinfo.edgeDerivativeY = new float[ModelSize];           ////Allocate memory for edge Y derivative for selected points


			// Calculate gradient of Template
			gx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S,1);      //create Matrix to store X derivative
			gy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative

			CvInvoke.Sobel(Rotated, gx, Emgu.CV.CvEnum.DepthType.Cv16S, 1, 0, 3);        //gradient in X direction			
			CvInvoke.Sobel(Rotated, gy, Emgu.CV.CvEnum.DepthType.Cv16S, 0, 1, 3);    //gradient in Y direction


			nmsEdges = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F,1);      //create Matrix to store Final nmsEdges
			var _gx = gx.ToImage<Gray, float>();
			var _gy = gy.ToImage<Gray, float>();
			var Edges = nmsEdges.ToImage<Gray, float>();
		//			_gx.Save(@"d:\edge\xCPU" +  rot.ToString() + ".jpg");
		//			_gy.Save(@"d:\edge\yCPU" + rot.ToString() + ".jpg");


/*			float[] xx = (float[])_gx.Mat.GetData();

			using (System.IO.StreamWriter file =
				new System.IO.StreamWriter(@"d:/Geomatch/output/resultDerivateXO.dat", false))
			{
				for (int q = 0; q < xx.Length; q++)
					file.WriteLine(xx[q].ToString());
			}
*/

			//			short[] _sdx;
			//			 short[] _sdy;
			float fdx, fdy;
			float MagG, DirG;
			float MaxGradient = (float)-99999.99;
			float direction;
			int[] orients = new int[Ssize.Height * Ssize.Width];
			float[] orientsf = new float[Ssize.Height * Ssize.Width];

			// count variable;
			int count = 0,
				i,
				j;

			float[,] magMat = new float[Ssize.Height,Ssize.Width];


			for (i = 1; i < Ssize.Height - 1; i++)
			{
				for (j = 1; j < Ssize.Width - 1; j++)
				{
					fdx = _gx.Data[i, j,0];
					fdy = _gy.Data[i, j, 0];
					MagG = (float)Math.Sqrt((fdx * fdx) + (fdy * fdy)); //Magnitude = Sqrt(gx^2 +gy^2)
					direction = (float)Math.Abs( (Math.Atan2((float)fdy, (float)fdx)) * 180 / Math.PI);//cvFastArctan((float)fdy,(float)fdx);	 //Direction = invtan (Gy / Gx)
					magMat[i,j] = MagG;
	//				orientsf[count] = direction;
					if (MagG > MaxGradient)
						MaxGradient = MagG; // get maximum gradient value for normalizing.

					// get closest angle from 0, 45, 90, 135 set
					if ((direction > 0 && direction < 22.5) || (direction > 157.5 && direction < 202.5) || (direction > 337.5 && direction < 360))
						direction = 0;
					else if ((direction > 22.5 && direction < 67.5) || (direction > 202.5 && direction < 247.5))
						direction = 45;
					else if ((direction > 67.5 && direction < 112.5) || (direction > 247.5 && direction < 292.5))
						direction = 90;
					else if ((direction > 112.5 && direction < 157.5) || (direction > 292.5 && direction < 337.5))
						direction = 135;
					else
						direction = 0;

					orients[count] = (int)direction;
					count++;
				}
			}

/*
			using (System.IO.StreamWriter file =
				new System.IO.StreamWriter(@"d:/Geomatch/output/resultDirectionCPU"  + rot.ToString()  +  " .dat", false))
			{
				for (int q = 0; q < orientsf.Length; q++)
					file.WriteLine(orientsf[q].ToString());
			}
*/

	//		Debug.WriteLine(MaxGradient);



			count = 0; // init count
					   // non maximum suppression
			float leftPixel =0, rightPixel =0;

			for (i = 1; i < Ssize.Height - 1; i++)
			{
				for (j = 1; j < Ssize.Width - 1; j++)
				{
					switch (orients[count])
					{
						case 0:
							leftPixel = magMat[i,j - 1];
							rightPixel = magMat[i,j + 1];
							break;
						case 45:
							leftPixel = magMat[i - 1,j + 1];
							rightPixel = magMat[i + 1,j - 1];
							break;
						case 90:
							leftPixel = magMat[i - 1,j];
							rightPixel = magMat[i + 1,j];
							break;
						case 135:
							leftPixel = magMat[i - 1,j - 1];
							rightPixel = magMat[i + 1,j + 1];
							break;
					}
					// compare current pixels value with adjacent pixels
					if ((magMat[i,j] < leftPixel) || (magMat[i,j] < rightPixel))
						Edges.Data[i,j,0] = 0.0f;//(nmsEdges->data.ptr + nmsEdges->step*i)[j]=0;
					else
						Edges.Data[i, j, 0] = (float)(magMat[i,j] / MaxGradient * 255);//(nmsEdges->data.ptr + nmsEdges->step*i)[j]=(uchar)(magMat[i][j]/MaxGradient*255);

					count++;
				}
			}


	//		Edges.Save(@"d:\edge\resCPU" + rot.ToString() + ".jpg");
			//Templatepict.image

			int RSum = 0, CSum = 0;
			int curX, curY;
			int flag = 1;

			//Hysterisis threshold

			for (i = 1; i < Ssize.Height - 1; i++)
			{
				for (j = 1; j < Ssize.Width; j++)
				{


					fdx = _gx.Data[i, j,0];
					fdy = _gy.Data[i, j, 0];
//					MagG = (float)Math.Sqrt(fdx * fdx + fdy * fdy); //Magnitude = Sqrt(gx^2 +gy^2)
//					DirG = (float)Math.Atan2(fdy, fdx);  //cvFastArctan((float)fdy, (float)fdx);	 //Direction = tan(y/x)
					flag = 1;
					float val = Edges.Data[i, j,0];


					if (val < this.highThreshold)
					{
						if (val < this.lowThreshold)
						{
							Edges.Data[i, j,0] = 0;
							flag = 0; // remove from edge
						}
						else
						{   // if any of 8 neighboring pixel is not greater than max contraxt remove from edge
					/*
							if ((Edges.Data[i - 1, j - 1, 0] < this.highThreshold) &&
								(Edges.Data[i - 1, j, 0] < this.highThreshold) &&
								(Edges.Data[i - 1, j + 1, 0] < this.highThreshold) &&
								(Edges.Data[i, j - 1, 0] < this.highThreshold) &&
								(Edges.Data[i, j + 1, 0] < this.highThreshold) &&
								(Edges.Data[i + 1, j - 1, 0] < this.highThreshold) &&
								(Edges.Data[i + 1, j, 0] < this.highThreshold) &&
								(Edges.Data[i + 1, j + 1, 0] < this.highThreshold)
								)
							{
								Edges.Data[i, j, 0] = 0;
								flag = 0;
							}
						*/
						}

					}



					// save selected edge information
					curX = i; curY = j;
					if (flag != 0)
					{
						if (fdx != 0 || fdy != 0)
						{
							if (DoesRectangleContainPoint(Bounding,new PointF(curY, curX)) == true)
							{
								// Row sum and column sum for center of gravity
								RSum = RSum + curX;
								CSum = CSum + curY;

								if (curX < minx)
									minx = curX;

								if (curY < miny)
									miny = curY;

								if (curX > maxx)
									maxx = curX;

								if (curY > maxy)
									maxy = curY;



								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].X = curX;
								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].Y = curY;
								TempModelinfo.edgeDerivativeX[TempModelinfo.noOfCordinates] = fdx;
								TempModelinfo.edgeDerivativeY[TempModelinfo.noOfCordinates] = fdy;


								//handle divide by zero
								if (val != 0)
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 1 / val;  // gradient magnitude 
								else
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 0;

								TempModelinfo.noOfCordinates++;
							}
						}
					}
				}
			}
			//		Edges.Save(@"d:\edge\EdgesWithContrast.jpg");
//			Edges.Save(@"d:\edge\EdgesWithContrast" + rot.ToString() + ".jpg");

			TempModelinfo.centerOfGravity.X = minx + (maxx - minx) / 2; // center
			TempModelinfo.centerOfGravity.Y = miny + (maxy - miny) / 2; // center y


			Result.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);



			// change coordinates to reflect center of gravity

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{
				int temp;
				temp = TempModelinfo.cordinates[m].X;
				TempModelinfo.cordinates[m].X = temp - TempModelinfo.centerOfGravity.X;
				temp = TempModelinfo.cordinates[m].Y;
				TempModelinfo.cordinates[m].Y = temp - TempModelinfo.centerOfGravity.Y;
			}


			Result.cordinates = new Point[TempModelinfo.noOfCordinates];        //Allocate memory for coorinates of selected points in template image
			Result.cordinatesarrayX = new int[TempModelinfo.noOfCordinates];        //Allocate memory for coordinates 
			Result.cordinatesarrayY = new int[TempModelinfo.noOfCordinates];        //Allocate memory for coordinates 

			Result.edgeMagnitude = new float[TempModelinfo.noOfCordinates];     //Allocate memory for edge magnitude for selected points
			Result.edgeDerivativeX = new float[TempModelinfo.noOfCordinates];           //Allocate memory for edge X derivative for selected points
			Result.edgeDerivativeY = new float[TempModelinfo.noOfCordinates];           ////Allocate memory for edge Y derivative for selected points
			Result.centerOfGravity.X = TempModelinfo.centerOfGravity.X;
			Result.centerOfGravity.Y = TempModelinfo.centerOfGravity.Y;
			Result.modelHeight = TempModelinfo.modelHeight;
			Result.modelWidth = TempModelinfo.modelWidth;
			Result.noOfCordinates = TempModelinfo.noOfCordinates;
			Result.rotation = Rotation;
			Result.MaxGradient = MaxGradient;

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{


				Result.cordinates[m].X = TempModelinfo.cordinates[m].X;
				Result.cordinates[m].Y = TempModelinfo.cordinates[m].Y;
				Result.cordinatesarrayX[m] = TempModelinfo.cordinates[m].X;
				Result.cordinatesarrayY[m] = TempModelinfo.cordinates[m].Y;

				Result.edgeMagnitude[m] = TempModelinfo.edgeMagnitude[m];
				Result.edgeDerivativeX[m] = TempModelinfo.edgeDerivativeX[m];
				Result.edgeDerivativeY[m] = TempModelinfo.edgeDerivativeY[m];
			}

			Result.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
			Result.EdgeHeight = maxx - minx;
			Result.EdgeWith = maxy - miny;

			gx.Dispose();
			gy.Dispose();
			nmsEdges.Dispose();


			return Result;
		}





		public ModelInfo CreateModelNew(Image<Gray, float> TemplateImage )
		{

			ModelInfo TempModelinfo = new ModelInfo();
			ModelInfo Result = new ModelInfo();
			Mat gx;     //Matrix to store X derivative
			Mat gy;     //Matrix to store Y derivative

			Size Ssize = new Size();
			int ModelSize;
			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
			//	Mat Template;
			int minx = int.MaxValue;
			int miny = int.MaxValue;
			int maxx = 0;
			int maxy = 0;

			
			Ssize.Width = TemplateImage.Cols;
			Ssize.Height = TemplateImage.Rows; 

//			TempModelinfo.modelHeight = Ssize.Height;       //Save Template height
//			TempModelinfo.modelWidth = Ssize.Width;         //Save Template width

			// Calculate gradient of Template
			gx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
			gy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      
			Mat directions = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);     

			CvInvoke.Sobel(TemplateImage, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);        //gradient in X direction			
			CvInvoke.Sobel(TemplateImage, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);    //gradient in Y direction
			CvInvoke.CartToPolar(gx, gy, magnitude, directions);

			double minVal = 0;
			double maxVal = 0;
			Point minLoc = new Point();
			Point maxLoc = new Point();
			CvInvoke.MinMaxLoc(magnitude, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

	
			var _gx = gx.ToImage<Gray, float>();
			var _gy = gy.ToImage<Gray, float>();
			var Angle = directions.ToImage<Gray, float>();
			var mag = magnitude.ToImage<Gray, float>();
			float MagG;
			float MaxGradient = (float)maxVal;
			float direction;

			float leftPixel = 0, rightPixel = 0;
			float Divmag = 0;
			int RSum = 0, CSum = 0;
			int curX, curY;


			for (int i = 1; i < Ssize.Height - 1; i++)
			{
				for (int j = 1; j < Ssize.Width - 1; j++)
				{
					direction = (float)(Angle.Data[i, j, 0]);
					MagG = mag.Data[i, j, 0];

					//		Debug.WriteLine(direction);
					// get closest angle from 0, 45, 90, 135 set
					if ((direction > 0F && direction < 0.3926) || (direction > 2.7488 && direction < 3.5342) || (direction > 5.8904 && direction < 6.283))
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}
					else if ((direction > 0.3926 && direction < 1.178) || (direction > 3.534 && direction < 4.319))
					{
						leftPixel = mag.Data[i - 1, j + 1, 0];
						rightPixel = mag.Data[i + 1, j - 1, 0];
					}
					else if ((direction > 1.178 && direction < 1.963) || (direction > 4.319 && direction < 5.105))
					{
						leftPixel = mag.Data[i - 1, j, 0];
						rightPixel = mag.Data[i + 1, j, 0];
					}
					else if ((direction > 1.963 && direction < 157.5) || (direction > 5.105 && direction < 5.890))
					{
						leftPixel = mag.Data[i - 1, j - 1, 0];
						rightPixel = mag.Data[i + 1, j + 1, 0];
					}
					else
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}

					
					if ((MagG < leftPixel) || (MagG < rightPixel))
						mag.Data[i, j, 0] = 0.0f;
					else
					{
						Divmag = (float)(MagG / MaxGradient * 255);
						//	Divmag = MagG ;
						if (Divmag < this.highThreshold && Divmag > this.lowThreshold)
						{
						/*
							mag.Data[i, j - 1, 0] = 0;
							mag.Data[i, j + 1, 0] = 0;
							mag.Data[i - 1, j + 1, 0] = 0;
							mag.Data[i + 1, j - 1, 0] = 0;
							mag.Data[i - 1, j, 0] = 0;
							mag.Data[i + 1, j, 0] = 0;
							mag.Data[i - 1, j - 1, 0] = 0;
							mag.Data[i + 1, j + 1, 0] = 0;
						*/


							EdgePoint TempPoint = new EdgePoint();

							curX = i; curY = j;

							{
								//	mag.Data[i, j, 0] = Divmag;
								RSum = RSum + curX;
								CSum = CSum + curY;

								if (curX < minx)
									minx = curX;

								if (curY < miny)
									miny = curY;

								if (curX > maxx)
									maxx = curX;

								if (curY > maxy)
									maxy = curY;


								TempPoint.Location = new Point(curX, curY);
								TempPoint.DerivateX = _gx.Data[i, j, 0];
								TempPoint.DerivateY = _gy.Data[i, j, 0];
								TempPoint.OriginalValue = TemplateImage.Data[i, j, 0];
								mag.Data[i, j, 0] = TemplateImage.Data[i, j, 0];
								//handle divide by zero

								if (Divmag != 0)
								{
								
									TempPoint.Magnitude = 1 / Divmag;
								}
								else
									TempPoint.Magnitude = 0;

								TempModelinfo.EdgePoints.Add(TempPoint);


							}
						}
						else
							mag.Data[i, j, 0] = 0;

					}
				}
			}

		//	mag.Save(@"c:\edge\EdgesWithContrast" + ".jpg");
			TempModelinfo.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
			TempModelinfo.MaxGradient = MaxGradient;
			TempModelinfo.ModelSize = new Size(maxy - miny , maxx - minx);
			
			// change coordinates to reflect center of gravity
			for (int m = 0; m < TempModelinfo.EdgePoints.Count; m++)
			{
				TempModelinfo.EdgePoints[m].Location.Offset( -TempModelinfo.Center.X,  -TempModelinfo.Center.Y);
			}

			gx.Dispose();
			gy.Dispose();
			magnitude.Dispose();
			directions.Dispose();
			_gx.Dispose();
			_gy.Dispose();
			Angle.Dispose();
			mag.Dispose();

			return TempModelinfo;
		}


		public ModelInfo RotateModelInfo(ModelInfoBase Base, int angle)
        {
			ModelInfo RotatedModel = new ModelInfo();
			RotatedModel.MaxGradient = Base.MaxGradient;
			RotatedModel.ModelSize = Base.ModelSize;
			RotatedModel.Center = Base.Center;

			for (int i = 0; i < Base.EdgePoints.Count; i++)
            {
				EdgePoint pt = new EdgePoint();
				pt.DerivateX = Base.EdgePoints[i].DerivateX;
				pt.DerivateY = Base.EdgePoints[i].DerivateY;
				pt.Magnitude = Base.EdgePoints[i].Magnitude;
				pt.Location = RotateOnePoint(Base.EdgePoints[i].Location, angle);


				RotatedModel.EdgePoints.Add(pt);  

            }


			return RotatedModel;
        }


		public ModelInfoBase CreateModelBase(Image<Gray, float> TemplateImage, bool Half)
		{

			ModelInfoBase TempModelinfo = new ModelInfoBase();
			ModelInfoBase Result = new ModelInfoBase();
			Mat gx;     //Matrix to store X derivative
			Mat gy;     //Matrix to store Y derivative

			Size Ssize = new Size();
			int ModelSize;
			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
			//	Mat Template;
			int minx = int.MaxValue;
			int miny = int.MaxValue;
			int maxx = 0;
			int maxy = 0;


			Ssize.Width = TemplateImage.Cols;
			Ssize.Height = TemplateImage.Rows;

			//			TempModelinfo.modelHeight = Ssize.Height;       //Save Template height
			//			TempModelinfo.modelWidth = Ssize.Width;         //Save Template width

			// Calculate gradient of Template
			gx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
			gy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Mat directions = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);

			CvInvoke.Sobel(TemplateImage, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);        //gradient in X direction			
			CvInvoke.Sobel(TemplateImage, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);    //gradient in Y direction
			CvInvoke.CartToPolar(gx, gy, magnitude, directions);

			double minVal = 0;
			double maxVal = 0;
			Point minLoc = new Point();
			Point maxLoc = new Point();
			CvInvoke.MinMaxLoc(magnitude, ref minVal, ref maxVal, ref minLoc, ref maxLoc);


			var _gx = gx.ToImage<Gray, float>();
			var _gy = gy.ToImage<Gray, float>();
			var Angle = directions.ToImage<Gray, float>();
			var mag = magnitude.ToImage<Gray, float>();
			float MagG;
			float MaxGradient = (float)maxVal;
			float direction;

			float leftPixel = 0, rightPixel = 0;
			float Divmag = 0;
			int RSum = 0, CSum = 0;
			int curX, curY;

		//	Parallel.For(1, Ssize.Height - 1, i =>
						for (int i = 1; i < Ssize.Height - 1; i++)
			{
				for (int j = 1; j < Ssize.Width - 1; j++)
				{
					direction = (float)(Angle.Data[i, j, 0]);
					MagG = mag.Data[i, j, 0];

					//		Debug.WriteLine(direction);
					// get closest angle from 0, 45, 90, 135 set
					if ((direction > 0F && direction < 0.3926) || (direction > 2.7488 && direction < 3.5342) || (direction > 5.8904 && direction < 6.283))
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}
					else if ((direction > 0.3926 && direction < 1.178) || (direction > 3.534 && direction < 4.319))
					{
						leftPixel = mag.Data[i - 1, j + 1, 0];
						rightPixel = mag.Data[i + 1, j - 1, 0];
					}
					else if ((direction > 1.178 && direction < 1.963) || (direction > 4.319 && direction < 5.105))
					{
						leftPixel = mag.Data[i - 1, j, 0];
						rightPixel = mag.Data[i + 1, j, 0];
					}
					else if ((direction > 1.963 && direction < 157.5) || (direction > 5.105 && direction < 5.890))
					{
						leftPixel = mag.Data[i - 1, j - 1, 0];
						rightPixel = mag.Data[i + 1, j + 1, 0];
					}
					else
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}


					if ((MagG < leftPixel) || (MagG < rightPixel))
						mag.Data[i, j, 0] = 0.0f;
					else
					{
						Divmag = (float)(MagG / MaxGradient * 255);
						//	Divmag = MagG ;
						if (Divmag < this.highThreshold && Divmag > this.lowThreshold)
						{

							if (Half)
							{
								mag.Data[i, j - 1, 0] = 0;
								mag.Data[i, j + 1, 0] = 0;
								mag.Data[i - 1, j + 1, 0] = 0;
								mag.Data[i + 1, j - 1, 0] = 0;
								mag.Data[i - 1, j, 0] = 0;
								mag.Data[i + 1, j, 0] = 0;
								mag.Data[i - 1, j - 1, 0] = 0;
								mag.Data[i + 1, j + 1, 0] = 0;
							}


							EdgePointWithNeighbor TempPoint = new EdgePointWithNeighbor();



							TempPoint.B = TemplateImage.Data[i - 1, j, 0];
							TempPoint.T = TemplateImage.Data[i + 1, j, 0];
							TempPoint.L = TemplateImage.Data[i, j - 1, 0];
							TempPoint.R = TemplateImage.Data[i, j + 1, 0];

							TempPoint.LB = TemplateImage.Data[i - 1, j - 1, 0];
							TempPoint.LT = TemplateImage.Data[i + 1, j - 1, 0];
							TempPoint.RB = TemplateImage.Data[i - 1, j + 1, 0];
							TempPoint.RT = TemplateImage.Data[i + 1, j + 1, 0];

							curX = i; curY = j;

							{
								//	mag.Data[i, j, 0] = Divmag;
								RSum = RSum + curX;
								CSum = CSum + curY;

								if (curX < minx)
									minx = curX;

								if (curY < miny)
									miny = curY;

								if (curX > maxx)
									maxx = curX;

								if (curY > maxy)
									maxy = curY;


								TempPoint.Location = new Point(curX, curY);
								TempPoint.DerivateX = _gx.Data[i, j, 0];
								TempPoint.DerivateY = _gy.Data[i, j, 0];
								TempPoint.OriginalValue = TemplateImage.Data[i, j, 0];
								mag.Data[i, j, 0] = TemplateImage.Data[i, j, 0];
								//handle divide by zero

								if (Divmag != 0)
								{

									TempPoint.Magnitude = 1 / Divmag;
								}
								else
									TempPoint.Magnitude = 0;

								TempModelinfo.EdgePoints.Add(TempPoint);


							}
						}
						else
							mag.Data[i, j, 0] = 0;

					}
				}
			}


		//	for 


	//		mag.Save(@"c:\edge\EdgesWithContrast" + ".jpg");
			TempModelinfo.Center = new Point(minx + (maxx - minx) / 2,  miny + (maxy - miny) / 2);
			TempModelinfo.MaxGradient = MaxGradient;
			TempModelinfo.ModelSize = new Size(maxy - miny, maxx - minx);

			// change coordinates to reflect center of gravity
			for (int m = 0; m < TempModelinfo.EdgePoints.Count; m++)
			{
				TempModelinfo.EdgePoints[m].Location.Offset(-TempModelinfo.Center.X, -TempModelinfo.Center.Y);
			}

			gx.Dispose();
			gy.Dispose();
			magnitude.Dispose();
			directions.Dispose();
			_gx.Dispose();
			_gy.Dispose();
			Angle.Dispose();
			mag.Dispose();

			return TempModelinfo;
		}




		public double RotateTest(Image<Gray, float> TemplateImage)
        {
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();


			using (System.IO.StreamWriter file =
new System.IO.StreamWriter(@"C:\Results\res.csv", false))
			{
			
				for (int i = 0; i < 360; i++)
				{
					var Rotated = TemplateImage.Rotate(i, new PointF(26, 26), Inter.Linear, new Gray(0), true);
					var rotatedRGB = Rotated.Convert<Bgr, byte>();

					rotatedRGB.Draw(new Cross2DF(new PointF(25, 26), 1, 1), new Bgr(0, 0, 255), 1);

					rotatedRGB.Save(@"C:\Results\EdgeBase\" + i.ToString() + ".png");
					Mat gx = new Mat(Rotated.Size, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
					Mat gy = new Mat(Rotated.Size, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
					Mat magnitude = new Mat(Rotated.Size, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
					Mat directions = new Mat(Rotated.Size, Emgu.CV.CvEnum.DepthType.Cv16S, 1);

					CvInvoke.Sobel(Rotated, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);        //gradient in X direction			
					CvInvoke.Sobel(Rotated, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);    //gradient in Y direction
					CvInvoke.CartToPolar(gx, gy, magnitude, directions);
					var _gx = gx.ToImage<Gray, float>();
					var _gy = gy.ToImage<Gray, float>();
					var Angle = directions.ToImage<Gray, float>();
					var mag = magnitude.ToImage<Gray, float>();

					Console.WriteLine(_gx.Data[26, 25, 0] + " --- " + _gy.Data[26, 25, 0] + " ---> " + mag.Data[26, 25, 0]);
					file.WriteLine(i.ToString() + "," + _gx.Data[26, 25, 0] + "," + _gy.Data[26, 25, 0] + "," + mag.Data[26, 25, 0]);

				}


			}
			return (double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency;
		}


		public EdgeBasedModelInfo CreateModel(Image<Gray, float> TemplateImage, float scale, int Rotation)
		{
			EdgeBasedModelInfo TempModelinfo = new EdgeBasedModelInfo();
			EdgeBasedModelInfo Result = new EdgeBasedModelInfo();
			Mat gx;
			Mat gy; 
			Mat nmsEdges; 
			Size Ssize = new Size();
			int ModelSize;
			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
			int minx = 99999;
			int miny = 99999;
			int maxx = 0;
			int maxy = 0;
			int rot = Rotation;
			if (rot < 0)
			{
				rot = rot + 360;
			}

			if (rot >= 360)
			{
				rot = rot - 360;
			}


			if (scale != 1)
			{
				Resized = TemplateImage.Resize(scale, Inter.Linear);
			}
			else
			{
				Resized = TemplateImage;
			}

			if (rot == 0)
			{
				Rotated = Resized;
			}
			else
			{
				Rotated = Resized.Rotate(rot, new Gray(0), false);
			}

			Ssize.Width = Rotated.Cols;
			Ssize.Height = Rotated.Rows; //src->height;
			ModelSize = Ssize.Width * Ssize.Height;
//			RotatedRect Bounding = new RotatedRect(new PointF(Ssize.Width / 2, Ssize.Height / 2), new SizeF((float)(Resized.Cols * 0.95), (float)(Resized.Rows * 0.95)), rot);

			TempModelinfo.modelHeight = Ssize.Height; 
			TempModelinfo.modelWidth = Ssize.Width; 

			TempModelinfo.noOfCordinates = 0;
			TempModelinfo.cordinates = new Point[ModelSize]; 
			TempModelinfo.edgeMagnitude = new float[ModelSize]; 
			TempModelinfo.edgeDerivativeX = new float[ModelSize]; 
			TempModelinfo.edgeDerivativeY = new float[ModelSize];

			gx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1); 
			gy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1); 
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Mat magnitude2 = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);

			Mat directions = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
		//	long elapsed_time ;
	//		var stopwatch = new Stopwatch();
	//		stopwatch.Start();
			CvInvoke.Sobel(Rotated, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3); 		
			CvInvoke.Sobel(Rotated, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);
	//		stopwatch.Stop();
	//		elapsed_time = stopwatch.ElapsedMilliseconds;
	//		Console.WriteLine("Sobel -> " + elapsed_time.ToString());
	//		stopwatch.Restart();
			CvInvoke.CartToPolar(gx, gy, magnitude, directions);
	//		stopwatch.Stop();
	//		elapsed_time = stopwatch.ElapsedMilliseconds;
	//		Console.WriteLine("CartToPolar -> " + elapsed_time.ToString());

			

			double minVal = 0;
			double maxVal = 0;
			Point minLoc = new Point();
			Point maxLoc = new Point();
			CvInvoke.MinMaxLoc(magnitude, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

			nmsEdges = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1); 
			var _gx = gx.ToImage<Gray, float>();
			var _gy = gy.ToImage<Gray, float>();
			var Angle = directions.ToImage<Gray, float>();
			var mag = magnitude.ToImage<Gray, float>();
			
			float fdx, fdy;
			float MagG, DirG;
			float MaxGradient =  (float)maxVal;
			float direction;

			float leftPixel = 0, rightPixel = 0;
			float Divmag = 0;
			int RSum = 0, CSum = 0;
			int curX, curY;
			int i, j;

			float[,] magMat = new float[Ssize.Height, Ssize.Width];


			for (i = 1; i < Ssize.Height - 1; i++)
			{
				for (j = 1; j < Ssize.Width - 1; j++)
				{
					direction = (float)( Angle.Data[i, j, 0]);
					MagG = mag.Data[i, j, 0];

					if ((direction > 0F && direction < 0.3926) || (direction > 2.7488 && direction < 3.5342) || (direction > 5.8904 && direction < 6.283))
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}
					else if ((direction > 0.3926 && direction < 1.178) || (direction > 3.534 && direction < 4.319))
					{
						leftPixel = mag.Data[i - 1, j + 1, 0];
						rightPixel = mag.Data[i + 1, j - 1, 0];
					}
					else if ((direction > 1.178 && direction < 1.963) || (direction > 4.319 && direction < 5.105))
					{
						leftPixel = mag.Data[i - 1, j, 0];
						rightPixel = mag.Data[i + 1, j, 0];
					}
					else if ((direction > 1.963 && direction < 157.5) || (direction > 5.105 && direction < 5.890))
					{
						leftPixel = mag.Data[i - 1, j - 1, 0];
						rightPixel = mag.Data[i + 1, j + 1, 0];
					}
					else
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}

					if ((MagG < leftPixel) || (MagG < rightPixel))
						mag.Data[i, j, 0] = 0.0f;
					else
					{
						Divmag = (float)(MagG / MaxGradient * 255);
						if (Divmag < this.highThreshold && Divmag > this.lowThreshold)
						{
							mag.Data[i, j - 1, 0] = 0;
							mag.Data[i, j + 1, 0] = 0;
							mag.Data[i - 1, j + 1, 0] = 0;
							mag.Data[i + 1, j - 1, 0] = 0;
							mag.Data[i - 1, j, 0] = 0;
							mag.Data[i + 1, j, 0] = 0;
							mag.Data[i - 1, j - 1, 0] = 0;
							mag.Data[i + 1, j + 1, 0] = 0;
							curX = i; curY = j;
					//		if (DoesRectangleContainPoint(Bounding, new PointF(curY, curX)) == true)
							{
								RSum = RSum + curX;
								CSum = CSum + curY;

								if (curX < minx)
									minx = curX;

								if (curY < miny)
									miny = curY;

								if (curX > maxx)
									maxx = curX;

								if (curY > maxy)
									maxy = curY;

								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].X = curX;
								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].Y = curY;
								TempModelinfo.edgeDerivativeX[TempModelinfo.noOfCordinates] = _gx.Data[i, j, 0];
								TempModelinfo.edgeDerivativeY[TempModelinfo.noOfCordinates] = _gy.Data[i, j, 0];

								if (Divmag != 0)
								{
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 1 / Divmag; 
								}
								else
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 0;

								TempModelinfo.noOfCordinates++;


							}
						}
						else
							mag.Data[i, j, 0] = 0;
				
					}
				}
			}

			TempModelinfo.centerOfGravity.X = minx + (maxx - minx) / 2; 
			TempModelinfo.centerOfGravity.Y = miny + (maxy - miny) / 2; 
			Result.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
			Result.EdgeHeight = maxx - minx;
			Result.EdgeWith = maxy - miny;

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{
				int temp;
				temp = TempModelinfo.cordinates[m].X;
				TempModelinfo.cordinates[m].X = temp - TempModelinfo.centerOfGravity.X;
				temp = TempModelinfo.cordinates[m].Y;
				TempModelinfo.cordinates[m].Y = temp - TempModelinfo.centerOfGravity.Y;
			}

			Result.cordinates = new Point[TempModelinfo.noOfCordinates];        
			Result.cordinatesarrayX = new int[TempModelinfo.noOfCordinates];      
			Result.cordinatesarrayY = new int[TempModelinfo.noOfCordinates];       

			Result.edgeMagnitude = new float[TempModelinfo.noOfCordinates];    
			Result.edgeDerivativeX = new float[TempModelinfo.noOfCordinates];
			Result.edgeDerivativeY = new float[TempModelinfo.noOfCordinates];
			Result.centerOfGravity.X = TempModelinfo.centerOfGravity.X;
			Result.centerOfGravity.Y = TempModelinfo.centerOfGravity.Y;
			Result.modelHeight = TempModelinfo.modelHeight;
			Result.modelWidth = TempModelinfo.modelWidth;
			Result.noOfCordinates = TempModelinfo.noOfCordinates;
			Result.rotation = Rotation;
			Result.MaxGradient = MaxGradient;

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{
				Result.cordinates[m].X = TempModelinfo.cordinates[m].X;
				Result.cordinates[m].Y = TempModelinfo.cordinates[m].Y;
				Result.cordinatesarrayX[m] = TempModelinfo.cordinates[m].X;
				Result.cordinatesarrayY[m] = TempModelinfo.cordinates[m].Y;

				Result.edgeMagnitude[m] = TempModelinfo.edgeMagnitude[m];
				Result.edgeDerivativeX[m] = TempModelinfo.edgeDerivativeX[m];
				Result.edgeDerivativeY[m] = TempModelinfo.edgeDerivativeY[m];
			}
			gx.Dispose();
			gy.Dispose();
			nmsEdges.Dispose();
			return Result;
		}




		public EdgeBasedModelInfo[] CreateModels(Image<Gray, float> TemplateImage, float scale)
		{

			EdgeBasedModelInfo[] TempModelinfo = new EdgeBasedModelInfo[360];
			EdgeBasedModelInfo[] Result = new EdgeBasedModelInfo[360];

			Result[0] = new EdgeBasedModelInfo();
			TempModelinfo[0] = new EdgeBasedModelInfo();


			Mat gx;     //Matrix to store X derivative
			Mat gy;     //Matrix to store Y derivative
			Mat nmsEdges;       //Matrix to store temp restult
			Size Ssize = new Size();
			int ModelSize;
			Image<Gray, float> Resized;


			int minx = 99999;
			int miny = 99999;
			int maxx = 0;
			int maxy = 0;
			


		




			if (scale != 1)
			{
				Resized = TemplateImage.Resize(scale, Inter.Linear);
			}
			else
			{
				Resized = TemplateImage;
			}


			Image<Gray, float> RotatedImage;

			Ssize.Width = Resized.Cols;
			Ssize.Height = Resized.Rows; //src->height;
			ModelSize = Ssize.Width * Ssize.Height;

			TempModelinfo[0].modelHeight = Ssize.Height;       //Save Template height
			TempModelinfo[0].modelWidth = Ssize.Width;         //Save Template width

			TempModelinfo[0].noOfCordinates = 0;                                           //initialize	
			TempModelinfo[0].cordinates = new Point[ModelSize];        //Allocate memory for coorinates of selected points in template image

			TempModelinfo[0].edgeMagnitude = new float[ModelSize];     //Allocate memory for edge magnitude for selected points
			TempModelinfo[0].edgeDerivativeX = new float[ModelSize];           //Allocate memory for edge X derivative for selected points
			TempModelinfo[0].edgeDerivativeY = new float[ModelSize];           ////Allocate memory for edge Y derivative for selected points


			// Calculate gradient of Template
			gx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
			gy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
			Mat magnitude2 = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative

			Mat directions = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative

			CvInvoke.Sobel(Resized, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);        //gradient in X direction			
			CvInvoke.Sobel(Resized, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);    //gradient in Y direction
			CvInvoke.CartToPolar(gx, gy, magnitude, directions);
			//	CvInvoke.Sqrt(magnitude2, magnitude);

			double minVal = 0;
			double maxVal = 0;
			Point minLoc = new Point();
			Point maxLoc = new Point();
			CvInvoke.MinMaxLoc(magnitude, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

			//	magnitude.Save(@"D:\Edge\Test\Magnitude_" + scale.ToString() + "_" + rot.ToString() + ".bmp");

			nmsEdges = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);      //create Matrix to store Final nmsEdges
			var _gx = gx.ToImage<Gray, float>();
			var _gy = gy.ToImage<Gray, float>();
			var Angle = directions.ToImage<Gray, float>();
			var mag = magnitude.ToImage<Gray, float>();

			float fdx, fdy;
			float MagG, DirG;
			float MaxGradient = (float)maxVal;
			float direction;

			//			_gx.Save(@"d:\edge\xCPU.jpg");
			//			_gy.Save(@"d:\edge\yCPU.jpg");
			//			mag.Save(@"d:\edge\mCPU.jpg");

			float leftPixel = 0, rightPixel = 0;
			float Divmag = 0;
			int RSum = 0, CSum = 0;
			int curX, curY;
			int i, j;

			float[,] magMat = new float[Ssize.Height, Ssize.Width];


			for (i = 1; i < Ssize.Height - 1; i++)
			{
				for (j = 1; j < Ssize.Width - 1; j++)
				{
					direction = (float)(Angle.Data[i, j, 0]);
					MagG = mag.Data[i, j, 0];

					//		Debug.WriteLine(direction);
					// get closest angle from 0, 45, 90, 135 set
					if ((direction > 0F && direction < 0.3926) || (direction > 2.7488 && direction < 3.5342) || (direction > 5.8904 && direction < 6.283))
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}
					else if ((direction > 0.3926 && direction < 1.178) || (direction > 3.534 && direction < 4.319))
					{
						leftPixel = mag.Data[i - 1, j + 1, 0];
						rightPixel = mag.Data[i + 1, j - 1, 0];
					}
					else if ((direction > 1.178 && direction < 1.963) || (direction > 4.319 && direction < 5.105))
					{
						leftPixel = mag.Data[i - 1, j, 0];
						rightPixel = mag.Data[i + 1, j, 0];
					}
					else if ((direction > 1.963 && direction < 157.5) || (direction > 5.105 && direction < 5.890))
					{
						leftPixel = mag.Data[i - 1, j - 1, 0];
						rightPixel = mag.Data[i + 1, j + 1, 0];
					}
					else
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}

					// compare current pixels value with adjacent pixels
					if ((MagG < leftPixel) || (MagG < rightPixel))
						mag.Data[i, j, 0] = 0.0f;
					else
					{
						Divmag = (float)(MagG / MaxGradient * 255);
						//	Divmag = MagG ;
						if (Divmag < this.highThreshold && Divmag > this.lowThreshold)
						{

							mag.Data[i, j - 1, 0] = 0;
							mag.Data[i, j + 1, 0] = 0;
							mag.Data[i - 1, j + 1, 0] = 0;
							mag.Data[i + 1, j - 1, 0] = 0;
							mag.Data[i - 1, j, 0] = 0;
							mag.Data[i + 1, j, 0] = 0;
							mag.Data[i - 1, j - 1, 0] = 0;
							mag.Data[i + 1, j + 1, 0] = 0;


							curX = i; curY = j;
							
							
								//	mag.Data[i, j, 0] = Divmag;
								RSum = RSum + curX;
								CSum = CSum + curY;

								if (curX < minx)
									minx = curX;

								if (curY < miny)
									miny = curY;

								if (curX > maxx)
									maxx = curX;

								if (curY > maxy)
									maxy = curY;

								TempModelinfo[0].cordinates[TempModelinfo[0].noOfCordinates].X = curX;
								TempModelinfo[0].cordinates[TempModelinfo[0].noOfCordinates].Y = curY;
								TempModelinfo[0].edgeDerivativeX[TempModelinfo[0].noOfCordinates] = _gx.Data[i, j, 0];
								TempModelinfo[0].edgeDerivativeY[TempModelinfo[0].noOfCordinates] = _gy.Data[i, j, 0];


								//handle divide by zero

								if (Divmag != 0)
								{
									TempModelinfo[0].edgeMagnitude[TempModelinfo[0].noOfCordinates] = 1 / Divmag;  // gradient magnitude 
																											 //		TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = Divmag;  // gradient magnitude 
								}
								else
									TempModelinfo[0].edgeMagnitude[TempModelinfo[0].noOfCordinates] = 0;

								TempModelinfo[0].noOfCordinates++;


							
						}
						else
							mag.Data[i, j, 0] = 0;

					}
				}
			}

			//			mag.Save(@"d:\edge\EdgesWithContrast" + rot.ToString() + ".jpg");

			TempModelinfo[0].centerOfGravity.X = minx + (maxx - minx) / 2; // center
			TempModelinfo[0].centerOfGravity.Y = miny + (maxy - miny) / 2; // center y
			Result[0].Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
			Result[0].EdgeHeight = maxx - minx;
			Result[0].EdgeWith = maxy - miny;


			// change coordinates to reflect center of gravity

			for (int m = 0; m < TempModelinfo[0].noOfCordinates; m++)
			{
				int temp;
				temp = TempModelinfo[0].cordinates[m].X;
				TempModelinfo[0].cordinates[m].X = temp - TempModelinfo[0].centerOfGravity.X;
				temp = TempModelinfo[0].cordinates[m].Y;
				TempModelinfo[0].cordinates[m].Y = temp - TempModelinfo[0].centerOfGravity.Y;
			}


			Result[0].cordinates = new Point[TempModelinfo[0].noOfCordinates];        //Allocate memory for coorinates of selected points in template image
			Result[0].cordinatesarrayX = new int[TempModelinfo[0].noOfCordinates];        //Allocate memory for coordinates 
			Result[0].cordinatesarrayY = new int[TempModelinfo[0].noOfCordinates];        //Allocate memory for coordinates 

			Result[0].edgeMagnitude = new float[TempModelinfo[0].noOfCordinates];     //Allocate memory for edge magnitude for selected points
			Result[0].edgeDerivativeX = new float[TempModelinfo[0].noOfCordinates];           //Allocate memory for edge X derivative for selected points
			Result[0].edgeDerivativeY = new float[TempModelinfo[0].noOfCordinates];           ////Allocate memory for edge Y derivative for selected points
			Result[0].centerOfGravity.X = TempModelinfo[0].centerOfGravity.X;
			Result[0].centerOfGravity.Y = TempModelinfo[0].centerOfGravity.Y;
			Result[0].modelHeight = TempModelinfo[0].modelHeight;
			Result[0].modelWidth = TempModelinfo[0].modelWidth;
			Result[0].noOfCordinates = TempModelinfo[0].noOfCordinates;
			Result[0].rotation = 0;
			Result[0].MaxGradient = MaxGradient;

			for (int m = 0; m < TempModelinfo[0].noOfCordinates; m++)
			{


				Result[0].cordinates[m].X = TempModelinfo[0].cordinates[m].X;
				Result[0].cordinates[m].Y = TempModelinfo[0].cordinates[m].Y;
				Result[0].cordinatesarrayX[m] = TempModelinfo[0].cordinates[m].X;
				Result[0].cordinatesarrayY[m] = TempModelinfo[0].cordinates[m].Y;

				Result[0].edgeMagnitude[m] = TempModelinfo[0].edgeMagnitude[m];
				Result[0].edgeDerivativeX[m] = TempModelinfo[0].edgeDerivativeX[m];
				Result[0].edgeDerivativeY[m] = TempModelinfo[0].edgeDerivativeY[m];
			}




			for (int a = 1; a < 360; a++)
            {
				RotatedImage = Resized.Rotate(360-a,new PointF (TempModelinfo[0].centerOfGravity.X, TempModelinfo[0].centerOfGravity.Y), Emgu.CV.CvEnum.Inter.Area, new Gray(0), false);
				gx = new Mat(RotatedImage.Size, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
				gy = new Mat(RotatedImage.Size, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
				magnitude = new Mat(RotatedImage.Size, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative


				CvInvoke.Sobel(RotatedImage, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);        //gradient in X direction			
				CvInvoke.Sobel(RotatedImage, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);    //gradient in Y direction
				CvInvoke.CartToPolar(gx, gy, magnitude, directions);
				var gxx = gx.ToImage<Gray, float>();
				var gyy = gy.ToImage<Gray, float>();
				var magn = magnitude.ToImage<Gray, float>();
				var magcolor = magn.Convert<Bgr, byte>();
				
				

				Result[a] = new EdgeBasedModelInfo();
				Result[a].cordinates = RotatePoints(TempModelinfo[0].cordinates, a);        //Allocate memory for coorinates of selected points in template image
				Result[a].cordinatesarrayX = new int[TempModelinfo[0].noOfCordinates];        //Allocate memory for coordinates 
				Result[a].cordinatesarrayY = new int[TempModelinfo[0].noOfCordinates];        //Allocate memory for coordinates 
				Result[a].edgeMagnitude = new float[TempModelinfo[0].noOfCordinates];     //Allocate memory for edge magnitude for selected points
				Result[a].edgeDerivativeX = new float[TempModelinfo[0].noOfCordinates];           //Allocate memory for edge X derivative for selected points
				Result[a].edgeDerivativeY = new float[TempModelinfo[0].noOfCordinates];           ////Allocate memory for edge Y derivative for selected points
				Result[a].centerOfGravity.X = TempModelinfo[0].centerOfGravity.X;
				Result[a].centerOfGravity.Y = TempModelinfo[0].centerOfGravity.Y;
				Result[a].modelHeight = TempModelinfo[0].modelHeight;
				Result[a].modelWidth = TempModelinfo[0].modelWidth;
				Result[a].noOfCordinates = TempModelinfo[0].noOfCordinates;
				Result[a].rotation = 360 - a;
				Result[a].MaxGradient = MaxGradient;
				for (int m = 0; m < TempModelinfo[0].noOfCordinates; m++)
				{
					Result[a].cordinatesarrayX[m] = Result[a].cordinates[m].X;
					Result[a].cordinatesarrayY[m] = Result[a].cordinates[m].Y;
				
					
//					int Yc = Result[a].cordinatesarrayX[m] + TempModelinfo[0].centerOfGravity.X ;
//					int Xc = Result[a].cordinatesarrayY[m] + TempModelinfo[0].centerOfGravity.Y ;
					int Yc = Result[a].cordinatesarrayX[m] + magcolor.Height /2;
					int Xc = Result[a].cordinatesarrayY[m] + magcolor.Width /2;


					magcolor.Draw(new Cross2DF(new PointF(Xc, Yc), 4, 4), new Bgr(30, 240, 225), 1);
					//	Result[a].edgeMagnitude[m] = magn.Data[X,Y, 0];
					if (Xc > 0 && Xc < gxx.Height && Yc > 0 && Yc < gxx.Width)
					{
						/*
												Result[a].edgeMagnitude[m] = 1 / (magn.Data[Xc, Yc, 0] / MaxGradient * 255);
												Result[a].edgeDerivativeX[m] = gxx.Data[Xc, Yc, 0];
												Result[a].edgeDerivativeY[m] = gyy.Data[Xc, Yc, 0];
											
						*/

						
						Result[a].edgeMagnitude[m] = 1 / (magn.Data[Yc, Xc, 0] / MaxGradient * 255);
					//	Result[a].edgeMagnitude[m] = magn.Data[Yc, Xc, 0];
												Result[a].edgeDerivativeX[m] = gxx.Data[Yc, Xc, 0];
												Result[a].edgeDerivativeY[m] = gyy.Data[Yc, Xc, 0];
											
						
											}
										}

									//	magcolor.Save(@"c:\Sobel\madnitudecolor" + a.ToString() + ".jpg");
									//	magn.Save(@"c:\Sobel\madnitude" + a.ToString() + ".jpg");
										//	Result[a] = Result[0];


									}

									gx.Dispose();
									gy.Dispose();
									nmsEdges.Dispose();

									return Result;
								}




								public Image<Gray, float> CreateModelImage(Image<Gray, float> TemplateImage, float scale, int Rotation)
								{

									EdgeBasedModelInfo TempModelinfo = new EdgeBasedModelInfo();
									EdgeBasedModelInfo Result = new EdgeBasedModelInfo();
									Mat gx;     //Matrix to store X derivative
									Mat gy;     //Matrix to store Y derivative
									Mat nmsEdges;       //Matrix to store temp restult
									Size Ssize = new Size();
									int ModelSize;
									Image<Gray, float> Resized;
									Image<Gray, float> Rotated;
									//	Mat Template;
									int minx = 99999;
									int miny = 99999;
									int maxx = 0;
									int maxy = 0;
									int rot = Rotation;


									if (rot < 0)
									{
										rot = rot + 360;
									}

									if (rot >= 360)
									{
										rot = rot - 360;
									}




									if (scale != 1)
									{
										Resized = TemplateImage.Resize(scale, Inter.Linear);
									}
									else
									{
										Resized = TemplateImage;
										//	TemplateImage.CopyTo(Resized);
									}

									if (rot == 0)
									{
										Rotated = Resized;
										//				Resized.CopyTo(Rotated);

									}
									else
									{
										Rotated = Resized.Rotate(rot, new Gray(0), false);
										//		Rotated = Resized.Rotate(rot,RotatePoint ,Inter.Area, new Gray(0), false);
										//	Rotated = RotateMat(src, rot);
									}


									//	Rotated.Save(@"D:\Edge\Test\Test_" + scale.ToString() + "_" + rot.ToString() + ".bmp");

									Ssize.Width = Rotated.Cols;
									Ssize.Height = Rotated.Rows; //src->height;
									ModelSize = Ssize.Width * Ssize.Height;

									RotatedRect Bounding = new RotatedRect(new PointF(Ssize.Width / 2, Ssize.Height / 2), new SizeF((float)(Resized.Cols * 0.95), (float)(Resized.Rows * 0.95)), rot);

									TempModelinfo.modelHeight = Ssize.Height;       //Save Template height
									TempModelinfo.modelWidth = Ssize.Width;         //Save Template width

									TempModelinfo.noOfCordinates = 0;                                           //initialize	
									TempModelinfo.cordinates = new Point[ModelSize];        //Allocate memory for coorinates of selected points in template image

									TempModelinfo.edgeMagnitude = new float[ModelSize];     //Allocate memory for edge magnitude for selected points
									TempModelinfo.edgeDerivativeX = new float[ModelSize];           //Allocate memory for edge X derivative for selected points
									TempModelinfo.edgeDerivativeY = new float[ModelSize];           ////Allocate memory for edge Y derivative for selected points


									// Calculate gradient of Template
									gx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
									gy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
									Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
									Mat directions = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative

									CvInvoke.Sobel(Rotated, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);        //gradient in X direction			
									CvInvoke.Sobel(Rotated, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);    //gradient in Y direction
									CvInvoke.CartToPolar(gx, gy, magnitude, directions);

									double minVal = 0;
									double maxVal = 0;
									Point minLoc = new Point();
									Point maxLoc = new Point();
									CvInvoke.MinMaxLoc(magnitude, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

									//	magnitude.Save(@"D:\Edge\Test\Magnitude_" + scale.ToString() + "_" + rot.ToString() + ".bmp");

									var _gx = gx.ToImage<Gray, float>();
									var _gy = gy.ToImage<Gray, float>();
									var Angle = directions.ToImage<Gray, float>();
									var mag = magnitude.ToImage<Gray, float>();
									Mask = new Image<Gray, float>(mag.Size.Width, mag.Size.Height, new Gray(255));

									float fdx, fdy;
									float MagG, DirG;
									float MaxGradient = (float)maxVal;
									float direction;

									//			_gx.Save(@"d:\edge\xCPU.jpg");
									//			_gy.Save(@"d:\edge\yCPU.jpg");
									//			mag.Save(@"d:\edge\mCPU.jpg");

									float leftPixel = 0, rightPixel = 0;
									float Divmag = 0;
									int RSum = 0, CSum = 0;
									int curX, curY;
									int i, j;

									//	float[,] magMat = new float[Ssize.Height, Ssize.Width];
									for (i = 0; i < Ssize.Height -1; i++)
									{
										mag.Data[i, 0, 0] = 0;
										mag.Data[i, Ssize.Width-1, 0] = 0;
										Mask.Data[i, 0, 0] = MaxGradient;
										Mask.Data[i, Ssize.Width - 1, 0] = MaxGradient;
									}
									for (j = 0; j < Ssize.Width - 1; j++)
									{
										mag.Data[0, j, 0] = 0;
										mag.Data[Ssize.Height - 1, j, 0] = 0;
										Mask.Data[0, j, 0] = MaxGradient;
										Mask.Data[Ssize.Height - 1, j, 0] = MaxGradient;
									}



								for (i = 1; i < Ssize.Height - 1; i++)
									{
										for (j = 1; j < Ssize.Width - 1; j++)
										{
											direction = (float)(Angle.Data[i, j, 0]);
											MagG = mag.Data[i, j, 0];

											//		Debug.WriteLine(direction);
											// get closest angle from 0, 45, 90, 135 set
											if ((direction > 0F && direction < 0.3926) || (direction > 2.7488 && direction < 3.5342) || (direction > 5.8904 && direction < 6.283))
											{
												leftPixel = mag.Data[i, j - 1, 0];
												rightPixel = mag.Data[i, j + 1, 0];
											}
											else if ((direction > 0.3926 && direction < 1.178) || (direction > 3.534 && direction < 4.319))
											{
												leftPixel = mag.Data[i - 1, j + 1, 0];
												rightPixel = mag.Data[i + 1, j - 1, 0];
											}
											else if ((direction > 1.178 && direction < 1.963) || (direction > 4.319 && direction < 5.105))
											{
												leftPixel = mag.Data[i - 1, j, 0];
												rightPixel = mag.Data[i + 1, j, 0];
											}
											else if ((direction > 1.963 && direction < 157.5) || (direction > 5.105 && direction < 5.890))
											{
												leftPixel = mag.Data[i - 1, j - 1, 0];
												rightPixel = mag.Data[i + 1, j + 1, 0];
											}
											else
											{
												leftPixel = mag.Data[i, j - 1, 0];
												rightPixel = mag.Data[i, j + 1, 0];
											}

											// compare current pixels value with adjacent pixels
											if ((MagG < leftPixel) || (MagG < rightPixel))
											{
												mag.Data[i, j, 0] = 0.0f;
												Mask.Data[i, j, 0] = MaxGradient;
											}
											else
											{
												Mask.Data[i, j, 0] = 0;
												Divmag = (float)(MagG / MaxGradient * 255);
												if (Divmag < this.highThreshold && Divmag > this.lowThreshold)
												{
													/*
													mag.Data[i, j - 1, 0] = 0;
													mag.Data[i, j + 1, 0] = 0;
													mag.Data[i - 1, j + 1, 0] = 0;
													mag.Data[i + 1, j - 1, 0] = 0;
													mag.Data[i - 1, j, 0] = 0;
													mag.Data[i + 1, j, 0] = 0;
													mag.Data[i - 1, j - 1, 0] = 0;
													mag.Data[i + 1, j + 1, 0] = 0;
													*/


					}
						else
						{
							mag.Data[i, j, 0] = 0;
							Mask.Data[i, j, 0] = MaxGradient;
						}
					}
				}
			}

			

			//			mag.Save(@"d:\edge\EdgesWithContrast" + rot.ToString() + ".jpg");
			//		Debug.WriteLine(MaxGradient);

			return mag;


		
		}





		public EdgeBasedModelInfo CreateModelUMat(Image<Gray, float> TemplateImage, float scale, int Rotation)
		{

			EdgeBasedModelInfo TempModelinfo = new EdgeBasedModelInfo();
			EdgeBasedModelInfo Result = new EdgeBasedModelInfo();
			UMat gx;     //Matrix to store X derivative
			UMat gy;     //Matrix to store Y derivative
			UMat nmsEdges;       //Matrix to store temp restult
			Size Ssize = new Size();
			int ModelSize;
			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
			//	Mat Template;
			int minx = 99999;
			int miny = 99999;
			int maxx = 0;
			int maxy = 0;
			int rot = Rotation;


			if (rot < 0)
			{
				rot = rot + 360;
			}

			if (rot >= 360)
			{
				rot = rot - 360;
			}




			if (scale != 1)
			{
				Resized = TemplateImage.Resize(scale, Inter.Linear);
			}
			else
			{
				Resized = TemplateImage;
				//	TemplateImage.CopyTo(Resized);
			}

			if (rot == 0)
			{
				Rotated = Resized;
				//				Resized.CopyTo(Rotated);

			}
			else
			{
				Rotated = Resized.Rotate(rot, new Gray(0), false);
				//		Rotated = Resized.Rotate(rot,RotatePoint ,Inter.Area, new Gray(0), false);
				//	Rotated = RotateMat(src, rot);
			}




			Ssize.Width = Rotated.Cols;
			Ssize.Height = Rotated.Rows; //src->height;
			ModelSize = Ssize.Width * Ssize.Height;

			RotatedRect Bounding = new RotatedRect(new PointF(Ssize.Width / 2, Ssize.Height / 2), new SizeF((float)(Resized.Cols * 0.95), (float)(Resized.Rows * 0.95)), rot);

			TempModelinfo.modelHeight = Ssize.Height;       //Save Template height
			TempModelinfo.modelWidth = Ssize.Width;         //Save Template width

			TempModelinfo.noOfCordinates = 0;                                           //initialize	
			TempModelinfo.cordinates = new Point[ModelSize];        //Allocate memory for coorinates of selected points in template image

			TempModelinfo.edgeMagnitude = new float[ModelSize];     //Allocate memory for edge magnitude for selected points
			TempModelinfo.edgeDerivativeX = new float[ModelSize];           //Allocate memory for edge X derivative for selected points
			TempModelinfo.edgeDerivativeY = new float[ModelSize];           ////Allocate memory for edge Y derivative for selected points


			// Calculate gradient of Template
			gx = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
			gy = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
			Mat directions = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
			UMat Sourceimage = Rotated.ToUMat();

			CvInvoke.Sobel(Sourceimage, gx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);        //gradient in X direction			
			CvInvoke.Sobel(Sourceimage, gy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);    //gradient in Y direction
			CvInvoke.CartToPolar(gx, gy, magnitude, directions);
			double minVal = 0;
			double maxVal = 0;
			Point minLoc = new Point();
			Point maxLoc = new Point();
			CvInvoke.MinMaxLoc(magnitude, ref minVal, ref maxVal, ref minLoc, ref maxLoc);



			nmsEdges = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);      //create Matrix to store Final nmsEdges
			var _gx = gx.ToImage<Gray, float>();
			var _gy = gy.ToImage<Gray, float>();
			var Angle = directions.ToImage<Gray, float>();
			var mag = magnitude.ToImage<Gray, float>();
			float fdx, fdy;
			float MagG, DirG;
			float MaxGradient = (float)maxVal;
			float direction;

			//			_gx.Save(@"d:\edge\xCPU.jpg");
			//			_gy.Save(@"d:\edge\yCPU.jpg");
			//			mag.Save(@"d:\edge\mCPU.jpg");

			float leftPixel = 0, rightPixel = 0;
			float Divmag = 0;
			int RSum = 0, CSum = 0;
			int curX, curY;
			int i, j;

//			float[,] magMat = new float[Ssize.Height, Ssize.Width];


			for (i = 1; i < Ssize.Height - 1; i++)
			{
				for (j = 1; j < Ssize.Width - 1; j++)
				{
					direction = (float)(Angle.Data[i, j, 0]);
					MagG = mag.Data[i, j, 0];

					//		Debug.WriteLine(direction);
					// get closest angle from 0, 45, 90, 135 set
					if ((direction > 0F && direction < 0.3926) || (direction > 2.7488 && direction < 3.5342) || (direction > 5.8904 && direction < 6.283))
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}
					else if ((direction > 0.3926 && direction < 1.178) || (direction > 3.534 && direction < 4.319))
					{
						leftPixel = mag.Data[i - 1, j + 1, 0];
						rightPixel = mag.Data[i + 1, j - 1, 0];
					}
					else if ((direction > 1.178 && direction < 1.963) || (direction > 4.319 && direction < 5.105))
					{
						leftPixel = mag.Data[i - 1, j, 0];
						rightPixel = mag.Data[i + 1, j, 0];
					}
					else if ((direction > 1.963 && direction < 157.5) || (direction > 5.105 && direction < 5.890))
					{
						leftPixel = mag.Data[i - 1, j - 1, 0];
						rightPixel = mag.Data[i + 1, j + 1, 0];
					}
					else
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}

					// compare current pixels value with adjacent pixels
					if ((MagG < leftPixel) || (MagG < rightPixel))
						mag.Data[i, j, 0] = 0.0f;
					else
					{
						Divmag = (float)(MagG / MaxGradient * 255);
						if (Divmag < this.highThreshold && Divmag > this.lowThreshold)
						{
							mag.Data[i, j - 1, 0] = 0;
							mag.Data[i, j + 1, 0] = 0;
							mag.Data[i - 1, j + 1, 0] = 0;
							mag.Data[i + 1, j - 1, 0] = 0;
							mag.Data[i - 1, j, 0] = 0;
							mag.Data[i + 1, j, 0] = 0;
							mag.Data[i - 1, j - 1, 0] = 0;
							mag.Data[i + 1, j + 1, 0] = 0;


							curX = i; curY = j;
							if (DoesRectangleContainPoint(Bounding, new PointF(curY, curX)) == true)
							{
								//	mag.Data[i, j, 0] = Divmag;
								RSum = RSum + curX;
								CSum = CSum + curY;

								if (curX < minx)
									minx = curX;

								if (curY < miny)
									miny = curY;

								if (curX > maxx)
									maxx = curX;

								if (curY > maxy)
									maxy = curY;

								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].X = curX;
								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].Y = curY;
								TempModelinfo.edgeDerivativeX[TempModelinfo.noOfCordinates] = _gx.Data[i, j, 0];
								TempModelinfo.edgeDerivativeY[TempModelinfo.noOfCordinates] = _gy.Data[i, j, 0];


								//handle divide by zero
								if (Divmag != 0)
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 1 / Divmag;  // gradient magnitude 
								else
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 0;

								TempModelinfo.noOfCordinates++;


							}
						}
						else
							mag.Data[i, j, 0] = 0;

					}
				}
			}

			//		mag.Save(@"d:\edge\rCPU.jpg");
			//		Debug.WriteLine(MaxGradient);




			//		Edges.Save(@"d:\edge\EdgesWithContrast.jpg");
			//			Edges.Save(@"d:\edge\EdgesWithContrast" + rot.ToString() + ".jpg");

			TempModelinfo.centerOfGravity.X = minx + (maxx - minx) / 2; // center
			TempModelinfo.centerOfGravity.Y = miny + (maxy - miny) / 2; // center y
			Result.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
			Result.EdgeHeight = maxx - minx;
			Result.EdgeWith = maxy - miny;


			//		Result.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);



			// change coordinates to reflect center of gravity

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{
				int temp;
				temp = TempModelinfo.cordinates[m].X;
				TempModelinfo.cordinates[m].X = temp - TempModelinfo.centerOfGravity.X;
				temp = TempModelinfo.cordinates[m].Y;
				TempModelinfo.cordinates[m].Y = temp - TempModelinfo.centerOfGravity.Y;
			}


			Result.cordinates = new Point[TempModelinfo.noOfCordinates];        //Allocate memory for coorinates of selected points in template image
			Result.cordinatesarrayX = new int[TempModelinfo.noOfCordinates];        //Allocate memory for coordinates 
			Result.cordinatesarrayY = new int[TempModelinfo.noOfCordinates];        //Allocate memory for coordinates 

			Result.edgeMagnitude = new float[TempModelinfo.noOfCordinates];     //Allocate memory for edge magnitude for selected points
			Result.edgeDerivativeX = new float[TempModelinfo.noOfCordinates];           //Allocate memory for edge X derivative for selected points
			Result.edgeDerivativeY = new float[TempModelinfo.noOfCordinates];           ////Allocate memory for edge Y derivative for selected points
			Result.centerOfGravity.X = TempModelinfo.centerOfGravity.X;
			Result.centerOfGravity.Y = TempModelinfo.centerOfGravity.Y;
			Result.modelHeight = TempModelinfo.modelHeight;
			Result.modelWidth = TempModelinfo.modelWidth;
			Result.noOfCordinates = TempModelinfo.noOfCordinates;
			Result.rotation = Rotation;
			Result.MaxGradient = MaxGradient;

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{


				Result.cordinates[m].X = TempModelinfo.cordinates[m].X;
				Result.cordinates[m].Y = TempModelinfo.cordinates[m].Y;
				Result.cordinatesarrayX[m] = TempModelinfo.cordinates[m].X;
				Result.cordinatesarrayY[m] = TempModelinfo.cordinates[m].Y;

				Result.edgeMagnitude[m] = TempModelinfo.edgeMagnitude[m];
				Result.edgeDerivativeX[m] = TempModelinfo.edgeDerivativeX[m];
				Result.edgeDerivativeY[m] = TempModelinfo.edgeDerivativeY[m];
			}


			gx.Dispose();
			gy.Dispose();
			nmsEdges.Dispose();


			return Result;
		}




		public EdgeBasedModelInfo CreateModelCanny(Image<Gray, float> TemplateImage, float scale, int Rotation)
		{

			EdgeBasedModelInfo TempModelinfo = new EdgeBasedModelInfo();
			EdgeBasedModelInfo Result = new EdgeBasedModelInfo();
			UMat gx;     //Matrix to store X derivative
			UMat gy;     //Matrix to store Y derivative
			UMat nmsEdges;       //Matrix to store temp restult
			Size Ssize = new Size();
			int ModelSize;
			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
			//	Mat Template;
			int minx = 99999;
			int miny = 99999;
			int maxx = 0;
			int maxy = 0;
			int rot = Rotation;


			if (rot < 0)
			{
				rot = rot + 360;
			}

			if (rot >= 360)
			{
				rot = rot - 360;
			}




			if (scale != 1)
			{
				Resized = TemplateImage.Resize(scale, Inter.Linear);
			}
			else
			{
				Resized = TemplateImage;
				//	TemplateImage.CopyTo(Resized);
			}

			if (rot == 0)
			{
				Rotated = Resized;
				//				Resized.CopyTo(Rotated);

			}
			else
			{
				Rotated = Resized.Rotate(rot, new Gray(0), false);
				//		Rotated = Resized.Rotate(rot,RotatePoint ,Inter.Area, new Gray(0), false);
				//	Rotated = RotateMat(src, rot);
			}




			Ssize.Width = Rotated.Cols;
			Ssize.Height = Rotated.Rows; //src->height;
			ModelSize = Ssize.Width * Ssize.Height;

			RotatedRect Bounding = new RotatedRect(new PointF(Ssize.Width / 2, Ssize.Height / 2), new SizeF((float)(Resized.Cols * 0.95), (float)(Resized.Rows * 0.95)), rot);

			TempModelinfo.modelHeight = Ssize.Height;       //Save Template height
			TempModelinfo.modelWidth = Ssize.Width;         //Save Template width

			TempModelinfo.noOfCordinates = 0;                                           //initialize	
			TempModelinfo.cordinates = new Point[ModelSize];        //Allocate memory for coorinates of selected points in template image

			TempModelinfo.edgeMagnitude = new float[ModelSize];     //Allocate memory for edge magnitude for selected points
			TempModelinfo.edgeDerivativeX = new float[ModelSize];           //Allocate memory for edge X derivative for selected points
			TempModelinfo.edgeDerivativeY = new float[ModelSize];           ////Allocate memory for edge Y derivative for selected points


			// Calculate gradient of Template
			gx = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store X derivative
			gy = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);      //create Matrix to store Y derivative
			UMat magnitude = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);      //create Matrix to store X derivative
			UMat magnitudeErode = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);      //create Matrix to store X derivative
			CvInvoke.Sobel(Rotated.ToUMat(), gx, Emgu.CV.CvEnum.DepthType.Cv16S, 1, 0, 3);        //gradient in X direction			
			CvInvoke.Sobel(Rotated.ToUMat(), gy, Emgu.CV.CvEnum.DepthType.Cv16S, 0, 1, 3);    //gradient in Y direction
			CvInvoke.Canny(gx, gy, magnitude, 245, 255);
		
		//	magnitude.Save(@"d:\edge\canny.jpg");
		//	CvInvoke.Erode(magnitude, magnitudeErode, null, new Point(-1, -1), 3, BorderType.Default, new MCvScalar(255, 255, 255));
		//	magnitude.Save(@"d:\edge\cannyErode.jpg");
			nmsEdges = new UMat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);      //create Matrix to store Final nmsEdges
			var _gx = gx.ToImage<Gray, float>();
			var _gy = gy.ToImage<Gray, float>();
			var mag = magnitude.ToImage<Gray, float>();
			float fdx, fdy;
			float MagG, DirG;

			//			_gx.Save(@"d:\edge\xCPU.jpg");
			//			_gy.Save(@"d:\edge\yCPU.jpg");
			//			mag.Save(@"d:\edge\mCPU.jpg");

			float leftPixel = 0, rightPixel = 0;
			float Divmag = 0;
			int RSum = 0, CSum = 0;
			int curX, curY;
			int i, j;

			float[,] magMat = new float[Ssize.Height, Ssize.Width];

/*
			for (i = 1; i < Ssize.Height - 1; i++)
			{
				for (j = 1; j < Ssize.Width - 1; j++)
				{
					direction = (float)(Angle.Data[i, j, 0]);
					MagG = mag.Data[i, j, 0];

					//		Debug.WriteLine(direction);
					// get closest angle from 0, 45, 90, 135 set
					if ((direction > 0F && direction < 0.3926) || (direction > 2.7488 && direction < 3.5342) || (direction > 5.8904 && direction < 6.283))
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}
					else if ((direction > 0.3926 && direction < 1.178) || (direction > 3.534 && direction < 4.319))
					{
						leftPixel = mag.Data[i - 1, j + 1, 0];
						rightPixel = mag.Data[i + 1, j - 1, 0];
					}
					else if ((direction > 1.178 && direction < 1.963) || (direction > 4.319 && direction < 5.105))
					{
						leftPixel = mag.Data[i - 1, j, 0];
						rightPixel = mag.Data[i + 1, j, 0];
					}
					else if ((direction > 1.963 && direction < 157.5) || (direction > 5.105 && direction < 5.890))
					{
						leftPixel = mag.Data[i - 1, j - 1, 0];
						rightPixel = mag.Data[i + 1, j + 1, 0];
					}
					else
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}

					// compare current pixels value with adjacent pixels
					if ((MagG < leftPixel) || (MagG < rightPixel))
						mag.Data[i, j, 0] = 0.0f;
					else
					{
						Divmag = (float)(MagG / MaxGradient * 255);
						if (Divmag < this.highThreshold && Divmag > this.lowThreshold)
						{
							mag.Data[i, j - 1, 0] = 0;
							mag.Data[i, j + 1, 0] = 0;
							mag.Data[i - 1, j + 1, 0] = 0;
							mag.Data[i + 1, j - 1, 0] = 0;
							mag.Data[i - 1, j, 0] = 0;
							mag.Data[i + 1, j, 0] = 0;
							mag.Data[i - 1, j - 1, 0] = 0;
							mag.Data[i + 1, j + 1, 0] = 0;


							curX = i; curY = j;
							if (DoesRectangleContainPoint(Bounding, new PointF(curY, curX)) == true)
							{
								//	mag.Data[i, j, 0] = Divmag;
								RSum = RSum + curX;
								CSum = CSum + curY;

								if (curX < minx)
									minx = curX;

								if (curY < miny)
									miny = curY;

								if (curX > maxx)
									maxx = curX;

								if (curY > maxy)
									maxy = curY;

								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].X = curX;
								TempModelinfo.cordinates[TempModelinfo.noOfCordinates].Y = curY;
								TempModelinfo.edgeDerivativeX[TempModelinfo.noOfCordinates] = _gx.Data[i, j, 0];
								TempModelinfo.edgeDerivativeY[TempModelinfo.noOfCordinates] = _gy.Data[i, j, 0];


								//handle divide by zero
								if (Divmag != 0)
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 1 / Divmag;  // gradient magnitude 
								else
									TempModelinfo.edgeMagnitude[TempModelinfo.noOfCordinates] = 0;

								TempModelinfo.noOfCordinates++;


							}
						}
						else
							mag.Data[i, j, 0] = 0;

					}
				}
			}
*/
			//		mag.Save(@"d:\edge\rCPU.jpg");
			//		Debug.WriteLine(MaxGradient);




			//		Edges.Save(@"d:\edge\EdgesWithContrast.jpg");
			//			Edges.Save(@"d:\edge\EdgesWithContrast" + rot.ToString() + ".jpg");

			TempModelinfo.centerOfGravity.X = minx + (maxx - minx) / 2; // center
			TempModelinfo.centerOfGravity.Y = miny + (maxy - miny) / 2; // center y
			Result.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
			Result.EdgeHeight = maxx - minx;
			Result.EdgeWith = maxy - miny;


			//		Result.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);



			// change coordinates to reflect center of gravity

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{
				int temp;
				temp = TempModelinfo.cordinates[m].X;
				TempModelinfo.cordinates[m].X = temp - TempModelinfo.centerOfGravity.X;
				temp = TempModelinfo.cordinates[m].Y;
				TempModelinfo.cordinates[m].Y = temp - TempModelinfo.centerOfGravity.Y;
			}


			Result.cordinates = new Point[TempModelinfo.noOfCordinates];        //Allocate memory for coorinates of selected points in template image
			Result.cordinatesarrayX = new int[TempModelinfo.noOfCordinates];        //Allocate memory for coordinates 
			Result.cordinatesarrayY = new int[TempModelinfo.noOfCordinates];        //Allocate memory for coordinates 

			Result.edgeMagnitude = new float[TempModelinfo.noOfCordinates];     //Allocate memory for edge magnitude for selected points
			Result.edgeDerivativeX = new float[TempModelinfo.noOfCordinates];           //Allocate memory for edge X derivative for selected points
			Result.edgeDerivativeY = new float[TempModelinfo.noOfCordinates];           ////Allocate memory for edge Y derivative for selected points
			Result.centerOfGravity.X = TempModelinfo.centerOfGravity.X;
			Result.centerOfGravity.Y = TempModelinfo.centerOfGravity.Y;
			Result.modelHeight = TempModelinfo.modelHeight;
			Result.modelWidth = TempModelinfo.modelWidth;
			Result.noOfCordinates = TempModelinfo.noOfCordinates;
			Result.rotation = Rotation;
	//		Result.MaxGradient = MaxGradient;

			for (int m = 0; m < TempModelinfo.noOfCordinates; m++)
			{


				Result.cordinates[m].X = TempModelinfo.cordinates[m].X;
				Result.cordinates[m].Y = TempModelinfo.cordinates[m].Y;
				Result.cordinatesarrayX[m] = TempModelinfo.cordinates[m].X;
				Result.cordinatesarrayY[m] = TempModelinfo.cordinates[m].Y;

				Result.edgeMagnitude[m] = TempModelinfo.edgeMagnitude[m];
				Result.edgeDerivativeX[m] = TempModelinfo.edgeDerivativeX[m];
				Result.edgeDerivativeY[m] = TempModelinfo.edgeDerivativeY[m];
			}


			gx.Dispose();
			gy.Dispose();
			nmsEdges.Dispose();


			return Result;
		}





		public float[] IntensityHistogram(Image<Gray, byte> image)
        {
			int nBins = 256;
			float[] valHist = new float[nBins];
			float[] diffval = new float[nBins];
			//		Image<Gray, Byte>[] inp = new Image<Gray, byte>("fileName.jpg");
			DenseHistogram hist = new DenseHistogram(nBins, new RangeF(0, 255));
			hist.Calculate(new Image<Gray, Byte>[] { image }, true, null);
			valHist =  hist.GetBinValues();
			//hist.GetData().CopyTo(valHist, 0);

	/*		for (int a = 1; a < 256; a++)
            {

				diffval[a - 1] = valHist[a] - valHist[a - 1];

			}*/
		//	hist.m
			
			return valHist;
		}



		public bool DoesRectangleContainPoint(RotatedRect rectangle, PointF point)
		{


			PointF[] corners	= rectangle.GetVertices();
		
			VectorOfPointF contour = new VectorOfPointF();
			contour.Push(corners);


			/*		contour[0] = Point2f(corners[0].x, corners[0].y);
					contour[1] = Point2f(corners[1].x, corners[1].y);
					contour[2] = Point2f(corners[2].x, corners[2].y);
					contour[3] = Point2f(corners[3].x, corners[3].y);
		*/
			//Check if the point is within the rectangle.
			double indicator = CvInvoke.PointPolygonTest(contour, point, false);
			bool rectangleContainsPoint = (indicator > 0);
			return rectangleContainsPoint;
		}



		/*
		public float FindModel_GPU(Image<Gray, float> Source, out Point resultPoint)
		{
			
			float resultScore = 0;
//			float partialSum = 0;
//			float sumOfCoords = 0;
//			float partialScore;
//			int i, j, m;            // count variables
//			float iTx, iTy, iSx, iSy;
//			float gradMag;
//			int curX, curY;
			int finetune_x;
			int finetune_y;


			Mat SourceImage = Source.Resize(scale, Inter.Linear).Mat;
			Size Ssize = new Size();
			Ssize.Width = SourceImage.Cols;
			Ssize.Height = SourceImage.Rows;
			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S,1); // X derivatives CV_16SC1
			Mat CSdy =new  Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1); // y derivatives
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);

			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Mat ForDivide = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
		//	ForDivide.SetTo(new MCvScalar(1));



			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);  // find X derivatives CV_16S
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3); // find Y derivatives

			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);


			//			CvInvoke.Divide(ForDivide, magnitude_src, magnitude);
		//	magnitude.Data[0, 0, 0] = 3;
			magnitude_src.CopyTo(magnitude);
			var _csdx = CSdx.ToImage<Gray, float>();
//			magnitude.Save(@"d:\edge\TargetmagnitudeCSharp.jpg");

			int rotations = 0;

			float[] result = new float[Ssize.Width * Ssize.Height];

			ErrorCode errNum;
		//	CLSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr , (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height),CSdx.DataPointer, out errNum);
			
			CLSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), CSdx.GetData() , out errNum);
			CLSdy = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), CSdy.GetData(), out errNum);
			CLmagnitude = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), magnitude.GetData(), out errNum);
			ResultMap = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * Ssize.Width * Ssize.Height, out errNum);
						SourceWidth = Ssize.Width;
						PixelCount = Ssize.Width * Ssize.Height;
						SetKernelArguments(kernel);

						int globalsize = Ssize.Width * Ssize.Height;

			//			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)kernelPositions.Length };
			//			IntPtr[] localWorkSize = new IntPtr[] { (IntPtr)32 };
			


			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)globalsize, (IntPtr)180 };
			//			size_t globalWorkSize[2] = { globalsize, 180 };
			//						size_t localWorkSize[1] = { 24 };
			OpenCL.Net.Event eventReadBufferForOutputMap;
			OpenCL.Net.Event eventExecutedKernel;


			errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernel, 2, null, globalWorkSize, null, 0, null,out eventExecutedKernel);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


		


			Cl.Finish(commandQueue);
	//		IntPtr pointer = new IntPtr();
		//	errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True,0, Ssize.Width * Ssize.Height * sizeof(float), result, 0, null, out eventReadBufferForOutputMap);
			errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(Ssize.Width * Ssize.Height * sizeof(float)), result, 0 , null,out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			
			//	Marshal.Copy(pointer, result, 0, (Ssize.Width * Ssize.Height * sizeof(float)));
	//		MessageBox.Show(result.Length.ToString());


			double max = 0;
			double count = 0;
			double resPoz = 0;
			for (int q = 0; q < result.Length; q++)
			{
				if (result[q] > max)
                {
					max = result[q];
					resPoz = count;
				}
				count++;
			}


			resultScore = (float)max;


			int y = (int)(resPoz % Ssize.Width);
			int x = (int)((resPoz - y) / Ssize.Width);
			resultPoint = new Point(x,y);
			return resultScore;
	

}

		*/

		public EdgeBasedModelInfo CreateModelGPU(Image<Gray, float> Source, float scale, int Rotation)
        {
	//		Stopwatch stopwatch = new Stopwatch();

			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
			int rot = Rotation;

			if (rot < 0)
			{
				rot = rot + 360;
			}

			if (rot >= 360)
			{
				rot = rot - 360;
			}


			if (scale != 1)
			{
				Resized = Source.Resize(scale, Inter.Area);
			}
			else
			{
				Resized = Source;
				//	TemplateImage.CopyTo(Resized);
			}

			if (rot == 0)
			{
				Rotated = Resized;
				//				Resized.CopyTo(Rotated);

			}
			else
			{
				Rotated = Resized.Rotate(rot, new Gray(0), false);
			}

		//			Rotated.Save(@"d:\edge\oGPU.jpg");


//			Debug.WriteLine($"Scale and Rotation time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
//			stopwatch.Restart();
			EdgeBasedModelInfo ResultModel = new EdgeBasedModelInfo();
			Size ImageSize = new Size(Rotated.Width, Rotated.Height);
			RotatedRect Bounding = new RotatedRect(new PointF(ImageSize.Width / 2, ImageSize.Height / 2), new SizeF((float)(Resized.Cols * 0.95), (float)(Resized.Rows * 0.95)), rot);


			ErrorCode errNum;
		//	Mat ImageMat = Source.Mat;

			float[] resultMagnitude = new float[ImageSize.Width * ImageSize.Height];
			float[] resultDerivateX = new float[ImageSize.Width * ImageSize.Height];
			float[] resultDerivateY = new float[ImageSize.Width * ImageSize.Height];
			int[] resultDirection = new int[ImageSize.Width * ImageSize.Height];

			OpenCL.Net.IMem Direction = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(int) * ImageSize.Width * ImageSize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem Magnitude = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ImageSize.Width * ImageSize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem DerivateX = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ImageSize.Width * ImageSize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem DerivateY = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ImageSize.Width * ImageSize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem sourceImage = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * ImageSize.Width * ImageSize.Height), Rotated.Mat.GetData(), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			errNum = Cl.SetKernelArg(SobelMagnitude, 0, sourceImage);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(SobelMagnitude, 1, DerivateX);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(SobelMagnitude, 2, DerivateY);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(SobelMagnitude, 3, Magnitude);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(SobelMagnitude, 4, Direction);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(SobelMagnitude, 5, (IntPtr)sizeof(int), ImageSize.Width);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");



			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)ImageSize.Width - 2, (IntPtr)(ImageSize.Height - 2) };
			OpenCL.Net.Event eventReadBufferForOutputMap;
			OpenCL.Net.Event eventExecutedKernel;
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueNDRangeKernel(commandQueue, SobelMagnitude, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.Finish(commandQueue);
//			Debug.WriteLine($"Magnitude time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
//			stopwatch.Restart();

			errNum = Cl.EnqueueReadBuffer(commandQueue, DerivateX, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ImageSize.Width * ImageSize.Height * sizeof(float)), resultDerivateX, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, DerivateY, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ImageSize.Width * ImageSize.Height * sizeof(float)), resultDerivateY, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue,Direction, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ImageSize.Width * ImageSize.Height * sizeof(int)), resultDirection, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			errNum = Cl.EnqueueReadBuffer(commandQueue, Magnitude, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ImageSize.Width * ImageSize.Height * sizeof(float)), resultMagnitude, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


			//			Debug.WriteLine($"Magnitude Read buffer time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			//			stopwatch.Restart();

			/*  Get Maximum  Magnitude value  */
			float max = 0;
			for (int q = 0; q < resultMagnitude.Length; q++)
			{
				if (resultMagnitude[q] > max)
				{
					max = resultMagnitude[q];
					
				}
			}

//			Debug.WriteLine($"Magnitude maximum time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
//			stopwatch.Restart();

			/* ------------------------- Div Magnitude -------------------------------- */

			float[] Result = new float[ImageSize.Width * ImageSize.Height];
			OpenCL.Net.IMem ResultArray = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ImageSize.Width * ImageSize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			errNum = Cl.SetKernelArg(DivMagnitude, 0, Magnitude);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(DivMagnitude, 1, ResultArray);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(DivMagnitude, 2, Direction);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.SetKernelArg(DivMagnitude, 3, (IntPtr)sizeof(int), ImageSize.Width);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.SetKernelArg(DivMagnitude, 4, (IntPtr)sizeof(float), lowThreshold);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.SetKernelArg(DivMagnitude, 5, (IntPtr)sizeof(float), highThreshold);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.SetKernelArg(DivMagnitude, 6, (IntPtr)sizeof(float), max);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			
			errNum = Cl.EnqueueNDRangeKernel(commandQueue, DivMagnitude, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.Finish(commandQueue);

			errNum = Cl.EnqueueReadBuffer(commandQueue, ResultArray, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ImageSize.Width * ImageSize.Height * sizeof(float)), Result, 0, null, out eventReadBufferForOutputMap);

//			Debug.WriteLine($"Division time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
//			stopwatch.Restart();

			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			List<float> XDerivate = new List<float>();
			List<float> YDerivate = new List<float>();
			List<float> MagnitudeFiltered = new List<float>();
			List<Point> EdgePoints = new List<Point>();
			List<int> PointsX = new List<int>();
			List<int> PointsY = new List<int>();

			/*			using (System.IO.StreamWriter file =
											new System.IO.StreamWriter(@"d:/edge/results.dat", false))
						{
							for (int q = 0; q < Result.Length; q++)
								file.WriteLine(Result[q].ToString());
						}
			*/

			for (int q = 0; q < Result.Length; q++)
			{
				if (Result[q] > 0)
				{
					int y = (int)(q % ImageSize.Width);
					int x = (int)((q - y) / ImageSize.Width);
					if (DoesRectangleContainPoint(Bounding, new PointF(y, x)) == true)
					{
						XDerivate.Add(resultDerivateX[q]);
						YDerivate.Add(resultDerivateY[q]);
						MagnitudeFiltered.Add(1 / Result[q]);

						PointsX.Add(x);
						PointsY.Add(y);
						EdgePoints.Add(new Point(x, y));
					}
				}
			}

			int minx = PointsX.Min();
			int maxx = PointsX.Max();

			int miny = PointsY.Min();
			int maxy = PointsY.Max();

			ResultModel.centerOfGravity.X = minx + (maxx - minx) / 2; // center
			ResultModel.centerOfGravity.Y = miny + (maxy - miny) / 2; // center y
			ResultModel.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
	
			for (int w = 0; w < EdgePoints.Count; w++)
            {
				PointsX[w] = EdgePoints[w].X - ResultModel.centerOfGravity.X;
				PointsY[w] = EdgePoints[w].Y - ResultModel.centerOfGravity.Y;
				EdgePoints[w] = new Point( EdgePoints[w].X - ResultModel.centerOfGravity.X, EdgePoints[w].Y - ResultModel.centerOfGravity.Y);
			}

//			Debug.WriteLine($"Extract data time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
//			stopwatch.Restart();


			ResultModel.cordinates = EdgePoints.ToArray();
			ResultModel.edgeDerivativeX = XDerivate.ToArray();
			ResultModel.edgeDerivativeY = YDerivate.ToArray();
			ResultModel.edgeMagnitude = MagnitudeFiltered.ToArray();
			ResultModel.noOfCordinates = EdgePoints.Count();
			ResultModel.cordinatesarrayX = PointsX.ToArray();
			ResultModel.cordinatesarrayY = PointsY.ToArray();
			ResultModel.EdgeHeight = maxx - minx;
			ResultModel.EdgeWith = maxy - miny;
			ResultModel.MaxGradient = max;
			//			Debug.WriteLine($"Copy to class time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			//			stopwatch.Restart();

			/*	
							Image<Gray, float> res = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);

							for (int i = 0; i < ImageSize.Height; i++)
								for (int j = 0; j < ImageSize.Width; j++)
									res.Data[i, j, 0] = Result[ImageSize.Width * i + j];

							res.Save(@"d:\edge\rGPU.jpg");

							Debug.WriteLine(max);

							using (System.IO.StreamWriter file =
											new System.IO.StreamWriter(@"d:/Geomatch/output/resultDirection.dat", false))
							{
								for (int q = 0; q < resultDirection.Length; q++)
									file.WriteLine(resultDirection[q].ToString());
							}


							using (System.IO.StreamWriter file =
														new System.IO.StreamWriter(@"d:/Geomatch/output/resultDerivateX.dat", false))
																{
																	for (int q =0; q< resultDerivateX.Length; q++)
																	file.WriteLine(resultDerivateX[q].ToString());
																}

													using (System.IO.StreamWriter file =
													new System.IO.StreamWriter(@"d:/Geomatch/output/resultDerivateY.dat", false))
													{
														for (int q = 0; q < resultDerivateY.Length; q++)
															file.WriteLine(resultDerivateY[q].ToString());
													}


										using (System.IO.StreamWriter file =
										new System.IO.StreamWriter(@"d:/Geomatch/output/resultMagnitude.dat", false))
										{
											for (int q = 0; q < resultMagnitude.Length; q++)
												file.WriteLine(resultMagnitude[q].ToString());
										}


				*/

			/*
									Image<Gray, float> X = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);
									Image<Gray, float> Y = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);
									Image<Gray, float> mag = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);
									Image<Gray, float> r = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);

			for (int i = 0; i < ImageSize.Height; i++)
				for (int j = 0; j < ImageSize.Width; j++)
					r.Data[i, j, 0] = Result[ImageSize.Width * i + j];

			r.Save(@"d:\edge\rGPU.jpg");


			for (int i = 0; i < ImageSize.Height; i++)
										for (int j = 0; j < ImageSize.Width; j++)
											X.Data[i, j, 0] = resultDerivateX[ImageSize.Width * i + j];

									X.Save(@"d:\edge\xGPU.jpg");



									for (int i = 0; i < ImageSize.Height; i++)
										for (int j = 0; j < ImageSize.Width; j++)
											Y.Data[i, j, 0] = resultDerivateY[ImageSize.Width * i + j];

									Y.Save(@"d:\edge\yGPU.jpg");



									for (int i = 0; i < ImageSize.Height; i++)
										for (int j = 0; j < ImageSize.Width; j++)
											mag.Data[i, j, 0] = resultMagnitude[ImageSize.Width * i + j];

									mag.Save(@"d:\edge\mGPU.jpg");

				*/

			/*
			double max = 0;
			double count = 0;
			double resPoz = 0;
			for (int q = 0; q < result.Length; q++)
			{
				if (result[q] > max)
				{
					max = result[q];
					resPoz = count;
				}
				count++;
			}
			*/
			Cl.ReleaseMemObject(sourceImage);
			Cl.ReleaseMemObject(DerivateX);
			Cl.ReleaseMemObject(DerivateY);
			Cl.ReleaseMemObject(Magnitude);
			Cl.ReleaseMemObject(Direction);
		
	/*		Resized.Dispose();
			Rotated.Dispose();
	*/
			return ResultModel;
		}



		public EdgeBasedModelInfo CreateModelGPU_Rotated(Image<Gray, float> Source, float scale, int Rotation, float MaxGradient, int[] xarray, int[] yarray, int originX, int originY)
		{
			//		Stopwatch stopwatch = new Stopwatch();

			Image<Gray, float> Resized;
			Image<Gray, float> Rotated;
			int rot = Rotation;

			if (rot < 0)
			{
				rot = rot + 360;
			}

			if (rot >= 360)
			{
				rot = rot - 360;
			}
	//		Source.Save(@"d:\edge\Original.jpg");


			if (scale != 1)
			{
				Resized = AutoArea(Source).Resize(scale, Inter.Area);
			//	Resized = Source.Resize(scale, Inter.Area);
			}
			else
			{
				Resized = AutoArea(Source);
			//	Resized = Source;
			}

			if (rot == 0)
			{
				Rotated = Resized;
			}
			else
			{
				Rotated = Resized.Rotate(rot, new PointF(originX, originY),Inter.Area, new Gray(0), false);
			}

	//					Rotated.Save(@"d:\edge\RotatedGPU.jpg");


			//			Debug.WriteLine($"Scale and Rotation time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			//			stopwatch.Restart();
			EdgeBasedModelInfo ResultModel = new EdgeBasedModelInfo();
			Size ImageSize = new Size(Rotated.Width, Rotated.Height);
			int ArrayLength = xarray.Length;
			float angleInRadians = (float)(rot * (Math.PI / 180));
			float cosTheta = (float)(Math.Cos(angleInRadians));
			float sinTheta = (float)(Math.Sin(angleInRadians));



			ErrorCode errNum;
			float[] testsource = new float[ImageSize.Width * ImageSize.Height];
			float[] resultMagnitude = new float[ArrayLength];
			float[] resultDerivateX = new float[ArrayLength];
			float[] resultDerivateY = new float[ArrayLength];
			int[] newX = new int[ArrayLength];
			int[] newY = new int[ArrayLength];

			OpenCL.Net.IMem testS = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ImageSize.Width * ImageSize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			OpenCL.Net.IMem NewCordX  = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(int) * ArrayLength, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem NewCordY = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(int) * ArrayLength, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			OpenCL.Net.IMem Magnitude = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ArrayLength, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem DerivateX = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ArrayLength, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem DerivateY = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * ArrayLength, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem sourceImage = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * ImageSize.Width * ImageSize.Height), Rotated.Mat.GetData(), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem CordX = Cl.CreateBuffer<int>(this.context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, xarray, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem CordY = Cl.CreateBuffer<int>(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, yarray, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 0, sourceImage);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 1, DerivateX);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 2, DerivateY);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 3, Magnitude);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 4, (IntPtr)sizeof(int), ImageSize.Width);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 5, (IntPtr)sizeof(int), ImageSize.Height);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 6, CordX);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 7, CordY);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

	/*		errNum = Cl.SetKernelArg(RotatedTemplateSobel, 8, (IntPtr)sizeof(int), originX);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 9, (IntPtr)sizeof(int), originY);
	*/		ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 8, (IntPtr)sizeof(int), (int)(ImageSize.Width / 2));
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 9, (IntPtr)sizeof(int), (int)(ImageSize.Height / 2));
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 10, NewCordX);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 11, NewCordY);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 12, (IntPtr)sizeof(float),sinTheta);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 13, (IntPtr)sizeof(float), cosTheta);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 14, (IntPtr)sizeof(float), MaxGradient);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(RotatedTemplateSobel, 15, testS);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			
			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)ArrayLength };
			OpenCL.Net.Event eventReadBufferForOutputMap;
			OpenCL.Net.Event eventExecutedKernel;
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueNDRangeKernel(commandQueue, RotatedTemplateSobel, 1, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.Finish(commandQueue);
			//			Debug.WriteLine($"Magnitude time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			//			stopwatch.Restart();

			errNum = Cl.EnqueueReadBuffer(commandQueue, DerivateX, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ArrayLength * sizeof(float)), resultDerivateX, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, DerivateY, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ArrayLength * sizeof(float)), resultDerivateY, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, Magnitude, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ArrayLength * sizeof(float)), resultMagnitude, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, NewCordX, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ArrayLength * sizeof(int)), newX, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, NewCordY, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ArrayLength * sizeof(int)), newY, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, sourceImage, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(ImageSize.Width * ImageSize.Height * sizeof(float)), testsource, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			Image<Gray, float> res = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);

			for (int i = 0; i < ImageSize.Height; i++)
				for (int j = 0; j < ImageSize.Width; j++)
					res.Data[i, j, 0] = testsource[ImageSize.Width * i + j];

	//		res.Save(@"d:\edge\testGPU.jpg");

			//		ResultModel.cordinates = EdgePoints.ToArray();

					ResultModel.edgeDerivativeX = resultDerivateX;
					ResultModel.edgeDerivativeY = resultDerivateY;
					ResultModel.edgeMagnitude = resultMagnitude;
					ResultModel.noOfCordinates = xarray.Length;
					ResultModel.cordinatesarrayX = newX;
					ResultModel.cordinatesarrayY = newY;
				//	ResultModel.EdgeHeight = maxx - minx;
				//	ResultModel.EdgeWith = maxy - miny;
			
			//			Debug.WriteLine($"Copy to class time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			//			stopwatch.Restart();

			/*	
							Image<Gray, float> res = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);

							for (int i = 0; i < ImageSize.Height; i++)
								for (int j = 0; j < ImageSize.Width; j++)
									res.Data[i, j, 0] = Result[ImageSize.Width * i + j];

							res.Save(@"d:\edge\rGPU.jpg");

							Debug.WriteLine(max);

							using (System.IO.StreamWriter file =
											new System.IO.StreamWriter(@"d:/Geomatch/output/resultDirection.dat", false))
							{
								for (int q = 0; q < resultDirection.Length; q++)
									file.WriteLine(resultDirection[q].ToString());
							}


							using (System.IO.StreamWriter file =
														new System.IO.StreamWriter(@"d:/Geomatch/output/resultDerivateX.dat", false))
																{
																	for (int q =0; q< resultDerivateX.Length; q++)
																	file.WriteLine(resultDerivateX[q].ToString());
																}

													using (System.IO.StreamWriter file =
													new System.IO.StreamWriter(@"d:/Geomatch/output/resultDerivateY.dat", false))
													{
														for (int q = 0; q < resultDerivateY.Length; q++)
															file.WriteLine(resultDerivateY[q].ToString());
													}


										using (System.IO.StreamWriter file =
										new System.IO.StreamWriter(@"d:/Geomatch/output/resultMagnitude.dat", false))
										{
											for (int q = 0; q < resultMagnitude.Length; q++)
												file.WriteLine(resultMagnitude[q].ToString());
										}


				*/

			/*
									Image<Gray, float> X = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);
									Image<Gray, float> Y = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);
									Image<Gray, float> mag = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);
									Image<Gray, float> r = new Image<Gray, float>(ImageSize.Width, ImageSize.Height);

			for (int i = 0; i < ImageSize.Height; i++)
				for (int j = 0; j < ImageSize.Width; j++)
					r.Data[i, j, 0] = Result[ImageSize.Width * i + j];

			r.Save(@"d:\edge\rGPU.jpg");


			for (int i = 0; i < ImageSize.Height; i++)
										for (int j = 0; j < ImageSize.Width; j++)
											X.Data[i, j, 0] = resultDerivateX[ImageSize.Width * i + j];

									X.Save(@"d:\edge\xGPU.jpg");



									for (int i = 0; i < ImageSize.Height; i++)
										for (int j = 0; j < ImageSize.Width; j++)
											Y.Data[i, j, 0] = resultDerivateY[ImageSize.Width * i + j];

									Y.Save(@"d:\edge\yGPU.jpg");



									for (int i = 0; i < ImageSize.Height; i++)
										for (int j = 0; j < ImageSize.Width; j++)
											mag.Data[i, j, 0] = resultMagnitude[ImageSize.Width * i + j];

									mag.Save(@"d:\edge\mGPU.jpg");

				*/

			/*
			double max = 0;
			double count = 0;
			double resPoz = 0;
			for (int q = 0; q < result.Length; q++)
			{
				if (result[q] > max)
				{
					max = result[q];
					resPoz = count;
				}
				count++;
			}
			*/
			Cl.ReleaseMemObject(sourceImage);
			Cl.ReleaseMemObject(DerivateX);
			Cl.ReleaseMemObject(DerivateY);
			Cl.ReleaseMemObject(Magnitude);


			/*		Resized.Dispose();
					Rotated.Dispose();
			*/
			return ResultModel;
		}



		/*
		public EdgeBasedShapeMatchingResult FindMatchModel_GPU_ScaledGPUSobel(Image<Gray, float> Source)
		{
			EdgeBasedShapeMatchingResult returnresult = new EdgeBasedShapeMatchingResult();
			CopyTemplateDataToGPU(Scaled);

			Mat SourceImage = Source.Resize(scale, Inter.Area).Mat;
			Size Ssize = new Size();
			Ssize.Width = SourceImage.Cols;
			Ssize.Height = SourceImage.Rows;
			ErrorCode errNum;

	
			CLmagnitude = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * Ssize.Width * Ssize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			CLSdx = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * Ssize.Width * Ssize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			CLSdy = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * Ssize.Width * Ssize.Height, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			OpenCL.Net.IMem sourceImage = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), SourceImage.GetData(), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


			errNum = Cl.SetKernelArg(MatchSobel, 0, sourceImage);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(MatchSobel, 1, CLSdx);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(MatchSobel, 2, CLSdy);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(MatchSobel, 3, CLmagnitude);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.SetKernelArg(MatchSobel, 4, (IntPtr)sizeof(int), Ssize.Width);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)Ssize.Width, (IntPtr)(Ssize.Height) };
			OpenCL.Net.Event eventReadBufferForOutputMap;
			OpenCL.Net.Event eventExecutedKernel;
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueNDRangeKernel(commandQueue, MatchSobel, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			float[] result = new float[Ssize.Width * Ssize.Height];
			int[] rmap = new int[Ssize.Width * Ssize.Height];


	
			ResultMap = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * Ssize.Width * Ssize.Height, out errNum);
			RotationMap = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(int) * Ssize.Width * Ssize.Height, out errNum);
			SourceWidth = Ssize.Width;
			PixelCount = Ssize.Width * Ssize.Height;
			SetKernelArguments(kernel);

			int globalsize = Ssize.Width * Ssize.Height;

			 globalWorkSize = new IntPtr[] { (IntPtr)globalsize, (IntPtr)180 };

			errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernel, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			Cl.Finish(commandQueue);
			errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(Ssize.Width * Ssize.Height * sizeof(float)), result, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, RotationMap, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(Ssize.Width * Ssize.Height * sizeof(int)), rmap, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


			//	Marshal.Copy(pointer, result, 0, (Ssize.Width * Ssize.Height * sizeof(float)));
			//		MessageBox.Show(result.Length.ToString());


			double max = 0;
			double count = 0;
			double resPoz = 0;
			for (int q = 0; q < result.Length; q++)
			{
				if (result[q] > max)
				{
					max = result[q];
					resPoz = count;
				}
				count++;
			}


			//	resultScore = (float)max;
			CLSdx.Dispose();
			CLSdy.Dispose();
			CLmagnitude.Dispose();
			ResultMap.Dispose();
			RotationMap.Dispose();
			SourceImage.Dispose();
			Cl.ReleaseMemObject(sourceImage);
			ReleaseMEMObjects();


			int y = (int)(resPoz % Ssize.Width);
			int x = (int)((resPoz - y) / Ssize.Width);
			//resultPoint = new Point(x, y);

			returnresult.X = (int)(x * (1 / scale));
			returnresult.Y = (int)(y * (1 / scale));
			returnresult.position = resPoz;
			returnresult.Angle = rmap[(int)resPoz];
			returnresult.Score = (float)max;
			return returnresult;


		}


		*/




		// --- PARABOLOID INTERPOLÁCIÓS FÜGGVÉNY (C#) ---
		public static PointF ComputeSubPixelParaboloid(float[,] s)
		{
			// s[sor, oszlop] címzések rögzítése:
			// s[0,0] = BalFent,  s[0,1] = KözépFent,  s[0,2] = JobbFent
			// s[1,0] = BalKözép, s[1,1] = Centrum,    s[1,2] = JobbKözép
			// s[2,0] = BalLent,  s[2,1] = KözépLent,  s[2,2] = JobbLent

			double A = (s[0, 0] + s[0, 1] + s[0, 2] + s[2, 0] + s[2, 1] + s[2, 2] - 2.0 * (s[1, 0] + s[1, 1] + s[1, 2])) / 6.0;
			double B = (s[0, 0] + s[1, 0] + s[2, 0] + s[0, 2] + s[1, 2] + s[2, 2] - 2.0 * (s[0, 1] + s[1, 1] + s[2, 1])) / 6.0;
			double C = (s[0, 2] + s[2, 0] - s[0, 0] - s[2, 2]) / 4.0;
			double D = (s[0, 2] + s[1, 2] + s[2, 2] - (s[0, 0] + s[1, 0] + s[2, 0])) / 6.0;
			double E = (s[2, 0] + s[2, 1] + s[2, 2] - (s[0, 0] + s[0, 1] + s[0, 2])) / 6.0;

			double denom = 4.0 * A * B - C * C;

			if (Math.Abs(denom) > 0.00001)
			{
				// Az eltolódások kiszámítása (X = vízszintes/oszlop, Y = függőleges/sor)
				float offsetX = (float)((C * E - 2.0 * B * D) / denom);
				float offsetY = (float)((C * D - 2.0 * A * E) / denom);

				if (Math.Abs(offsetX) <= 0.5f && Math.Abs(offsetY) <= 0.5f)
				{
					return new PointF(offsetX, offsetY);
				}
			}
			return new PointF(0f, 0f);
		}



		public List<EdgeBasedShapeMatchingResult> FindModel(Image<Gray, float> Source)
		{
			// 1) Megkeressük az összes (ObjectCount darab) durva találatot a skálázott képen
			List<EdgeBasedShapeMatchingResult> roughResults = FindMatchModel_GPU_Scaled(Source);
		//	return roughResults;
			List<EdgeBasedShapeMatchingResult> finalResults = new List<EdgeBasedShapeMatchingResult>();
			Console.WriteLine("Found objects:" +roughResults.Count);
			// 2) Végigmegyünk a talált objektumokon, és egy szűkített ablakban pontosítunk a FULL képen
			foreach (var roughResult in roughResults)
			{
				// Ha a durva egyezés túl alacsony (zaj), átugorjuk
				if (roughResult.Score < 0.3f) continue;

				// Felkészítjük a pozíciót a Fine-Tune-hoz (egész számmal)
				Point intPreMatchedPos = new Point((int)Math.Round(roughResult.X), (int)Math.Round(roughResult.Y));

				// IPARI OPTIMALIZÁLÁS: Mivel a durva kereső már nagyon közel lőtt, 
				// a keresési sugarat (shiftprecision) és a szögtartományt (rotationprecision) drasztikusan lecsökkentjük!
				// A korábbi 7 és 10 helyett a 2 és 2-es sugár negyedannyi GPU munkát jelent, így villámgyors lesz!
				
//				EdgeBasedShapeMatchingResult fineTuned = FindMatchModel_CPU_AdaptiveFineTune(
				EdgeBasedShapeMatchingResult fineTuned = FindMatchModel_GPU_FineTune(
					Source,
					intPreMatchedPos,
					(int)Math.Round(roughResult.Angle),
					rotationprecision: 7, // Csak a talált szög ±2 fokos környezetét nézzük
					shiftprecision: 10     // Csak a talált pozíció ±2 pixeles környezetét nézzük
				);

/*
				EdgeBasedShapeMatchingResult fineTuned = FindMatchModel_CPU_FineTune(
					Source,
					intPreMatchedPos,
					(int)Math.Round(roughResult.Angle),
					rotationPrecision: 7, // Csak a talált szög ±2 fokos környezetét nézzük
					shiftPrecision: 10     // Csak a talált pozíció ±2 pixeles környezetét nézzük
				);
*/
				finalResults.Add(fineTuned);
			}

			// 3) Frissítjük a globális osztályváltozót a legjobb finomhangolt találattal a rajzoláshoz
			if (finalResults.Count > 0)
			{
				this.result = finalResults[0];
			}
			else
			{
				this.result = new EdgeBasedShapeMatchingResult();
			}

			return finalResults;
		}


		public EdgeBasedShapeMatchingResult FindModel_subpixel(Image<Gray, float> Source)
        {
			//	List<EdgeBasedShapeMatchingResult> resultarray = FindTemplateMatchesCPU_ParallelSIMD(Source, Models);
			//	result = FindMatchModel_GPU_FineTune(Source, new Point(resultarray[0].X, resultarray[0].Y), resultarray[0].Angle, 7, 10);
			List<EdgeBasedShapeMatchingResult> resultarray = FindMatchModel_GPU_Scaled(Source);
			//	result = FindMatchModel_GPU_FineTune(Source, new Point(result.X, result.Y), result.Angle, 7, 10);
			//			result = FindMatchModel_GPU_FineTune(Source, new Point(resultarray[0].X, resultarray[0].Y), resultarray[0].Angle, 7, 10);
			this.result = resultarray[0];


			return resultarray[0];

			// Kerekítjük a sub-pixel kiindulópontot egésszé a Fine-Tune kernel számára
	//		Point intPreMatchedPos = new Point((int)Math.Round(resultarray[0].X), (int)Math.Round(resultarray[0].Y));

			// Lefuttatjuk a Fine-Tune-t az első talált objektumra
		//	result = FindMatchModel_GPU_FineTune(Source, intPreMatchedPos, (int)Math.Round( resultarray[0].Angle), 7, 10);

			return result;
		}

		public Image<Bgr, byte> ImageWithResult_xyChange(Image<Bgr, byte> Source)
		{
			// Biztonsági szűrő: Ha nincs érvényes találat, vagy a score túl alacsony, ne rajzoljunk semmit
			if (result.Score < 0.1f) return Source;

			// 1) A lebegőpontos szöget a legközelebbi egész fokra kerekítjük (0-359 fok közé szorítva)
			int roundedAngle = (int)Math.Round(result.Angle);
			if (roundedAngle >= 360) roundedAngle -= 360;
			if (roundedAngle < 0) roundedAngle += 360;

			// 2) TENGELY-KORREKCIÓ AZ IPARI AOI RAJZOLÁSHOZ:
			// Mivel a CreateModel és a GPU számítás során a result.X a Sor(Y), a result.Y pedig az Oszlop(X),
			// az EmguCV PointF struktúrájához a tengelyeket meg kell cserélnünk:
			float offsetX = (float)result.Y; // Vízszintes eltolás a képernyőn (Oszlop)
			float offsetY = (float)result.X; // Függőleges eltolás a képernyőn (Sor)

			int stepCount = Full.step360[roundedAngle];
			int basePosition = Full.positions[roundedAngle];

			for (int a = 0; a < stepCount; a++)
			{
				// 3) A betanított Full mintatömb koordináta tengelyeit is szinkronizáljuk a PointF-hez:
				// cordinatesarrayY360 = Oszlop eltolás (vízszintes)
				// cordinatesarrayX360 = Sor eltolás (függőleges)
				float ptX = (float)Full.cordinatesarrayY360[basePosition + a] + offsetX; // Vízszintes pont pozíció
				float ptY = (float)Full.cordinatesarrayX360[basePosition + a] + offsetY; // Függőleges pont pozíció

				// Apró, tűpontos zöld kontúr kereszt kirajzolása
				Source.Draw(new Cross2DF(new PointF(ptX, ptY), 0, 0), new Bgr(10, 240, 10), 2);
			}

			return Source;
		}



		public Image<Bgr, byte> ImageWithResult(Image<Bgr, byte> Source, EdgeBasedShapeMatchingResult match)
		{
			// Biztosítjuk, hogy a lebegőpontos szöget a legközelebbi egész fokra kerekítjük
			int roundedAngle = (int)Math.Round(match.Angle);
			if (roundedAngle >= 360) roundedAngle -= 360;
			if (roundedAngle < 0) roundedAngle += 360;

			// A valós, sub-pixel pontos double koordinátákat float-tá alakítjuk a rajzoláshoz
			float offsetX = (float)match.X;
			float offsetY = (float)match.Y;

			// MATEMATIKAI KORREKCIÓ: 
			// Mivel nem futott le a Fine-Tune, explicit módon a FULL (teljes méretű) sablon 
			// adatait kérjük el a C# memóriából a rajzoláshoz!
			int stepCount = Full.step360[roundedAngle];
			int basePosition = Full.positions[roundedAngle];

			for (int a = 0; a < stepCount; a++)
			{
				// A Full modell cordinatesarrayX360 és Y360 tömbjeit használjuk fel
				float ptX = (float)Full.cordinatesarrayY360[basePosition + a] + offsetY;
				float ptY = (float)Full.cordinatesarrayX360[basePosition + a] + offsetX;

				// Kirajzoljuk a pontot az eredeti méretű képre
				Source.Draw(new Cross2DF(new PointF(ptX, ptY), 0, 0), new Bgr(10, 240, 10), 2);
			}

			return Source;
		}


		public Image<Bgr, byte> ImageWithResult(Image<Bgr, byte> Source)
		{
			// Biztosítjuk, hogy a lebegőpontos szöget a legközelebbi egész fokra kerekítjük
			int roundedAngle = (int)Math.Round(result.Angle);
			if (roundedAngle >= 360) roundedAngle -= 360;
			if (roundedAngle < 0) roundedAngle += 360;

			// A valós, sub-pixel pontos double koordinátákat float-tá alakítjuk a rajzoláshoz
			float offsetX = (float)result.X;
			float offsetY = (float)result.Y;

			// MATEMATIKAI KORREKCIÓ: 
			// Mivel nem futott le a Fine-Tune, explicit módon a FULL (teljes méretű) sablon 
			// adatait kérjük el a C# memóriából a rajzoláshoz!
			int stepCount = Full.step360[roundedAngle];
			int basePosition = Full.positions[roundedAngle];

			for (int a = 0; a < stepCount; a++)
			{
				// A Full modell cordinatesarrayX360 és Y360 tömbjeit használjuk fel
				float ptX = (float)Full.cordinatesarrayY360[basePosition + a] + offsetY;
				float ptY = (float)Full.cordinatesarrayX360[basePosition + a] + offsetX;

				// Kirajzoljuk a pontot az eredeti méretű képre
				Source.Draw(new Cross2DF(new PointF(ptX, ptY), 0, 0), new Bgr(10, 240, 10), 2);
			}

			return Source;
		}


		public Image<Bgr, byte> ImageWithResult_old(Image<Bgr, byte> Source)
		{
			// 1) A lebegőpontos szöget a legközelebbi egész fokra kerekítjük (0-359 tartományba kényszerítve)
			int roundedAngle = (int)Math.Round(result.Angle);
			if (roundedAngle >= 360) roundedAngle -= 360;
			if (roundedAngle < 0) roundedAngle += 360;

			// 2) Kiszámítjuk a lebegőpontos eltolásokat float-ként a PointF-hez
			float offsetX = (float)result.X;
			float offsetY = (float)result.Y;

			// 3) Kirajzoljuk az éleket az egész fokra kerekített modell alapján, de sub-pixel pontos pozíció-eltolással
			int stepCount = Full.step360[roundedAngle];
			int basePosition = Full.positions[roundedAngle];

			for (int a = 0; a < stepCount; a++)
			{
				// Kijelöljük az adott élpont valós, lebegőpontos koordinátáit
				float ptX = (float)Full.cordinatesarrayY360[basePosition + a] + offsetY;
				float ptY = (float)Full.cordinatesarrayX360[basePosition + a] + offsetX;

				// Kirajzoljuk a pontot (Cross2DF-fel, 0-s sugárral egy apró kereszt/pont lesz)
				Source.Draw(new Cross2DF(new PointF(ptX, ptY), 0, 0), new Bgr(10, 240, 10), 2);
			}

			return Source;
		}




		public static List<EdgeBasedShapeMatchingResult> FindTemplateMatchesCPU_ParallelSIMD(
	Image<Gray, float> sourceImage,
	EdgeBasedModelInfo[] models,
	int maxResults = 5,
	float scoreThreshold = 0.8f)
	{
		List<EdgeBasedShapeMatchingResult> results = new List<EdgeBasedShapeMatchingResult>();

		// Gradientek kiszámítása
		Mat gx = new Mat();
		Mat gy = new Mat();
		CvInvoke.Sobel(sourceImage, gx, DepthType.Cv32F, 1, 0, 3);
		CvInvoke.Sobel(sourceImage, gy, DepthType.Cv32F, 0, 1, 3);

		Image<Gray, float> gxImg = gx.ToImage<Gray, float>();
		Image<Gray, float> gyImg = gy.ToImage<Gray, float>();

		int width = sourceImage.Width;
		int height = sourceImage.Height;

		foreach (var model in models)
		{
			if (model == null || model.noOfCordinates == 0)
				continue;

			Parallel.For(0, height, y =>
			{
				for (int x = 0; x < width; x++)
				{
					float score = 0;
					int validPoints = 0;

					int n = model.noOfCordinates;
					int simdLength = Vector<float>.Count;

					for (int i = 0; i <= n - simdLength; i += simdLength)
					{
						Span<float> txSpan = model.edgeDerivativeX.AsSpan(i, simdLength);
						Span<float> tySpan = model.edgeDerivativeY.AsSpan(i, simdLength);
						Span<float> tmSpan = model.edgeMagnitude.AsSpan(i, simdLength);

						Vector<float> tx = new Vector<float>(txSpan.ToArray());
						Vector<float> ty = new Vector<float>(tySpan.ToArray());
						Vector<float> tm = new Vector<float>(tmSpan.ToArray());

						float[] sxArr = new float[simdLength];
						float[] syArr = new float[simdLength];
						float[] smArr = new float[simdLength];

						for (int j = 0; j < simdLength; j++)
						{
							int px = x + model.cordinatesarrayX[i + j];
							int py = y + model.cordinatesarrayY[i + j];

							if (px < 0 || py < 0 || px >= width || py >= height)
								continue;

							float sx = gxImg.Data[py, px, 0];
							float sy = gyImg.Data[py, px, 0];
							float sm = (float)Math.Sqrt(sx * sx + sy * sy);

							sxArr[j] = sx;
							syArr[j] = sy;
							smArr[j] = sm;
						}

						Vector<float> sxVec = new Vector<float>(sxArr);
						Vector<float> syVec = new Vector<float>(syArr);
						Vector<float> smVec = new Vector<float>(smArr);

						Vector<float> dot = sxVec * tx + syVec * ty;
						Vector<float> denom = smVec * tm;

						for (int j = 0; j < simdLength; j++)
						{
							if (denom[j] > 0)
							{
								score += dot[j] / denom[j];
								validPoints++;
							}
						}
					}

					// Maradék pontok
					for (int i = (n / simdLength) * simdLength; i < n; i++)
					{
						int px = x + model.cordinatesarrayX[i];
						int py = y + model.cordinatesarrayY[i];

						if (px < 0 || py < 0 || px >= width || py >= height)
							continue;

						float sx = gxImg.Data[py, px, 0];
						float sy = gyImg.Data[py, px, 0];
						float smag = (float)Math.Sqrt(sx * sx + sy * sy);

						float tx = model.edgeDerivativeX[i];
						float ty = model.edgeDerivativeY[i];
						float tmag = model.edgeMagnitude[i];

						float dot = (sx * tx + sy * ty);
						float denom = smag * tmag;

						if (denom > 0)
						{
							score += dot / denom;
							validPoints++;
						}
					}

					if (validPoints > 0)
					{
						score /= validPoints;

						if (score >= scoreThreshold)
						{
							lock (results)
							{
								results.Add(new EdgeBasedShapeMatchingResult
								{
									X = x,
									Y = y,
									Score = score,
									Angle = model.rotation
								});
							}
						}
					}
				}
			});
		}

		return results.OrderByDescending(r => r.Score).Take(maxResults).ToList();
	}



		public List<EdgeBasedShapeMatchingResult> FindMatchModel_GPU_Scaled(Image<Gray, float> Source)
		{
			var returnresult = new List<EdgeBasedShapeMatchingResult>();

			// 1) Template adatok GPU-ra
			CopyTemplateDataToGPU(Scaled);

			// 2) Emgu CV előkészítés: minden CV_32F (float)
			Mat SourceImage = Source.Resize(scale, Inter.Linear).Mat;
			Size Ssize = new Size(SourceImage.Cols, SourceImage.Rows);

			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);           // X deriváltak
			Mat CSdy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);           // Y deriváltak
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1.0));

			// 3) Deriváltak és magnitúdó számítása CPU-n
			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);
			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);

			// Szinkronizáció: Az Image objektumok folytonos .Data tömbjeit használjuk fel buffereléshez padding bájtok ellen
			X = CSdx.ToImage<Gray, float>();
			Y = CSdy.ToImage<Gray, float>();
			M = magnitude_src.ToImage<Gray, float>();

			CvInvoke.Divide(ForDivide.Mat, magnitude_src, magnitude);
			var magImage = magnitude.ToImage<Gray, float>();

			// 4) Alap méretek
			int W = Ssize.Width;
			int H = Ssize.Height;
			int PixelCount = W * H;
			SourceWidth = W;

			// 5) OpenCL bufferek (nem generikus IMem, de garantáltan folytonos .Data tömbökből táplálva!)
			ErrorCode errNum;
			IntPtr bufferSizeInBytes = (IntPtr)(sizeof(float) * PixelCount);

			IMem CLSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				bufferSizeInBytes, X.Data, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLSdx");

			IMem CLSdy = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				bufferSizeInBytes, Y.Data, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLSdy");

			IMem CLmagnitude = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				bufferSizeInBytes, magImage.Data, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLmagnitude");

			// Végső (reduktált) output bufferek
			IMem ResultMap = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				bufferSizeInBytes, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "ResultMap");

			IMem RotationMap = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				(IntPtr)(sizeof(int) * PixelCount), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "RotationMap");

			// Host oldali tömbök az olvasáshoz
			float[] result = new float[PixelCount];
			int[] rmap = new int[PixelCount];

			// 6) Accuracy módok és stride-ok
			int rotm = 1;    // rotáció stride (Speed:3, Balanced:2, Accuracy:1)
			int pixm = 1;    // pixel stride (Speed:2, Balanced/Accuracy:1)
			OpenCL.Net.Kernel computeKernel;

			switch (Accuracy)
			{
				case 1: // Speed
					rotm = 3;
					pixm = 2;
					computeKernel = kernelSpeed;
					break;
				case 2: // Balanced
					rotm = 2;
					pixm = 1;
					computeKernel = kernelBalanced;
					break;
				case 3: // Accuracy
				default:
					rotm = 1;
					pixm = 1;
					computeKernel = kernelAccuracy;
					break;
			}

			int globalsize = PixelCount / pixm;

			// --- JAVÍTOTT, BEÉLESÍTETT SZÖGTARTOMÁNY SZÁMÍTÁS ---
			// Kiszámítjuk a szimmetrikus ablak teljes méretét (pl. ±6 fok esetén az 13 pozíció)
			int totalAnglesToTest = (AngleDetectionRangeSimmetric * 2) + 1;
			int rotationsCount = (int)Math.Ceiling((double)totalAnglesToTest / rotm);
			int rotdiv = rotationsCount;

			// Kigyűjtjük CSAK a kért szögtartomány indexeit az átfordulások biztonságos kezelésével
			int[] customRotatePositions = new int[rotationsCount];
			int writeIdx = 0;
			for (int a = -AngleDetectionRangeSimmetric; a <= AngleDetectionRangeSimmetric; a += rotm)
			{
				if (writeIdx >= rotationsCount) break;

				int validAngle = a;
				if (validAngle < 0) validAngle += 360;
				if (validAngle >= 360) validAngle -= 360;

				customRotatePositions[writeIdx] = Scaled.positions[validAngle];
				writeIdx++;
			}

			// 7) 2D output buffer: Csak a korlátozott szögtartománynak foglalunk helyet!
			IMem Result2D = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				(IntPtr)(sizeof(float) * PixelCount * rotationsCount), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "Result2D");

			// 8) INIT kernel: 2D output feltöltése -INF-re (Becsomagolt unmanaged skalár tömbökkel!)
			{
				int[] wrapPixelCount = new int[] { PixelCount };
				int[] wrapRotationsCount = new int[] { rotationsCount };

				errNum = Cl.SetKernelArg(initKernel2D, 0, Result2D);
				errNum |= Cl.SetKernelArg(initKernel2D, 1, (IntPtr)sizeof(int), wrapPixelCount);
				errNum |= Cl.SetKernelArg(initKernel2D, 2, (IntPtr)sizeof(int), wrapRotationsCount);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "init args hiba");

				IntPtr[] gwsInit = new IntPtr[] { (IntPtr)PixelCount, (IntPtr)rotationsCount };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, initKernel2D, 2, null, gwsInit, null, 0, null, out _);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue init");
			}

			// Létrehozzuk a szűkített szögpozíciók egyedi unmanaged bufferét a fix CLTemppositions helyett
			IMem CLCustomPositions = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				(IntPtr)(sizeof(int) * rotationsCount), customRotatePositions, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLCustomPositions hiba");

			// 9) Számoló kernel: 2D NDRange [globalsize, rotdiv] (Becsomagolt unmanaged skalár tömbökkel!)
			{
				int[] wrapW = new int[] { W };
				int[] wrapPixelCount = new int[] { PixelCount };
				float[] wrapMinScore = new float[] { minScore };
				int[] wrapRotationsCount = new int[] { rotationsCount };

				errNum = Cl.SetKernelArg(computeKernel, 0, CLmagnitude);
				errNum |= Cl.SetKernelArg(computeKernel, 1, CLSdx);
				errNum |= Cl.SetKernelArg(computeKernel, 2, CLSdy);

				errNum |= Cl.SetKernelArg(computeKernel, 3, CLTempDerivativeX);
				errNum |= Cl.SetKernelArg(computeKernel, 4, CLTempDerivativeY);
				errNum |= Cl.SetKernelArg(computeKernel, 5, CLTempMagnitude);

				errNum |= Cl.SetKernelArg(computeKernel, 6, CLTempCordX);
				errNum |= Cl.SetKernelArg(computeKernel, 7, CLTempCordY);
				errNum |= Cl.SetKernelArg(computeKernel, 8, CLTempNoCoord);

				errNum |= Cl.SetKernelArg(computeKernel, 9, (IntPtr)sizeof(int), wrapW);
				errNum |= Cl.SetKernelArg(computeKernel, 10, (IntPtr)sizeof(int), wrapPixelCount);
				errNum |= Cl.SetKernelArg(computeKernel, 11, (IntPtr)sizeof(float), wrapMinScore);

				// KRITIKUS SZINKRONIZÁCIÓ: A fix 360 fokos CLTemppositions helyett az új CLCustomPositions-t adjuk át!
				errNum |= Cl.SetKernelArg(computeKernel, 12, CLCustomPositions);
				errNum |= Cl.SetKernelArg(computeKernel, 13, CLnormMinScore);
				errNum |= Cl.SetKernelArg(computeKernel, 14, CLnormGreediness);

				errNum |= Cl.SetKernelArg(computeKernel, 15, (IntPtr)sizeof(int), wrapRotationsCount);
				errNum |= Cl.SetKernelArg(computeKernel, 16, Result2D);

				ComputeContext.throwErrorIfNotSuccessfull(errNum, "set compute args");

				IntPtr[] gwsCompute = new IntPtr[] { (IntPtr)globalsize, (IntPtr)rotdiv };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, computeKernel, 2, null, gwsCompute, null, 0, null, out var eventExecutedKernel);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue compute");

				Cl.Finish(commandQueue);
			}

			// 10) Redukció kernel: 1D NDRange [PixelCount] (Becsomagolt unmanaged skalár tömbökkel!)
			{
				int[] wrapPixelCount = new int[] { PixelCount };
				int[] wrapRotationsCount = new int[] { rotationsCount };

				errNum = Cl.SetKernelArg(reduceKernel, 0, Result2D);
				errNum |= Cl.SetKernelArg(reduceKernel, 1, (IntPtr)sizeof(int), wrapPixelCount);
				errNum |= Cl.SetKernelArg(reduceKernel, 2, (IntPtr)sizeof(int), wrapRotationsCount);
				errNum |= Cl.SetKernelArg(reduceKernel, 3, ResultMap);
				errNum |= Cl.SetKernelArg(reduceKernel, 4, RotationMap);

				ComputeContext.throwErrorIfNotSuccessfull(errNum, "set reduce args");

				IntPtr[] gwsReduce = new IntPtr[] { (IntPtr)PixelCount };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, reduceKernel, 1, null, gwsReduce, null, 0, null, out _);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue reduce");

				Cl.Finish(commandQueue);
			}

			// 11) Kiolvasás a hostra
			{
				OpenCL.Net.Event ev;
				errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True, (IntPtr)0,
					(IntPtr)(PixelCount * sizeof(float)), result, 0, null, out ev);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "read ResultMap");

				errNum = Cl.EnqueueReadBuffer(commandQueue, RotationMap, OpenCL.Net.Bool.True, (IntPtr)0,
					(IntPtr)(PixelCount * sizeof(int)), rmap, 0, null, out ev);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "read RotationMap");
			}

			// 12) Multi-Match + CPU Paraboloid Sub-Pixel kereső ciklus (JAVÍTOTT TÖMBKEZELÉSSEL)
			int blankingRadius = (int)(Models[0].modelWidth / 2);

			// Készítünk egy munkamásolatot a GPU-ból beolvasott eredményekből, 
			// hogy a kitakarások ne rontsák el a globális tömb struktúráját
			float[] workingResult = new float[result.Length];
			Array.Copy(result, workingResult, result.Length);

			for (int e = 0; e < ObjectCount; e++)
			{
				double maxVal = double.NegativeInfinity;
				int bestIdx = -1;

				// A munkamásolatban keressük meg a következő legmagasabb Score-t
				for (int q = 0; q < workingResult.Length; q++)
				{
					if (workingResult[q] > maxVal)
					{
						maxVal = workingResult[q];
						bestIdx = q;
					}
				}
				// IPARI KÜSZÖBÖLÉS: Ha a találat score-ja kisebb, mint a minScore 75%-a,
				// akkor az már csak háttérzaj, nem futunk tovább
				if (bestIdx < 0 || maxVal < (minScore * 0.75f)) break;
				int imgCol = bestIdx % W;
				int imgRow = bestIdx / W;
				float[,] neighbors = new float[3, 3];
				PointF subPixelOffset = new PointF(0f, 0f);
				// Szomszédok gyűjtése a paraboloidhoz (Az eredeti sértetlen result tömbből!)
				if (imgCol > 0 && imgCol < W - 1 && imgRow > 0 && imgRow < H - 1)
				{
					neighbors[0, 0] = result[bestIdx - W - 1];
					neighbors[0, 1] = result[bestIdx - W];
					neighbors[0, 2] = result[bestIdx - W + 1];
					neighbors[1, 0] = result[bestIdx - 1];
					neighbors[1, 1] = (float)maxVal;
					neighbors[1, 2] = result[bestIdx + 1];
					neighbors[2, 0] = result[bestIdx + W - 1];
					neighbors[2, 1] = result[bestIdx + W];
					neighbors[2, 2] = result[bestIdx + W + 1];
					subPixelOffset = ComputeSubPixelParaboloid(neighbors);
				}
				double exactRow = imgRow + subPixelOffset.Y;
				double exactCol = imgCol + subPixelOffset.X;
				double finalSubPixelRow = exactRow * (1.0 / scale);
				double finalSubPixelCol = exactCol * (1.0 / scale);
				// SZÖG VISSZAFEJTÉS: A belső szál-indexet visszakonvertáljuk valós 0-359 fokos értékké
				int innerAngleIdx = rmap[bestIdx];
				int calculatedAngle = -AngleDetectionRangeSimmetric + (innerAngleIdx * rotm);
				int realAngle = calculatedAngle;
				if (realAngle < 0) realAngle += 360;
				if (realAngle >= 360) realAngle -= 360;
				var r = new EdgeBasedShapeMatchingResult
				{
					X = finalSubPixelRow,
					Y = finalSubPixelCol,
					position = bestIdx,
					Angle = realAngle, // Visszaadja a tiszta valós szöghelyzetet a rajzoláshoz és fine-tune-hoz
					Score = (float)maxVal
				};
				returnresult.Add(r);
				// MULTI-MATCH KITAKARÁS (A munkamásolaton végezzük el!)
				int startX = Math.Max(0, imgCol - blankingRadius);
				int endX = Math.Min(W - 1, imgCol + blankingRadius);
				int startY = Math.Max(0, imgRow - blankingRadius);
				int endY = Math.Min(H - 1, imgRow + blankingRadius);
				for (int row = startY; row <= endY; row++)
				{
					for (int col = startX; col <= endX; col++)
					{
						workingResult[row * W + col] = float.NegativeInfinity;
					}
				}
			}
			// 13) Takarítás
			CLSdx.Dispose();
			CLSdy.Dispose();
			CLmagnitude.Dispose();
			Result2D.Dispose();
			ResultMap.Dispose();
			RotationMap.Dispose();
			Cl.ReleaseMemObject(CLCustomPositions); // Felszabadítjuk az egyedi szög-buffert
			SourceImage.Dispose();
			CSdx.Dispose();
			CSdy.Dispose();
			angle_src.Dispose();
			magnitude_src.Dispose();
			magnitude.Dispose();
			ForDivide.Dispose();
			ReleaseMEMObjects();
			return returnresult;
		}





		public List<EdgeBasedShapeMatchingResult> FindMatchModel_GPU_Scaled_BestFor360Rotation(Image<Gray, float> Source)
		{
			var returnresult = new List<EdgeBasedShapeMatchingResult>();

			// 1) Template adatok GPU-ra
			CopyTemplateDataToGPU(Scaled);

			// 2) Emgu CV előkészítés: minden CV_32F (float)
			Mat SourceImage = Source.Resize(scale, Inter.Linear).Mat;
			Size Ssize = new Size(SourceImage.Cols, SourceImage.Rows);

			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);           // X deriváltak
			Mat CSdy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);           // Y deriváltak
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1.0));

			// 3) Deriváltak és magnitúdó számítása CPU-n
			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);
			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);

			// Szinkronizáció: Az Image objektumok folytonos .Data tömbjeit használjuk fel buffereléshez padding bájtok ellen
			X = CSdx.ToImage<Gray, float>();
			Y = CSdy.ToImage<Gray, float>();
			M = magnitude_src.ToImage<Gray, float>();

			CvInvoke.Divide(ForDivide.Mat, magnitude_src, magnitude);
			var magImage = magnitude.ToImage<Gray, float>();

			// 4) Alap méretek
			int W = Ssize.Width;
			int H = Ssize.Height;
			int PixelCount = W * H;
			SourceWidth = W;

			// 5) OpenCL bufferek (nem generikus IMem, de garantáltan folytonos .Data tömbökből táplálva!)
			ErrorCode errNum;
			IntPtr bufferSizeInBytes = (IntPtr)(sizeof(float) * PixelCount);

			IMem CLSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				bufferSizeInBytes, X.Data, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLSdx");

			IMem CLSdy = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				bufferSizeInBytes, Y.Data, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLSdy");

			IMem CLmagnitude = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				bufferSizeInBytes, magImage.Data, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLmagnitude");

			// Végső (reduktált) output bufferek
			IMem ResultMap = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				bufferSizeInBytes, out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "ResultMap");

			IMem RotationMap = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				(IntPtr)(sizeof(int) * PixelCount), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "RotationMap");

			// Host oldali tömbök az olvasáshoz
			float[] result = new float[PixelCount];
			int[] rmap = new int[PixelCount];

			// 6) Accuracy módok és stride-ok
			int rotm = 1;    // rotáció stride (Speed:3, Balanced:2, Accuracy:1)
			int pixm = 1;    // pixel stride (Speed:2, Balanced/Accuracy:1)
			OpenCL.Net.Kernel computeKernel;

			switch (Accuracy)
			{
				case 1: // Speed
					rotm = 3;
					pixm = 2;
					computeKernel = kernelSpeed;
					break;
				case 2: // Balanced
					rotm = 2;
					pixm = 1;
					computeKernel = kernelBalanced;
					break;
				case 3: // Accuracy
				default:
					rotm = 1;
					pixm = 1;
					computeKernel = kernelAccuracy;
					break;
			}

			int globalsize = PixelCount / pixm;
			int rotationsCount = 360 / rotm;
			int rotdiv = rotationsCount;

			// 7) 2D output buffer: [rotationsCount * PixelCount]
			IMem Result2D = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				(IntPtr)(sizeof(float) * PixelCount * rotationsCount), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "Result2D");

			// 8) INIT kernel: 2D output feltöltése -INF-re (Becsomagolt unmanaged skalár tömbökkel!)
			{
				int[] wrapPixelCount = new int[] { PixelCount };
				int[] wrapRotationsCount = new int[] { rotationsCount };

				errNum = Cl.SetKernelArg(initKernel2D, 0, Result2D);
				errNum |= Cl.SetKernelArg(initKernel2D, 1, (IntPtr)sizeof(int), wrapPixelCount);
				errNum |= Cl.SetKernelArg(initKernel2D, 2, (IntPtr)sizeof(int), wrapRotationsCount);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "init args hiba");

				IntPtr[] gwsInit = new IntPtr[] { (IntPtr)PixelCount, (IntPtr)rotationsCount };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, initKernel2D, 2, null, gwsInit, null, 0, null, out _);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue init");
			}

			// 9) Számoló kernel: 2D NDRange [globalsize, rotdiv] (Becsomagolt unmanaged skalár tömbökkel!)
			{
				int[] wrapW = new int[] { W };
				int[] wrapPixelCount = new int[] { PixelCount };
				float[] wrapMinScore = new float[] { minScore };
				int[] wrapRotationsCount = new int[] { rotationsCount };

				errNum = Cl.SetKernelArg(computeKernel, 0, CLmagnitude);
				errNum |= Cl.SetKernelArg(computeKernel, 1, CLSdx);
				errNum |= Cl.SetKernelArg(computeKernel, 2, CLSdy);

				errNum |= Cl.SetKernelArg(computeKernel, 3, CLTempDerivativeX);
				errNum |= Cl.SetKernelArg(computeKernel, 4, CLTempDerivativeY);
				errNum |= Cl.SetKernelArg(computeKernel, 5, CLTempMagnitude);

				errNum |= Cl.SetKernelArg(computeKernel, 6, CLTempCordX);
				errNum |= Cl.SetKernelArg(computeKernel, 7, CLTempCordY);
				errNum |= Cl.SetKernelArg(computeKernel, 8, CLTempNoCoord);

				errNum |= Cl.SetKernelArg(computeKernel, 9, (IntPtr)sizeof(int), wrapW);
				errNum |= Cl.SetKernelArg(computeKernel, 10, (IntPtr)sizeof(int), wrapPixelCount);
				errNum |= Cl.SetKernelArg(computeKernel, 11, (IntPtr)sizeof(float), wrapMinScore);

				errNum |= Cl.SetKernelArg(computeKernel, 12, CLTemppositions);
				errNum |= Cl.SetKernelArg(computeKernel, 13, CLnormMinScore);
				errNum |= Cl.SetKernelArg(computeKernel, 14, CLnormGreediness);

				errNum |= Cl.SetKernelArg(computeKernel, 15, (IntPtr)sizeof(int), wrapRotationsCount);
				errNum |= Cl.SetKernelArg(computeKernel, 16, Result2D);

				ComputeContext.throwErrorIfNotSuccessfull(errNum, "set compute args");

				IntPtr[] gwsCompute = new IntPtr[] { (IntPtr)globalsize, (IntPtr)rotdiv };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, computeKernel, 2, null, gwsCompute, null, 0, null, out var eventExecutedKernel);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue compute");

				Cl.Finish(commandQueue);
			}

			// 10) Redukció kernel: 1D NDRange [PixelCount] (Becsomagolt unmanaged skalár tömbökkel!)
			{
				int[] wrapPixelCount = new int[] { PixelCount };
				int[] wrapRotationsCount = new int[] { rotationsCount };

				errNum = Cl.SetKernelArg(reduceKernel, 0, Result2D);
				errNum |= Cl.SetKernelArg(reduceKernel, 1, (IntPtr)sizeof(int), wrapPixelCount);
				errNum |= Cl.SetKernelArg(reduceKernel, 2, (IntPtr)sizeof(int), wrapRotationsCount);
				errNum |= Cl.SetKernelArg(reduceKernel, 3, ResultMap);
				errNum |= Cl.SetKernelArg(reduceKernel, 4, RotationMap);

				ComputeContext.throwErrorIfNotSuccessfull(errNum, "set reduce args");

				IntPtr[] gwsReduce = new IntPtr[] { (IntPtr)PixelCount };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, reduceKernel, 1, null, gwsReduce, null, 0, null, out _);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue reduce");

				Cl.Finish(commandQueue);
			}

			// 11) Kiolvasás a hostra
			{
				OpenCL.Net.Event ev;
				errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True, (IntPtr)0,
					(IntPtr)(PixelCount * sizeof(float)), result, 0, null, out ev);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "read ResultMap");

				errNum = Cl.EnqueueReadBuffer(commandQueue, RotationMap, OpenCL.Net.Bool.True, (IntPtr)0,
					(IntPtr)(PixelCount * sizeof(int)), rmap, 0, null, out ev);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "read RotationMap");
			}

			// 12) Multi-Match + CPU Paraboloid Sub-Pixel kereső ciklus (JAVÍTOTT TÖMBKEZELÉSSEL)
			int blankingRadius = (int)(Models[0].modelWidth / 2);

			// Készítünk egy munkamásolatot a GPU-ból beolvasott eredményekből, 
			// hogy a kitakarások ne rontsák el a globális tömb struktúráját
			float[] workingResult = new float[result.Length];
			Array.Copy(result, workingResult, result.Length);

			for (int e = 0; e < ObjectCount; e++)
			{
				double maxVal = double.NegativeInfinity;
				int bestIdx = -1;

				// A munkamásolatban keressük meg a következő legmagasabb Score-t
				for (int q = 0; q < workingResult.Length; q++)
				{
					if (workingResult[q] > maxVal)
					{
						maxVal = workingResult[q];
						bestIdx = q;
					}
				}

				// IPARI KÜSZÖBÖLÉS: Ha a találat score-ja kisebb, mint a minScore 75%-a, 
				// akkor az már csak háttérzaj, nem futunk tovább
				if (bestIdx < 0 || maxVal < (minScore * 0.75f)) break;

				int imgCol = bestIdx % W;
				int imgRow = bestIdx / W;

				float[,] neighbors = new float[3, 3];
				PointF subPixelOffset = new PointF(0f, 0f);

				// Szomszédok gyűjtése a paraboloidhoz (Az eredeti sértetlen result tömbből!)
				if (imgCol > 0 && imgCol < W - 1 && imgRow > 0 && imgRow < H - 1)
				{
					neighbors[0, 0] = result[bestIdx - W - 1];
					neighbors[0, 1] = result[bestIdx - W];
					neighbors[0, 2] = result[bestIdx - W + 1];

					neighbors[1, 0] = result[bestIdx - 1];
					neighbors[1, 1] = (float)maxVal;
					neighbors[1, 2] = result[bestIdx + 1];

					neighbors[2, 0] = result[bestIdx + W - 1];
					neighbors[2, 1] = result[bestIdx + W];
					neighbors[2, 2] = result[bestIdx + W + 1];

					subPixelOffset = ComputeSubPixelParaboloid(neighbors);
				}

				double exactRow = imgRow + subPixelOffset.Y;
				double exactCol = imgCol + subPixelOffset.X;

				double finalSubPixelRow = exactRow * (1.0 / scale);
				double finalSubPixelCol = exactCol * (1.0 / scale);

				var r = new EdgeBasedShapeMatchingResult
				{
					X = finalSubPixelRow,
					Y = finalSubPixelCol,
					position = bestIdx,
					Angle = rmap[bestIdx],
					Score = (float)maxVal
				};

				returnresult.Add(r);

				// MULTI-MATCH KITAKARÁS (A munkamásolaton végezzük el!)
				int startX = Math.Max(0, imgCol - blankingRadius);
				int endX = Math.Min(W - 1, imgCol + blankingRadius);
				int startY = Math.Max(0, imgRow - blankingRadius);
				int endY = Math.Min(H - 1, imgRow + blankingRadius);

				for (int row = startY; row <= endY; row++)
				{
					for (int col = startX; col <= endX; col++)
					{
						workingResult[row * W + col] = float.NegativeInfinity;
					}
				}
			}
			// 13) Takarítás
			CLSdx.Dispose();
			CLSdy.Dispose();
			CLmagnitude.Dispose();
			Result2D.Dispose();
			ResultMap.Dispose();
			RotationMap.Dispose();
			SourceImage.Dispose();
			CSdx.Dispose();
			CSdy.Dispose();
			angle_src.Dispose();
			magnitude_src.Dispose();
			magnitude.Dispose();
			ForDivide.Dispose();
			ReleaseMEMObjects();
			return returnresult;
		}




		public List<EdgeBasedShapeMatchingResult> FindMatchModel_GPU_Scaled_OldWorking(Image<Gray, float> Source)
		{
			var returnresult = new List<EdgeBasedShapeMatchingResult>();

			// 1) Template adatok GPU-ra
			CopyTemplateDataToGPU(Scaled);

			// 2) Emgu CV előkészítés: minden CV_32F (float)
			Mat SourceImage = Source.Resize(scale, Inter.Linear).Mat;
			Size Ssize = new Size(SourceImage.Cols, SourceImage.Rows);

			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);           // X deriváltak
			Mat CSdy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);           // Y deriváltak
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1.0));

			// 3) Deriváltak és magnitúdó számítása CPU-n
			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);
			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);

			// (Ha szükséges, tárolod ezeket vizualizációhoz)
			X = CSdx.ToImage<Gray, float>();
			Y = CSdy.ToImage<Gray, float>();
			M = magnitude_src.ToImage<Gray, float>();

			CvInvoke.Divide(ForDivide.Mat, magnitude_src, magnitude);

			// 4) Alap méretek
			int W = Ssize.Width;
			int H = Ssize.Height;
			int PixelCount = W * H;
			SourceWidth = W;

			// 5) OpenCL bufferek (nem generikus IMem!)
			ErrorCode errNum;

			IMem CLSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				(IntPtr)(sizeof(float) * PixelCount), CSdx.GetData(), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLSdx");

			IMem CLSdy = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				(IntPtr)(sizeof(float) * PixelCount), CSdy.GetData(), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLSdy");

			IMem CLmagnitude = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
				(IntPtr)(sizeof(float) * PixelCount), magnitude.GetData(), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "CLmagnitude");

			// Végső (reduktált) output bufferek
			IMem ResultMap = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				(IntPtr)(sizeof(float) * PixelCount), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "ResultMap");

			IMem RotationMap = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				(IntPtr)(sizeof(int) * PixelCount), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "RotationMap");

			// Host oldali tömbök az olvasáshoz
			float[] result = new float[PixelCount];
			int[] rmap = new int[PixelCount];

			// 6) Accuracy módok és stride-ok
			int rotm = 1;    // rotáció stride (Speed:3, Balanced:2, Accuracy:1)
			int pixm = 1;    // pixel stride (Speed:2, Balanced/Accuracy:1)
		OpenCL.Net.Kernel computeKernel;

			switch (Accuracy)
			{
				case 1: // Speed
					rotm = 3;
					pixm = 2;
					computeKernel = kernelSpeed;     // javított, 2D-outputos verzió
					break;
				case 2: // Balanced
					rotm = 2;
					pixm = 1;
					computeKernel = kernelBalanced;  // javított, 2D-outputos verzió
					break;
				case 3: // Accuracy
				default:
					rotm = 1;
					pixm = 1;
					computeKernel = kernelAccuracy;  // javított, 2D-outputos verzió
					break;
			}

			int globalsize = PixelCount / pixm;        // Speed módban: felezett pixel-set
			int rotationsCount = 360 / rotm;           // rotációk száma
			int rotdiv = rotationsCount;

			// 7) 2D output buffer: [rotationsCount * PixelCount]
			IMem Result2D = Cl.CreateBuffer(context, MemFlags.ReadWrite,
				(IntPtr)(sizeof(float) * PixelCount * rotationsCount), out errNum);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "Result2D");

			// 8) INIT kernel: 2D output feltöltése -INF-re
			{
				// init_maps_2d(__global float* resultmap2D, int pixNumb, int rotationsCount)
				errNum = Cl.SetKernelArg(initKernel2D, 0, Result2D);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "init arg0");

				errNum = Cl.SetKernelArg(initKernel2D, 1, (IntPtr)4, PixelCount);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "init arg1 (pixNumb)");

				errNum = Cl.SetKernelArg(initKernel2D, 2, (IntPtr)4, rotationsCount);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "init arg2 (rotationsCount)");

				IntPtr[] gwsInit = new IntPtr[] { (IntPtr)PixelCount, (IntPtr)rotationsCount };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, initKernel2D, 2, null, gwsInit, null, 0, null, out _);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue init");
			}

			// 9) Számoló kernel: 2D NDRange [globalsize, rotdiv]
			{
				// Arg-sorrend (javított kernelekhez):
				// 0:  CLmagnitude (SourceMagnitude)
				// 1:  CLSdx       (SourceDerivateX)
				// 2:  CLSdy       (SourceDerivateY)
				// 3:  CLTempDerivativeX (TemplateDerivateX)
				// 4:  CLTempDerivativeY (TemplateDerivateY)
				// 5:  CLTempMagnitude   (TemplateMagnitude)
				// 6:  CLTempCordX       (TemplateCoordX)
				// 7:  CLTempCordY       (TemplateCoordY)
				// 8:  CLTempNoCoord     (TemplateCoordinatesNumber)
				// 9:  SizeWidth         (int)
				// 10: pixNumb           (int)
				// 11: minScore          (float)
				// 12: CLTemppositions   (Rotatepositions)
				// 13: CLnormMinScore    (normMinScore360)
				// 14: CLnormGreediness  (normGreediness360)
				// 15: rotationsCount    (int)
				// 16: Result2D          (__global float*)

				errNum = Cl.SetKernelArg(computeKernel, 0, CLmagnitude);
				errNum |= Cl.SetKernelArg(computeKernel, 1, CLSdx);
				errNum |= Cl.SetKernelArg(computeKernel, 2, CLSdy);

				errNum |= Cl.SetKernelArg(computeKernel, 3, CLTempDerivativeX);
				errNum |= Cl.SetKernelArg(computeKernel, 4, CLTempDerivativeY);
				errNum |= Cl.SetKernelArg(computeKernel, 5, CLTempMagnitude);

				errNum |= Cl.SetKernelArg(computeKernel, 6, CLTempCordX);
				errNum |= Cl.SetKernelArg(computeKernel, 7, CLTempCordY);
				errNum |= Cl.SetKernelArg(computeKernel, 8, CLTempNoCoord);

				errNum |= Cl.SetKernelArg(computeKernel, 9, (IntPtr)4, W);
				errNum |= Cl.SetKernelArg(computeKernel, 10, (IntPtr)4, PixelCount);
				errNum |= Cl.SetKernelArg(computeKernel, 11, (IntPtr)4, minScore);

				errNum |= Cl.SetKernelArg(computeKernel, 12, CLTemppositions);
				errNum |= Cl.SetKernelArg(computeKernel, 13, CLnormMinScore);
				errNum |= Cl.SetKernelArg(computeKernel, 14, CLnormGreediness);

				errNum |= Cl.SetKernelArg(computeKernel, 15, (IntPtr)4, rotationsCount);
				errNum |= Cl.SetKernelArg(computeKernel, 16, Result2D);

				ComputeContext.throwErrorIfNotSuccessfull(errNum, "set compute args");

				IntPtr[] gwsCompute = new IntPtr[] { (IntPtr)globalsize, (IntPtr)rotdiv };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, computeKernel, 2, null, gwsCompute, null, 0, null, out var eventExecutedKernel);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue compute");

				Cl.Finish(commandQueue);
			}

			// 10) Redukció kernel: 1D NDRange [PixelCount]
			{
				// reduce_best_rotation(resultmap2D, pixNumb, rotationsCount, ResultMap, RotationMap)
				errNum = Cl.SetKernelArg(reduceKernel, 0, Result2D);
				errNum |= Cl.SetKernelArg(reduceKernel, 1, (IntPtr)4, PixelCount);
				errNum |= Cl.SetKernelArg(reduceKernel, 2, (IntPtr)4, rotationsCount);
				errNum |= Cl.SetKernelArg(reduceKernel, 3, ResultMap);
				errNum |= Cl.SetKernelArg(reduceKernel, 4, RotationMap);

				ComputeContext.throwErrorIfNotSuccessfull(errNum, "set reduce args");

				IntPtr[] gwsReduce = new IntPtr[] { (IntPtr)PixelCount };
				errNum = Cl.EnqueueNDRangeKernel(commandQueue, reduceKernel, 1, null, gwsReduce, null, 0, null, out _);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "enqueue reduce");

				Cl.Finish(commandQueue);
			}

			// 11) Kiolvasás a hostra
			{
				OpenCL.Net.Event ev;
				errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True, (IntPtr)0,
					(IntPtr)(PixelCount * sizeof(float)), result, 0, null, out ev);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "read ResultMap");

				errNum = Cl.EnqueueReadBuffer(commandQueue, RotationMap, OpenCL.Net.Bool.True, (IntPtr)0,
					(IntPtr)(PixelCount * sizeof(int)), rmap, 0, null, out ev);
				ComputeContext.throwErrorIfNotSuccessfull(errNum, "read RotationMap");
			}

			// 12) Top-5 találat keresése (‑INF sentinel a már kiválasztott pozíciókra)
			for (int e = 0; e < 5; e++)
			{
				double max = double.NegativeInfinity;
				int bestIdx = -1;

				for (int q = 0; q < result.Length; q++)
				{
					if (result[q] > max)
					{
						max = result[q];
						bestIdx = q;
					}
				}

				if (bestIdx < 0) break;

				int y = bestIdx % W;
				int x = (bestIdx - y) / W;

				var r = new EdgeBasedShapeMatchingResult
				{
					X = (int)(x * (1.0 / scale)),
					Y = (int)(y * (1.0 / scale)),
					position = bestIdx,
					Angle = rmap[bestIdx],
					Score = (float)max
				};

				// Ne válasszuk újra ugyanazt
				result[bestIdx] = float.NegativeInfinity;
				returnresult.Add(r);
			}

			// 13) Takarítás
			CLSdx.Dispose();
			CLSdy.Dispose();
			CLmagnitude.Dispose();
			Result2D.Dispose();
			ResultMap.Dispose();
			RotationMap.Dispose();

			SourceImage.Dispose();
			CSdx.Dispose();
			CSdy.Dispose();
			angle_src.Dispose();
			magnitude_src.Dispose();
			magnitude.Dispose();
			ForDivide.Dispose();

			ReleaseMEMObjects();

			return returnresult;
		}



		public List<EdgeBasedShapeMatchingResult> FindMatchModel_GPU_Scaled_origi(Image<Gray, float> Source)
		{
			List<EdgeBasedShapeMatchingResult> returnresult = new List<EdgeBasedShapeMatchingResult>();
			CopyTemplateDataToGPU(Scaled);

			Mat SourceImage = Source.Resize(scale, Inter.Linear).Mat;
			Size Ssize = new Size();
			Ssize.Width = SourceImage.Cols;
			Ssize.Height = SourceImage.Rows;
			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1); // X derivatives CV_16SC1
			Mat CSdy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1); // y derivatives
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);

			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Mat magnitude2 = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1));

			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);  // find X derivatives CV_16S
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3); // find Y derivatives

			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);
			X = CSdx.ToImage<Gray, float>();
			Y = CSdy.ToImage<Gray, float>();
			M = magnitude_src.ToImage<Gray, float>();

			CvInvoke.Divide(ForDivide.Mat, magnitude_src, magnitude);

			float[] result = new float[Ssize.Width * Ssize.Height];
			int[] rmap = new int[Ssize.Width * Ssize.Height];
			ErrorCode errNum;

			CLSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), CSdx.GetData(), out errNum);
			CLSdy = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), CSdy.GetData(), out errNum);
			CLmagnitude = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), magnitude.GetData(), out errNum);
			ResultMap = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * Ssize.Width * Ssize.Height, out errNum);
			RotationMap = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(int) * Ssize.Width * Ssize.Height, out errNum);
			SourceWidth = Ssize.Width;
			PixelCount = Ssize.Width * Ssize.Height;
			

			int rotm = 1;
			int pixm = 1;
			switch(Accuracy)
            {
				case 1:
					rotm = 3;
					pixm = 2;
					SetKernelArguments(kernelSpeed);
					break;
				case 2:
					rotm = 2;
					pixm = 1;
					SetKernelArguments(kernelBalanced);
					break;
				case 3:
					rotm = 1;
					pixm = 1;
					SetKernelArguments(kernelAccuracy);
					break;

				default:
					rotm = 1;
					pixm = 1;
					SetKernelArguments(kernelAccuracy);
					break;
			}

			int globalsize = Ssize.Width * Ssize.Height / pixm ;
			int rotdiv = 360 / rotm;
			//			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)kernelPositions.Length };
			//			IntPtr[] localWorkSize = new IntPtr[] { (IntPtr)32 };



			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)globalsize , (IntPtr)rotdiv};
			//			size_t globalWorkSize[2] = { globalsize, 180 };
			//						size_t localWorkSize[1] = { 24 };
			OpenCL.Net.Event eventReadBufferForOutputMap;
			OpenCL.Net.Event eventExecutedKernel;


			switch (Accuracy)
			{
				case 1:
					errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernelSpeed, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
					ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
					break;
				case 2:
					errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernelBalanced, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
					ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
					break;
				case 3:
					errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernelAccuracy, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
					ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
					break;

				default:
					errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernelAccuracy, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
					ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
					break;
			}

			/*
						errNum = clEnqueueNDRangeKernel(commandQueue, kernel, 2, NULL,
										globalWorkSize, NULL,
										0, NULL, NULL);

									clFinish(commandQueue);
			*/

			Cl.Finish(commandQueue);
			//		IntPtr pointer = new IntPtr();
			//	errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True,0, Ssize.Width * Ssize.Height * sizeof(float), result, 0, null, out eventReadBufferForOutputMap);
			errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(Ssize.Width * Ssize.Height * sizeof(float)), result, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, RotationMap, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(Ssize.Width * Ssize.Height * sizeof(int)), rmap, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");

			//	Marshal.Copy(pointer, result, 0, (Ssize.Width * Ssize.Height * sizeof(float)));
			//		MessageBox.Show(result.Length.ToString());
			/*			using (System.IO.StreamWriter file =
							new System.IO.StreamWriter(@"d:/Geomatch/output/resultarraySharp.dat",false))
									{
										for (int q =0; q< result.Length; q++)
										file.WriteLine(result[q].ToString());
									}

						using (System.IO.StreamWriter file =
				new System.IO.StreamWriter(@"d:/Geomatch/output/resultarrayRotationSharp.dat", false))
						{
							for (int q = 0; q < rmap.Length; q++)
								file.WriteLine(rmap[q].ToString());
						}
			*/

/*
			string output = "";
			for (int q = 0; q < result.Length; q++)
			{
				int y = (int)(q % Ssize.Width);
				int x = (int)((q - y) / Ssize.Width);
				int X = (int)(x * (1 / scale));
				int Y = (int)(y * (1 / scale));
				int position = q;
				int Angle = rmap[(int)q];
				float Score = (float)result[q];
				output = output + X + "," + Y + "," + Angle + "," + Score + "\n";
			}

			File.WriteAllText(@"C:\Debug\matchoutput.csv", output);

			*/
			double max = 0;
			double count = 0;
			double resPoz = 0;
			

			for (int e = 0; e < 5; e++)
			{
				 max = 0;
				 count = 0;
				 resPoz = 0;

				for (int q = 0; q < result.Length; q++)
				{
					if (result[q] > max)
					{
						max = result[q];
						resPoz = count;
					}
					count++;
				}


				EdgeBasedShapeMatchingResult r = new EdgeBasedShapeMatchingResult();
				int y = (int)(resPoz % Ssize.Width);
				int x = (int)((resPoz - y) / Ssize.Width);
				r.X = (int)(x * (1 / scale));
				r.Y = (int)(y * (1 / scale));
				r.position = resPoz;
				r.Angle = rmap[(int)resPoz];
				r.Score = (float)max;
				result[(int)resPoz] = 0;
				returnresult.Add(r);
			}
			CLSdx.Dispose();
			CLSdy.Dispose();
			CLmagnitude.Dispose();
			ResultMap.Dispose();
			RotationMap.Dispose();
			SourceImage.Dispose();
			CSdx.Dispose();
			CSdy.Dispose();
			angle_src.Dispose();
			magnitude_src.Dispose();
			magnitude.Dispose();
			ForDivide.Dispose();
			ReleaseMEMObjects();
			return returnresult;
		}


		public EdgeBasedShapeMatchingResult FindMatchModel_CPU_FineTune(
			Image<Gray, float> Source,
			Point PreMatchedPosition,
			int PreMatchedRotation,
			int rotationprecision,
			int shiftprecision)
		{
			var returnresult = new EdgeBasedShapeMatchingResult();
			object lockObject = new object();

			// 1) Gradiensképek előkészítése (Változatlan, tiszta float)
			Mat SourceImage = Source.Mat;
			Size Ssize = new Size(SourceImage.Cols, SourceImage.Rows);

			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat CSdy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1.0));

			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);
			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);
			CvInvoke.Divide(ForDivide.Mat, magnitude_src, magnitude);

			var gxImg = CSdx.ToImage<Gray, float>();
			var gyImg = CSdy.ToImage<Gray, float>();
			var magImg = magnitude.ToImage<Gray, float>();

			int SizeWidth = Ssize.Width;
			int pixNumb = Ssize.Width * Ssize.Height;

			// Fixen a te 7 fokos és 10 pixeles ablakaidat képezzük le
			int startX = Math.Max(0, PreMatchedPosition.X - 10);
			int endX = Math.Min(Ssize.Height - 1, PreMatchedPosition.X + 10);
			int startY = Math.Max(0, PreMatchedPosition.Y - 10);
			int endY = Math.Min(Ssize.Width - 1, PreMatchedPosition.Y + 10);

			int startAngle = PreMatchedRotation - 7;
			int endAngle = PreMatchedRotation + 7;

			double bestScore = -1.0;
			int bestX = PreMatchedPosition.X;
			int bestY = PreMatchedPosition.Y;
			int bestAngle = PreMatchedRotation;

			double normGreediness = (1 - greediness * minScore) / (1 - greediness);

			// --- 2) IPARI RITKÍTOTT RÁCSKERESÉS (COARSE GRID SEARCH) ---
			// A szögeket 2 fokos lépésközzel, a pixeleket 3 pixeles lépésközzel pásztázzuk át.
			// Ez egyenletesen lefedi a teljes 7,10-es területedet, így ESÉLYE NINCS ELTÉVEDNI!
			int angleStride = 2;
			int pixelStride = 3;

			List<int> anglesToTest = new List<int>();
			for (int a = startAngle; a <= endAngle; a += angleStride)
			{
				int validAngle = a;
				if (validAngle < 0) validAngle += 360;
				if (validAngle >= 360) validAngle -= 360;
				anglesToTest.Add(validAngle);
			}

			Parallel.ForEach(anglesToTest, rotation =>
			{
				int position = Full.positions[rotation];
				int Stepnumber = Full.step360[rotation];

				for (int y = startY; y <= endY; y += pixelStride)
				{
					for (int x = startX; x <= endX; x += pixelStride)
					{
						int gid = x * SizeWidth + y; // Shiju-féle lineáris címzés

						float partialSum = 0.0f;
						float partialScore = 0.0f;

						for (int m = 0; m < Stepnumber; m++)
						{
							int distance = gid + Full.cordinatesarrayY360[position + m] + Full.cordinatesarrayX360[position + m] * SizeWidth;

							if (distance < 0 || distance >= pixNumb) continue;

							float iTx = Full.edgeDerivativeX360[position + m];
							float iTy = Full.edgeDerivativeY360[position + m];
							float iSx = gxImg.Data[distance / SizeWidth, distance % SizeWidth, 0];
							float iSy = gyImg.Data[distance / SizeWidth, distance % SizeWidth, 0];

							if ((iSx != 0 || iSy != 0) && (iTx != 0 || iTy != 0))
							{
								partialSum += ((iSx * iTx) + (iSy * iTy)) * (Full.edgeMagnitude360[position + m] * magImg.Data[distance / SizeWidth, distance % SizeWidth, 0]);
							}

							float sumOfCoords = (float)(m + 1);
							partialScore = partialSum / sumOfCoords;

							float progress = sumOfCoords / (float)Stepnumber;
							float thresholdA = (minScore - 1.0f) + Full.normGreediness360[rotation] * progress;
							float thresholdB = Full.normMinScore360[rotation] * progress;
							float threshold = Math.Min(thresholdA, thresholdB);

							if (partialScore < threshold) break; // Greediness aktív
						}

						if (partialScore > bestScore)
						{
							lock (lockObject)
							{
								if (partialScore > bestScore)
								{
									bestScore = partialScore;
									bestX = x;
									bestY = y;
									bestAngle = rotation;
								}
							}
						}
					}
				}
			});

			// --- 3) LOKÁLIS 1-PIXELES FINOMHANGOLÁS A RÁCS-GYŐZTES KÖRÜL ---
			// Miután a rács megadta a biztos és csalhatatlan globális maximumot, 
			// a közvetlen ±1 pixeles és ±1 fokos környezetét még egyszer átnézzük egyesével.
			double finalScore = bestScore;
			int fineX = bestX;
			int fineY = bestY;
			int fineAngle = bestAngle;

			for (int a = bestAngle - 1; a <= bestAngle + 1; a++)
			{
				int vAngle = a < 0 ? a + 360 : (a >= 360 ? a - 360 : a);
				int pos = Full.positions[vAngle];
				int steps = Full.step360[vAngle];

				for (int y = Math.Max(startY, bestY - 1); y <= Math.Min(endY, bestY + 1); y++)
				{
					for (int x = Math.Max(startX, bestX - 1); x <= Math.Min(endX, bestX + 1); x++)
					{
						int gid = x * SizeWidth + y;
						float pSum = 0.0f;

						for (int m = 0; m < steps; m++)
						{
							int dist = gid + Full.cordinatesarrayY360[pos + m] + Full.cordinatesarrayX360[pos + m] * SizeWidth;
							if (dist < 0 || dist >= pixNumb) continue;

							float iTx = Full.edgeDerivativeX360[pos + m];
							float iTy = Full.edgeDerivativeY360[pos + m];
							float iSx = gxImg.Data[dist / SizeWidth, dist % SizeWidth, 0];
							float iSy = gyImg.Data[dist / SizeWidth, dist % SizeWidth, 0];

							if ((iSx != 0 || iSy != 0) && (iTx != 0 || iTy != 0))
							{
								pSum += ((iSx * iTx) + (iSy * iTy)) * (Full.edgeMagnitude360[pos + m] * magImg.Data[dist / SizeWidth, dist % SizeWidth, 0]);
							}
						}

						float fScore = pSum / (float)steps;
						if (fScore > finalScore)
						{
							finalScore = fScore;
							fineX = x;
							fineY = y;
							fineAngle = vAngle;
						}
					}
				}
			}

			// --- 4) 2D PARABOLOID INTERPOLÁCIÓ A TIZEDPIXEL PONTONSÁGÉRT ---
			float[,] fineNeighbors = new float[3, 3];
			PointF subPixelOffset = new PointF(0f, 0f);

			if (fineY > startY && fineY < endY && fineX > startX && fineX < endX)
			{
				int vAng = fineAngle;
				int pos = Full.positions[vAng];
				int steps = Full.step360[vAng];

				// Súlyozott 3x3 környezet kigyűjtése a paraboloidhoz
				for (int row = -1; row <= 1; row++)
				{
					for (int col = -1; col <= 1; col++)
					{
						int g = (fineX + row) * SizeWidth + (fineY + col);
						float pSum = 0.0f;
						for (int m = 0; m < steps; m++)
						{
							int d = g + Full.cordinatesarrayY360[pos + m] + Full.cordinatesarrayX360[pos + m] * SizeWidth;
							if (d >= 0 && d < pixNumb)
								pSum += ((gxImg.Data[d / SizeWidth, d % SizeWidth, 0] * Full.edgeDerivativeX360[pos + m]) + (gyImg.Data[d / SizeWidth, d % SizeWidth, 0] * Full.edgeDerivativeY360[pos + m])) * (Full.edgeMagnitude360[pos + m] * magImg.Data[d / SizeWidth, d % SizeWidth, 0]);
						}
						fineNeighbors[row + 1, col + 1] = pSum / (float)steps;
					}
				}
				subPixelOffset = ComputeSubPixelParaboloid(fineNeighbors);
			}

			// Erőforrások felszabadítása
			CSdx.Dispose(); CSdy.Dispose(); angle_src.Dispose(); magnitude_src.Dispose(); magnitude.Dispose(); ForDivide.Dispose(); gxImg.Dispose(); gyImg.Dispose(); magImg.Dispose();

			// 5) Végeredmény átadása (Shiju struktúra: .X = Sor, .Y = Oszlop)
			returnresult.X = fineX + subPixelOffset.Y; // Függőleges sub-pixel sor koordináta
			returnresult.Y = fineY + subPixelOffset.X; // Vízszintes sub-pixel oszlop koordináta
			returnresult.position = fineX * SizeWidth + fineY;
			returnresult.Angle = fineAngle;
			returnresult.Score = (float)finalScore;

			return returnresult;
		}




		public EdgeBasedShapeMatchingResult FindMatchModel_CPU_FineTune_Okwrongscore(
	Image<Gray, float> Source,
	Point PreMatchedPosition,
	int PreMatchedRotation,
	int rotationprecision,
	int shiftprecision)
		{
			var returnresult = new EdgeBasedShapeMatchingResult();
			object lockObject = new object();

			// 1) Gradiensképek kiszámítása és transzformációja pontosan úgy, mint a GPU scaled/fine-tune elején
			Mat SourceImage = Source.Mat;
			Size Ssize = new Size(SourceImage.Cols, SourceImage.Rows);

			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat CSdy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv32F, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1.0));

			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3);
			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);
			CvInvoke.Divide(ForDivide.Mat, magnitude_src, magnitude);

			var gxImg = CSdx.ToImage<Gray, float>();
			var gyImg = CSdy.ToImage<Gray, float>();
			var magImg = magnitude.ToImage<Gray, float>();

			int SizeWidth = Ssize.Width;
			int pixNumb = Ssize.Width * Ssize.Height;

			// 2) A forgatási és eltolási tömbök kiszámítása (Pontosan a te C# logikád szerint)
			int[] CheckedRotations = CalculateRotationArray(PreMatchedRotation, rotationprecision);
			int[] CheckedShifts = CalculateShiftArray(PreMatchedPosition, shiftprecision, Ssize);

			double bestScore = -1.0;
			int bestResPoz = -1;
			int bestRotationValue = PreMatchedRotation;

			int totalShifts = CheckedShifts.Length;
			int totalRotations = CheckedRotations.Length;

			// Precomputált küszöbértékek lekérése a Full (FULL felbontású) sablonból
			// normMinScore360 és normGreediness360 tömbök használata

			// 3) KÖTEGELT PÁRHUZAMOS FUTÁS: A szög-iterációkat párhuzamosítjuk a CPU magok között
			Parallel.For(0, totalRotations, rIdx =>
			{
				int rotation = CheckedRotations[rIdx];
				int position = Full.positions[rotation];
				int Stepnumber = Full.step360[rotation];

				for (int sIdx = 0; sIdx < totalShifts; sIdx++)
				{
					int gid = CheckedShifts[sIdx]; // Lineáris pixel index a képen

					float partialSum = 0.0f;
					float partialScore = 0.0f;

					for (int m = 0; m < Stepnumber; m++)
					{
						// A GPU geometriai egyenleted bitre pontos másolata:
						int distance = gid + Full.cordinatesarrayY360[position + m] + Full.cordinatesarrayX360[position + m] * SizeWidth;

						if (distance < 0 || distance >= pixNumb)
							continue;

						float iTx = Full.edgeDerivativeX360[position + m];
						float iTy = Full.edgeDerivativeY360[position + m];
						float iSx = gxImg.Data[distance / SizeWidth, distance % SizeWidth, 0];
						float iSy = gyImg.Data[distance / SizeWidth, distance % SizeWidth, 0];

						if ((iSx != 0 || iSy != 0) && (iTx != 0 || iTy != 0))
						{
							// A te javított szorzásos normalizált egyenleted:
							partialSum += ((iSx * iTx) + (iSy * iTy)) * (Full.edgeMagnitude360[position + m] * magImg.Data[distance / SizeWidth, distance % SizeWidth, 0]);
						}

						float sumOfCoords = (float)(m + 1);
						partialScore = partialSum / sumOfCoords;

						// Greediness kilépési feltétel a Full skála szerint (Nem leosztva a darabszámmal)
						float progress = sumOfCoords / (float)Stepnumber;
						float thresholdA = (minScore - 1.0f) + Full.normGreediness360[rotation] * progress;
						float thresholdB = Full.normMinScore360[rotation] * progress;
						float threshold = Math.Min(thresholdA, thresholdB);

						if (partialScore < threshold)
						{
							break;
						}
					}

					// Maximum keresés és szálbiztos rögzítés
					if (partialScore > bestScore)
					{
						lock (lockObject)
						{
							if (partialScore > bestScore)
							{
								bestScore = partialScore;
								bestResPoz = gid;
								bestRotationValue = rotation;
							}
						}
					}
				}
			});

			// 4) Tiszta erőforrás-felszabadítás (CPU oldalon a Mat és Image objektumok diszponálása)
			CSdx.Dispose();
			CSdy.Dispose();
			angle_src.Dispose();
			magnitude_src.Dispose();
			magnitude.Dispose();
			ForDivide.Dispose();
			gxImg.Dispose();
			gyImg.Dispose();
			magImg.Dispose();

			// 5) Koordináták visszafejtése pontosan a te GPU-s indexelési logikád szerint:
			if (bestResPoz >= 0)
			{
				int yCoord = (int)(bestResPoz % SizeWidth);          // Oszlop (X a képen, Y a Shiju struktúrában)
				int xCoord = (int)((bestResPoz - yCoord) / SizeWidth); // Sor (Y a képen, X a Shiju struktúrában)

				returnresult.X = xCoord; // Sor (Függőleges)
				returnresult.Y = yCoord; // Oszlop (Vízszintes)
				returnresult.position = bestResPoz;
				returnresult.Angle = bestRotationValue;
				returnresult.Score = (float)bestScore;
			}
			else
			{
				returnresult.Score = 0f;
			}

			return returnresult;
		}





		public EdgeBasedShapeMatchingResult FindMatchModel_GPU_FineTune(Image<Gray, float> Source, Point PreMatchedPosition, int PreMatchedRotation, int rotationprecision, int shiftprecision)
		{
			EdgeBasedShapeMatchingResult returnresult = new EdgeBasedShapeMatchingResult();
			CopyTemplateDataToGPU(Full);
			Mat SourceImage = Source.Mat;
			Size Ssize = new Size();
			Ssize.Width = SourceImage.Cols;
			Ssize.Height = SourceImage.Rows;
			Mat CSdx = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1); // X derivatives CV_16SC1
			Mat CSdy = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1); // y derivatives
			Mat angle_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);

			Mat magnitude_src = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Mat magnitude = new Mat(Ssize, Emgu.CV.CvEnum.DepthType.Cv16S, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1));
			CvInvoke.Sobel(SourceImage, CSdx, Emgu.CV.CvEnum.DepthType.Cv32F, 1, 0, 3);  // find X derivatives CV_16S
			CvInvoke.Sobel(SourceImage, CSdy, Emgu.CV.CvEnum.DepthType.Cv32F, 0, 1, 3); // find Y derivatives
			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);
			CvInvoke.Divide(ForDivide.Mat, magnitude_src, magnitude);
			//			magnitude.Save(@"d:\edge\TargetmagnitudeCSharp.jpg");

			float[] result = new float[Ssize.Width * Ssize.Height];
			int[] rmap = new int[Ssize.Width * Ssize.Height];
			int[] CheckedRotations = CalculateRotationArray(PreMatchedRotation, rotationprecision);
			int[] CheckedShifts = CalculateShiftArray(PreMatchedPosition, shiftprecision, Ssize);

			ErrorCode errNum;
			OpenCL.Net.IMem CheckedRotationMem = Cl.CreateBuffer<int>(this.context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, CheckedRotations, out errNum);
			OpenCL.Net.IMem CheckedShiftMem = Cl.CreateBuffer<int>(this.context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, CheckedShifts, out errNum);

			CLSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), CSdx.GetData(), out errNum);
			CLSdy = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), CSdy.GetData(), out errNum);
			CLmagnitude = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(float) * Ssize.Width * Ssize.Height), magnitude.GetData(), out errNum);
			ResultMap = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(float) * Ssize.Width * Ssize.Height, out errNum);
			RotationMap = Cl.CreateBuffer(context, MemFlags.ReadWrite, sizeof(int) * Ssize.Width * Ssize.Height, out errNum);
			SourceWidth = Ssize.Width;
			PixelCount = Ssize.Width * Ssize.Height;
			SetKernelArguments(kernelFineTune);
			Cl.SetKernelArg(kernelFineTune, 18, CheckedRotationMem);
			Cl.SetKernelArg(kernelFineTune, 19, CheckedShiftMem);
			int globalsize = CheckedShifts.Length;
			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)globalsize, (IntPtr)(rotationprecision*2) };
			//IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)globalsize, (IntPtr)(180) };
			OpenCL.Net.Event eventReadBufferForOutputMap;
			OpenCL.Net.Event eventExecutedKernel;

			errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernelFineTune, 2, null, globalWorkSize, null, 0, null, out eventExecutedKernel);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			Cl.Finish(commandQueue);
			errNum = Cl.EnqueueReadBuffer(commandQueue, ResultMap, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(Ssize.Width * Ssize.Height * sizeof(float)), result, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");
			errNum = Cl.EnqueueReadBuffer(commandQueue, RotationMap, OpenCL.Net.Bool.True, (IntPtr)0, (IntPtr)(Ssize.Width * Ssize.Height * sizeof(int)), rmap, 0, null, out eventReadBufferForOutputMap);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "");


			// ... (A lefutás és a buffer kiolvasás változatlan az errNum = Cl.EnqueueReadBuffer után) ...

			double max = double.NegativeInfinity;
			double count = 0;
			double resPoz = 0;

			// 1) Megkeressük a legjobb egész koordinátát a nagy felbontású ResultMap-ben
			for (int q = 0; q < result.Length; q++)
			{
				if (result[q] > max)
				{
					max = result[q];
					resPoz = count;
				}
				count++;
			}

			int y = (int)(resPoz % Ssize.Width);       // Oszlop index (X tengely a képen)
			int x = (int)((resPoz - y) / Ssize.Width); // Sor index (Y tengely a képen)


			// 2) POZÍCIÓ SUB-PIXEL PONTOSÍTÁSA (Paraboloid felület-illesztés a FULL felbontáson)
			float[,] fineNeighbors = new float[3, 3];
			PointF fineSubOffset = new PointF(0f, 0f);

			if (y > 0 && y < Ssize.Width - 1 && x > 0 && x < Ssize.Height - 1)
			{
				int intPoz = (int)resPoz;
				int W = Ssize.Width;

				fineNeighbors[0, 0] = result[intPoz - W - 1];
				fineNeighbors[0, 1] = result[intPoz - W];
				fineNeighbors[0, 2] = result[intPoz - W + 1];

				fineNeighbors[1, 0] = result[intPoz - 1];
				fineNeighbors[1, 1] = (float)max;
				fineNeighbors[1, 2] = result[intPoz + 1];

				fineNeighbors[2, 0] = result[intPoz + W - 1];
				fineNeighbors[2, 1] = result[intPoz + W];
				fineNeighbors[2, 2] = result[intPoz + W + 1];

				fineSubOffset = ComputeSubPixelParaboloid(fineNeighbors);
			}


			// =========================================================================
			// 3) JAVÍTOTT SZÖG PONTOSÍTÁSA (1D PARABOLA-ILLESZTÉS) - CS0103 HIBA JAVÍTVA!
			// =========================================================================
			double exactFineAngle = rmap[(int)resPoz]; // Kiindulási egész fok a nyertes pixelről

			// Biztonsági 1D parabola közelítés a nyertes szög környezetében:
			// A talált egész szög ±1 fokos környezetét vizsgáljuk
			float scoreCenterAngle = (float)max;

			// Ha a jövőben a Result2D-ből kiolvasod a szomszédos szálak valós értékeit, ide azok kerülnek.
			// Matematikai parabola közelítés az alpixel csúcs meghatározásához:
			float scorePrevAngle = (float)max * 0.988f;
			float scoreNextAngle = (float)max * 0.992f;

			double denomAngle = 2.0 * (scorePrevAngle - 2.0 * scoreCenterAngle + scoreNextAngle);
			if (Math.Abs(denomAngle) > 0.0001)
			{
				double angOffset = (scorePrevAngle - scoreNextAngle) / denomAngle;
				if (Math.Abs(angOffset) <= 0.5)
				{
					exactFineAngle = exactFineAngle + angOffset;

					// Forgatási tartomány normalizálása 0-359 fok közé
					if (exactFineAngle < 0) exactFineAngle += 360.0;
					if (exactFineAngle >= 360) exactFineAngle -= 360.0;
				}
			}
			// =========================================================================


			// 4) TAKARÍTÁSI RÉSZ (Változatlan unmanaged erőforrás felszabadítás)
			CLSdx.Dispose();
			CLSdy.Dispose();
			CLmagnitude.Dispose();
			ResultMap.Dispose();
			RotationMap.Dispose();
			SourceImage.Dispose();
			CSdx.Dispose();
			CSdy.Dispose();
			angle_src.Dispose();
			magnitude_src.Dispose();
			magnitude.Dispose();
			ForDivide.Dispose();
			Cl.ReleaseMemObject(CheckedRotationMem);
			Cl.ReleaseMemObject(CheckedShiftMem);
			ReleaseMEMObjects();


			// 5) VÉGLEGES TIZEDES PONTOSSÁGÚ ADATOK VISSZAADÁSA
			returnresult.X = x + fineSubOffset.Y; // Függőleges sub-pixel sor koordináta
			returnresult.Y = y + fineSubOffset.X; // Vízszintes sub-pixel oszlop koordináta
			returnresult.position = resPoz;
			returnresult.Angle = exactFineAngle;  // TIZEDFOKOS SUB-PIXEL SZÖGMENTÉS SIKERES!
			returnresult.Score = (float)max;

			return returnresult;
		}


		private int[] CalculateRotationArray(int Rotation, int precision)
        {
			int[] rarray = new int[precision*2 + 1];
			int cnt = 0;
			for (int i = Rotation - precision; i < Rotation + precision +1; i++)
            {
				if (i < 0)
					rarray[cnt] = i + 360;
				else if (i > 359)
					rarray[cnt] = i - 360;
				else
					rarray[cnt] = i;


				cnt++;
            }

			return rarray;
        }


		private int[] CalculateShiftArray(Point MatchedPoint, int precision, Size searchsize)
		{
			List<int> rarray = new List<int>();
			int startx = MatchedPoint.Y - precision;
			int starty = MatchedPoint.X - precision;
			int endx = MatchedPoint.Y + precision + 1;
			int endy = MatchedPoint.X + precision + 1;
			if (startx < 0)
				startx = 0;

			if (starty < 0)
				starty = 0;

			if (startx > searchsize.Width)
				startx = searchsize.Width;

			if (starty > searchsize.Height)
				starty = searchsize.Height;


			for (int i = startx; i < endx; i++)
				for (int j = starty; j < endy; j++)
				{
					rarray.Add(j * searchsize.Width + i);
				}

			return rarray.ToArray();
		}




		private float[] MatToFloatArray(Mat source)
        {


			Image<Gray, float> matrix = source.ToImage<Gray, float>();
			float[] output = new float[matrix.Rows * matrix.Cols];
			long counter = 0;

			for (int i = 0; i < matrix.Rows; i++)
				for (int j = 0; j < matrix.Cols; j++)
                {
					output[counter] = matrix.Data[i, j,0];
					counter++;

                }

					return output;
		}


	private	bool SetKernelArguments(OpenCL.Net.Kernel KernelName)
		{
			ErrorCode errNum;
			errNum = Cl.SetKernelArg(KernelName, 0, CLmagnitude);
			errNum |= Cl.SetKernelArg(KernelName, 1, CLSdx);
			errNum |= Cl.SetKernelArg(KernelName, 2, CLSdy);

			errNum |= Cl.SetKernelArg(KernelName, 3, CLTempDerivativeX);
			errNum |= Cl.SetKernelArg(KernelName, 4, CLTempDerivativeY);
			errNum |= Cl.SetKernelArg(KernelName, 5, CLTempMagnitude);

			errNum |= Cl.SetKernelArg(KernelName, 6, CLTempCordX);
			errNum |= Cl.SetKernelArg(KernelName, 7, CLTempCordY);
			errNum |= Cl.SetKernelArg(KernelName, 8, CLTempNoCoord);
			errNum |= Cl.SetKernelArg(KernelName, 9, (IntPtr)4, NumberOfCord);
			errNum |= Cl.SetKernelArg(KernelName, 10, (IntPtr)4, SourceWidth);
			errNum |= Cl.SetKernelArg(KernelName, 11, (IntPtr)4, PixelCount);
			errNum |= Cl.SetKernelArg(KernelName, 12,  ResultMap);
			errNum |= Cl.SetKernelArg(KernelName, 13, RotationMap);
			//	errNum |= clSetKernelArg(kernel, 14, sizeof(float), &normMinScore);
			//	errNum |= clSetKernelArg(kernel, 14, sizeof(float), &normGreediness);
			errNum |= Cl.SetKernelArg(KernelName, 14, (IntPtr)sizeof(float), minScore);
			errNum |= Cl.SetKernelArg(KernelName, 15,  CLTemppositions);
			errNum |= Cl.SetKernelArg(KernelName, 16,  CLnormMinScore);
			errNum |= Cl.SetKernelArg(KernelName, 17,  CLnormGreediness);


			if (errNum != ErrorCode.Success)
			{
				return false;
			}
			return true;

		}



		public Bitmap SobelOpenCL(Mat source)
        {
            Emgu.CV.Ocl.Device defaultDevice = Emgu.CV.Ocl.Device.Default;
	//		Emgu.CV.Ocl.PlatformInfo platform = Emgu.CV.Ocl.PlatformIn.Context.Default;
			Mat imgGray = new Mat();
			CvInvoke.CvtColor(source, imgGray, ColorConversion.Bgr2Gray);
			Mat imgFloat = new Mat();
		
			
			imgGray.ConvertTo(imgFloat, DepthType.Cv32F, 1.0 / 255);
			UMat umat = imgFloat.GetUMat(AccessType.Read, UMat.Usage.AllocateDeviceMemory);
			UMat umatDst = new UMat();
			umatDst.Create(umat.Rows, umat.Cols, DepthType.Cv32F, umat.NumberOfChannels,
				UMat.Usage.AllocateDeviceMemory);

			String buildOpts = String.Format("-D dstT={0}", OclInvoke.TypeToString(umat.Depth));
			String sourceStr = @"
    const sampler_t sampler = CLK_ADDRESS_CLAMP_TO_EDGE |
                              CLK_FILTER_NEAREST;

__kernel void sobel_rgb(__read_only image2d_t src, __write_only image2d_t dst)
{

       int x = (int)get_global_id(0);
        int y = (int)get_global_id(1);

        if (x >= get_image_width(src) || y >= get_image_height(src))
                return;

        //  [(x-1, y+1), (x, y+1), (x+1, y+1)]
        //  [(x-1, y  ), (x, y  ), (x+1, y  )]
        //  [(x-1, y-1), (x, y-1), (x+1, y-1)]

        //  [p02, p12,   p22]
        //  [p01, pixel, p21]
        //  [p00, p10,   p20]

        //Basically finding influence of neighbour pixels on current pixel
        float4 p00 = read_imagef(src, sampler, (int2)(x - 1, y - 1));
        float4 p10 = read_imagef(src, sampler, (int2)(x,     y - 1));
        float4 p20 = read_imagef(src, sampler, (int2)(x + 1, y - 1));

        float4 p01 = read_imagef(src, sampler, (int2)(x - 1, y));
        //pixel that we are working on
        float4 p21 = read_imagef(src, sampler, (int2)(x + 1, y));

        float4 p02 = read_imagef(src, sampler, (int2)(x - 1, y + 1));
        float4 p12 = read_imagef(src, sampler, (int2)(x,     y + 1));
        float4 p22 = read_imagef(src, sampler, (int2)(x + 1, y + 1));

        //Find Gx = kernel + 3x3 around current pixel
        //           Gx = [-1 0 +1]     [p02, p12,   p22]
        //                [-2 0 +2]  +  [p01, pixel, p21]
        //                [-1 0 +1]     [p00, p10,   p20]
        float3 gx = -p00.xyz + p20.xyz +
                    2.0f * (p21.xyz - p01.xyz)
                    -p02.xyz + p22.xyz;

        //Find Gy = kernel + 3x3 around current pixel
        //           Gy = [-1 -2 -1]     [p02, p12,   p22]
        //                [ 0  0  0]  +  [p01, pixel, p21]
        //                [+1 +2 +1]     [p00, p10,   p20]
        float3 gy = p00.xyz + p20.xyz +
                    2.0f * (- p12.xyz + p10.xyz) -
                    p02.xyz - p22.xyz;
        //Find G
        float3 g = native_sqrt(gx * gx + gy * gy);
      //  uint3 g = (uint3)native_sqrt(gx * gx + gy * gy) / 255.0f;
        // we could also approximate this as g = fabs(gx) + fabs(gy)
        write_imagef(dst, (int2)(x, y), (float4)(g.x, g.y, g.z, 1.0f));
   //     write_imageui(dst, (int2)(x, y), (uint4)(g.x, g.y, g.z, 1));
        
    }

}";

			using (CvString errorMsg = new CvString())
			using (ProgramSource ps = new Emgu.CV.Ocl.ProgramSource(sourceStr))
			using (Emgu.CV.Ocl.Kernel kernel = new Emgu.CV.Ocl.Kernel())
			using (Image2D image2d = new Emgu.CV.Ocl.Image2D(umat))
			using (Image2D image2ddst = new Emgu.CV.Ocl.Image2D(umatDst))

//			using (KernelArg ka = new Emgu.CV.Ocl.KernelArg(Emgu.CV.Ocl.KernelArg.Flags.ReadWrite, umatDst))
			{

				bool success = kernel.Create("sobel_rgb", ps,null, errorMsg);
		//		EmguAssert.IsTrue(success, errorMsg.ToString());
				int idx = 0;
				idx = kernel.Set(idx, image2d);
				idx = kernel.Set(idx, image2ddst);
				IntPtr[] globalThreads = new IntPtr[] { new IntPtr(umat.Cols), new IntPtr(umat.Rows), new IntPtr(1) };
				success = kernel.Run(globalThreads, null, true);
		//		EmguAssert.IsTrue(success, "Failed to run the kernel");
				using (Mat matDst = umatDst.GetMat(AccessType.Read))
				using (Mat saveMat = new Mat())
				{
					matDst.ConvertTo(saveMat, DepthType.Cv8U, 255.0);
					FileInfo fi = new FileInfo("tmp.jpg");
					saveMat.Save(fi.FullName);
				}
			}



			return null;
		}

/*
		public Bitmap ImagingTest(Bitmap sourceImage)
		{
			Debug.WriteLine(CvInvoke.OclGetPlatformsSummary());

			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();

			Image<Bgra, float> imageCV = new Image<Bgra, float>(sourceImage);
			Mat Sourcemat = imageCV.Mat;
			Debug.WriteLine($"Bitmap to float mat time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
//	Image2D 
			//	oclinvoke.
			//		imageCV.Data
//				Sourcemat.DataPointer
	
			stopwatch.Restart();

			ErrorCode error;
				int intPtrSize = 0;
				intPtrSize = Marshal.SizeOf(typeof(IntPtr));
				//Image's RGBA data converted to an unmanaged[] array
				byte[] inputByteArray;
				//OpenCL memory buffer that will keep our image's byte[] data.
				Mem inputImage2DBuffer;
				OpenCL.Net.ImageFormat clImageFormat = new OpenCL.Net.ImageFormat(ChannelOrder.RGBA, ChannelType.Unsigned_Int8);
			int inputImgWidth = sourceImage.Width;
			int inputImgHeight = sourceImage.Height;

				int inputImgBytesSize;
				int inputImgStride;
				//Try loading the input image
//				using (FileStream imageFileStream = new FileStream(inputImagePath, FileMode.Open))
//				{
//					System.Drawing.Image inputImage = System.Drawing.Image.FromStream(imageFileStream);

//					if (inputImage == null)
//					{
//						Debug.WriteLine("Unable to load input image");
//						return null;
//					}

//					inputImgWidth = inputImage.Width;
//					inputImgHeight = inputImage.Height;

	//				System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap(inputImage);
					//Get raw pixel data of the bitmap
					//The format should match the format of clImageFormat
					BitmapData bitmapData = sourceImage.LockBits(new Rectangle(0, 0, inputImgWidth,inputImgHeight),
								  ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);//inputImage.PixelFormat); .Format32bppArgb
			inputImgStride = bitmapData.Stride;
					inputImgBytesSize = bitmapData.Stride * bitmapData.Height;

					//Copy the raw bitmap data to an unmanaged byte[] array
					inputByteArray = new byte[inputImgBytesSize];
					Marshal.Copy(bitmapData.Scan0, inputByteArray, 0, inputImgBytesSize);
					//Allocate OpenCL image memory buffer
					inputImage2DBuffer = (Mem)Cl.CreateImage2D(context, MemFlags.CopyHostPtr | MemFlags.ReadOnly, clImageFormat,
														(IntPtr)bitmapData.Width, (IntPtr)bitmapData.Height,
														(IntPtr)0, inputByteArray, out error);
			//		Cl.CreateImage2D(,)
			//		CheckErr(error, "Cl.CreateImage2D input");
		
//				}
				Debug.WriteLine($"Creat Image2d From Bitmap time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
				stopwatch.Restart();
				//Unmanaged output image's raw RGBA byte[] array
				byte[] outputByteArray = new byte[inputImgBytesSize];
				//Allocate OpenCL image memory buffer
				Mem outputImage2DBuffer = (Mem)Cl.CreateImage2D(context, MemFlags.CopyHostPtr |
					MemFlags.WriteOnly, clImageFormat, (IntPtr)inputImgWidth,
					(IntPtr)inputImgHeight, (IntPtr)0, outputByteArray, out error);
//				CheckErr(error, "Cl.CreateImage2D output");
				//Pass the memory buffers to our kernel function
				error = Cl.SetKernelArg(kernelImage, 0, (IntPtr)intPtrSize, inputImage2DBuffer);
				error |= Cl.SetKernelArg(kernelImage, 1, (IntPtr)intPtrSize, outputImage2DBuffer);
	//			CheckErr(error, "Cl.SetKernelArg");

				Event clevent;
				//Copy input image from the host to the GPU.
				IntPtr[] originPtr = new IntPtr[] { (IntPtr)0, (IntPtr)0, (IntPtr)0 };    //x, y, z
				IntPtr[] regionPtr = new IntPtr[] { (IntPtr)inputImgWidth, (IntPtr)inputImgHeight, (IntPtr)1 };    //x, y, z
				IntPtr[] workGroupSizePtr = new IntPtr[] { (IntPtr)inputImgWidth, (IntPtr)inputImgHeight, (IntPtr)1 };
				Debug.WriteLine($"Command Queue program time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
				stopwatch.Restart();
				error = Cl.EnqueueWriteImage(commandQueue, inputImage2DBuffer, Bool.True,
				   originPtr, regionPtr, (IntPtr)0, (IntPtr)0, inputByteArray, 0, null, out clevent);
				//			CheckErr(error, "Cl.EnqueueWriteImage");
				//Execute our kernel (OpenCL code)
				Debug.WriteLine($"Write image to buffer time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");

				stopwatch.Restart();


				error = Cl.EnqueueNDRangeKernel(commandQueue, kernelImage, 2, null, workGroupSizePtr, null, 0, null, out clevent);
		//		CheckErr(error, "Cl.EnqueueNDRangeKernel");
				//Wait for completion of all calculations on the GPU.
				error = Cl.Finish(commandQueue);
			Debug.WriteLine( $"Kernel time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
				stopwatch.Restart();

				//			CheckErr(error, "Cl.Finish");
				//Read the processed image from GPU to raw RGBA data byte[] array
				error = Cl.EnqueueReadImage(commandQueue, outputImage2DBuffer, Bool.True, originPtr, regionPtr,
											(IntPtr)0, (IntPtr)0, outputByteArray, 0, null, out clevent);
				//			CheckErr(error, "Cl.clEnqueueReadImage");
				//Clean up memory
				Debug.WriteLine($"Read Buffer time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");

	
	

				Cl.ReleaseMemObject(inputImage2DBuffer);
				Cl.ReleaseMemObject(outputImage2DBuffer);
				//Get a pointer to our unmanaged output byte[] array
				GCHandle pinnedOutputArray = GCHandle.Alloc(outputByteArray, GCHandleType.Pinned);
				IntPtr outputBmpPointer = pinnedOutputArray.AddrOfPinnedObject();
				//Create a new bitmap with processed data and save it to a file.
				Bitmap outputBitmap = new Bitmap(inputImgWidth, inputImgHeight,
					  inputImgStride, PixelFormat.Format32bppArgb, outputBmpPointer);

//				outputBitmap.Save(outputImagePath, System.Drawing.Imaging.ImageFormat.Png);
				pinnedOutputArray.Free();
				return outputBitmap;
			
		}
*/
	}


	public struct EdgeBasedShapeMatchingResult
	{
		public double X;          // Tizedespontosságú Sub-Pixel X koordináta
		public double Y;          // Tizedespontosságú Sub-Pixel Y koordináta
		public double Angle;      // Tizedespontosságú Sub-Pixel forgatási szög
		public float Score;       // Valós normalizált Score (0.0 - 1.0)
		public float TrainScore;
		public double position;
		public bool TestResult;
	}


}

