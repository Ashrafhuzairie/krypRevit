using System;
using System.Threading.Tasks;
using System.Text.Json;
namespace krypRevit.Addin
{
    // Only endpoints and Revit-thread dispatch are substituted. HttpServer.cs is production source.
    public class Bridge { public Task<string> Invoke(Func<object,string> work) => Task.FromResult(work(new object())); }
    public static class Json { public static string Serialize(object o)=>JsonSerializer.Serialize(o); public static string Error(string message) => JsonSerializer.Serialize(new {ok=false,error=message}); }
}
namespace krypRevit.Addin.Endpoints
{
    public static class Health { public static string Run(object app) => "{\"ok\":true,\"transport_harness\":true}"; }
    public static class ListHosts { public static string Run(object app,string kind) => JsonSerializer.Serialize(new {ok=true,kind}); }
    public static class Model
    {
        public static string Run(object app,string uid,int limit) => JsonSerializer.Serialize(new {ok=true,uid,limit});
        public static string Search(object app,string query,string category,int limit,bool includeTypes=false) => JsonSerializer.Serialize(new {ok=true,query,category,limit,includeTypes});
    }
    public static class UnitConversion { public static string Run(string body) {using var json=JsonDocument.Parse(body);return JsonSerializer.Serialize(new {ok=true,echo=json.RootElement.Clone()});} }
    public static class Plans
    {
        public static string PlanOpenings(object app,string body)=>Propose(app,body);
        public static string Status() => "{\"ok\":true,\"state\":\"none\"}";
        public static string Propose(object app,string body) { using var json=JsonDocument.Parse(body); return JsonSerializer.Serialize(new {ok=true,echo=json.RootElement.Clone()}); }
    }
}
class Program
{
    static void Main(string[] args)
    {
        var server=new krypRevit.Addin.HttpServer(new krypRevit.Addin.Bridge(),int.Parse(args[0]));
        server.Start(); Console.WriteLine("READY"); Console.Out.Flush(); Console.ReadLine(); server.Stop();
    }
}

namespace krypRevit.Addin.Openings
{
    public static class OpeningRuleStore {public static object Load()=>new {circular=new[]{new {service_mm=50,opening_mm=80}}};}
}
