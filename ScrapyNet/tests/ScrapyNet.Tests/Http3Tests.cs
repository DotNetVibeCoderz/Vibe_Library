using System.Net;
using System.Net.Quic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ScrapyNet.Tests;

public class Http3Tests
{
    private sealed class ProtocolSpider(string url, int times) : Spider
    {
        public override string Name => "protocols";

        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < times; i++)
                yield return new Request($"{url}?n={i}", Parse) { Priority = -i };
            await Task.CompletedTask;
        }

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new Dictionary<string, object?> { ["protocol"] = response.Protocol, ["body"] = response.Text };
            await Task.CompletedTask;
        }
    }

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SslStream on Windows needs a key it can use from a persisted (PFX-loaded) certificate.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    private static async Task<(WebApplication App, string Url)> StartServerAsync()
    {
        var port = FreeTcpPort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var cert = SelfSigned();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port, listen =>
        {
            listen.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            listen.UseHttps(cert);
        }));
        var app = builder.Build();
        app.MapGet("/", (HttpContext ctx) => $"hello over {ctx.Request.Protocol}");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app, $"https://127.0.0.1:{port}/");
    }

    private static int FreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Settings Http3Settings(string policy)
    {
        var s = new Settings();
        TestSettings.Quiet(s);
        s.Set(SettingKeys.DownloadVerifySsl, false);
        s.Set(SettingKeys.DownloadHttpVersion, "3");
        s.Set(SettingKeys.DownloadHttpVersionPolicy, policy);
        s.Set(SettingKeys.ConcurrentRequests, 1);
        s.Set(SettingKeys.RobotsTxtObey, false);
        return s;
    }

    [Fact]
    public async Task Exact_policy_downloads_over_quic()
    {
        Assert.SkipUnless(QuicListener.IsSupported && QuicConnection.IsSupported, "QUIC is not available on this machine.");
        var (app, url) = await StartServerAsync();
        await using var _ = app;

        var (result, items) = await Scrapy.CrawlAndCollectAsync(new ProtocolSpider(url, 2), Http3Settings("exact"), TestContext.Current.CancellationToken);

        Assert.Equal("finished", result.FinishReason);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("HTTP/3.0", ((Dictionary<string, object?>)i)["protocol"]));
        Assert.All(items, i => Assert.Equal("hello over HTTP/3", ((Dictionary<string, object?>)i)["body"]));
    }

    [Fact]
    public async Task Or_lower_policy_upgrades_after_alt_svc()
    {
        Assert.SkipUnless(QuicListener.IsSupported && QuicConnection.IsSupported, "QUIC is not available on this machine.");
        var (app, url) = await StartServerAsync();
        await using var _ = app;

        // The first response arrives over TCP and advertises h3 in Alt-Svc; later requests switch.
        var (result, items) = await Scrapy.CrawlAndCollectAsync(new ProtocolSpider(url, 6), Http3Settings("or-lower"), TestContext.Current.CancellationToken);

        var protocols = items.Select(i => (string?)((Dictionary<string, object?>)i)["protocol"]).ToList();
        Assert.Equal("finished", result.FinishReason);
        Assert.NotEqual("HTTP/3.0", protocols[0]);
        Assert.Equal("HTTP/3.0", protocols[^1]);
    }
}
