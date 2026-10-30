using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace krypRevit.Addin.Endpoints
{
    /// <summary>
    /// Read-only. Lists walls and/or floors as potential opening hosts.
    /// Returns id, type, level, and the bounding box so the MCP side can check crossings.
    /// </summary>
    public static class ListHosts
    {
        public static string Run(UIApplication app, string type)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return Json.Error("no active document");
            if (doc.IsFamilyDocument) return Json.Error("active document is a family");

            var rows = new List<Dictionary<string, object>>();

            if (type == "all" || type == "wall")
                foreach (var w in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
                    rows.Add(Describe(doc, w, "wall"));

            if (type == "all" || type == "floor")
                foreach (var f in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
                    rows.Add(Describe(doc, f, "floor"));

            return Json.Serialize(new Dictionary<string, object>
            {
                ["ok"] = true,
                ["count"] = rows.Count,
                ["hosts"] = rows
            });
        }

        private static Dictionary<string, object> Describe(Document doc, Element e, string kind)
        {
            var level = doc.GetElement(e.LevelId) as Level;
            var bb = e.get_BoundingBox(null);
            var row = new Dictionary<string, object>
            {
                ["id"] = e.Id.Value.ToString(),
                ["unique_id"] = e.UniqueId,
                ["kind"] = kind,
                ["name"] = e.Name,
                ["type_name"] = doc.GetElement(e.GetTypeId())?.Name,
                ["level"] = level?.Name,
                ["bbox_min_ft"] = bb == null ? null : new[] { bb.Min.X, bb.Min.Y, bb.Min.Z },
                ["bbox_max_ft"] = bb == null ? null : new[] { bb.Max.X, bb.Max.Y, bb.Max.Z }
            };
            return row;
        }
    }
}
