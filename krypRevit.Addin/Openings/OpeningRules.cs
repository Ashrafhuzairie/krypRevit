using System;
using System.Collections.Generic;
using System.Linq;
namespace krypRevit.Addin.Openings
{
    public sealed class RectangularRule
    {
        public bool enabled {get;set;}=true;
        public double left_mm {get;set;}
        public double right_mm {get;set;}
        public double top_mm {get;set;}
        public double bottom_mm {get;set;}
    }
    public sealed class DiameterRule
    {
        public double service_mm {get;set;}
        public double opening_mm {get;set;}
    }
    public sealed class OpeningRules
    {
        public RectangularRule cable_tray {get;set;}=new RectangularRule {left_mm=50,right_mm=50,top_mm=25,bottom_mm=25};
        public RectangularRule trunking {get;set;}=new RectangularRule {left_mm=25,right_mm=25,top_mm=25,bottom_mm=25};
        public RectangularRule rectangular_duct {get;set;}=new RectangularRule {enabled=false,left_mm=25,right_mm=25,top_mm=25,bottom_mm=25};
        public string trunking_type_contains {get;set;}="trunking";
        public string unmatched_circle {get;set;}="exact";
        public double match_tolerance_mm {get;set;}=.5;
        public List<DiameterRule> circular {get;set;}=new List<DiameterRule> {
            new DiameterRule {service_mm=25,opening_mm=50},new DiameterRule {service_mm=50,opening_mm=80},new DiameterRule {service_mm=80,opening_mm=100},new DiameterRule {service_mm=100,opening_mm=150},new DiameterRule {service_mm=150,opening_mm=200}};
        public const double MaximumCircleMm=200;
        private static double Valid(double n,double max)
        {if(double.IsNaN(n)||double.IsInfinity(n)||n<0||n>max)throw new ArgumentException("Rule values must be finite, nonnegative and within limits.");return n;}
        public void Validate()
        {
            foreach(var r in new[]{cable_tray,trunking,rectangular_duct})
            {
                if(r==null)throw new ArgumentException("All rectangular rule rows are required.");
                foreach(var n in new[]{r.left_mm,r.right_mm,r.top_mm,r.bottom_mm})Valid(n,1000);
            }
            if(string.IsNullOrWhiteSpace(trunking_type_contains)||trunking_type_contains.Length>100)throw new ArgumentException("Enter the trunking type-name marker.");
            if(unmatched_circle!="exact"&&unmatched_circle!="next_larger")throw new ArgumentException("Choose exact or next_larger circular matching.");
            Valid(match_tolerance_mm,5);
            if(circular==null||circular.Count<1||circular.Count>100)throw new ArgumentException("Provide 1-100 diameter rules.");
            if(circular.Any(r=>r==null))throw new ArgumentException("Invalid diameter row.");
            var rows=circular.OrderBy(r=>r.service_mm).ToArray();
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];if(r==null)throw new ArgumentException("Invalid diameter row.");
                Valid(r.service_mm,MaximumCircleMm);Valid(r.opening_mm,MaximumCircleMm);
                if(r.service_mm<=0||r.opening_mm<r.service_mm)throw new ArgumentException("Opening diameter must fit the service and be at most 200 mm.");
                if(i>0&&r.service_mm-rows[i-1].service_mm<=2*match_tolerance_mm)throw new ArgumentException("Diameter match ranges overlap; separate rows or reduce tolerance.");
            }
        }
        public double Circle(double nominalMm,double physicalMm)
        {
            Validate();Valid(nominalMm,1000000);Valid(physicalMm,1000000);if(nominalMm<=0||physicalMm<=0)throw new ArgumentException("Service diameter must be positive.");
            var row=circular.OrderBy(r=>r.service_mm).FirstOrDefault(r=>Math.Abs(r.service_mm-nominalMm)<=match_tolerance_mm);
            if(row==null&&unmatched_circle=="next_larger")row=circular.OrderBy(r=>r.service_mm).FirstOrDefault(r=>r.service_mm>=nominalMm);
            if(row==null)throw new ArgumentException("No circular rule matches "+nominalMm+" mm. Add a rule in Opening rules; maximum opening is 200 mm.");
            if(row.opening_mm<physicalMm||row.opening_mm>MaximumCircleMm)throw new ArgumentException("Mapped opening cannot fit the physical service within the 200 mm limit.");
            return row.opening_mm;
        }
        public string ClassifyTray(string familyAndType) => (familyAndType??"").IndexOf(trunking_type_contains,StringComparison.OrdinalIgnoreCase)>=0?"trunking":"cable_tray";
        public RectangularSize Rectangle(string kind,double widthMm,double heightMm)
        {
            Validate();if(widthMm<=0||heightMm<=0||double.IsNaN(widthMm)||double.IsNaN(heightMm)||double.IsInfinity(widthMm)||double.IsInfinity(heightMm))throw new ArgumentException("Service dimensions must be finite and positive.");
            var r=kind=="cable_tray"?cable_tray:kind=="trunking"?trunking:kind=="duct"?rectangular_duct:null;
            if(r==null||!r.enabled)throw new ArgumentException("Enable/configure the rectangular rule for "+kind+" in Opening rules.");
            return new RectangularSize {width_mm=widthMm+r.left_mm+r.right_mm,height_mm=heightMm+r.top_mm+r.bottom_mm,offset_x_mm=(r.right_mm-r.left_mm)/2,offset_y_mm=(r.top_mm-r.bottom_mm)/2};
        }
    }
    public sealed class RectangularSize
    {
        public double width_mm {get;set;}
        public double height_mm {get;set;}
        public double offset_x_mm {get;set;}
        public double offset_y_mm {get;set;}
    }
}
