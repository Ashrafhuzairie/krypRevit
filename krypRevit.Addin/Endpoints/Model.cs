using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace krypRevit.Addin.Endpoints
{
    public static class Model
    {
        public static Document Document(UIApplication app)
        {
            var doc=app.ActiveUIDocument?.Document;
            if(doc==null || doc.IsFamilyDocument || doc.IsReadOnly || doc.IsModifiable) throw new InvalidOperationException("Open an editable project document with no active transaction.");
            return doc;
        }
        public static Element Element(Document doc,string uid)
        {
            var reference=ElementReference.Parse(uid);Element result;
            try {result=reference.NumericId.HasValue ? doc.GetElement(new ElementId(reference.NumericId.Value)) : doc.GetElement(reference.Text);}
            catch(ArgumentException) {throw new ArgumentException("Invalid Revit element reference. Supply a numeric ElementId or an actual UniqueId, not a name search.");}
            if(result==null) throw new ArgumentException((reference.NumericId.HasValue ? "ElementId "+reference.NumericId.Value : "UniqueId "+reference.Text)+" was not found in the active project '"+doc.Title+"'. IDs are document-specific; the element may be in another/linked model or may have been deleted. No target was substituted.");
            return result;
        }
        public static bool CanPlace(FamilySymbol symbol)
        {
            if(symbol==null || symbol.Family.IsInPlace || symbol.Family.FamilyPlacementType!=FamilyPlacementType.OneLevelBased || symbol.Category==null || symbol.Category.CategoryType!=CategoryType.Model) return false;
            long category=symbol.Category.Id.Value;
            return category!=(long)BuiltInCategory.OST_StructuralColumns && category!=(long)BuiltInCategory.OST_StructuralFraming && category!=(long)BuiltInCategory.OST_StructuralFoundation;
        }
        public static string Run(UIApplication app,string uid,int limit)
        {
            var doc=Document(app);
            if(!string.IsNullOrWhiteSpace(uid))
            {
                var e=Element(doc,uid);
                var parameters=e.Parameters.Cast<Parameter>().Select(p=>new {
                    parameter_id=p.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),name=p.Definition.Name,
                    storage=p.StorageType.ToString(),read_only=p.IsReadOnly,value=Value(p),display=p.AsValueString(),
                    data_type=p.Definition.GetDataType().TypeId,display_unit_type=DisplayUnit(p)
                }).ToArray();
                return Json.Serialize(new {ok=true,document=doc.Title,requested_reference=uid,resolved_by=ElementReference.Parse(uid).NumericId.HasValue ? "element_id" : "unique_id",element=Describe(e),parameters,
                    valid_types=e.GetValidTypes().Take(200).Select(id=>doc.GetElement(id)).Where(t=>t!=null).Select(t=>new {unique_id=t.UniqueId,name=t.Name}).ToArray()});
            }
            var selected=app.ActiveUIDocument.Selection.GetElementIds();
            IEnumerable<Element> elements=selected.Count>0 ? selected.Select(id=>doc.GetElement(id)) :
                new FilteredElementCollector(doc).WhereElementIsNotElementType().WhereElementIsViewIndependent().Cast<Element>();
            var rows=elements.Where(e=>e!=null).Take(limit+1).ToArray();
            return Json.Serialize(new {ok=true,scope=selected.Count>0 ? "selection" : "model",limit,truncated=rows.Length>limit,
                elements=rows.Take(limit).Select(Describe).ToArray(),
                levels=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Take(500).Select(e=>new {unique_id=e.UniqueId,name=e.Name,elevation_ft=e.ProjectElevation,display_elevation_ft=e.Elevation}).ToArray(),
                wall_types=new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Where(t=>t.Kind==WallKind.Basic).Take(200).Select(t=>new {unique_id=t.UniqueId,name=t.Name,width_ft=t.Width}).ToArray(),
                floor_types=new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().Where(t=>!t.IsFoundationSlab).Take(200).Select(t=>new {unique_id=t.UniqueId,name=t.Name,thickness_ft=t.GetCompoundStructure()?.GetWidth()}).ToArray(),
                family_symbols=new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(CanPlace)
                    .Take(200).Select(t=>new {unique_id=t.UniqueId,name=t.Name,family=t.FamilyName,category=t.Category.Name}).ToArray(),
                catalog_note="Catalogs are bounded: 500 levels, 200 types/symbols each. Family placement supports loaded, unhosted one-level nonstructural families only. Use find_elements for target instances."});
        }
        public static string Search(UIApplication app,string query,string category,int limit,bool includeTypes=false)
        {
            if(limit<1 || limit>500) throw new ArgumentException("Limit must be 1-500.");
            if((query ?? "").Length>200 || (category ?? "").Length>200) throw new ArgumentException("Search strings must be at most 200 characters.");
            var doc=Document(app);
            if(!string.IsNullOrWhiteSpace(query) && query.Trim().All(c=>c>='0' && c<='9'))
            {
                var exact=Element(doc,query);
                bool matchesScope=includeTypes ? exact is ElementType : !(exact is ElementType);
                bool matchesCategory=string.IsNullOrWhiteSpace(category) || (exact.Category?.Name ?? "").IndexOf(category,StringComparison.OrdinalIgnoreCase)>=0;
                return Json.Serialize(new {ok=true,resolved_by="element_id",count=matchesScope && matchesCategory ? 1 : 0,truncated=false,elements=matchesScope && matchesCategory ? new[]{Describe(exact)} : new object[0]});
            }
            var collector=new FilteredElementCollector(doc);
            IEnumerable<Element> candidates=includeTypes ? collector.WhereElementIsElementType().Cast<Element>() : collector.WhereElementIsNotElementType().WhereElementIsViewIndependent().Cast<Element>();
            var matches=candidates
                .Where(e=>string.IsNullOrWhiteSpace(category) || (e.Category?.Name ?? "").IndexOf(category,StringComparison.OrdinalIgnoreCase)>=0)
                .Where(e=>string.IsNullOrWhiteSpace(query) || e.Name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 || (doc.GetElement(e.GetTypeId())?.Name ?? "").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 || ((e as ElementType)?.FamilyName ?? "").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)
                .Take(limit+1).ToArray();
            return Json.Serialize(new {ok=true,scope=includeTypes ? "types" : "instances",count=Math.Min(matches.Length,limit),truncated=matches.Length>limit,elements=matches.Take(limit).Select(Describe).ToArray()});
        }
        private static string DisplayUnit(Parameter p)
        {
            if(p.StorageType!=StorageType.Double || !UnitUtils.IsMeasurableSpec(p.Definition.GetDataType())) return null;
            try {return p.GetUnitTypeId().TypeId;} catch {return null;}
        }
        public static double[] Coordinates(XYZ p)=>new[]{p.X,p.Y,p.Z};
        public static object Describe(Element e)
        {
            var box=e.get_BoundingBox(null);var point=e.Location as LocationPoint;var curve=(e.Location as LocationCurve)?.Curve;
            return new {unique_id=e.UniqueId,id=e.Id.Value.ToString(),name=e.Name,category=e.Category?.Name,
                type_thickness_ft=TypeThickness(e),type_unique_id=e.Document.GetElement(e.GetTypeId())?.UniqueId,type_name=e.Document.GetElement(e.GetTypeId())?.Name,
                level_unique_id=e.Document.GetElement(e.LevelId)?.UniqueId,pinned=e.Pinned,
                point_ft=point==null ? null : Coordinates(point.Point),
                curve_start_ft=curve==null || !curve.IsBound ? null : Coordinates(curve.GetEndPoint(0)),
                curve_end_ft=curve==null || !curve.IsBound ? null : Coordinates(curve.GetEndPoint(1)),
                bbox_min_ft=box==null ? null : Coordinates(box.Min),bbox_max_ft=box==null ? null : Coordinates(box.Max)};
        }
        private static double? TypeThickness(Element element)
        {
            var type=element as ElementType ?? element.Document.GetElement(element.GetTypeId()) as ElementType;
            if(type is WallType wall) return wall.Width;
            if(type is FloorType floor) return floor.GetCompoundStructure()?.GetWidth();
            return null;
        }
        public static object Value(Parameter p)
        {
            if(!p.HasValue) return null;
            switch(p.StorageType)
            {
                case StorageType.String:return p.AsString();case StorageType.Integer:return p.AsInteger();
                case StorageType.Double:return p.AsDouble();case StorageType.ElementId:return p.AsElementId().Value.ToString();default:return null;
            }
        }
    }
}
