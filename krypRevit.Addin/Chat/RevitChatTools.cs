using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using krypRevit.Addin.Endpoints;
using krypRevit.Addin.Openings;

namespace krypRevit.Addin.Chat
{
    public sealed class RevitChatTools : IChatTools
    {
        private readonly Bridge bridge;
        private Autodesk.Revit.DB.Document expectedDocument;
        public RevitChatTools(Bridge bridge) { this.bridge=bridge ?? throw new ArgumentNullException(nameof(bridge)); }
        public Task<string> ContextAsync(CancellationToken token) => bridge.Invoke(app => {
            token.ThrowIfCancellationRequested();
            var doc=app.ActiveUIDocument?.Document;expectedDocument=doc;
            return Json.Serialize(new { document=doc?.Title, session=doc?.GetHashCode().ToString("X"), revit=app.Application.VersionNumber });
        });
        public Task<string> ExecuteAsync(string name,Dictionary<string,object> args,CancellationToken token) => bridge.Invoke(app => {
            token.ThrowIfCancellationRequested();
            if(!ReferenceEquals(app.ActiveUIDocument?.Document,expectedDocument))throw new InvalidOperationException("The active document changed during this request. Send the request again in the intended document.");
            args=args ?? Data.Obj();
            switch(name)
            {
                case "revit_health": return Health.Run(app);
                case "list_hosts":
                    var kind=Data.Text(args,"kind");if(kind.Length==0) kind="all";
                    if(kind!="all" && kind!="wall" && kind!="floor") throw new ArgumentException("Invalid host kind.");
                    return ListHosts.Run(app,kind);
                case "inspect_model":
                    int limit=100;
                    if(args.TryGetValue("limit",out var value))
                    {
                        if(!(value is int || value is long || value is decimal || value is double)) throw new ArgumentException("Limit must be an integer.");
                        var number=Convert.ToDouble(value);
                        if(double.IsNaN(number) || number<1 || number>500 || number!=Math.Truncate(number)) throw new ArgumentException("Limit must be 1-500.");
                        limit=(int)number;
                    }
                    return Model.Run(app,null,limit);
                case "get_element_by_id":
                    var numeric=Reference(args,"element_id");
                    if(!ElementReference.Parse(numeric).NumericId.HasValue) throw new ArgumentException("element_id must be numeric digits.");
                    return Model.Run(app,numeric,1);
                case "inspect_element":
                    var uid=Reference(args,"unique_id");
                    return Model.Run(app,uid,1);
                case "find_elements":
                    int searchLimit=100;
                    if(args.TryGetValue("limit",out var requested)) {var n=ModellingRules.Number(requested);if(n<1 || n>500 || n!=Math.Truncate(n)) throw new ArgumentException("Limit must be 1-500.");searchLimit=(int)n;}
                    if(args.TryGetValue("include_types",out var include) && !(include is bool)) throw new ArgumentException("include_types must be boolean.");
                    return Model.Search(app,Data.Text(args,"query"),Data.Text(args,"category"),searchLimit,include is bool includeTypes && includeTypes);
                case "get_opening_rules":return Json.Serialize(new {ok=true,rules=OpeningRuleStore.Load()});
                case "plan_service_openings":return Plans.PlanOpenings(app,Json.Serialize(args));
                case "convert_units":return UnitConversion.Run(Json.Serialize(args));
                case "propose_actions": return Plans.Propose(app,Json.Serialize(ToolCatalog.NormalizeProposal(args)));
                case "plan_status": return Plans.Status();
                default: throw new ArgumentException("Tool not allowed: "+name);
            }
        });
        private static string Reference(Dictionary<string,object> args,string key)
        {
            var value=Data.Get(args,key);
            if(value is string text) return text;
            if(value is int || value is long) return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
            throw new ArgumentException(key+" must be a numeric ElementId string or an actual UniqueId (no floating-point IDs).");
        }
        public Task<string> PlanStatusAsync() => bridge.Invoke(app=>Plans.Status());
        public Task<string> ShowIn3DAsync(CancellationToken token) => bridge.Invoke(app=>{token.ThrowIfCancellationRequested();return Plans.ShowIn3D(app);});
        // This entry point belongs only to the human-operated chat button; it is not in ToolCatalog.
        public Task<string> ReviewAsync(CancellationToken token) => bridge.Invoke(app => { token.ThrowIfCancellationRequested();Plans.Review(app);return Plans.Status(); });
    }
}
