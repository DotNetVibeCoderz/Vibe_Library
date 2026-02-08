using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsvr
{
    // Middleware delegate: (context, next) -> Task
    public delegate Task Middleware(HttpListenerContext context, Func<Task> next);

    public class MicroServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly List<string> _prefixes;
        private readonly List<Middleware> _middlewares;
        private readonly Router _router;
        private string _staticFileRoot;

        public Router Router => _router;

        public MicroServer()
        {
            _listener = new HttpListener();
            _prefixes = new List<string>();
            _middlewares = new List<Middleware>();
            _router = new Router();
        }

        public void AddPrefix(string prefix)
        {
            if (!_listener.Prefixes.Contains(prefix))
            {
                _listener.Prefixes.Add(prefix);
                _prefixes.Add(prefix);
            }
        }

        public void Use(Middleware middleware)
        {
            _middlewares.Add(middleware);
        }

        public void UseStaticFiles(string rootPath)
        {
            _staticFileRoot = rootPath;
            if (!Directory.Exists(_staticFileRoot))
            {
                try { Directory.CreateDirectory(_staticFileRoot); } catch { }
            }
        }

        public async Task StartAsync(CancellationToken token = default)
        {
            if (_prefixes.Count == 0)
                throw new InvalidOperationException("No prefixes defined. Use AddPrefix().");

            _listener.Start();
            Console.WriteLine($"MicroServer running on: {string.Join(", ", _prefixes)}");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    // Fire and forget to handle concurrent requests
                    _ = HandleRequestAsync(context);
                }
                catch (HttpListenerException)
                {
                    break; // Listener stopped
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Listener error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                // Construct the pipeline execution
                int index = 0;
                
                Func<Task> next = null;
                next = async () =>
                {
                    if (index < _middlewares.Count)
                    {
                        var middleware = _middlewares[index++];
                        await middleware(context, next);
                    }
                    else
                    {
                        // Final handler: Router -> Static -> 404
                        await FinalHandler(context);
                    }
                };

                await next();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Request processing error: {ex}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        private async Task FinalHandler(HttpListenerContext context)
        {
            // 1. Try Router
            if (await _router.HandleRequest(context))
            {
                return;
            }

            // 2. Try Static Files
            if (!string.IsNullOrEmpty(_staticFileRoot))
            {
                if (await ServeStaticFileAsync(context))
                    return;
            }

            // 3. Not Found
            context.Response.StatusCode = 404;
            byte[] buffer = Encoding.UTF8.GetBytes("404 Not Found - Microsvr");
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        private async Task<bool> ServeStaticFileAsync(HttpListenerContext context)
        {
            try
            {
                // Simple security check
                string requestPath = context.Request.Url.AbsolutePath.TrimStart('/');
                if (string.IsNullOrEmpty(requestPath)) requestPath = "index.html";
                
                // Decode URL chars
                requestPath = WebUtility.UrlDecode(requestPath);

                string fullPath = Path.GetFullPath(Path.Combine(_staticFileRoot, requestPath));
                string rootPath = Path.GetFullPath(_staticFileRoot);

                if (!fullPath.StartsWith(rootPath)) return false; // Directory traversal attack prevention

                if (File.Exists(fullPath))
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(fullPath);
                    string ext = Path.GetExtension(fullPath);
                    context.Response.ContentType = HttpUtils.GetMimeType(ext);
                    context.Response.ContentLength64 = fileBytes.Length;
                    await context.Response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                    context.Response.Close();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Static file error: {ex.Message}");
            }
            return false;
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }

        public void Dispose()
        {
            ((IDisposable)_listener).Dispose();
        }
    }
}