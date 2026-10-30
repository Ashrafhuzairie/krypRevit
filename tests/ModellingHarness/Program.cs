using System;
using System.Linq;
using krypRevit.Addin.Endpoints;
class Program
{
    static int passed;
    static readonly double Tol=0.0026;
    static double[] P(double x,double y,double z=0)=>new[]{x,y,z};
    static double[][] Rect()=>new[]{P(0,0),P(20,0),P(20,15),P(0,15)};
    static void Check(bool ok,string message) {if(!ok) throw new Exception(message);}
    static void Test(string name,Action test) {test();passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action test) {try {test();}catch(ArgumentException){return;}throw new Exception("Expected invalid input rejection");}
    static EditAction A(string operation)=>new EditAction{operation=operation,unique_id="instance",service_unique_id="service",parameter_id="parameter",value="test",wall_type_unique_id="wall-type",floor_type_unique_id="floor-type",level_unique_id="level",symbol_unique_id="symbol",type_unique_id="valid-type",name="Level 2",elevation_ft=10,height_ft=10,start_ft=P(0,0),end_ft=P(20,0),point_ft=P(1,2,3),boundary_ft=Rect(),vector_ft=P(1,2,3),origin_ft=P(0,0),axis=P(0,0,1),angle_radians=Math.PI/2};
    static void Main()
    {
        Test("all twelve action shapes accepted with required fields",()=>{foreach(var op in ModellingRules.OperationNames) ModellingRules.Validate(A(op),Tol);Check(ModellingRules.OperationNames.Length==12,"Unexpected catalog");});
        Test("clockwise and counterclockwise floors accepted",()=>{ModellingRules.Boundary(Rect(),Tol);ModellingRules.Boundary(Rect().Reverse().ToArray(),Tol);});
        Test("closing vertex normalized without mutating input",()=>{var a=Rect().Concat(new[]{P(0,0)}).ToArray();var b=ModellingRules.Boundary(a,Tol);Check(b.Length==4 && a.Length==5,"Wrong normalization");b[0][0]=99;Check(a[0][0]==0,"Input mutated");});
        Test("self-crossing floor rejected",()=>Reject(()=>ModellingRules.Boundary(new[]{P(0,0),P(10,10),P(0,10),P(10,0)},Tol)));
        Test("touching nonadjacent floor edges rejected",()=>Reject(()=>ModellingRules.Boundary(new[]{P(0,0),P(10,0),P(10,10),P(5,0),P(0,10)},Tol)));
        Test("nonhorizontal floor rejected",()=>{var a=Rect();a[2][2]=1;Reject(()=>ModellingRules.Boundary(a,Tol));});
        Test("short floor edge rejected",()=>Reject(()=>ModellingRules.Boundary(new[]{P(0,0),P(0.001,0),P(10,10)},Tol)));
        Test("collinear floor rejected",()=>Reject(()=>ModellingRules.Boundary(new[]{P(0,0),P(1,0),P(2,0)},Tol)));
        Test("oversized boundary rejected",()=>Reject(()=>ModellingRules.Boundary(Enumerable.Range(0,66).Select(i=>P(i,i*i)).ToArray(),Tol)));
        Test("nonfinite and out-of-range coordinates rejected",()=>{Reject(()=>ModellingRules.Point(P(double.NaN,0)));Reject(()=>ModellingRules.Point(P(0,double.PositiveInfinity)));Reject(()=>ModellingRules.Point(P(1000001,0)));});
        Test("wall line must be horizontal and long enough",()=>{var a=A("create_wall");a.end_ft=P(20,0,1);Reject(()=>ModellingRules.Validate(a,Tol));a.end_ft=P(.001,0);Reject(()=>ModellingRules.Validate(a,Tol));});
        Test("wall height must be positive",()=>{var a=A("create_wall");a.height_ft=0;Reject(()=>ModellingRules.Validate(a,Tol));});
        Test("level zero elevation allowed but missing elevation rejected",()=>{var a=A("create_level");a.elevation_ft=0;ModellingRules.Validate(a,Tol);a.elevation_ft=null;Reject(()=>ModellingRules.Validate(a,Tol));});
        Test("creation references and replacement type required",()=>{var a=A("place_family");a.symbol_unique_id=null;Reject(()=>ModellingRules.Validate(a,Tol));a=A("change_type");a.type_unique_id=null;Reject(()=>ModellingRules.Validate(a,Tol));});
        Test("nonzero translation and rotation axis required",()=>{var a=A("move");a.vector_ft=P(0,0);Reject(()=>ModellingRules.Validate(a,Tol));a=A("rotate");a.axis=P(0,0);Reject(()=>ModellingRules.Validate(a,Tol));});
        Test("unknown operation and missing target rejected",()=>{Reject(()=>ModellingRules.Validate(A("run_code"),Tol));var a=A("set_wall_curve");a.unique_id=null;Reject(()=>ModellingRules.Validate(a,Tol));});
        Test("numeric strings and booleans rejected",()=>{Reject(()=>ModellingRules.Number("1000"));Reject(()=>ModellingRules.Number(true));});
        Test("reported numeric ID resolves as an ID, never a UniqueId",()=>{var r=ElementReference.Parse("697693");Check(r.NumericId==697693,"Numeric ID misclassified");});
        Test("full 64-bit IDs preserved without floating-point rounding",()=>{Check(ElementReference.Parse("9007199254740993").NumericId==9007199254740993L,"64-bit precision lost");Check(ElementReference.Parse(long.MaxValue.ToString()).NumericId==long.MaxValue,"Int64 maximum rejected");});
        Test("numeric ID whitespace and leading zeroes handled",()=>Check(ElementReference.Parse(" 000697693 ").NumericId==697693,"Normalization failed"));
        Test("actual UniqueId retained",()=>{var r=ElementReference.Parse("eac12345-1234-4321-8765-0123456789ab-000aa54d");Check(!r.NumericId.HasValue && r.Text.EndsWith("000aa54d"),"UniqueId changed");});
        Test("invalid or overflowing numeric IDs rejected",()=>{Reject(()=>ElementReference.Parse("0"));Reject(()=>ElementReference.Parse("-1"));Reject(()=>ElementReference.Parse("9223372036854775808"));Reject(()=>ElementReference.Parse(" "));});
        Test("deletion-only single-target batch enforced",()=>{DeletionRules.ValidateBatch(new[]{A("delete_element")});Reject(()=>DeletionRules.ValidateBatch(new[]{A("delete_element"),A("move")}));Reject(()=>DeletionRules.ValidateBatch(new[]{A("delete_element"),A("delete_element")}));});
        Test("native deletion set may be reordered",()=>DeletionRules.ValidateImpacts(new long[]{697693,697694},new long[]{697694,697693}));
        Test("changed deletion closure rejected",()=>{try {DeletionRules.ValidateImpacts(new long[]{1,2},new long[]{1,2,3});throw new Exception("Unexpected dependent accepted");}catch(InvalidOperationException){} });
        Test("oversized or duplicate deletion impacts rejected",()=>{foreach(var ids in new[]{new long[]{1,1},Enumerable.Range(1,51).Select(i=>(long)i).ToArray()}) {try {DeletionRules.ValidateImpacts(ids,ids);throw new Exception("Bad preview accepted");}catch(InvalidOperationException){}}});
        Console.WriteLine($"MODELLING GEOMETRY CHECKS PASSED: {passed}");
    }
}
