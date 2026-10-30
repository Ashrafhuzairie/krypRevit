using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace krypRevit.Addin.Endpoints
{
    public static class UnitConversion
    {
        public static string Run(string json)
        {
            var request=Json.Deserialize<Dictionary<string,object>>(json);
            if(request==null || !request.TryGetValue("unit",out var rawUnit) || !(rawUnit is string unit)) throw new ArgumentException("A supported unit is required.");
            if(!request.TryGetValue("values",out var raw) || !(raw is IEnumerable sequence) || raw is string || raw is IDictionary) throw new ArgumentException("values must be a numeric array.");
            var values=sequence.Cast<object>().Select(ModellingRules.Number).ToArray();
            if(values.Length<1 || values.Length>256) throw new ArgumentException("Convert 1-256 values at a time.");
            ForgeTypeId id;string output;
            switch(unit.Trim().ToLowerInvariant())
            {
                case "mm":id=UnitTypeId.Millimeters;output="ft";break;
                case "cm":id=UnitTypeId.Centimeters;output="ft";break;
                case "m":id=UnitTypeId.Meters;output="ft";break;
                case "ft":id=UnitTypeId.Feet;output="ft";break;
                case "in":id=UnitTypeId.Inches;output="ft";break;
                case "deg":id=UnitTypeId.Degrees;output="rad";break;
                case "rad":id=UnitTypeId.Radians;output="rad";break;
                case "m2":id=UnitTypeId.SquareMeters;output="ft2";break;
                case "ft2":id=UnitTypeId.SquareFeet;output="ft2";break;
                case "m3":id=UnitTypeId.CubicMeters;output="ft3";break;
                case "ft3":id=UnitTypeId.CubicFeet;output="ft3";break;
                default:throw new ArgumentException("Supported units: mm, cm, m, ft, in, deg, rad, m2, ft2, m3, ft3.");
            }
            return Json.Serialize(new {ok=true,input_unit=unit,internal_unit=output,values_internal=values.Select(v=>UnitUtils.ConvertToInternalUnits(v,id)).ToArray()});
        }
    }
}
