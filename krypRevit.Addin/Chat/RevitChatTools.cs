using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using krypRevit.Addin.Endpoints;

namespace krypRevit.Addin.Chat
{
    public sealed class RevitChatTools : IChatTools
    {
        private readonly Bridge bridge;
        public RevitChatTools(Bridge bridge) { this.bridge=bridge ?? throw new ArgumentNullException(nameof(bridge)); }
        public Task<string> ContextAsync(CancellationToken token) => bridge.Invoke(app => {
            token.ThrowIfCancellationRequested();
            var doc=app.ActiveUIDocument?.Document;
            return Json.Serialize(new { document=doc?.Title, session=doc?.GetHashCode().ToString("X"), revit=app.Application.VersionNumber });
        });
        public Task<string> ExecuteAsync(string name,Dictionary<string,object> args,CancellationToken token) => bridge.Invoke(app => {
            token.ThrowIfCancellationRequested();
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
                case "inspect_element":
                    var uid=Data.Text(args,"unique_id");if(uid.Length==0) throw new ArgumentException("Element unique_id is required.");
                    return Model.Run(app,uid,1);
                case "find_elements":
                    int searchLimit=100;
                    if(args.TryGetValue("limit",out var requested)) {var n=ModellingRules.Number(requested);if(n<1 || n>500 || n!=Math.Truncate(n)) throw new ArgumentException("Limit must be 1-500.");searchLimit=(int)n;}
                    if(args.TryGetValue("include_types",out var include) && !(include is bool)) throw new ArgumentException("include_types must be boolean.");
                    return Model.Search(app,Data.Text(args,"query"),Data.Text(args,"category"),searchLimit,include is bool includeTypes && includeTypes);
                case "convert_units":return UnitConversion.Run(Json.Serialize(args));
                case "propose_actions": return Plans.Propose(app,Json.Serialize(ToolCatalog.NormalizeProposal(args)));
                case "plan_status": return Plans.Status();
                default: throw new ArgumentException("Tool not allowed: "+name);
            }
        });
        public Task<string> ShowIn3DAsync(CancellationToken token) => bridge.Invoke(app=>{token.ThrowIfCancellationRequested();return Plans.ShowIn3D(app);});
        // This entry point belongs only to the human-operated chat button; it is not in ToolCatalog.
        public Task<string> ReviewAsync(CancellationToken token) => bridge.Invoke(app => { token.ThrowIfCancellationRequested();Plans.Review(app);return Plans.Status(); });
    }
}
