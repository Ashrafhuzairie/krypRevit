using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using krypRevit.Addin.Endpoints;
using krypRevit.Addin.Openings;
namespace krypRevit.Addin
{
    // User-mode loopback HTTP transport: no HTTP.sys registration / URL ACL is needed.
    // One request per connection, bounded workers/headers/body and socket timeouts.
    public sealed class HttpServer
    {
        private readonly Bridge bridge;
        private readonly TcpListener listener;
        private readonly int port;
        private readonly SemaphoreSlim workers = new SemaphoreSlim(8);
        private readonly ConcurrentDictionary<TcpClient, byte> clients = new ConcurrentDictionary<TcpClient, byte>();
        private volatile bool running;
        public bool IsRunning => running;
        public HttpServer(Bridge bridge, int port)
        {
            this.bridge=bridge; this.port=port;
            listener=new TcpListener(IPAddress.Loopback,port);
        }
        public void Start() { listener.Start(16); running=true; Task.Run(LoopAsync); }
        public void Stop()
        {
            running=false; listener.Stop();
            foreach (var client in clients.Keys) client.Close();
        }
        private async Task LoopAsync()
        {
            while (running)
            {
                TcpClient client;
                try { client=await listener.AcceptTcpClientAsync().ConfigureAwait(false); } catch { running=false;break; }
                if (!workers.Wait(0)) { client.Close(); continue; }
                clients.TryAdd(client,0);
                if (!running) { client.Close(); clients.TryRemove(client,out _); workers.Release(); break; }
                _ = Task.Run(async () =>
                {
                    try { await HandleAsync(client).ConfigureAwait(false); }
                    catch (IOException) { } catch (SocketException) { } catch (ObjectDisposedException) { }
                    finally { clients.TryRemove(client,out _); client.Close(); workers.Release(); }
                });
            }
        }
        private sealed class Request
        {
            public string Method;
            public Uri Url;
            public Dictionary<string,string> Headers;
            public string Body;
        }
        private Request Read(NetworkStream stream)
        {
            var bytes=new List<byte>(); int next;
            while (true)
            {
                next=stream.ReadByte();
                if (next < 0) throw new IOException("Incomplete HTTP headers.");
                bytes.Add((byte)next);
                if (bytes.Count > 16384) throw new ArgumentException("Headers too large.");
                int n=bytes.Count;
                if (n >= 4 && bytes[n-4]==13 && bytes[n-3]==10 && bytes[n-2]==13 && bytes[n-1]==10) break;
            }
            foreach (var b in bytes) if (b > 127 || b == 0) throw new ArgumentException("Invalid header encoding.");
            var lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
            var first=lines[0].Split(' ');
            if (first.Length != 3 || (first[2] != "HTTP/1.1" && first[2] != "HTTP/1.0") || !first[1].StartsWith("/") || first[1].StartsWith("//"))
                throw new ArgumentException("Invalid HTTP request line.");
            var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for (int i=1;i<lines.Length && lines[i].Length>0;i++)
            {
                int colon=lines[i].IndexOf(':');
                if (colon <= 0 || char.IsWhiteSpace(lines[i][0])) throw new ArgumentException("Invalid header.");
                var key=lines[i].Substring(0,colon);
                foreach (char c in key) if (!(char.IsLetterOrDigit(c) || "!#$%&'*+-.^_`|~".IndexOf(c)>=0)) throw new ArgumentException("Invalid header name.");
                if (headers.ContainsKey(key)) throw new ArgumentException("Duplicate headers are not supported.");
                headers.Add(key,lines[i].Substring(colon+1).Trim());
            }
            if (!headers.TryGetValue("Host",out var host) || host != "127.0.0.1:"+port)
                throw new ArgumentException("Expected the configured loopback Host header.");
            if (headers.ContainsKey("Transfer-Encoding") || headers.ContainsKey("Expect"))
                throw new ArgumentException("Transfer-Encoding and Expect are not supported.");
            int length=0;
            if (headers.TryGetValue("Content-Length",out var raw) &&
                (!int.TryParse(raw,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out length) || length > 65536))
                throw new ArgumentException("Body must be at most 64 KiB.");
            var body=new byte[length]; int offset=0;
            while (offset < length)
            {
                int read=stream.Read(body,offset,length-offset);
                if (read==0) throw new IOException("Incomplete request body.");
                offset+=read;
            }
            return new Request { Method=first[0], Url=new Uri("http://127.0.0.1:"+port+first[1]), Headers=headers,
                Body=new UTF8Encoding(false,true).GetString(body) };
        }
        private async Task HandleAsync(TcpClient client)
        {
            client.ReceiveTimeout=10000; client.SendTimeout=5000;
            using (var stream=client.GetStream())
            {
                int status=200; string body;
                try
                {
                    var req=Read(stream);
                    if(req.Url.AbsolutePath=="/" && req.Method=="GET" && !req.Headers.ContainsKey("Origin"))
                    {body=Json.Serialize(new {ok=true,service="krypRevit optional external MCP bridge",version=typeof(HttpServer).Assembly.GetName().Version.ToString(),transport="HTTP on loopback",assistant="Open Revit > krypRevit > AI Chat. Built-in chat does not need this endpoint. Model routes require the MCP client header."});}
                    else if (req.Headers.ContainsKey("Origin") || !req.Headers.TryGetValue("X-krypRevit-Client",out var marker) || marker != "mcp")
                    { status=403; body=Json.Error("Use the krypRevit local MCP client; browser requests are not accepted."); }
                    else
                    {
                        var path=req.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                        var query=HttpUtility.ParseQueryString(req.Url.Query);
                        var method=(path == "/plans" || path == "/units" || path=="/openings/plan") ? "POST" : "GET";
                        if (req.Method != method) { status=405; body=Json.Error("Method not allowed."); }
                        else switch (path)
                        {
                            case "/health": body=await bridge.Invoke(Health.Run).ConfigureAwait(false); break;
                            case "/hosts":
                                var kind=query["type"] ?? "all";
                                if (kind != "all" && kind != "wall" && kind != "floor") throw new ArgumentException("Invalid host type.");
                                body=await bridge.Invoke(app => ListHosts.Run(app,kind)).ConfigureAwait(false); break;
                            case "/model":
                                int limit;
                                if (!int.TryParse(query["limit"] ?? "100",out limit) || limit < 1 || limit > 500) throw new ArgumentException("Limit must be 1–500.");
                                var uid=query["element_id"] ?? query["unique_id"];
                                if(query["element_id"]!=null && query["unique_id"]!=null) throw new ArgumentException("Specify element_id or unique_id, not both.");
                                body=await bridge.Invoke(app => Model.Run(app,uid,limit)).ConfigureAwait(false); break;
                            case "/search":
                                int searchLimit;
                                if(!int.TryParse(query["limit"] ?? "100",out searchLimit) || searchLimit<1 || searchLimit>500) throw new ArgumentException("Limit must be 1-500.");
                                bool includeTypes;
                                if(!bool.TryParse(query["include_types"] ?? "false",out includeTypes)) throw new ArgumentException("include_types must be boolean.");
                                body=await bridge.Invoke(app=>Model.Search(app,query["query"],query["category"],searchLimit,includeTypes)).ConfigureAwait(false);break;
                            case "/opening-rules":body=Json.Serialize(new {ok=true,rules=OpeningRuleStore.Load()});break;
                            case "/plans/status": body=await bridge.Invoke(app => Plans.Status()).ConfigureAwait(false); break;
                            case "/plans":case "/units":case "/openings/plan":
                                if (!req.Headers.TryGetValue("Content-Type",out var contentType) ||
                                    !string.Equals(contentType.Split(';')[0].Trim(),"application/json",StringComparison.OrdinalIgnoreCase))
                                    throw new ArgumentException("Expected application/json.");
                                body=path=="/plans" ? await bridge.Invoke(app => Plans.Propose(app,req.Body)).ConfigureAwait(false) : path=="/openings/plan" ? await bridge.Invoke(app=>Plans.PlanOpenings(app,req.Body)).ConfigureAwait(false) : await bridge.Invoke(app=>UnitConversion.Run(req.Body)).ConfigureAwait(false);break;
                            default: status=404; body=Json.Error("Route not found."); break;
                        }
                    }
                }
                catch (TimeoutException ex) { status=504; body=Json.Error(ex.Message); }
                catch (ArgumentException ex) { status=400; body=Json.Error(ex.Message); }
                catch (Exception ex) when (!(ex is IOException || ex is SocketException || ex is ObjectDisposedException))
                { status=500; body=Json.Error(ex.Message); }
                var payload=Encoding.UTF8.GetBytes(body);
                var header=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Response\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                stream.Write(header,0,header.Length); stream.Write(payload,0,payload.Length);
            }
        }
    }
}
