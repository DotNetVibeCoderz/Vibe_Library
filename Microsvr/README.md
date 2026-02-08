# Microsvr

**Microsvr** is a lightweight, cross-platform micro web server built with C#. It is designed to be embedded in applications or run as a standalone service with minimal resource usage.

## Features

- **Lightweight**: Built on top of `System.Net.HttpListener`, no heavy framework dependencies.
- **Routing**: Simple regex-based routing for GET, POST, PUT, DELETE.
- **Middleware**: Express.js-style middleware support (logging, auth, etc.).
- **Static Files**: Serve HTML, CSS, JS, and images.
- **WebSocket**: Built-in support for real-time communication.
- **REST API Ready**: Easy helper methods for JSON responses.
- **File Upload**: Basic support for `multipart/form-data`.
- **Security**: Basic Authentication and path protection examples included.

## Getting Started

### Prerequisites

- .NET 6.0 SDK or later.

### Running the Project

```bash
cd Microsvr
dotnet run
```

The server will start at `http://localhost:8080/`.

### Code Examples (from Program.cs)

#### 1. Server Setup & Static Files
```csharp
using var server = new MicroServer();
server.AddPrefix("http://localhost:8080/");

// Setup Static Files
string wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (!Directory.Exists(wwwroot)) Directory.CreateDirectory(wwwroot);
server.UseStaticFiles(wwwroot);
```

#### 2. Basic Authentication Middleware
```csharp
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
```

#### 3. REST API (GET & POST)
```csharp
// GET JSON Response
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

// POST Echo
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
```

#### 4. File Upload Handling
```csharp
server.Router.Post("/upload", async (ctx) =>
{
    if (!ctx.Request.ContentType.StartsWith("multipart/form-data"))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }
    // Parse using HttpUtils helper
    using var ms = new MemoryStream();
    await ctx.Request.InputStream.CopyToAsync(ms);
    ms.Position = 0;
    var files = HttpUtils.ParseMultipart(ctx.Request.ContentType, ms, ctx.Request.ContentEncoding);
    
    ctx.Response.StatusCode = 200;
    byte[] ok = Encoding.UTF8.GetBytes($"Upload OK: {files.Count} items received.");
    await ctx.Response.OutputStream.WriteAsync(ok, 0, ok.Length);
    ctx.Response.Close();
});
```

#### 5. WebSocket Support
```csharp
server.Router.Get("/ws", async (ctx) =>
{
    if (ctx.Request.IsWebSocketRequest)
    {
        HttpListenerWebSocketContext wsContext = await ctx.AcceptWebSocketAsync(null);
        WebSocket webSocket = wsContext.WebSocket;
        
        byte[] buffer = new byte[1024];
        while (webSocket.State == WebSocketState.Open)
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
    }
});
```

---

# Bahasa Indonesia

**Microsvr** adalah web server mikro yang ringan dan lintas platform yang dibuat dengan C#. Server ini dirancang untuk disematkan dalam aplikasi lain atau dijalankan sebagai layanan mandiri dengan penggunaan sumber daya yang minimal.

## Fitur

- **Ringan**: Dibangun di atas `System.Net.HttpListener`, tanpa ketergantungan framework yang berat.
- **Routing**: Routing berbasis regex sederhana untuk GET, POST, PUT, DELETE.
- **Middleware**: Mendukung middleware gaya Express.js (logging, auth, dll).
- **File Statis**: Menyajikan HTML, CSS, JS, dan gambar.
- **WebSocket**: Dukungan bawaan untuk komunikasi real-time.
- **Siap untuk REST API**: Metode pembantu mudah untuk respon JSON.
- **Upload File**: Dukungan dasar untuk `multipart/form-data`.
- **Keamanan**: Contoh Basic Authentication dan proteksi path disertakan.

## Memulai

### Prasyarat

- .NET 6.0 SDK atau yang lebih baru.

### Menjalankan Proyek

```bash
cd Microsvr
dotnet run
```

Server akan berjalan di `http://localhost:8080/`.

### Contoh Kode (dari Program.cs)

#### 1. Setup Server & File Statis
```csharp
using var server = new MicroServer();
server.AddPrefix("http://localhost:8080/");

// Setup File Statis
string wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (!Directory.Exists(wwwroot)) Directory.CreateDirectory(wwwroot);
server.UseStaticFiles(wwwroot);
```

#### 2. Middleware Otentikasi Dasar
```csharp
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
```

#### 3. REST API (GET & POST)
```csharp
// GET JSON Response
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

// POST Echo
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
```

#### 4. Penanganan Upload File
```csharp
server.Router.Post("/upload", async (ctx) =>
{
    if (!ctx.Request.ContentType.StartsWith("multipart/form-data"))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }
    // Parse menggunakan bantuan HttpUtils
    using var ms = new MemoryStream();
    await ctx.Request.InputStream.CopyToAsync(ms);
    ms.Position = 0;
    var files = HttpUtils.ParseMultipart(ctx.Request.ContentType, ms, ctx.Request.ContentEncoding);
    
    ctx.Response.StatusCode = 200;
    byte[] ok = Encoding.UTF8.GetBytes($"Upload OK: {files.Count} items received.");
    await ctx.Response.OutputStream.WriteAsync(ok, 0, ok.Length);
    ctx.Response.Close();
});
```

#### 5. Dukungan WebSocket
```csharp
server.Router.Get("/ws", async (ctx) =>
{
    if (ctx.Request.IsWebSocketRequest)
    {
        HttpListenerWebSocketContext wsContext = await ctx.AcceptWebSocketAsync(null);
        WebSocket webSocket = wsContext.WebSocket;
        
        byte[] buffer = new byte[1024];
        while (webSocket.State == WebSocketState.Open)
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
    }
});
```
