using System;
using krypRevit.Addin.Openings;
class Program
{
    static int passed;
    static void Test(string name,Action test){test();passed++;Console.WriteLine("PASS "+name);}
    static void Check(bool ok){if(!ok)throw new Exception("Rule result incorrect");}
    static void Reject(Action test){try{test();}catch(ArgumentException){return;}throw new Exception("Invalid rule accepted");}
    static void Main()
    {
        Test("cable tray adds 50 each side and 25 top/bottom",()=>{var s=new OpeningRules().Rectangle("cable_tray",300,100);Check(s.width_mm==400&&s.height_mm==150&&s.offset_x_mm==0&&s.offset_y_mm==0);});
        Test("trunking adds 25 on all four sides",()=>{var s=new OpeningRules().Rectangle("trunking",100,50);Check(s.width_mm==150&&s.height_mm==100);});
        Test("asymmetric clearance shifts opening center correctly",()=>{var r=new OpeningRules();r.cable_tray.left_mm=20;r.cable_tray.right_mm=60;r.cable_tray.bottom_mm=10;r.cable_tray.top_mm=30;var s=r.Rectangle("cable_tray",100,50);Check(s.width_mm==180&&s.height_mm==90&&s.offset_x_mm==20&&s.offset_y_mm==10);});
        foreach(var pair in new[]{new[]{25d,50d},new[]{50d,80d},new[]{80d,100d},new[]{100d,150d},new[]{150d,200d}})
            Test("diameter "+pair[0]+" -> "+pair[1],()=>Check(new OpeningRules().Circle(pair[0],pair[0])==pair[1]));
        Test("unlisted 75mm stops in exact mode",()=>Reject(()=>new OpeningRules().Circle(75,75)));
        Test("next-larger mode is explicit and uses 80mm row",()=>{var r=new OpeningRules {unmatched_circle="next_larger"};Check(r.Circle(75,75)==100);});
        Test("circular opening above 200mm rejected",()=>{var r=new OpeningRules();r.circular[4].opening_mm=201;Reject(r.Validate);});
        Test("physical outer diameter must fit",()=>Reject(()=>new OpeningRules().Circle(80,101)));
        Test("bounded nominal tolerance",()=>{Check(new OpeningRules().Circle(50.1,60)==80);Reject(()=>new OpeningRules().Circle(50.8,60));});
        Test("overlapping circular matching ranges rejected",()=>{var r=new OpeningRules();r.circular.Add(new DiameterRule {service_mm=50.2,opening_mm=90});Reject(r.Validate);});
        Test("trunking marker matches family/type case-insensitively",()=>{var r=new OpeningRules();Check(r.ClassifyTray("CableTray TRUNKING 100x50")=="trunking"&&r.ClassifyTray("Ladder tray")=="cable_tray");});
        Test("rectangular duct rule requires explicit enable",()=>{var r=new OpeningRules();Reject(()=>r.Rectangle("duct",200,100));r.rectangular_duct.enabled=true;Check(r.Rectangle("duct",200,100).width_mm==250);});
        Test("negative/nonfinite clearances rejected",()=>{var r=new OpeningRules();r.cable_tray.left_mm=-1;Reject(r.Validate);r.cable_tray.left_mm=double.NaN;Reject(r.Validate);});
        Test("missing diameter row rejected",()=>{var r=new OpeningRules();r.circular.Add(null);Reject(r.Validate);});
        Test("nonfinite diameters rejected",()=>{Reject(()=>new OpeningRules().Circle(double.NaN,50));Reject(()=>new OpeningRules().Circle(50,double.PositiveInfinity));});
        Console.WriteLine("OPENING RULE CHECKS PASSED: "+passed);
    }
}
