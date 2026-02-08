using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsvr
{
    public static class HttpUtils
    {
        public static string GetMimeType(string extension)
        {
            return extension.ToLower() switch
            {
                ".html" => "text/html",
                ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".ico" => "image/x-icon",
                ".svg" => "image/svg+xml",
                ".txt" => "text/plain",
                ".xml" => "application/xml",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                _ => "application/octet-stream"
            };
        }

        public static Dictionary<string, object> ParseMultipart(string contentType, Stream stream, Encoding encoding)
        {
            var result = new Dictionary<string, object>();
            
            // 1. Robust Boundary Extraction
            // Handles: boundary="value", boundary=value, and trailing parameters like ; charset=utf-8
            string boundary = null;
            var boundaryMatch = Regex.Match(contentType, @"boundary=(?:""(.+?)""|([^\s;]+))", RegexOptions.IgnoreCase);
            if (boundaryMatch.Success)
            {
                boundary = boundaryMatch.Groups[1].Success ? boundaryMatch.Groups[1].Value : boundaryMatch.Groups[2].Value;
            }

            if (string.IsNullOrEmpty(boundary))
                return result; // Cannot parse without boundary

            // 2. Read Stream
            using (var reader = new StreamReader(stream, encoding))
            {
                string content = reader.ReadToEnd();
                
                // 3. Split by Boundary
                // The boundary in the body is prefixed with "--"
                string delimiter = "--" + boundary;
                string[] parts = content.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    // Ignore empty parts or the closing boundary marker "--" (often followed by newlines)
                    // The closing marker is usually just "--" or "--\r\n" after the split
                    if (string.IsNullOrWhiteSpace(part) || part.Trim().StartsWith("--")) continue;

                    // 4. Split Headers and Body
                    // Look for the first double-newline which separates headers from body
                    int headerEndIndex = part.IndexOf("\r\n\r\n");
                    // Fallback for non-standard line endings
                    if (headerEndIndex == -1) headerEndIndex = part.IndexOf("\n\n");
                    
                    if (headerEndIndex == -1) continue; // Invalid part structure

                    string headers = part.Substring(0, headerEndIndex);
                    
                    // Determine body start (skip the 4 or 2 newline chars)
                    int bodyStartIndex = headerEndIndex + (part[headerEndIndex] == '\r' ? 4 : 2);
                    string body = part.Substring(bodyStartIndex);

                    // 5. Clean trailing newlines from body (artifact of split)
                    // The split removes the delimiter but leaves the preceding CRLF
                    if (body.EndsWith("\r\n")) body = body.Substring(0, body.Length - 2);
                    else if (body.EndsWith("\n")) body = body.Substring(0, body.Length - 1);

                    // 6. Extract Metadata
                    string name = ExtractHeaderValue(headers, "name");
                    string filename = ExtractHeaderValue(headers, "filename");

                    // 7. Safety check for Key
                    if (string.IsNullOrEmpty(name)) 
                        name = "unknown_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                    if (!string.IsNullOrEmpty(filename))
                    {
                        // It's a file
                        result[name] = new UploadedFile { FileName = filename, Content = body };
                    }
                    else
                    {
                        // It's a form field
                        result[name] = body;
                    }
                }
            }
            return result;
        }

        private static string ExtractHeaderValue(string headers, string key)
        {
            // Use Regex for safer extraction of values inside quotes
            // Matches key="value"
            var match = Regex.Match(headers, $"{key}=\"(.*?)\"", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;

            // Matches key=value (no quotes)
            match = Regex.Match(headers, $"{key}=([^;\\s]+)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;

            return null;
        }
    }

    public class UploadedFile
    {
        public string FileName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}