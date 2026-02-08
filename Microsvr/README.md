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

### Code Examples

#### 1. Basic Routing
```csharp
server.Router.Get("/hello", async (ctx) =>
{
    byte[] data = Encoding.UTF8.GetBytes("Hello World");
    await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
    ctx.Response.Close();
});
```

#### 2. JSON API
```csharp
server.Router.Get("/api/time", async (ctx) =>
{
    var json = JsonSerializer.Serialize(new { Time = DateTime.Now });
    ctx.Response.ContentType = "application/json";
    byte[] data = Encoding.UTF8.GetBytes(json);
    await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
    ctx.Response.Close();
});
```

#### 3. Middleware (Logging)
```csharp
server.Use(async (context, next) =>
{
    Console.WriteLine($"Request: {context.Request.Url}");
    await next();
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

### Contoh Kode

#### 1. Routing Dasar
```csharp
server.Router.Get("/halo", async (ctx) =>
{
    byte[] data = Encoding.UTF8.GetBytes("Halo Dunia");
    await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
    ctx.Response.Close();
});
```

#### 2. JSON API
```csharp
server.Router.Get("/api/waktu", async (ctx) =>
{
    var json = JsonSerializer.Serialize(new { Waktu = DateTime.Now });
    ctx.Response.ContentType = "application/json";
    byte[] data = Encoding.UTF8.GetBytes(json);
    await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
    ctx.Response.Close();
});
```

#### 3. Middleware (Logging)
```csharp
server.Use(async (context, next) =>
{
    Console.WriteLine($"Permintaan: {context.Request.Url}");
    await next();
});
```
