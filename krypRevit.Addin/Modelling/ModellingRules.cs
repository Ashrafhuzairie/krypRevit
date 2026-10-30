using System;
using System.Linq;
namespace krypRevit.Addin.Endpoints
{
    // Revit-independent input/geometry checks shared by the proposal layer and tests.
    public static class ModellingRules
    {
        public static string[] OperationNames => new[]{"set_parameter","move","rotate","copy","create_wall","create_floor","create_level","place_family","change_type","set_wall_curve","delete_element","create_service_opening"};
        public static bool IsCreation(string operation) => operation=="create_wall" || operation=="create_floor" || operation=="create_level" || operation=="place_family" || operation=="create_service_opening";
        public static double Number(object value)
        {
            if(!(value is double || value is float || value is int || value is long || value is decimal)) throw new ArgumentException("Numeric values must be JSON numbers.");
            var n=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);
            if(double.IsNaN(n) || double.IsInfinity(n) || Math.Abs(n)>1000000) throw new ArgumentException("Number must be finite and within +/- 1,000,000.");
            return n;
        }
        public static double[] Point(double[] p)
        {
            if(p==null || p.Length!=3) throw new ArgumentException("Expected three coordinates.");
            foreach(var x in p) Number(x);return p;
        }
        private static void Required(string value,string field)
        { if(string.IsNullOrWhiteSpace(value)) throw new ArgumentException(field+" is required; use IDs returned by model inspection."); }
        public static double Distance(double[] a,double[] b)=>Math.Sqrt(a.Zip(b,(x,y)=>(x-y)*(x-y)).Sum());
        public static void Validate(EditAction a,double tolerance)
        {
            if(a==null || !OperationNames.Contains(a.operation)) throw new ArgumentException("Unsupported operation.");
            if(!IsCreation(a.operation)) Required(a.unique_id,"unique_id");
            switch(a.operation)
            {
                case "create_service_opening":Required(a.unique_id,"host unique_id");Required(a.service_unique_id,"service_unique_id");if(a.opening_shape!="auto"&&a.opening_shape!="rectangular"&&a.opening_shape!="circular")throw new ArgumentException("Invalid opening shape.");break;
                case "set_parameter":Required(a.parameter_id,"parameter_id");break;
                case "move":case "copy":if(Distance(Point(a.vector_ft),new double[3])<1e-9) throw new ArgumentException("Translation must be nonzero.");break;
                case "rotate":
                    Point(a.origin_ft);if(Distance(Point(a.axis),new double[3])<1e-9) throw new ArgumentException("Rotation axis must be nonzero.");
                    if(Math.Abs(Number(a.angle_radians))>2*Math.PI || Math.Abs(a.angle_radians)<1e-9) throw new ArgumentException("Rotation must be nonzero and within +/- 2 pi radians.");break;
                case "create_wall":case "set_wall_curve":
                    Point(a.start_ft);Point(a.end_ft);
                    if(Math.Abs(a.start_ft[2]-a.end_ft[2])>1e-6 || Distance(a.start_ft,a.end_ft)<tolerance) throw new ArgumentException("Wall line must be horizontal and longer than Revit's short-curve tolerance.");
                    if(a.operation=="create_wall") {Required(a.wall_type_unique_id,"wall_type_unique_id");Required(a.level_unique_id,"level_unique_id");if(Number(a.height_ft)<=0 || a.height_ft>30000) throw new ArgumentException("Wall height must be positive and at most 30,000 feet.");}break;
                case "create_floor":Required(a.floor_type_unique_id,"floor_type_unique_id");Required(a.level_unique_id,"level_unique_id");Boundary(a.boundary_ft,tolerance);break;
                case "create_level":
                    if(!a.elevation_ft.HasValue) throw new ArgumentException("elevation_ft is required (zero is allowed).");Number(a.elevation_ft.Value);
                    Required(a.name,"name");if(a.name.Length>120) throw new ArgumentException("Level name is too long.");break;
                case "place_family":Required(a.symbol_unique_id,"symbol_unique_id");Required(a.level_unique_id,"level_unique_id");Point(a.point_ft);break;
                case "change_type":Required(a.type_unique_id,"type_unique_id");break;
            }
        }
        public static double[][] Boundary(double[][] input,double tolerance)
        {
            if(input==null || input.Length<3 || input.Length>65) throw new ArgumentException("Floor boundary needs 3-64 distinct vertices, optionally repeating the first at the end.");
            foreach(var p in input) Point(p);
            var points=input.Select(p=>(double[])p.Clone()).ToArray();
            if(Distance(points[0],points[points.Length-1])<tolerance) points=points.Take(points.Length-1).ToArray();
            if(points.Length<3 || points.Length>64) throw new ArgumentException("Floor boundary needs 3-64 vertices.");
            int count=points.Length;
            if(points.Any(p=>Math.Abs(p[2]-points[0][2])>1e-6)) throw new ArgumentException("Floor boundary must be horizontal.");
            double area=0;
            for(int i=0;i<count;i++)
            {
                var p=points[i];var q=points[(i+1)%count];
                if(Distance(p,q)<tolerance) throw new ArgumentException("Floor boundary has a zero/short edge.");
                area+=p[0]*q[1]-q[0]*p[1];
                for(int j=i+1;j<count;j++)
                {
                    if(j==i+1 || (i==0 && j==count-1)) continue;
                    if(Intersects(p,q,points[j],points[(j+1)%count])) throw new ArgumentException("Floor boundary intersects or touches itself.");
                }
            }
            if(Math.Abs(area)<tolerance*tolerance) throw new ArgumentException("Floor boundary has no area.");
            return points;
        }
        private static double Cross(double[] a,double[] b,double[] c)=>(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]);
        private static bool On(double[] a,double[] b,double[] p)=>Math.Abs(Cross(a,b,p))<1e-9 && p[0]>=Math.Min(a[0],b[0])-1e-9 && p[0]<=Math.Max(a[0],b[0])+1e-9 && p[1]>=Math.Min(a[1],b[1])-1e-9 && p[1]<=Math.Max(a[1],b[1])+1e-9;
        private static bool Intersects(double[] a,double[] b,double[] c,double[] d)
        {
            double c1=Cross(a,b,c),c2=Cross(a,b,d),c3=Cross(c,d,a),c4=Cross(c,d,b);
            return ((c1>0 && c2<0 || c1<0 && c2>0) && (c3>0 && c4<0 || c3<0 && c4>0)) || On(a,b,c) || On(a,b,d) || On(c,d,a) || On(c,d,b);
        }
    }
}
