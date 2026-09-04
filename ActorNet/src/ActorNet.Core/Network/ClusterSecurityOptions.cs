// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ActorNet.Network;

/// <summary>
/// Transport security between nodes: encryption, and proof that a peer belongs in this cluster.
/// </summary>
/// <remarks>
/// <para>
/// Both are off by default, which is the honest default for a library whose first deployment is a
/// developer's laptop - and a documented reason to keep a cluster on a trusted network until they
/// are turned on.
/// </para>
/// <para>
/// They answer different questions and are independent. TLS stops a third party reading or
/// rewriting the traffic. The shared secret stops an <em>unauthorised process</em> joining the
/// cluster, which TLS with an unauthenticated client does not: anyone who can reach the port could
/// otherwise send a Join and start receiving actors.
/// </para>
/// </remarks>
public sealed class ClusterSecurityOptions
{
    /// <summary>
    /// The certificate this node serves. Setting it turns TLS on for every node-to-node connection.
    /// </summary>
    /// <remarks>
    /// Every node in a cluster must agree: a node with TLS on cannot talk to one with it off, and
    /// the failure is a handshake error rather than anything subtle. Roll it out to every node
    /// before enabling it anywhere.
    /// </remarks>
    public X509Certificate2? ServerCertificate { get; set; }

    /// <summary>True when this node encrypts its inter-node traffic.</summary>
    public bool TlsEnabled => ServerCertificate is not null;

    /// <summary>
    /// Accept the peer's certificate. Null means the platform default, which rejects anything
    /// untrusted.
    /// </summary>
    /// <remarks>
    /// Cluster nodes usually serve certificates from a private CA, or self-signed ones, so the
    /// default will refuse them. Supply a callback that checks whatever you actually trust - a
    /// pinned thumbprint, or your own CA. <see cref="PinnedThumbprint"/> is the short version.
    /// </remarks>
    public RemoteCertificateValidationCallback? RemoteCertificateValidation { get; set; }

    /// <summary>
    /// Require the client to present a certificate too.
    /// </summary>
    /// <remarks>
    /// Mutual TLS is the strongest option and the most work to deploy, since every node needs a
    /// key pair and a way to rotate it. The shared secret is the cheaper answer to the same
    /// question; this is here for deployments that already have a certificate story.
    /// </remarks>
    public bool RequireClientCertificate { get; set; }

    /// <summary>The certificate a client presents when <see cref="RequireClientCertificate"/> is on across the cluster.</summary>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <summary>
    /// A secret every node in the cluster shares. Setting it turns on a challenge-response
    /// handshake before any frame is accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The secret itself is never sent. The listening side offers a random nonce, the connecting
    /// side answers with an HMAC of it, and the answer is compared in constant time. That makes the
    /// handshake safe to run without TLS - a passive observer learns a nonce and a MAC, neither of
    /// which is reusable - and means an operator can turn on authentication without first solving
    /// certificate distribution.
    /// </para>
    /// <para>
    /// It is authentication, not encryption. Without TLS the messages themselves are still in the
    /// clear.
    /// </para>
    /// </remarks>
    public string? SharedSecret { get; set; }

    /// <summary>True when this node requires peers to prove they know the shared secret.</summary>
    public bool AuthenticationEnabled => !string.IsNullOrEmpty(SharedSecret);

    /// <summary>How long a peer has to answer the challenge before the connection is dropped.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Accepts exactly one certificate, by thumbprint.
    /// </summary>
    /// <remarks>
    /// The usual answer for a small cluster with a self-signed certificate: no CA to run, and
    /// nothing accepted that was not pinned. It does mean a certificate rotation is a
    /// configuration change on every node.
    /// </remarks>
    public ClusterSecurityOptions PinnedThumbprint(string thumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);
        var expected = thumbprint.Replace(" ", string.Empty);

        RemoteCertificateValidation = (_, certificate, _, _) =>
            certificate is not null &&
            string.Equals(certificate.GetCertHashString(), expected, StringComparison.OrdinalIgnoreCase);

        return this;
    }

    /// <summary>
    /// Accepts any certificate. Development only.
    /// </summary>
    /// <remarks>
    /// This encrypts the traffic and authenticates nobody, so it stops passive eavesdropping and
    /// not an active attacker. It is a deliberate method rather than a boolean so that it is
    /// greppable in a review.
    /// </remarks>
    public ClusterSecurityOptions AcceptAnyCertificate()
    {
        RemoteCertificateValidation = (_, _, _, _) => true;
        return this;
    }

    /// <summary>Throws when the settings cannot produce a working transport.</summary>
    public void Validate()
    {
        if (RequireClientCertificate && !TlsEnabled)
            throw new ArgumentException(
                "RequireClientCertificate needs TLS: without a ServerCertificate there is no handshake to present one in.",
                nameof(RequireClientCertificate));

        if (RequireClientCertificate && ClientCertificate is null)
            throw new ArgumentException(
                "RequireClientCertificate is on but no ClientCertificate was supplied; this node could not connect to its own peers.",
                nameof(ClientCertificate));

        if (HandshakeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HandshakeTimeout), HandshakeTimeout, "HandshakeTimeout must be positive.");

        if (SharedSecret is { Length: > 0 and < 16 })
            throw new ArgumentException(
                $"SharedSecret is {SharedSecret.Length} characters. Use at least 16 - it is the only thing standing between " +
                "an open port and an unauthorised node joining the cluster.",
                nameof(SharedSecret));
    }

    /// <summary>The HMAC answer to a challenge. Shared by both sides so they cannot disagree.</summary>
    internal byte[] Answer(byte[] nonce)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SharedSecret!));
        return hmac.ComputeHash(nonce);
    }
}
