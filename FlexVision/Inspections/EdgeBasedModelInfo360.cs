using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOI.Inspections.EdgeBasedTemplateDetection
{
  public  class EdgeBasedModelInfo360
    {
		public int[] cordinatesarrayX360;  // edge X coordinate * 360 
		public int[] cordinatesarrayY360;  // edge Y coordinate * 360 
		public float[] edgeMagnitude360;   // gradient magnitude * 360
		public float[] edgeDerivativeX360;     // gradient in X direction * 360
		public float[] edgeDerivativeY360;     // gradient in Y direction * 360
		public int[] step360;
		public int[] positions;
		public int noOfCordinates360;
		public float[] normMinScore360;
		public float[] normGreediness360;

		public EdgeBasedModelInfo360() { }

		

		}

}
