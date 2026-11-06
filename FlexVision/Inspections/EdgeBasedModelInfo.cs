using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace AOI.Inspections.EdgeBasedTemplateDetection
{
  public  class EdgeBasedModelInfo
    {
		public	int noOfCordinates = 0;     //Number of elements in coordinate array
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




public EdgeBasedModelInfo GetRotatedModel_SIMD(double angleDegrees)
	{
		double angleRadians = -angleDegrees * Math.PI / 180.0; // negatív irány, hogy egyezzen a képi forgatással
		float cosTheta = (float)Math.Cos(angleRadians);
		float sinTheta = (float)Math.Sin(angleRadians);

		int n = this.noOfCordinates;

		var rotatedModel = new EdgeBasedModelInfo
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
			cordinatesarrayY = new int[n],
			cordinates = new Point[n]
		};

		// SIMD: gradient forgatás
		int simdLength = Vector<float>.Count;
		int i = 0;

		for (; i <= n - simdLength; i += simdLength)
		{
			var gxVec = new Vector<float>(this.edgeDerivativeX, i);
			var gyVec = new Vector<float>(this.edgeDerivativeY, i);

			var gxRotVec = cosTheta * gxVec - sinTheta * gyVec;
			var gyRotVec = sinTheta * gxVec + cosTheta * gyVec;

			gxRotVec.CopyTo(rotatedModel.edgeDerivativeX, i);
			gyRotVec.CopyTo(rotatedModel.edgeDerivativeY, i);

			var magVec = Vector.SquareRoot(Vector.Add(Vector.Multiply(gxRotVec, gxRotVec), Vector.Multiply(gyRotVec, gyRotVec)));
			magVec.CopyTo(rotatedModel.edgeMagnitude, i);
		}

		// Maradék elemek (ha nem osztható SIMD hosszra)
		for (; i < n; i++)
		{
			float gx = this.edgeDerivativeX[i];
			float gy = this.edgeDerivativeY[i];

			float gxRot = cosTheta * gx - sinTheta * gy;
			float gyRot = sinTheta * gx + cosTheta * gy;

			rotatedModel.edgeDerivativeX[i] = gxRot;
			rotatedModel.edgeDerivativeY[i] = gyRot;
			rotatedModel.edgeMagnitude[i] = (float)Math.Sqrt(gxRot * gxRot + gyRot * gyRot);
		}

		// Koordináták forgatása párhuzamosan
		Parallel.For(0, n, j =>
		{
			int x = this.cordinatesarrayX[j];
			int y = this.cordinatesarrayY[j];

			int xRot = (int)Math.Round(cosTheta * x - sinTheta * y);
			int yRot = (int)Math.Round(sinTheta * x + cosTheta * y);

			rotatedModel.cordinatesarrayX[j] = xRot;
			rotatedModel.cordinatesarrayY[j] = yRot;
			rotatedModel.cordinates[j] = new Point(xRot, yRot);
		});

		return rotatedModel;
	}



	public EdgeBasedModelInfo GetRotatedModel(double angleDegrees)
		{
			double angleRadiansright = -angleDegrees * Math.PI / 180.0;
			double angleRadians = angleDegrees * Math.PI / 180.0;
			double cosTheta = Math.Cos(angleRadians);
			double sinTheta = Math.Sin(angleRadians);
			double cosThetar = Math.Cos(angleRadiansright);
			double sinThetar = Math.Sin(angleRadiansright);

			EdgeBasedModelInfo rotatedModel = new EdgeBasedModelInfo
			{
				noOfCordinates = this.noOfCordinates,
				modelHeight = this.modelHeight,
				modelWidth = this.modelWidth,
				centerOfGravity = this.centerOfGravity,
				Center = this.Center,
				EdgeHeight = this.EdgeHeight,
				EdgeWith = this.EdgeWith,
				rotation = (int)angleDegrees,
				MaxGradient = this.MaxGradient
			};

			int n = this.noOfCordinates;

			rotatedModel.edgeDerivativeX = new float[n];
			rotatedModel.edgeDerivativeY = new float[n];
			rotatedModel.edgeMagnitude = new float[n];
			rotatedModel.cordinatesarrayX = new int[n];
			rotatedModel.cordinatesarrayY = new int[n];
			rotatedModel.cordinates = new Point[n];
			float MaxgradNorm = MaxGradient * 255;

			//	Parallel.For(0, n, i =>

			for (int i = 0; i < n; i++)
			{
				// Gradient forgatása
				float gx = this.edgeDerivativeX[i];
				float gy = this.edgeDerivativeY[i];

				float gxRot = (float)(cosTheta * gx - sinTheta * gy);
				float gyRot = (float)(sinTheta * gx + cosTheta * gy);

				float mag = (float)Math.Sqrt(gxRot * gxRot + gyRot * gyRot);
				float Divmag = (float)(mag / MaxgradNorm);

				rotatedModel.edgeDerivativeX[i] = gxRot;
				rotatedModel.edgeDerivativeY[i] = gyRot;
				//rotatedModel.edgeMagnitude[i] = mag;
				if (Divmag != 0)
					rotatedModel.edgeMagnitude[i] = 1 / Divmag;
				else
					rotatedModel.edgeMagnitude[i] = 0;
				// Koordináta forgatása (lokális koordináták, nem kell újra eltolni)
				int x = this.cordinatesarrayX[i];
				int y = this.cordinatesarrayY[i];

				int xRot = (int)Math.Round(cosThetar * x - sinThetar * y);
				int yRot = (int)Math.Round(sinThetar * x + cosThetar * y);

				rotatedModel.cordinatesarrayX[i] = xRot;
				rotatedModel.cordinatesarrayY[i] = yRot;
				rotatedModel.cordinates[i] = new Point(xRot, yRot);
			}
	//	});
			return rotatedModel;
		}

		/*
		public EdgeBasedModelInfo GetRotatedModel(double angleDegrees)
		{
			double angleRadians = angleDegrees * Math.PI / 180.0;
			double cosTheta = Math.Cos(angleRadians);
			double sinTheta = Math.Sin(angleRadians);
			int minx = int.MaxValue;
			int miny = int.MaxValue;
			int maxx = int.MinValue;
			int maxy = int.MinValue;

			EdgeBasedModelInfo rotatedModel = new EdgeBasedModelInfo
			{
				noOfCordinates = this.noOfCordinates,
				modelHeight = this.modelHeight,
				modelWidth = this.modelWidth,
				centerOfGravity = this.centerOfGravity,
				Center = this.Center,
				EdgeHeight = this.EdgeHeight,
				EdgeWith = this.EdgeWith,
				rotation = (int)angleDegrees,
				MaxGradient = this.MaxGradient
			};

			int n = this.noOfCordinates;

			rotatedModel.edgeDerivativeX = new float[n];
			rotatedModel.edgeDerivativeY = new float[n];
			rotatedModel.edgeMagnitude = new float[n];
			rotatedModel.cordinatesarrayX = new int[n];
			rotatedModel.cordinatesarrayY = new int[n];
			rotatedModel.cordinates = new Point[n];

			for (int i = 0; i < n; i++)
			{
				// Gradient forgatása
				float gx = this.edgeDerivativeX[i];
				float gy = this.edgeDerivativeY[i];

				float gxRot = (float)(cosTheta * gx - sinTheta * gy);
				float gyRot = (float)(sinTheta * gx + cosTheta * gy);

				float mag = (float)Math.Sqrt(gxRot * gxRot + gyRot * gyRot);
				float dir = (float)Math.Atan2(gyRot, gxRot); // radiánban

				rotatedModel.edgeDerivativeX[i] = gxRot;
				rotatedModel.edgeDerivativeY[i] = gyRot;
				rotatedModel.edgeMagnitude[i] = mag;

				// Koordináta forgatása a súlypont körül (már lokális koordináták)
				int x = this.cordinatesarrayX[i];
				int y = this.cordinatesarrayY[i];

				int xRot = (int)Math.Round(cosTheta * x - sinTheta * y) + this.centerOfGravity.X ;
				int yRot = (int)Math.Round(sinTheta * x + cosTheta * y) + this.centerOfGravity.Y ;
				if (xRot < minx)
					minx = xRot;

				if (xRot > maxx)
					maxx = xRot;

				if (yRot < miny)
					miny = yRot;

				if (yRot > maxy)
					maxy = yRot;
				rotatedModel.cordinatesarrayX[i] = xRot;
				rotatedModel.cordinatesarrayY[i] = yRot;
				rotatedModel.cordinates[i] = new Point(xRot, yRot);
			}



			return rotatedModel;
		}


		*/
		/*
		public EdgeBasedModelInfo GetRotatedModel(double angleDegrees)
		{
			double angleRadians = angleDegrees * Math.PI / 180.0;
			double cosTheta = Math.Cos(angleRadians);
			double sinTheta = Math.Sin(angleRadians);
			int minx = int.MaxValue;
			int miny = int.MaxValue;
			int maxx = int.MinValue;
			int maxy = int.MinValue;


			EdgeBasedModelInfo rotatedModel = new EdgeBasedModelInfo
			{
				noOfCordinates = this.noOfCordinates,
				modelHeight = this.modelHeight,
				modelWidth = this.modelWidth,
				centerOfGravity = new Point(this.centerOfGravity.X, this.centerOfGravity.Y),
				Center = new Point (this.Center.X, this.Center.Y),
				EdgeHeight = this.EdgeHeight,
				EdgeWith = this.EdgeWith,
				rotation = (int)angleDegrees,
				MaxGradient = this.MaxGradient
			};

			int n = this.noOfCordinates;

			rotatedModel.edgeDerivativeX = new float[n];
			rotatedModel.edgeDerivativeY = new float[n];
			rotatedModel.edgeMagnitude = new float[n];
			rotatedModel.cordinatesarrayX = new int[n];
			rotatedModel.cordinatesarrayY = new int[n];
			rotatedModel.cordinates = new Point[n];

			Point center = this.centerOfGravity;

			for (int i = 0; i < n; i++)
			{
				// Gradient forgatása
				float gx = this.edgeDerivativeX[i];
				float gy = this.edgeDerivativeY[i];

				float gxRot = (float)(cosTheta * gx - sinTheta * gy);
				float gyRot = (float)(sinTheta * gx + cosTheta * gy);

				float mag = (float)Math.Sqrt(gxRot * gxRot + gyRot * gyRot);
				float dir = (float)Math.Atan2(gyRot, gxRot); // radiánban
				float Divmag = (float)(mag / MaxGradient * 255);
				rotatedModel.edgeDerivativeX[i] = gxRot;
				rotatedModel.edgeDerivativeY[i] = gyRot;
				if (Divmag != 0)
					rotatedModel.edgeMagnitude[i] = 1 / Divmag;
				else
					rotatedModel.edgeMagnitude[i] = 0;

				// Koordináta forgatása a súlypont körül
				int x = this.cordinatesarrayX[i] ;
				int y = this.cordinatesarrayY[i] ;
	//			int x = this.cordinatesarrayX[i] - center.X;
	//			int y = this.cordinatesarrayY[i] - center.Y;
		//		int x = this.cordinatesarrayX[i] - (maxx - minx) / 2;
		//		int y = this.cordinatesarrayY[i] - (maxy - miny) / 2;


				int xRot = (int)Math.Round(cosTheta * x - sinTheta * y) + center.X;
				int yRot = (int)Math.Round(sinTheta * x + cosTheta * y) + center.Y;

				if (xRot < minx)
					minx = xRot;

				if (xRot > maxx)
					maxx = xRot;

				if (yRot < miny)
					miny = yRot;

				if (yRot > maxy)
					maxy = yRot;

				rotatedModel.cordinatesarrayX[i] = xRot;
				rotatedModel.cordinatesarrayY[i] = yRot;
				rotatedModel.cordinates[i] = new Point(xRot, yRot);
			}



			rotatedModel.centerOfGravity.X = minx + (maxx - minx) / 2; // center
			rotatedModel.centerOfGravity.Y = miny + (maxy - miny) / 2; // center y
			rotatedModel.Center = new Point(minx + (maxx - minx) / 2, miny + (maxy - miny) / 2);
			rotatedModel.EdgeHeight = maxx - minx;
			rotatedModel.EdgeWith = maxy - miny;
			rotatedModel.modelWidth = maxx - minx;
			rotatedModel.modelHeight = maxy - miny;

			for (int m = 0; m < rotatedModel.noOfCordinates; m++)
			{
				int temp;
				temp = rotatedModel.cordinates[m].X;
				rotatedModel.cordinates[m].X = temp - (maxx - minx) / 2;
				temp = rotatedModel.cordinates[m].Y;
				rotatedModel.cordinates[m].Y = temp - (maxy - miny) / 2;
			}



			return rotatedModel;
		}
		*/

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
