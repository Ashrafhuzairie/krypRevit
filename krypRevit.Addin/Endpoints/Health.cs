using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace krypRevit.Addin.Endpoints
{
    public static class Health
    {
        /// <summary>Runs on Revit's main thread. Confirms the bridge round trip works.</summary>
        public static string Run(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            return Json.Serialize(new Dictionary<string, object>
            {
                ["ok"] = true,
                ["revit_version"] = app.Application.VersionNumber,
                ["document"] = doc?.Title,
                ["is_family_doc"] = doc?.IsFamilyDocument ?? false
            });
        }
    }
}
