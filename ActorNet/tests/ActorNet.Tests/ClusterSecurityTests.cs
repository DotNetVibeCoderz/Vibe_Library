// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ActorNet.Network;

namespace ActorNet.Tests;

/// <summary>
/// Encryption and authentication between nodes.
/// </summary>
/// <remarks>
/// The claims worth testing are the negative ones. That a correctly configured cluster converges
/// says little - it does that without any security at all. What matters is that a peer without the
/// secret is refused, and that a refusal is visible rather than silent.
/// </remarks>
public sealed class ClusterSecurityTests
{
    private const string Secret = "a-shared-secret-long-enough";

    private static async Task<ActorSystem> NodeAsync(
        TestHarness harness, string id, IEnumerable<string>? seeds, Action<ClusterSecurityOptions> security) =>
        await harness.NetworkedAsync(id, seeds, configure: o => security(o.Security));

    [Fact]
    public void SecurityIsOffByDefault()
    {
        var options = new ActorSystemOptions();
        Assert.False(options.Security.TlsEnabled);
        Assert.False(options.Security.AuthenticationEnabled);
        options.Validate();
    }

    [Fact]
    public void AShortSecretIsRefused()
    {
        // A four-character secret is the only thing between an open port and an unauthorised node,
        // and it is guessable. Better to refuse it at configuration than to imply protection.
        var options = new ActorSystemOptions { Security = { SharedSecret = "abcd" } };
        var ex = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("at least 16", ex.Message);
    }

    [Fact]
    public void ClientCertificatesRequireTls()
    {
        var options = new ActorSystemOptions { Security = { RequireClientCertificate = true } };
        var ex = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("needs TLS", ex.Message);
    }

    [Fact]
    public void TheChallengeAnswerDependsOnBothTheSecretAndTheNonce()
    {
        var a = new ClusterSecurityOptions { SharedSecret = Secret };
        var b = new ClusterSecurityOptions { SharedSecret = "a-different-secret-entirely" };

        var nonce = RandomNumberGenerator.GetBytes(32);
        var other = RandomNumberGenerator.GetBytes(32);

        // Same secret and nonce agree; either one differing does not. Without the nonce mattering,
        // an observer could replay one captured answer forever.
        Assert.Equal(a.Answer(nonce), a.Answer(nonce));
        Assert.NotEqual(a.Answer(nonce), a.Answer(other));
        Assert.NotEqual(a.Answer(nonce), b.Answer(nonce));
    }

