using System.Collections.Generic;
namespace krypRevit.Addin.Endpoints
{
    public sealed class EditAction
    {
        public string operation { get; set; }
        public string unique_id { get; set; }
        public string service_unique_id {get;set;}
        public string link_unique_id {get;set;}
        public string opening_shape {get;set;}="auto";
        public string parameter_id { get; set; }
        public object value { get; set; }
        public double[] vector_ft { get; set; }
        public double[] origin_ft { get; set; }
        public double[] axis { get; set; }
        public double angle_radians { get; set; }
        public string wall_type_unique_id { get; set; }
        public string floor_type_unique_id { get; set; }
        public string type_unique_id { get; set; }
        public string symbol_unique_id { get; set; }
        public string level_unique_id { get; set; }
        public double[] start_ft { get; set; }
        public double[] end_ft { get; set; }
        public double[] point_ft { get; set; }
        public double[][] boundary_ft { get; set; }
        public double height_ft { get; set; }
        public double? elevation_ft { get; set; }
        public string name { get; set; }
    }
    public sealed class PlanRequest { public List<EditAction> actions { get; set; } }
}
