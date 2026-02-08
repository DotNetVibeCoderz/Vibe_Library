using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsvr
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   MicroServer - Lightweight C# Web Server");
            Console.WriteLine("==================================================");

            // Token to stop the server
            using var cts = new CancellationTokenSource();

            // 1. Start Server in a separate Task
            // We pass the token so the server loop can be cancelled gracefully
            Task serverTask = Task.Run(() => RunServer(cts.Token), cts.Token);

            // 2. Wait for server to boot
            Console.WriteLine("[System] Waiting for server to start...");
            await Task.Delay(2000);

            // 3. Run Client Tests & Benchmark
            try 
            {
                await RunClientTests();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client] Error during tests: {ex.Message}");
            }

            Console.WriteLine("\n[System] Tests finished. Press ENTER to stop the server.");
            Console.ReadLine();

            Console.WriteLine("[System] Stopping server...");
            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Console.WriteLine("[System] Server stopped. Goodbye!");
        }

        static async Task RunServer(CancellationToken token)
        {
            using var server = new MicroServer();
            
            // 1. Configure Port
            string url = "http://localhost:8080/";
            server.AddPrefix(url);

            // 2. Setup Static Files
            string wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            if (!Directory.Exists(wwwroot))
            {
                Directory.CreateDirectory(wwwroot);
                await File.WriteAllTextAsync(Path.Combine(wwwroot, "index.html"), "<h1>Hello from MicroServer Static File!</h1>");
            }
            server.UseStaticFiles(wwwroot);

            // 3. Middleware: Logging (Reduced noise for benchmark)
            server.Use(async (context, next) =>
            {
                // Uncomment to see every request log
                // Console.WriteLine($"[Server] {context.Request.HttpMethod} {context.Request.Url.AbsolutePath}");
                await next();
            });

            // 4. Middleware: Basic Auth
            server.Use(async (context, next) =>
            {
                if (context.Request.Url.AbsolutePath.StartsWith("/admin"))
                {
                    string authHeader = context.Request.Headers["Authorization"];
                    if (authHeader != null && authHeader.StartsWith("Basic "))
                    {
                        var credentialBytes = Convert.FromBase64String(authHeader.Substring(6));
                        var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':');
                        if (credentials.Length == 2 && credentials[0] == "admin" && credentials[1] == "password")
                        {
                            await next();
                            return;
                        }
                    }
                    context.Response.StatusCode = 401;
                    context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"Microsvr Admin\"");
                    context.Response.Close();
                    return;
                }
                await next();
            });

            // 5. Routes
            server.Router.Get("/api/hello", async (ctx) =>
            {
                var response = new { Message = "Hello World form REST API", Time = DateTime.Now };
                string json = JsonSerializer.Serialize(response);
                ctx.Response.ContentType = "application/json";
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentLength64 = buffer.Length;
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                ctx.Response.Close();
            });

            server.Router.Post("/api/echo", async (ctx) =>
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                string body = await reader.ReadToEndAsync();
                var response = new { Received = body };
                string json = JsonSerializer.Serialize(response);
                ctx.Response.ContentType = "application/json";
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentLength64 = buffer.Length;
                await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                ctx.Response.Close();
            });

            server.Router.Post("/upload", async (ctx) =>
            {
                if (!ctx.Request.ContentType.StartsWith("multipart/form-data"))
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    return;
                }
                try
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.InputStream.CopyToAsync(ms);
                    ms.Position = 0;
                    var files = HttpUtils.ParseMultipart(ctx.Request.ContentType, ms, ctx.Request.ContentEncoding);
                    
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/plain";
                    byte[] ok = Encoding.UTF8.GetBytes($"Upload OK: {files.Count} items received.");
                    await ctx.Response.OutputStream.WriteAsync(ok, 0, ok.Length);
                    ctx.Response.Close();
                }
                catch (Exception ex)
                {
                    ctx.Response.StatusCode = 500;
                    byte[] err = Encoding.UTF8.GetBytes("Error: " + ex.Message);
                    ctx.Response.OutputStream.Write(err, 0, err.Length);
                    ctx.Response.Close();
                }
            });

             // 7. WebSocket Support
            server.Router.Get("/ws", async (ctx) =>
            {
                if (ctx.Request.IsWebSocketRequest)
                {
                    HttpListenerWebSocketContext wsContext = await ctx.AcceptWebSocketAsync(null);
                    WebSocket webSocket = wsContext.WebSocket;
                    
                    byte[] buffer = new byte[1024];
                    while (webSocket.State == WebSocketState.Open)
                    {
                        try 
                        {
                            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            }
                            else
                            {
                                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                // Echo back
                                byte[] sendBuffer = Encoding.UTF8.GetBytes("Echo: " + msg);
                                await webSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                        }
                        catch { break; }
                    }
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
            });

            server.Router.Get("/admin/dashboard", async (ctx) =>
            {
                byte[] buffer = Encoding.UTF8.GetBytes("Admin Access Granted");
                ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
                ctx.Response.Close();
            });

            await server.StartAsync(token);
        }

        static async Task RunClientTests()
        {
            Console.WriteLine("\n[Client] Starting Tests...");
            using var client = new HttpClient();
            client.BaseAddress = new Uri("http://localhost:8080");
            Stopwatch sw = new Stopwatch();

            // Test 1: Static File
            Console.Write("[Test 1] Static File (GET /index.html)... ");
            sw.Start();
            var resp1 = await client.GetAsync("/index.html");
            sw.Stop();
            Console.WriteLine($"{resp1.StatusCode} ({sw.ElapsedMilliseconds}ms)");
            sw.Reset();

            // Test 2: REST API GET
            Console.Write("[Test 2] REST API (GET /api/hello)... ");
            sw.Start();
            var resp2 = await client.GetAsync("/api/hello");
            string body2 = await resp2.Content.ReadAsStringAsync();
            sw.Stop();
            Console.WriteLine($"{resp2.StatusCode} - {body2} ({sw.ElapsedMilliseconds}ms)");
            sw.Reset();

            // Test 3: REST API POST
            Console.Write("[Test 3] REST API (POST /api/echo)... ");
            var content = new StringContent("{\"data\":\"test\"}", Encoding.UTF8, "application/json");
            sw.Start();
            var resp3 = await client.PostAsync("/api/echo", content);
            string body3 = await resp3.Content.ReadAsStringAsync();
            sw.Stop();
            Console.WriteLine($"{resp3.StatusCode} - {body3} ({sw.ElapsedMilliseconds}ms)");
            sw.Reset();

            // Test 4: Auth Fail
            Console.Write("[Test 4] Auth Fail (GET /admin/dashboard)... ");
            sw.Start();
            var resp4 = await client.GetAsync("/admin/dashboard");
            sw.Stop();
            Console.WriteLine($"{resp4.StatusCode} (Expected 401) ({sw.ElapsedMilliseconds}ms)");
            sw.Reset();

            // Test 5: Auth Success
            Console.Write("[Test 5] Auth Success (GET /admin/dashboard)... ");
            var request = new HttpRequestMessage(HttpMethod.Get, "/admin/dashboard");
            var authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:password"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            sw.Start();
            var resp5 = await client.SendAsync(request);
            sw.Stop();
            string body5 = await resp5.Content.ReadAsStringAsync();
            Console.WriteLine($"{resp5.StatusCode} - {body5} ({sw.ElapsedMilliseconds}ms)");
            sw.Reset();

            // Test 6: File Upload
            Console.Write("[Test 6] File Upload (POST /upload)... ");
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("File Content Here")), "file", "test.txt");
            sw.Start();
            var resp6 = await client.PostAsync("/upload", multipart);
            string body6 = await resp6.Content.ReadAsStringAsync();
            sw.Stop();
            Console.WriteLine($"{resp6.StatusCode} - {body6} ({sw.ElapsedMilliseconds}ms)");
            sw.Reset();

            // Test 7: WebSocket
            Console.Write("[Test 7] WebSocket (WS /ws)... ");
            using var ws = new ClientWebSocket();
            sw.Start();
            await ws.ConnectAsync(new Uri("ws://localhost:8080/ws"), CancellationToken.None);
            byte[] sendBytes = Encoding.UTF8.GetBytes("Hello WS");
            await ws.SendAsync(new ArraySegment<byte>(sendBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            
            byte[] recvBuffer = new byte[1024];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(recvBuffer), CancellationToken.None);
            string wsMsg = Encoding.UTF8.GetString(recvBuffer, 0, result.Count);
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            sw.Stop();
            Console.WriteLine($"Connected & Echoed: {wsMsg} ({sw.ElapsedMilliseconds}ms)");
            sw.Reset();

            // Benchmark Loop
            int count = 2000;
            Console.WriteLine($"\n[Client] Running Benchmark ({count} requests to /api/hello)...");
            sw.Start();
            
            // Running in parallel to simulate load
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = client.GetAsync("/api/hello");
            }
            await Task.WhenAll(tasks);
            
            sw.Stop();
            double rps = count / (sw.ElapsedMilliseconds / 1000.0);
            Console.WriteLine($"[Client] Completed {count} requests in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"[Client] Throughput: {rps:F2} req/sec");
        }
    }
}