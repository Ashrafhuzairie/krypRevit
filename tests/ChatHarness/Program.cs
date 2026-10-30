using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using krypRevit.Addin.Chat;
using krypRevit.Addin.Endpoints;
namespace krypRevit.Addin
{
    // net8 JSON adapter only. The exact production provider and conversation source is linked above.
    public static class Json
    {
        public static string Serialize(object value)=>JsonSerializer.Serialize(value);
        public static string Error(string message)=>Serialize(new {ok=false,error=message});
        public static T Deserialize<T>(string text)
        {
            using var doc=JsonDocument.Parse(text);
            return (T)Convert(doc.RootElement);
        }
        private static object Convert(JsonElement e)
        {
            switch(e.ValueKind)
            {
                case JsonValueKind.Object:return e.EnumerateObject().ToDictionary(p=>p.Name,p=>Convert(p.Value));
                case JsonValueKind.Array:return e.EnumerateArray().Select(Convert).ToArray();
                case JsonValueKind.String:return e.GetString();
                case JsonValueKind.Number:if(e.TryGetInt32(out var n)) return n;return e.GetDouble();
                case JsonValueKind.True:return true;case JsonValueKind.False:return false;default:return null;
            }
        }
    }
}
class Handler : HttpMessageHandler
{
    public readonly Queue<(HttpStatusCode,string)> Replies=new();
    public readonly List<(string Url,string Body,Dictionary<string,string> Headers)> Requests=new();
    public void Add(object value,HttpStatusCode status=HttpStatusCode.OK)=>Replies.Enqueue((status,krypRevit.Addin.Json.Serialize(value)));
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Requests.Add((request.RequestUri.ToString(),await request.Content.ReadAsStringAsync(token),request.Headers.ToDictionary(h=>h.Key,h=>string.Join(",",h.Value),StringComparer.OrdinalIgnoreCase)));
        var reply=Replies.Dequeue();return new HttpResponseMessage(reply.Item1){Content=new StringContent(reply.Item2)};
    }
}
class Tools : IChatTools
{
    public string Context="document-one";
    public readonly List<string> Names=new();
    public Task<string> ContextAsync(CancellationToken token)=>Task.FromResult(Context);
    public Task<string> ExecuteAsync(string name,Dictionary<string,object> args,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();Names.Add(name);
        return Task.FromResult(name=="propose_actions" ? "{\"ok\":true,\"state\":\"pending_review\"}" : "{\"ok\":true}");
    }
}
class ModellingTools : IChatTools
{
    public readonly List<string> Names=new();
    public PlanRequest Proposal;
    public Task<string> ContextAsync(CancellationToken token)=>Task.FromResult("fixture-project");
    public Task<string> ExecuteAsync(string name,Dictionary<string,object> args,CancellationToken token)
    {
        Names.Add(name);
        if(name=="inspect_model") return Task.FromResult("{\"ok\":true,\"levels\":[{\"unique_id\":\"level-fixture\",\"elevation_ft\":0}],\"wall_types\":[{\"unique_id\":\"wall-fixture\"}],\"floor_types\":[{\"unique_id\":\"floor-fixture\"}]}");
        if(name=="convert_units") return Task.FromResult("{\"ok\":true,\"internal_unit\":\"ft\",\"values_internal\":[16.404199475,13.12335958,9.842519685]}");
        if(name=="propose_actions")
        {
            var normalized=ToolCatalog.NormalizeProposal(args);
            Proposal=JsonSerializer.Deserialize<PlanRequest>(krypRevit.Addin.Json.Serialize(normalized));
            foreach(var action in Proposal.actions) ModellingRules.Validate(action,.0026);
            return Task.FromResult("{\"ok\":true,\"state\":\"pending_review\"}");
        }
        throw new Exception("Unexpected operation "+name);
    }
}
class Program
{
    static int passed;
    static void Check(bool condition,string message) {if(!condition) throw new Exception(message);}
    static async Task Test(string name,Func<Task> action) {await action();Console.WriteLine("PASS "+name);passed++;}
    static object Obj(params object[] p)=>Data.Obj(p);
    static object OpenCall(string name)=>Obj("choices",new[]{Obj("message",Obj("content","","tool_calls",new[]{Obj("id","c1","type","function","function",Obj("name",name,"arguments","{}"))}))});
    static object OpenText(string text)=>Obj("choices",new[]{Obj("message",Obj("content",text))});
    static async Task Main()
    {
        await Test("OpenAI native tool roundtrip stages only, never applies",async()=>{
            var h=new Handler();h.Add(OpenCall("propose_actions"));h.Add(OpenText("Review the proposal in Revit."));
            using var p=new ProviderClient(h);var tools=new Tools();var runner=new ChatRunner(p,tools);
            await runner.RunAsync("OpenAI","gpt-4.1-mini","test-only","Propose a move",(_,_)=>{},CancellationToken.None);
            Check(tools.Names.SequenceEqual(new[]{"propose_actions"}),"Unexpected tool execution");
            using var j=JsonDocument.Parse(h.Requests[1].Body);
            var m=j.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Check(m.Last().GetProperty("role").GetString()=="tool","Missing tool result");
            Check(m.Last().GetProperty("tool_call_id").GetString()=="c1","Tool ID lost");
            Check(h.Requests[0].Headers["Authorization"]=="Bearer test-only","Wrong auth");
            Check(h.Requests[0].Url=="https://api.openai.com/v1/chat/completions","Wrong endpoint");
        });
        await Test("Claude native function and result grouping",async()=>{
            var h=new Handler();h.Add(Obj("content",new[]{Obj("type","tool_use","id","c1","name","revit_health","input",Obj()),Obj("type","tool_use","id","c2","name","plan_status","input",Obj())}));
            h.Add(Obj("content",new[]{Obj("type","text","text","Ready.")}));
            using var p=new ProviderClient(h);var t=new Tools();await new ChatRunner(p,t).RunAsync("Claude","model","test-only","Inspect",(_,_)=>{},CancellationToken.None);
            using var j=JsonDocument.Parse(h.Requests[1].Body);var messages=j.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Check(messages.Length==3,"Parallel results were not grouped");
            Check(messages.Last().GetProperty("content").GetArrayLength()==2,"Lost result");
            Check(h.Requests[0].Headers["x-api-key"]=="test-only" && h.Requests[0].Headers.ContainsKey("anthropic-version"),"Claude auth/version missing");
        });
        await Test("Gemini native signature preserved and key not put in URL",async()=>{
            var h=new Handler();h.Add(Obj("candidates",new[]{Obj("content",Obj("parts",new[]{Obj("functionCall",Obj("name","revit_health","args",Obj()),"thoughtSignature","opaque-test")}))}));
            h.Add(Obj("candidates",new[]{Obj("content",Obj("parts",new[]{Obj("text","Ready.")}))}));
            using var p=new ProviderClient(h);await new ChatRunner(p,new Tools()).RunAsync("Gemini","gemini-2.5-flash","test-only","Inspect",(_,_)=>{},CancellationToken.None);
            Check(!h.Requests[0].Url.Contains("test-only") && h.Requests[0].Headers["x-goog-api-key"]=="test-only","Key leaked to URL or auth missing");
            using var j=JsonDocument.Parse(h.Requests[1].Body);var contents=j.RootElement.GetProperty("contents").EnumerateArray().ToArray();
            Check(contents[1].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString()=="opaque-test","Signature lost");
            Check(contents[2].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("name").GetString()=="revit_health","Function response missing");
        });
        await Test("Unregistered apply tool is never executed",async()=>{
            var h=new Handler();h.Add(OpenCall("apply_actions"));h.Add(OpenText("Cannot apply remotely."));var t=new Tools();
            using var p=new ProviderClient(h);await new ChatRunner(p,t).RunAsync("OpenAI","model","test-only","Apply",(_,_)=>{},CancellationToken.None);
            Check(t.Names.Count==0,"Unregistered tool ran");
            Check(!ToolCatalog.All.Any(x=>x.Name.Contains("apply") || x.Name.Contains("review")),"AI can approve changes");
        });
        await Test("Parameter numeric normalization keeps numeric type",()=>{
            var a=ToolCatalog.NormalizeProposal(Data.Obj("actions",new[]{Data.Obj("operation","set_parameter","value_number",1.5)}));
            var item=Data.Map(Data.Items(a["actions"]).First());Check(item["value"] is double && (double)item["value"]==1.5,"Numeric type changed");
            Check(!item.ContainsKey("value_number"),"Transport field not removed");return Task.CompletedTask;
        });
        await Test("Parameter text normalization keeps text",()=>{
            var a=ToolCatalog.NormalizeProposal(Data.Obj("actions",new[]{Data.Obj("operation","set_parameter","value_text","Reviewed")}));
            Check((string)Data.Map(Data.Items(a["actions"]).First())["value"]=="Reviewed","Text changed");return Task.CompletedTask;
        });
        await Test("Ambiguous parameter values rejected",()=>{
            try {ToolCatalog.NormalizeProposal(Data.Obj("actions",new[]{Data.Obj("operation","set_parameter","value_text","x","value_number",1)}));throw new Exception("Accepted both");}
            catch(ArgumentException){} return Task.CompletedTask;
        });
        await Test("Oversized proposal rejected",()=>{
            try {ToolCatalog.NormalizeProposal(Data.Obj("actions",Enumerable.Range(0,11).Select(_=>Data.Obj("operation","move")).ToArray()));throw new Exception("Accepted oversized");}
            catch(ArgumentException){}return Task.CompletedTask;
        });
        await Test("Provider error redacts API key",async()=>{
            var h=new Handler();h.Add(Obj("error",Obj("message","Rejected test-only")),HttpStatusCode.Unauthorized);using var p=new ProviderClient(h);
            try {await p.SendAsync("OpenAI","model","test-only","system",new[]{new ChatMessage{Role="user",Text="hello"}},ToolCatalog.All,CancellationToken.None);throw new Exception("Accepted 401");}
            catch(InvalidOperationException ex) {Check(!ex.Message.Contains("test-only") && ex.Message.Contains("401"),"Key exposed or HTTP status lost");}
        });
        await Test("Failed exchange removed before next send",async()=>{
            var h=new Handler();h.Add(Obj("error",Obj("message","busy")),HttpStatusCode.TooManyRequests);h.Add(OpenText("Ready."));using var p=new ProviderClient(h);var r=new ChatRunner(p,new Tools());
            try {await r.RunAsync("OpenAI","model","test-only","first",(_,_)=>{},CancellationToken.None);} catch(InvalidOperationException){}
            await r.RunAsync("OpenAI","model","test-only","second",(_,_)=>{},CancellationToken.None);
            using var j=JsonDocument.Parse(h.Requests[1].Body);Check(j.RootElement.GetProperty("messages").GetArrayLength()==2,"Failed turn retained");
        });
        await Test("Changed document clears previous model context",async()=>{
            var h=new Handler();h.Add(OpenText("First."));h.Add(OpenText("Second."));using var p=new ProviderClient(h);var t=new Tools();var r=new ChatRunner(p,t);
            await r.RunAsync("OpenAI","model","test-only","first",(_,_)=>{},CancellationToken.None);t.Context="document-two";
            await r.RunAsync("OpenAI","model","test-only","second",(_,_)=>{},CancellationToken.None);
            using var j=JsonDocument.Parse(h.Requests[1].Body);Check(j.RootElement.GetProperty("messages").GetArrayLength()==2,"Old document history retained");
        });
        await Test("Cancelled request never reaches provider",async()=>{
            var h=new Handler();using var p=new ProviderClient(h);var r=new ChatRunner(p,new Tools());using var c=new CancellationTokenSource();c.Cancel();
            try {await r.RunAsync("OpenAI","model","test-only","hello",(_,_)=>{},c.Token);throw new Exception("Not cancelled");}catch(OperationCanceledException){}
            Check(h.Requests.Count==0,"Provider reached after cancellation");
        });
        await Test("Empty model response is a clear failure",async()=>{
            var h=new Handler();h.Add(OpenText(""));using var p=new ProviderClient(h);
            try {await new ChatRunner(p,new Tools()).RunAsync("OpenAI","model","test-only","hello",(_,_)=>{},CancellationToken.None);throw new Exception("Empty answer accepted");}
            catch(InvalidOperationException ex){Check(ex.Message.Contains("no answer"),"Unhelpful failure");}
        });
        await Test("Tool-loop budget terminates repeated requests",async()=>{
            var h=new Handler();for(int i=0;i<6;i++) h.Add(OpenCall("revit_health"));using var p=new ProviderClient(h);var t=new Tools();
            await new ChatRunner(p,t).RunAsync("OpenAI","model","test-only","inspect",(_,_)=>{},CancellationToken.None);Check(t.Names.Count==6,"Wrong round budget");
        });
        await Test("all modelling operations and unit/search tools exposed",()=>{
            var definition=ToolCatalog.All.Single(t=>t.Name=="propose_actions");
            using var doc=JsonDocument.Parse(krypRevit.Addin.Json.Serialize(definition.Schema));
            var names=doc.RootElement.GetProperty("properties").GetProperty("actions").GetProperty("items").GetProperty("properties").GetProperty("operation").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()).ToArray();
            Check(names.SequenceEqual(ModellingRules.OperationNames),"Schema/backend drift");
            Check(ToolCatalog.All.Any(t=>t.Name=="convert_units") && ToolCatalog.All.Any(t=>t.Name=="find_elements"),"Missing modelling context tools");
            return Task.CompletedTask;
        });
        await Test("metric room prompt pipeline stages four walls and floor without applying",async()=>{
            object Call(string name,object args)=>Obj("choices",new[]{Obj("message",Obj("content","","tool_calls",new[]{Obj("id",Guid.NewGuid().ToString("N"),"type","function","function",Obj("name",name,"arguments",krypRevit.Addin.Json.Serialize(args)))}))});
            double x=16.404199475,y=13.12335958,hgt=9.842519685;
            double[][] points={new[]{0d,0d,0d},new[]{x,0d,0d},new[]{x,y,0d},new[]{0d,y,0d}};
            var actions=new List<object>();
            for(int i=0;i<4;i++) actions.Add(Obj("operation","create_wall","wall_type_unique_id","wall-fixture","level_unique_id","level-fixture","start_ft",points[i],"end_ft",points[(i+1)%4],"height_ft",hgt));
            actions.Add(Obj("operation","create_floor","floor_type_unique_id","floor-fixture","level_unique_id","level-fixture","boundary_ft",points));
            var handler=new Handler();handler.Add(Call("inspect_model",Obj()));handler.Add(Call("convert_units",Obj("unit","m","values",new[]{5d,4d,3d})));handler.Add(Call("propose_actions",Obj("actions",actions)));handler.Add(OpenText("The four walls and floor are proposed. Click Review & apply."));
            using var provider=new ProviderClient(handler);var tools=new ModellingTools();
            await new ChatRunner(provider,tools).RunAsync("OpenAI","model","test-only","Create a 5m by 4m wall-centerline rectangle with 3m walls and a floor on the inspected level at internal origin (0,0,0), aligned with internal X/Y, using existing types.",(_,_)=>{},CancellationToken.None);
            Check(tools.Names.SequenceEqual(new[]{"inspect_model","convert_units","propose_actions"}),"Wrong pipeline or automatic apply");
            Check(tools.Proposal.actions.Count==5 && tools.Proposal.actions.Count(a=>a.operation=="create_wall")==4,"Model actions lost");
            Check(tools.Proposal.actions.All(a=>a.level_unique_id=="level-fixture"),"Did not preserve inspected level ID");
            Check(Math.Abs(tools.Proposal.actions[0].height_ft-hgt)<1e-9,"Height changed in pipeline");
        });
        Console.WriteLine($"CHAT TESTS PASSED: {passed}");
    }
}
