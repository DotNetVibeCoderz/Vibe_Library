using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsvr
{
    public delegate Task RouteHandler(HttpListenerContext context);

    public class Router
    {
        private readonly List<Route> _routes = new List<Route>();

        public void Add(string method, string pathPattern, RouteHandler handler)
        {
            _routes.Add(new Route
            {
                Method = method.ToUpper(),
                PathPattern = new Regex("^" + pathPattern + "$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Handler = handler
            });
        }

        public void Get(string path, RouteHandler handler) => Add("GET", path, handler);
        public void Post(string path, RouteHandler handler) => Add("POST", path, handler);
        public void Put(string path, RouteHandler handler) => Add("PUT", path, handler);
        public void Delete(string path, RouteHandler handler) => Add("DELETE", path, handler);

        public async Task<bool> HandleRequest(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath;
            string method = context.Request.HttpMethod;

            foreach (var route in _routes)
            {
                if (route.Method == method && route.PathPattern.IsMatch(path))
                {
                    // Match found!
                    try
                    {
                        await route.Handler(context);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing request: {ex.Message}");
                        context.Response.StatusCode = 500;
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes("Internal Server Error");
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.Close();
                    }
                    return true;
                }
            }
            return false;
        }
    }

    internal class Route
    {
        public string Method { get; set; }
        public Regex PathPattern { get; set; }
        public RouteHandler Handler { get; set; }
    }
}