    [Fact]
    public async Task NodesSharingASecretFormACluster()
    {
        await using var harness = new TestHarness();

        var seed = await NodeAsync(harness, "sec-seed", [], s => s.SharedSecret = Secret);
        var joiner = await NodeAsync(harness, "sec-joiner", [$"127.0.0.1:{seed.BoundPort}"], s => s.SharedSecret = Secret);

        await TestHarness.AssertEventuallyAsync(
            () => seed.Cluster.Members.Count == 2 && joiner.Cluster.Members.Count == 2,
            "nodes that share the secret should converge", TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task MessagesStillFlowAcrossAnAuthenticatedCluster()
    {
        await using var harness = new TestHarness();

        var first = await NodeAsync(harness, "sec-a", [], s => s.SharedSecret = Secret);
        var second = await NodeAsync(harness, "sec-b", [$"127.0.0.1:{first.BoundPort}"], s => s.SharedSecret = Secret);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        // The handshake runs once per connection, so an ask proves it did not break the frame
        // stream that follows it.
        var remote = Enumerable.Range(0, 500)
            .Select(i => ActorId.For<CounterActor>($"sec-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "sec-b");

        await first.TellAsync(remote, new Add(7));
        Assert.Equal(7, (await first.AskAsync<Total>(remote, new GetTotal(), TimeSpan.FromSeconds(20))).Value);
    }

    [Fact]
    public async Task ANodeWithTheWrongSecretIsRefused()
    {
        await using var harness = new TestHarness();

        var seed = await NodeAsync(harness, "sec-guard", [], s => s.SharedSecret = Secret);
        var intruder = await NodeAsync(harness, "sec-intruder", [$"127.0.0.1:{seed.BoundPort}"],
            s => s.SharedSecret = "a-completely-different-secret");

        // Give the join and several gossip rounds every chance to succeed.
        await Task.Delay(TimeSpan.FromSeconds(4));

        Assert.DoesNotContain(seed.Cluster.Members, m => m.NodeId == "sec-intruder");
        Assert.Single(seed.Cluster.Members);
    }

    [Fact]
    public async Task ANodeWithNoSecretCannotJoinAnAuthenticatedCluster()
    {
        await using var harness = new TestHarness();

        var seed = await NodeAsync(harness, "sec-closed", [], s => s.SharedSecret = Secret);
        var outsider = await harness.NetworkedAsync("sec-outsider", [$"127.0.0.1:{seed.BoundPort}"]);

        await Task.Delay(TimeSpan.FromSeconds(4));

        // The outsider never sends an answer, so the seed's read times out and the connection is
        // dropped before a single frame is parsed.
        Assert.DoesNotContain(seed.Cluster.Members, m => m.NodeId == "sec-outsider");
    }

    [Fact]
    public async Task ARawConnectionIsRefusedWithoutTheHandshake()
    {
        await using var harness = new TestHarness();
        var seed = await NodeAsync(harness, "sec-raw", [], s =>
        {
            s.SharedSecret = Secret;
            s.HandshakeTimeout = TimeSpan.FromSeconds(2);
        });

        // A socket that connects and immediately sends a frame - what an unauthenticated client
        // would do. It must never be parsed.
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", seed.BoundPort);
        await using var stream = client.GetStream();

        await FrameCodec.WriteAsync(stream, new Serialization.WireEnvelope
        {
            Kind = Serialization.WireKind.Message,
            Target = "CounterActor/intruder",
            FromNode = "intruder",
        }, CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.DoesNotContain(ActorId.For<CounterActor>("intruder"), seed.LocalActors);
    }

    [Fact]
    public async Task TlsEncryptsTheClusterAndStillDelivers()
    {
        using var certificate = SelfSigned();

        await using var harness = new TestHarness();

        var first = await NodeAsync(harness, "tls-a", [], s =>
        {
            s.ServerCertificate = certificate;
            s.AcceptAnyCertificate();
        });

        var second = await NodeAsync(harness, "tls-b", [$"127.0.0.1:{first.BoundPort}"], s =>
        {
            s.ServerCertificate = certificate;
            s.AcceptAnyCertificate();
        });

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "a TLS cluster should converge", TimeSpan.FromSeconds(20));

        var remote = Enumerable.Range(0, 500)
            .Select(i => ActorId.For<CounterActor>($"tls-{i}"))
            .First(id => first.Cluster.OwnerOf(id) == "tls-b");

        await first.TellAsync(remote, new Add(5));
        Assert.Equal(5, (await first.AskAsync<Total>(remote, new GetTotal(), TimeSpan.FromSeconds(20))).Value);
    }

    [Fact]
    public async Task APlaintextNodeCannotJoinATlsCluster()
    {
        using var certificate = SelfSigned();

        await using var harness = new TestHarness();
        var secure = await NodeAsync(harness, "tls-guard", [], s =>
        {
            s.ServerCertificate = certificate;
            s.AcceptAnyCertificate();
        });

        var plaintext = await harness.NetworkedAsync("tls-plain", [$"127.0.0.1:{secure.BoundPort}"]);

        await Task.Delay(TimeSpan.FromSeconds(4));

        // Every node has to agree about TLS. The failure is a handshake error rather than
        // something subtle, which is the point - a half-migrated cluster fails loudly.
        Assert.DoesNotContain(secure.Cluster.Members, m => m.NodeId == "tls-plain");
    }

    [Fact]
    public void APinnedThumbprintAcceptsOnlyThatCertificate()
    {
        using var mine = SelfSigned();
        using var theirs = SelfSigned();

        var options = new ClusterSecurityOptions().PinnedThumbprint(mine.GetCertHashString());

        Assert.True(options.RemoteCertificateValidation!(this, mine, null, System.Net.Security.SslPolicyErrors.None));
        Assert.False(options.RemoteCertificateValidation(this, theirs, null, System.Net.Security.SslPolicyErrors.None));
        Assert.False(options.RemoteCertificateValidation(this, null, null, System.Net.Security.SslPolicyErrors.None));
    }

    /// <summary>A throwaway certificate, so the TLS tests need nothing installed on the machine.</summary>
    private static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=actornet-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // SslStream needs the private key to come from a persisted key set on Windows, which an
        // in-memory certificate does not have until it is exported and re-imported.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
