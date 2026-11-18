using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace AOI.Inspections.EdgeBasedTemplateDetection
{
	public class EdgeBasedModelInfo
	{
		public int noOfCordinates = 0;     //Number of elements in coordinate array
		public Point[] cordinates;      //Coordinates array to store model points	
		public int[] cordinatesarrayX;
		public int[] cordinatesarrayY;
		public float[] edgeMagnitude;       //gradient magnitude
		public float[] edgeDerivativeX; //gradient in X direction
		public float[] edgeDerivativeY; //gradient in Y direction	
		public Point centerOfGravity;  //Center of gravity of template 
		public int modelHeight;        //Template height
		public int modelWidth;         //Template width
		public Point Center;
		public int EdgeWith;
		public int EdgeHeight;
		public int rotation;
		public float MaxGradient;
		public EdgeBasedModelInfo() { }






		public EdgeBasedModelInfo GetRotatedModel(double angleDegrees)
		{
			double angleRadiansRight = -angleDegrees * Math.PI / 180.0;
			double angleRadians = angleDegrees * Math.PI / 180.0;

			float cosTheta = (float)Math.Cos(angleRadians);
			float sinTheta = (float)Math.Sin(angleRadians);
			float cosThetaR = (float)Math.Cos(angleRadiansRight);
			float sinThetaR = (float)Math.Sin(angleRadiansRight);

			int n = this.noOfCordinates;

			EdgeBasedModelInfo rotatedModel = new EdgeBasedModelInfo
			{
				noOfCordinates = n,
				modelHeight = this.modelHeight,
				modelWidth = this.modelWidth,
				centerOfGravity = this.centerOfGravity,
				Center = this.Center,
				EdgeHeight = this.EdgeHeight,
				EdgeWith = this.EdgeWith,
				rotation = (int)angleDegrees,
				MaxGradient = this.MaxGradient,
				edgeDerivativeX = new float[n],
				edgeDerivativeY = new float[n],
				edgeMagnitude = new float[n],
				cordinatesarrayX = new int[n],
				cordinatesarrayY = new int[n]
			};

			// A vektor hossza forgatáskor invariáns: Math.Sqrt helyett gyors memóriamásolás!
			Array.Copy(this.edgeMagnitude, rotatedModel.edgeMagnitude, n);

			float[] srcGx = this.edgeDerivativeX;
			float[] srcGy = this.edgeDerivativeY;
			float[] dstGx = rotatedModel.edgeDerivativeX;
			float[] dstGy = rotatedModel.edgeDerivativeY;

			int[] srcX = this.cordinatesarrayX;
			int[] srcY = this.cordinatesarrayY;
			int[] dstX = rotatedModel.cordinatesarrayX;
			int[] dstY = rotatedModel.cordinatesarrayY;

			for (int i = 0; i < n; i++)
			{
				float gx = srcGx[i];
				float gy = srcGy[i];
				dstGx[i] = cosTheta * gx - sinTheta * gy;
				dstGy[i] = sinTheta * gx + cosTheta * gy;

				int x = srcX[i];
				int y = srcY[i];
				dstX[i] = (int)Math.Round(cosThetaR * x - sinThetaR * y);
				dstY[i] = (int)Math.Round(sinThetaR * x + cosThetaR * y);
			}

			return rotatedModel;
		}






	}

	public class ModelInfo
	{
		public List<EdgePoint> EdgePoints = new List<EdgePoint>();
		public Point Center;
		public float MaxGradient;
		public Size ModelSize;
		public ModelInfo() { }
	}


	public class ModelInfoBase
	{
		public List<EdgePointWithNeighbor> EdgePoints = new List<EdgePointWithNeighbor>();
		public Point Center;
		public float MaxGradient;
		public Size ModelSize;
		public ModelInfoBase() { }
	}

	public struct EdgePoint
	{
		public Point Location;
		public float Magnitude;
		public float DerivateX;
		public float DerivateY;
		public float OriginalValue;
	}

	public struct EdgePointWithNeighbor
	{
		public Point Location;
		public float Magnitude;
		public float DerivateX;
		public float DerivateY;
		public float OriginalValue;
		public float LT;
		public float L;
		public float LB;
		public float RT;
		public float R;
		public float RB;
		public float T;
		public float B;

		public float Magnitude90;
		public float DerivateX90;
		public float DerivateY90;

		public float Magnitude180;
		public float DerivateX180;
		public float DerivateY180;


		public float Magnitude270;
		public float DerivateX270;
		public float DerivateY270;
	}


}
