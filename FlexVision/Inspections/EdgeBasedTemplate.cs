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
		//		private OpenCL.Net.Kernel kernelNoRot;
		private OpenCL.Net.Kernel kernelFineTune;
		//		private OpenCL.Net.Kernel SobelMagnitude;
		//		private OpenCL.Net.Kernel DivMagnitude;
		//		private OpenCL.Net.Kernel MatchSobel;
		//		private OpenCL.Net.Kernel RotatedTemplateSobel;
		private OpenCL.Net.Kernel initKernel2D;
		private OpenCL.Net.Kernel reduceKernel;

		//		private OpenCL.Net.Program programImage;
		//		private OpenCL.Net.Kernel kernelImage;

		//		public int FoundRotate = 0;
		//	public int finetunerotate = 7;
		public float lowThreshold = 90;     //default value
		public float highThreshold = 255;    //default value
		public int minEdgepoints = 50;      //50
		public float minScore = 0.7F;     //default value
		public float greediness = 0.8F;   //default value
		public Point RotatePoint = new Point(0, 0);
		public float scale = 1;
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
    __global const int*   CheckedRotations,
    int                   rotationsCount,
    __global const int*   CheckedShifts,
    int                   shiftsCount,
    __global float*       shiftBestScores,
    __global int*         shiftBestRotIndices,
    __global float*       shiftPrevRotScores,
    __global float*       shiftNextRotScores
){
    int shiftIdx = get_global_id(0);
    if (shiftIdx < 0 || shiftIdx >= shiftsCount) return;

    int gid = CheckedShifts[shiftIdx];
    if (gid < 0 || gid >= pixNumb) {
        shiftBestScores[shiftIdx] = 0.0f;
        shiftBestRotIndices[shiftIdx] = 0;
        shiftPrevRotScores[shiftIdx] = 0.0f;
        shiftNextRotScores[shiftIdx] = 0.0f;
        return;
    }

    float bestScoreForPixel = -1.0f;
    int bestRotIdxForPixel = 0;

    // Lokális tömb a szög-score-ok tárolásához (max 64 szög támogatása a sub-pixel parabola illesztéshez)
    float localRotScores[64];
    int safeRotCount = rotationsCount < 64 ? rotationsCount : 64;

    for (int r = 0; r < safeRotCount; r++)
    {
        int rotation = CheckedRotations[r];
        int position = Rotatepositions[rotation];
        int stepnumber = TemplateCoordinatesNumber[rotation];

        float partialSum = 0.0f;

        for (int m = 0; m < stepnumber; m++)
        {
            int tx = TemplateCoordX[position + m];
            int ty = TemplateCoordY[position + m];
            int distance = gid + ty + tx * SizeWidth;

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
        }

        float finalScore = (stepnumber > 0) ? (partialSum / (float)stepnumber) : 0.0f;
        if (finalScore < 0.0f || isinf(finalScore) || isnan(finalScore)) finalScore = 0.0f;
        if (finalScore > 1.0f) finalScore = 1.0f;

        localRotScores[r] = finalScore;

        if (finalScore > bestScoreForPixel)
        {
            bestScoreForPixel = finalScore;
            bestRotIdxForPixel = r;
        }
    }

    shiftBestScores[shiftIdx] = bestScoreForPixel > 0.0f ? bestScoreForPixel : 0.0f;
    shiftBestRotIndices[shiftIdx] = bestRotIdxForPixel;

    // Szomszédos szögek score-jainak mentése a 1D parabola illesztéshez
    int prevIdx = (bestRotIdxForPixel > 0) ? (bestRotIdxForPixel - 1) : bestRotIdxForPixel;
    int nextIdx = (bestRotIdxForPixel < safeRotCount - 1) ? (bestRotIdxForPixel + 1) : bestRotIdxForPixel;
    shiftPrevRotScores[shiftIdx] = localRotScores[prevIdx];
    shiftNextRotScores[shiftIdx] = localRotScores[nextIdx];
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
			//			Cl.ReleaseKernel(kernelNoRot);
			Cl.ReleaseKernel(kernelFineTune);
			//			Cl.ReleaseKernel(SobelMagnitude);
			//			Cl.ReleaseKernel(DivMagnitude);
			//			Cl.ReleaseKernel(MatchSobel);
			//			Cl.ReleaseKernel(RotatedTemplateSobel);
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


		public SizeF GetTemplateSize()
		{

			return new SizeF(ModelsFullScale[0].EdgeWith, ModelsFullScale[0].EdgeHeight);
		}

		public void SaveModel(string filename)
		{
			using (System.IO.StreamWriter file =
				  new System.IO.StreamWriter(filename))
			{
				file.WriteLine(Full.edgeMagnitude360.Length + ";" + scale);

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

			initKernel2D = Cl.CreateKernel(program, "init_maps_2d", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");


			reduceKernel = Cl.CreateKernel(program, "reduce_best_rotation", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");

			//		kernelNoRot = Cl.CreateKernel(program, "edge_template_norot", out errorCode);
			//		ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");
			kernelFineTune = Cl.CreateKernel(program, "edge_match_finetune", out errorCode);
			ComputeContext.throwErrorIfNotSuccessfull(errorCode, "");





		}







		public void CreateRotatedTemplates(Image<Gray, float> templateArr, int rotationrange = 180)
		{
			Models[0] = CreateModel(templateArr, scale, 0);
			ModelsFullScale[0] = CreateModel(templateArr, 1, 0);

			// SZINKRONIZÁCIÓ: Visszaállítjuk a stabil 360-a alapú képi forgatást és indexelést
			Parallel.For(1, 360, i =>
			{
				Models[i] = Models[0].GetRotatedModel(i);
				ModelsFullScale[i] = ModelsFullScale[0].GetRotatedModel(i);
			});



			// Ipari memóriafelszabadítás a fagyások ellen
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}








		private EdgeBasedModelInfo360 Create1DarrayForGPU(EdgeBasedModelInfo[] modelinfos)
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



			return result360;
		}



		private float CalculatePyramidLevel(Image<Gray, float> templateArr)
		{
			EdgeBasedModelInfo Model = new EdgeBasedModelInfo(); ;
			float Scalefactor = 1;
			ModelsFullScale[0] = CreateModel(templateArr, 1, 0);
			RotatePoint = new Point(ModelsFullScale[0].centerOfGravity.X, ModelsFullScale[0].centerOfGravity.Y);

			Debug.WriteLine("FullScale Coordinates Number: " + ModelsFullScale[0].noOfCordinates);
			int Edgepoints = ModelsFullScale[0].noOfCordinates / 32;
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











		public void TrainTemplate(Image<Gray, float> templateArr)
		{
			//	Magnitude(templateArr);


			this.scale = CalculatePyramidLevel(templateArr);
			//			this.scale = 1;
			Debug.WriteLine("Scale factor: " + this.scale);

			//		CreateRotatedTemplates(AutoArea(templateArr));
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			CreateRotatedTemplates(templateArr);
			Debug.WriteLine($"Create Rotated Templates time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");
			stopwatch.Restart();
			Scaled = Create1DarrayForGPU(Models);
			Full = Create1DarrayForGPU(ModelsFullScale);
			CopyTemplateDataToGPU(Scaled);


			/*
						List<int> debugAngles = new List<int>();
						// Kigyűjtjük a vizsgálni kívánt szögeket (pl. -10 és +10 fok között)
						for (int a = -AngleDetectionRangeSimmetric; a <= AngleDetectionRangeSimmetric; a++)
						{
							int v = a < 0 ? a + 360 : a;
							debugAngles.Add(v);
						}
			
			// Elmentjük a Scaled (GPU-ra küldött kicsinyített) sablonok éleit grafikusan!
			SaveTemplateGradientsToImages(Scaled, @"C:\Test\Templates\", debugAngles.ToArray(), templateArr.Width, templateArr.Height);
			*/
			Debug.WriteLine($"Create 1D array time: {(double)stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency} ms");


		}





		public void CopyTemplateDataToGPU(EdgeBasedModelInfo360 modelinfo360)
		{
			ErrorCode errorCode;

			CLTempCordX = Cl.CreateBuffer<int>(this.context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
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
					//		Debug.WriteLine(direction);
					// get closest angle from 0, 45, 90, 135 set
					direction = (float)(Angle.Data[i, j, 0]);
					MagG = mag.Data[i, j, 0];

					// JAVÍTÁS: >= és <= jelek használata, hogy a tökéletes rácsirányok (0, 90, 180 fok) se essenek ki!
					if ((direction >= 0F && direction < 0.3926) || (direction > 2.7488 && direction <= 3.5342) || (direction > 5.8904 && direction <= 6.283))
					{
						leftPixel = mag.Data[i, j - 1, 0];
						rightPixel = mag.Data[i, j + 1, 0];
					}
					else if ((direction >= 0.3926 && direction < 1.178) || (direction > 3.534 && direction <= 4.319))
					{
						leftPixel = mag.Data[i - 1, j + 1, 0];
						rightPixel = mag.Data[i + 1, j - 1, 0];
					}
					else if ((direction >= 1.178 && direction <= 1.963) || (direction > 4.319 && direction <= 5.105))
					{
						// Ez a vízszintes falak ága (90 fok környéke) -> itt FÜGGŐLEGESEN kell ellenőrizni!
						leftPixel = mag.Data[i - 1, j, 0];
						rightPixel = mag.Data[i + 1, j, 0];
					}
					else if ((direction > 1.963 && direction < 2.7488) || (direction > 5.105 && direction < 5.890))
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
			TempModelinfo.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
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
				Resized = TemplateImage.Resize(scale, Inter.Area);
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
			float MaxGradient = (float)maxVal;
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
					direction = (float)(Angle.Data[i, j, 0]);
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
					else if ((direction > 1.963 && direction < 2.7488) || (direction > 5.105 && direction < 5.890))
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









		public bool DoesRectangleContainPoint(RotatedRect rectangle, PointF point)
		{


			PointF[] corners = rectangle.GetVertices();

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

			Console.WriteLine("RoughResults: " + roughResults.Count);
			//			Console.WriteLine("FSzm Rotation:" + AngleDetectionRangeSimmetric);
			//					return roughResults;
			List<EdgeBasedShapeMatchingResult> finalResults = new List<EdgeBasedShapeMatchingResult>();
			//			Console.WriteLine("Found objects:" + roughResults.Count);
			// 2) Végigmegyünk a talált objektumokon, és egy szűkített ablakban pontosítunk a FULL képen
			foreach (var roughResult in roughResults)
			{
				// Ha a durva egyezés túl alacsony (zaj), átugorjuk
				if (roughResult.Score < 0.15f) continue;

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
				Console.WriteLine($"SCALED -> X:{roughResult.X:F2}, Y:{roughResult.Y:F2}, Ang:{roughResult.Angle:F2}, Score:{roughResult.Score:F4}");
				Console.WriteLine($"FINE   -> X:{fineTuned.X:F2}, Y:{fineTuned.Y:F2}, Ang:{fineTuned.Angle:F2}, Score:{fineTuned.Score:F4}");

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









		public List<EdgeBasedShapeMatchingResult> FindMatchModel_GPU_Scaled(Image<Gray, float> Source)
		{
			var returnresult = new List<EdgeBasedShapeMatchingResult>();

			// 1) Template adatok GPU-ra
			CopyTemplateDataToGPU(Scaled);

			// 2) Emgu CV előkészítés: minden CV_32F (float)
			Mat SourceImage = Source.Resize(scale, Emgu.CV.CvEnum.Inter.Area).Mat;
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

			// =========================================================================
			// 🔥 IPARI TUNING: GRADIENS TOLERANCIA BEVEZETÉSE (Él-elmosás)
			// Egy finom 3x3-as GaussianBlur-rel az élek intenzitását picit szétterítjük.
			// Így a rendszer toleráns lesz az 1-2 pixeles méretbeli pontatlanságokra.
			// =========================================================================
			Mat blurredMagnitude = new Mat();
			CvInvoke.GaussianBlur(magnitude, blurredMagnitude, new Size(3, 3), 1.0, 1.0);

			// A GPU-ra küldendő magImage-et a simított képből példányosítjuk
			var magImage = blurredMagnitude.ToImage<Gray, float>();
			// =========================================================================

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

			// JAVÍTÁS: A nyers, merev kép helyett a toleranciával kiszélesített magImage.Data-t küldjük!
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

			// A lokális ideiglenes mátrixot felszabadítjuk a bufferelés után
			blurredMagnitude.Dispose();

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


			// Kigyűjtjük CSAK a kért szögtartomány indexeit az átfordulások biztonságos kezelésével
			int rotationsCount = 360 / rotm; // Fixen 360 szál az Accuracy/Balanced/Speed módokhoz
			int rotdiv = rotationsCount;

			// Kigyűjtjük a kért szögtartomány indexeit az átfordulások biztonságos kezelésével
			int[] customRotatePositions = new int[rotationsCount];
			int writeIdx = 0;
			for (int a = -AngleDetectionRangeSimmetric; a <= AngleDetectionRangeSimmetric; a += rotm)
			{
				if (writeIdx >= rotationsCount) break;

				int validAngle = a;
				if (validAngle < 0) validAngle += 360;
				if (validAngle >= 360) validAngle -= 360;

				// JAVÍTÁS: Visszatesszük a régi kód 360 - validAngle leképezését a fix szinkronhoz
				int arrayIndex = 360 - validAngle;
				if (arrayIndex >= 360) arrayIndex -= 360;

				customRotatePositions[writeIdx] = Scaled.positions[arrayIndex];
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

			/*

			float[] bestScorePerAngle = new float[360];
			int[] bestPosPerAngle = new int[360];

			for (int i = 0; i < 360; i++)
			{
				bestScorePerAngle[i] = float.NegativeInfinity;
				bestPosPerAngle[i] = -1;
			}

			for (int idx = 0; idx < result.Length; idx++)
			{
				int innerAngleIdx = rmap[idx];

				int calculatedAngle =
					-AngleDetectionRangeSimmetric +
					(innerAngleIdx * rotm);

				if (calculatedAngle < 0) calculatedAngle += 360;
				if (calculatedAngle >= 360) calculatedAngle -= 360;

				int realAngle = 360 - calculatedAngle;

				if (realAngle >= 360) realAngle -= 360;
				if (realAngle < 0) realAngle += 360;

				if (result[idx] > bestScorePerAngle[realAngle])
				{
					bestScorePerAngle[realAngle] = result[idx];
					bestPosPerAngle[realAngle] = idx;
				}
			}

			var top =
Enumerable.Range(0, 360)
	  .OrderByDescending(a =>
			bestScorePerAngle[a])
	  .Take(20);

			foreach (var a in top)
			{
				Console.WriteLine(
					$"Angle={a}, Score={bestScorePerAngle[a]:F6}");
			}

			*/

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
				if (bestIdx < 0 || maxVal < (minScore * 0.5f)) break;
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
				// SZÖG VISSZAFEJTÉS JAVÍTÁSA: 
				int innerAngleIdx = rmap[bestIdx]; // A GPU által visszaadott nyertes szál sorszáma
				int calculatedAngle = -AngleDetectionRangeSimmetric + (innerAngleIdx * rotm);

				if (calculatedAngle < 0) calculatedAngle += 360;
				if (calculatedAngle >= 360) calculatedAngle -= 360;

				// FIX: Mivel csökkenő rács-sorrendben ment fel a GPU-ra, a valós szög a visszatükrözött érték lesz!
				int realAngle = 360 - calculatedAngle;
				if (realAngle >= 360) realAngle -= 360;
				if (realAngle < 0) realAngle += 360;

				var r = new EdgeBasedShapeMatchingResult
				{
					X = finalSubPixelRow,
					Y = finalSubPixelCol,
					position = bestIdx,
					Angle = realAngle, // Tűpontos, ugrálásmentes, folyamatos mérés a teljes 360 fokon!
					Score = (float)maxVal
				};
				returnresult.Add(r);

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









		public EdgeBasedShapeMatchingResult FindMatchModel_GPU_FineTune(Image<Gray, float> Source, Point PreMatchedPosition, int PreMatchedRotation, int rotationprecision, int shiftprecision)
		{
			EdgeBasedShapeMatchingResult returnresult = new EdgeBasedShapeMatchingResult();
			CopyTemplateDataToGPU(Full);

			Mat SourceImage = Source.Mat;
			Size Ssize = new Size(SourceImage.Cols, SourceImage.Rows);

			// 1) Kép simítása a Sobel előtt, hogy a gradiensvektorok (CSdx, CSdy) is tolerálják az 1-2 pixeles kerekítést
			Mat smoothedSource = new Mat();
			CvInvoke.GaussianBlur(SourceImage, smoothedSource, new Size(5, 5), 1.2, 1.2);

			Mat CSdx = new Mat(Ssize, DepthType.Cv32F, 1);
			Mat CSdy = new Mat(Ssize, DepthType.Cv32F, 1);
			Mat angle_src = new Mat(Ssize, DepthType.Cv32F, 1);
			Mat magnitude_src = new Mat(Ssize, DepthType.Cv32F, 1);
			Mat blurredMagSrc = new Mat(Ssize, DepthType.Cv32F, 1);
			Mat magnitude = new Mat(Ssize, DepthType.Cv32F, 1);
			Image<Gray, float> ForDivide = new Image<Gray, float>(Ssize.Width, Ssize.Height, new Gray(1.0));

			CvInvoke.Sobel(smoothedSource, CSdx, DepthType.Cv32F, 1, 0, 3);
			CvInvoke.Sobel(smoothedSource, CSdy, DepthType.Cv32F, 0, 1, 3);
			CvInvoke.CartToPolar(CSdx, CSdy, magnitude_src, angle_src);

			// 2) A magnitúdó elmosása AZ OSZTÁS ELŐTT (így az él melletti 1 pixelen is stabil marad a sMag, de nem száll el a végtelenbe!)
			CvInvoke.GaussianBlur(magnitude_src, blurredMagSrc, new Size(5, 5), 1.2, 1.2);

			// Biztonsági küszöb, hogy a háttérzaj (0 közeli magnitúdó) ne okozzon hatalmas 1/x értéket
			CvInvoke.Max(blurredMagSrc, new ScalarArray(1.0), blurredMagSrc);
			CvInvoke.Divide(ForDivide.Mat, blurredMagSrc, magnitude);

			var imgX = CSdx.ToImage<Gray, float>();
			var imgY = CSdy.ToImage<Gray, float>();
			var imgMag = magnitude.ToImage<Gray, float>();
			int W = Ssize.Width;
			int H = Ssize.Height;
			int pixelCount = W * H;

			// 2) Szögek és eltolások előkészítése (a Scaled keresővel azonos tükrözött indexeléssel!)
			int[] realAngles;
			int[] checkedRotations = CalculateRotationArray(PreMatchedRotation, rotationprecision, out realAngles);
			int[] checkedShifts = CalculateShiftArray(PreMatchedPosition, shiftprecision, Ssize);

			int shiftsCount = checkedShifts.Length;
			int rotationsCount = checkedRotations.Length;

			if (shiftsCount == 0 || rotationsCount == 0)
			{
				smoothedSource.Dispose();
				CSdx.Dispose(); CSdy.Dispose(); angle_src.Dispose(); magnitude_src.Dispose(); magnitude.Dispose(); ForDivide.Dispose();
				imgX.Dispose(); imgY.Dispose(); imgMag.Dispose();
				ReleaseMEMObjects();
				return returnresult;
			}

			ErrorCode errNum;
			IntPtr imgBufferSize = (IntPtr)(sizeof(float) * pixelCount);

			IMem clSdx = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, imgBufferSize, imgX.Data, out errNum);
			IMem clSdy = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, imgBufferSize, imgY.Data, out errNum);
			IMem clMag = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, imgBufferSize, imgMag.Data, out errNum);

			IMem clCheckedRotations = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(int) * rotationsCount), checkedRotations, out errNum);
			IMem clCheckedShifts = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(sizeof(int) * shiftsCount), checkedShifts, out errNum);

			IMem clShiftScores = Cl.CreateBuffer(context, MemFlags.WriteOnly, (IntPtr)(sizeof(float) * shiftsCount), out errNum);
			IMem clShiftRotIndices = Cl.CreateBuffer(context, MemFlags.WriteOnly, (IntPtr)(sizeof(int) * shiftsCount), out errNum);
			IMem clShiftPrevRotScores = Cl.CreateBuffer(context, MemFlags.WriteOnly, (IntPtr)(sizeof(float) * shiftsCount), out errNum);
			IMem clShiftNextRotScores = Cl.CreateBuffer(context, MemFlags.WriteOnly, (IntPtr)(sizeof(float) * shiftsCount), out errNum);

			// 3) Kernel argumentumok beállítása
			errNum = Cl.SetKernelArg(kernelFineTune, 0, clMag);
			errNum |= Cl.SetKernelArg(kernelFineTune, 1, clSdx);
			errNum |= Cl.SetKernelArg(kernelFineTune, 2, clSdy);
			errNum |= Cl.SetKernelArg(kernelFineTune, 3, CLTempDerivativeX);
			errNum |= Cl.SetKernelArg(kernelFineTune, 4, CLTempDerivativeY);
			errNum |= Cl.SetKernelArg(kernelFineTune, 5, CLTempMagnitude);
			errNum |= Cl.SetKernelArg(kernelFineTune, 6, CLTempCordX);
			errNum |= Cl.SetKernelArg(kernelFineTune, 7, CLTempCordY);
			errNum |= Cl.SetKernelArg(kernelFineTune, 8, CLTempNoCoord);
			errNum |= Cl.SetKernelArg(kernelFineTune, 9, (IntPtr)sizeof(int), new int[] { W });
			errNum |= Cl.SetKernelArg(kernelFineTune, 10, (IntPtr)sizeof(int), new int[] { pixelCount });
			errNum |= Cl.SetKernelArg(kernelFineTune, 11, (IntPtr)sizeof(float), new float[] { minScore * 0.6f });
			errNum |= Cl.SetKernelArg(kernelFineTune, 12, CLTemppositions);
			errNum |= Cl.SetKernelArg(kernelFineTune, 13, CLnormMinScore);
			errNum |= Cl.SetKernelArg(kernelFineTune, 14, CLnormGreediness);
			errNum |= Cl.SetKernelArg(kernelFineTune, 15, clCheckedRotations);
			errNum |= Cl.SetKernelArg(kernelFineTune, 16, (IntPtr)sizeof(int), new int[] { rotationsCount });
			errNum |= Cl.SetKernelArg(kernelFineTune, 17, clCheckedShifts);
			errNum |= Cl.SetKernelArg(kernelFineTune, 18, (IntPtr)sizeof(int), new int[] { shiftsCount });
			errNum |= Cl.SetKernelArg(kernelFineTune, 19, clShiftScores);
			errNum |= Cl.SetKernelArg(kernelFineTune, 20, clShiftRotIndices);
			errNum |= Cl.SetKernelArg(kernelFineTune, 21, clShiftPrevRotScores);
			errNum |= Cl.SetKernelArg(kernelFineTune, 22, clShiftNextRotScores);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "FineTune SetKernelArg hiba");

			// 4) Kernel futtatása 1D-ben a jelölt pixeleken
			IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)shiftsCount };
			errNum = Cl.EnqueueNDRangeKernel(commandQueue, kernelFineTune, 1, null, globalWorkSize, null, 0, null, out _);
			ComputeContext.throwErrorIfNotSuccessfull(errNum, "FineTune EnqueueNDRangeKernel hiba");
			Cl.Finish(commandQueue);

			float[] shiftScores = new float[shiftsCount];
			int[] shiftRotIndices = new int[shiftsCount];
			float[] shiftPrevScores = new float[shiftsCount];
			float[] shiftNextScores = new float[shiftsCount];

			Cl.EnqueueReadBuffer(commandQueue, clShiftScores, OpenCL.Net.Bool.True, IntPtr.Zero, (IntPtr)(sizeof(float) * shiftsCount), shiftScores, 0, null, out _);
			Cl.EnqueueReadBuffer(commandQueue, clShiftRotIndices, OpenCL.Net.Bool.True, IntPtr.Zero, (IntPtr)(sizeof(int) * shiftsCount), shiftRotIndices, 0, null, out _);
			Cl.EnqueueReadBuffer(commandQueue, clShiftPrevRotScores, OpenCL.Net.Bool.True, IntPtr.Zero, (IntPtr)(sizeof(float) * shiftsCount), shiftPrevScores, 0, null, out _);
			Cl.EnqueueReadBuffer(commandQueue, clShiftNextRotScores, OpenCL.Net.Bool.True, IntPtr.Zero, (IntPtr)(sizeof(float) * shiftsCount), shiftNextScores, 0, null, out _);

			// 5) Legjobb találat megkeresése
			float maxScore = -1.0f;
			int bestShiftListIdx = -1;
			Dictionary<int, float> scoreLookup = new Dictionary<int, float>(shiftsCount);

			for (int i = 0; i < shiftsCount; i++)
			{
				scoreLookup[checkedShifts[i]] = shiftScores[i];
				if (shiftScores[i] > maxScore)
				{
					maxScore = shiftScores[i];
					bestShiftListIdx = i;
				}
			}

			if (bestShiftListIdx >= 0 && maxScore > 0)
			{
				int bestPixelPos = checkedShifts[bestShiftListIdx];
				int imgCol = bestPixelPos % W;
				int imgRow = bestPixelPos / W;

				// 2D Sub-Pixel Paraboloid illesztés a pozícióra
				PointF fineSubOffset = new PointF(0f, 0f);
				if (scoreLookup.TryGetValue(bestPixelPos - W - 1, out float s00) &&
					scoreLookup.TryGetValue(bestPixelPos - W, out float s01) &&
					scoreLookup.TryGetValue(bestPixelPos - W + 1, out float s02) &&
					scoreLookup.TryGetValue(bestPixelPos - 1, out float s10) &&
					scoreLookup.TryGetValue(bestPixelPos + 1, out float s12) &&
					scoreLookup.TryGetValue(bestPixelPos + W - 1, out float s20) &&
					scoreLookup.TryGetValue(bestPixelPos + W, out float s21) &&
					scoreLookup.TryGetValue(bestPixelPos + W + 1, out float s22))
				{
					float[,] neighbors = new float[,]
					{
	{ s00, s01, s02 },
	{ s10, maxScore, s12 },
	{ s20, s21, s22 }
					};
					fineSubOffset = ComputeSubPixelParaboloid(neighbors);
				}

				// 1D Sub-Pixel Parabola illesztés a szögre (valós szomszédos score-okból!)
				int winningRotArrayIdx = shiftRotIndices[bestShiftListIdx];
				double exactFineAngle = realAngles[winningRotArrayIdx];
				if (winningRotArrayIdx > 0 && winningRotArrayIdx < rotationsCount - 1)
				{
					float sPrev = shiftPrevScores[bestShiftListIdx];
					float sCurr = maxScore;
					float sNext = shiftNextScores[bestShiftListIdx];
					double denomAngle = 2.0 * (sPrev - 2.0 * sCurr + sNext);
					if (Math.Abs(denomAngle) > 0.0001)
					{
						double angOffset = (sPrev - sNext) / denomAngle;
						if (Math.Abs(angOffset) <= 0.5)
						{
							exactFineAngle += angOffset;
							if (exactFineAngle < 0) exactFineAngle += 360.0;
							if (exactFineAngle >= 360.0) exactFineAngle -= 360.0;
						}
					}
				}
				returnresult.X = imgRow + fineSubOffset.Y;
				returnresult.Y = imgCol + fineSubOffset.X;
				returnresult.position = bestPixelPos;
				returnresult.Angle = exactFineAngle;
				returnresult.Score = maxScore;
			}
			// 6) Erőforrások felszabadítása
			clSdx.Dispose();
			clSdy.Dispose();
			clMag.Dispose();
			clCheckedRotations.Dispose();
			clCheckedShifts.Dispose();
			clShiftScores.Dispose();
			clShiftRotIndices.Dispose();
			clShiftPrevRotScores.Dispose();
			clShiftNextRotScores.Dispose();
			smoothedSource.Dispose();
			CSdx.Dispose();
			CSdy.Dispose();
			angle_src.Dispose();
			magnitude_src.Dispose();
			magnitude.Dispose();
			ForDivide.Dispose();
			imgX.Dispose();
			imgY.Dispose();
			imgMag.Dispose();
			ReleaseMEMObjects();
			return returnresult;
		}
		private int[] CalculateRotationArray(int Rotation, int precision, out int[] realAngles)
		{
			int count = precision * 2 + 1;
			int[] gpuIndices = new int[count];
			realAngles = new int[count];

			int cnt = 0;
			for (int i = Rotation - precision; i <= Rotation + precision; i++)
			{
				int validAngle = i % 360;
				if (validAngle < 0) validAngle += 360;

				realAngles[cnt] = validAngle;
				gpuIndices[cnt] = validAngle; // Nincs tükrözés! Közvetlenül a Full[validAngle] modellt használjuk!

				cnt++;
			}

			return gpuIndices;
		}

		private int[] CalculateShiftArray(Point MatchedPoint, int precision, Size searchsize)
		{
			int startx = Math.Max(0, Math.Min(searchsize.Width, MatchedPoint.Y - precision));
			int starty = Math.Max(0, Math.Min(searchsize.Height, MatchedPoint.X - precision));
			int endx = Math.Max(0, Math.Min(searchsize.Width, MatchedPoint.Y + precision + 1));
			int endy = Math.Max(0, Math.Min(searchsize.Height, MatchedPoint.X + precision + 1));
			int capacity = Math.Max(0, (endx - startx) * (endy - starty));
			List<int> rarray = new List<int>(capacity);

			for (int j = starty; j < endy; j++)
			{
				int rowOffset = j * searchsize.Width;
				for (int i = startx; i < endx; i++)
				{
					rarray.Add(rowOffset + i);
				}
			}
			return rarray.ToArray();
		}





		private bool SetKernelArguments(OpenCL.Net.Kernel KernelName)
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
			errNum |= Cl.SetKernelArg(KernelName, 12, ResultMap);
			errNum |= Cl.SetKernelArg(KernelName, 13, RotationMap);
			//	errNum |= clSetKernelArg(kernel, 14, sizeof(float), &normMinScore);
			//	errNum |= clSetKernelArg(kernel, 14, sizeof(float), &normGreediness);
			errNum |= Cl.SetKernelArg(KernelName, 14, (IntPtr)sizeof(float), minScore);
			errNum |= Cl.SetKernelArg(KernelName, 15, CLTemppositions);
			errNum |= Cl.SetKernelArg(KernelName, 16, CLnormMinScore);
			errNum |= Cl.SetKernelArg(KernelName, 17, CLnormGreediness);


			if (errNum != ErrorCode.Success)
			{
				return false;
			}
			return true;

		}






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

