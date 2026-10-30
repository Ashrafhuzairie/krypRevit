using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace krypRevit.Addin.Chat
{
    public sealed class ProviderClient : IChatProvider, IDisposable
    {
        private readonly HttpClient client;
        public ProviderClient() : this(new HttpClientHandler { AllowAutoRedirect=false }) { }
        public ProviderClient(HttpMessageHandler handler) { client=new HttpClient(handler) { Timeout=TimeSpan.FromSeconds(90) }; }
        public void Dispose() { client.Dispose(); }
        public static string DefaultModel(string service)
        {
            switch(service) { case "Claude": return "claude-sonnet-4-5"; case "Gemini": return "gemini-2.5-flash"; default: return "gpt-4.1-mini"; }
        }
        public async Task<ProviderReply> SendAsync(string service,string model,string key,string system,IReadOnlyList<ChatMessage> messages,IReadOnlyList<ToolDefinition> tools,CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Enter an API key for the selected provider.");
            if (string.IsNullOrWhiteSpace(model) || model.Length>200) throw new ArgumentException("Enter a valid model name enabled for your provider account.");
            string url; object payload;
            switch(service)
            {
                case "OpenAI": url="https://api.openai.com/v1/chat/completions";payload=OpenAIRequest(model,system,messages,tools);break;
                case "Claude": url="https://api.anthropic.com/v1/messages";payload=ClaudeRequest(model,system,messages,tools);break;
                case "Gemini":
                    var id=model.StartsWith("models/",StringComparison.Ordinal) ? model.Substring(7) : model;
                    url="https://generativelanguage.googleapis.com/v1beta/models/"+Uri.EscapeDataString(id)+":generateContent";
                    payload=GeminiRequest(system,messages,tools);break;
                default: throw new ArgumentException("Unknown provider.");
            }
            using(var request=new HttpRequestMessage(HttpMethod.Post,url))
            {
                if(service=="OpenAI") request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key.Trim());
                else if(service=="Claude") { request.Headers.Add("x-api-key",key.Trim());request.Headers.Add("anthropic-version","2023-06-01"); }
                else request.Headers.Add("x-goog-api-key",key.Trim());
                request.Content=new StringContent(Json.Serialize(payload),Encoding.UTF8,"application/json");
                using(var response=await client.SendAsync(request,token).ConfigureAwait(false))
                {
                    var body=await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if(!response.IsSuccessStatusCode)
                    {
                        string detail="Check the API key, model availability, quota and company network access.";
                        try
                        {
                            var error=Data.Get(Json.Deserialize<Dictionary<string,object>>(body),"error") as Dictionary<string,object>;
                            var message=error==null ? "" : Data.Text(error,"message");
                            if(message.Length>0) detail=message.Replace(key,"[redacted]");
                        } catch { }
                        if(detail.Length>600) detail=detail.Substring(0,600);
                        throw new InvalidOperationException($"HTTP {(int)response.StatusCode} from {service}: {detail}");
                    }
                    Dictionary<string,object> data;
                    try { data=Json.Deserialize<Dictionary<string,object>>(body); }
                    catch { throw new InvalidOperationException(service+" returned invalid or oversized JSON."); }
                    if(data==null) throw new InvalidOperationException(service+" returned an empty response.");
                    switch(service)
                    {
                        case "OpenAI": return ReadOpenAI(data);
                        case "Claude": return ReadClaude(data);
                        default: return ReadGemini(data);
                    }
                }
            }
        }
        private static object OpenAIRequest(string model,string system,IReadOnlyList<ChatMessage> history,IReadOnlyList<ToolDefinition> tools)
        {
            var messages=new List<object> { Data.Obj("role","system","content",system) };
            foreach(var m in history)
            {
                var row=Data.Obj("role",m.Role,"content",m.Text ?? "");
                if(m.Role=="tool") row["tool_call_id"]=m.ToolId;
                if(m.Calls.Count>0) row["tool_calls"]=m.Calls.Select(c => Data.Obj("id",c.Id,"type","function","function",Data.Obj("name",c.Name,"arguments",Json.Serialize(c.Arguments)))).ToArray();
                messages.Add(row);
            }
            return Data.Obj("model",model,"messages",messages,"max_completion_tokens",2048,"tools",tools.Select(t => Data.Obj("type","function","function",Data.Obj("name",t.Name,"description",t.Description,"parameters",t.Schema))).ToArray());
        }
        private static object ClaudeRequest(string model,string system,IReadOnlyList<ChatMessage> history,IReadOnlyList<ToolDefinition> tools)
        {
            var messages=new List<object>(); List<object> results=null;
            foreach(var m in history)
            {
                if(m.Role=="tool")
                {
                    if(results==null) { results=new List<object>();messages.Add(Data.Obj("role","user","content",results)); }
                    results.Add(Data.Obj("type","tool_result","tool_use_id",m.ToolId,"content",m.Text ?? ""));
                    continue;
                }
                results=null;
                var content=new List<object>();
                if(!string.IsNullOrEmpty(m.Text)) content.Add(Data.Obj("type","text","text",m.Text));
                content.AddRange(m.Calls.Select(c => (object)Data.Obj("type","tool_use","id",c.Id,"name",c.Name,"input",c.Arguments)));
                messages.Add(Data.Obj("role",m.Role,"content",content));
            }
            return Data.Obj("model",model,"system",system,"messages",messages,"max_tokens",2048,"tools",tools.Select(t => Data.Obj("name",t.Name,"description",t.Description,"input_schema",t.Schema)).ToArray());
        }
        private static object GeminiRequest(string system,IReadOnlyList<ChatMessage> history,IReadOnlyList<ToolDefinition> tools)
        {
            var contents=new List<object>(); List<object> results=null;
            foreach(var m in history)
            {
                if(m.Role=="tool")
                {
                    if(results==null) { results=new List<object>();contents.Add(Data.Obj("role","user","parts",results)); }
                    object result;
                    try { result=Json.Deserialize<Dictionary<string,object>>(m.Text); } catch { result=Data.Obj("text",m.Text); }
                    results.Add(Data.Obj("functionResponse",Data.Obj("name",m.ToolName,"response",result)));
                    continue;
                }
                results=null;
                var parts=new List<object>();
                if(!string.IsNullOrEmpty(m.Text)) parts.Add(Data.Obj("text",m.Text));
                parts.AddRange(m.Calls.Select(c => c.NativePart ?? Data.Obj("functionCall",Data.Obj("name",c.Name,"args",c.Arguments))));
                contents.Add(Data.Obj("role",m.Role=="assistant" ? "model" : "user","parts",parts));
            }
            return Data.Obj("systemInstruction",Data.Obj("parts",new[]{Data.Obj("text",system)}),"contents",contents,"generationConfig",Data.Obj("maxOutputTokens",2048),
                "tools",new[]{Data.Obj("functionDeclarations",tools.Select(t => Data.Obj("name",t.Name,"description",t.Description,"parameters",t.Schema)).ToArray())});
        }
        private static ProviderReply ReadOpenAI(Dictionary<string,object> data)
        {
            var choice=Data.Items(Data.Get(data,"choices")).FirstOrDefault();
            if(choice==null) throw new InvalidOperationException("OpenAI returned no choices. Check the model name and provider account.");
            var message=Data.Map(Data.Get(Data.Map(choice),"message"));
            var reply=new ProviderReply { Text=Data.Text(message,"content") };
            foreach(var value in Data.Items(Data.Get(message,"tool_calls")))
            {
                var call=Data.Map(value);var function=Data.Map(Data.Get(call,"function"));
                var id=Data.Text(call,"id");
                reply.Calls.Add(new ToolCall {Id=string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,Name=Data.Text(function,"name"),Arguments=Json.Deserialize<Dictionary<string,object>>(Data.Text(function,"arguments")) ?? Data.Obj()});
            }
            return reply;
        }
        private static ProviderReply ReadClaude(Dictionary<string,object> data)
        {
            var reply=new ProviderReply();var text=new List<string>();
            foreach(var value in Data.Items(Data.Get(data,"content")))
            {
                var part=Data.Map(value);
                if(Data.Text(part,"type")=="text") text.Add(Data.Text(part,"text"));
                if(Data.Text(part,"type")=="tool_use") reply.Calls.Add(new ToolCall {Id=Data.Text(part,"id"),Name=Data.Text(part,"name"),Arguments=Data.Map(Data.Get(part,"input"))});
            }
            reply.Text=string.Join("\n",text);return reply;
        }
        private static ProviderReply ReadGemini(Dictionary<string,object> data)
        {
            var candidate=Data.Items(Data.Get(data,"candidates")).FirstOrDefault();
            if(candidate==null) throw new InvalidOperationException("Gemini returned no candidate. Check the model, provider account or content restrictions.");
            var content=Data.Get(Data.Map(candidate),"content") as Dictionary<string,object>;
            if(content==null) throw new InvalidOperationException("Gemini returned no content.");
            var reply=new ProviderReply();var text=new List<string>();
            foreach(var value in Data.Items(Data.Get(content,"parts")))
            {
                var part=Data.Map(value);
                if(!(Data.Get(part,"thought") is bool thought && thought) && Data.Text(part,"text").Length>0) text.Add(Data.Text(part,"text"));
                var call=Data.Get(part,"functionCall") as Dictionary<string,object>;
                if(call!=null) reply.Calls.Add(new ToolCall {Name=Data.Text(call,"name"),Arguments=Data.Get(call,"args") as Dictionary<string,object> ?? Data.Obj(),NativePart=part});
            }
            reply.Text=string.Join("\n",text);return reply;
        }
    }
}
