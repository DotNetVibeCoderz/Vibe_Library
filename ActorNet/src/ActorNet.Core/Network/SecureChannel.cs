// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace ActorNet.Network;

/// <summary>
/// Turns a raw socket into the stream the framing layer reads: TLS if configured, then the
/// authentication handshake if configured.
/// </summary>
/// <remarks>
/// <para>
/// Both steps happen before a single frame is exchanged, and both sides run them in the same order,
/// so a node that rejects a peer does so without ever having parsed anything the peer sent.
/// </para>
/// <para>
/// The challenge deliberately does not identify who is answering. It proves membership of the
/// cluster, not identity - identity is what mutual TLS is for. Conflating the two would make the
/// shared secret look like more than it is.
/// </para>
/// </remarks>
internal static class SecureChannel
{
    private const int NonceBytes = 32;
    private const int AnswerBytes = 32;   // HMAC-SHA256

    /// <summary>Wraps an accepted connection: server-side TLS, then challenge the caller.</summary>
    public static async Task<Stream> AcceptAsync(
        TcpClient client, ClusterSecurityOptions security, CancellationToken cancellationToken)
    {
        Stream stream = client.GetStream();

        if (security.ServerCertificate is { } certificate)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false,
                security.RequireClientCertificate ? security.RemoteCertificateValidation : null);

            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = security.RequireClientCertificate,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            }, cancellationToken).ConfigureAwait(false);

            stream = ssl;
        }

        if (security.AuthenticationEnabled)
            await ChallengeAsync(stream, security, cancellationToken).ConfigureAwait(false);

        return stream;
    }

    /// <summary>Wraps an outbound connection: client-side TLS, then answer the challenge.</summary>
    public static async Task<Stream> ConnectAsync(
        TcpClient client, string targetHost, ClusterSecurityOptions security, CancellationToken cancellationToken)
    {
        Stream stream = client.GetStream();

        if (security.TlsEnabled)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false, security.RemoteCertificateValidation);

            var options = new SslClientAuthenticationOptions
            {
                // The name the certificate is checked against. A cluster addresses peers by the
                // address they advertise, so that is what a certificate has to match.
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            };

            if (security.ClientCertificate is { } clientCertificate)
                options.ClientCertificates = [clientCertificate];

            await ssl.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            stream = ssl;
        }

        if (security.AuthenticationEnabled)
            await AnswerAsync(stream, security, cancellationToken).ConfigureAwait(false);

        return stream;
    }

    /// <summary>Server side: offer a nonce, check the answer, hang up if it is wrong.</summary>
    private static async Task ChallengeAsync(Stream stream, ClusterSecurityOptions security, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(security.HandshakeTimeout);

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        await stream.WriteAsync(nonce, deadline.Token).ConfigureAwait(false);
        await stream.FlushAsync(deadline.Token).ConfigureAwait(false);

        var answer = new byte[AnswerBytes];
        await ReadExactlyAsync(stream, answer, deadline.Token).ConfigureAwait(false);

        // Fixed-time comparison. A byte-by-byte one leaks how much of the MAC matched, which is
        // enough to forge an answer given enough attempts.
        if (!CryptographicOperations.FixedTimeEquals(answer, security.Answer(nonce)))
            throw new ClusterAuthenticationException("A peer failed the shared-secret challenge; the connection was refused.");
    }

    /// <summary>Client side: take the nonce, send back the HMAC.</summary>
    private static async Task AnswerAsync(Stream stream, ClusterSecurityOptions security, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(security.HandshakeTimeout);

        var nonce = new byte[NonceBytes];
        await ReadExactlyAsync(stream, nonce, deadline.Token).ConfigureAwait(false);

        await stream.WriteAsync(security.Answer(nonce), deadline.Token).ConfigureAwait(false);
        await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
    }

    /// <summary>Fills the buffer, or throws. A short handshake read is a refusal, not a retry.</summary>
    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (got == 0)
                throw new ClusterAuthenticationException(
                    $"The peer closed the connection {read} of {buffer.Length} bytes into the handshake. " +
                    "The usual cause is one side having authentication or TLS enabled and the other not.");

            read += got;
        }
    }
}

/// <summary>Thrown when a peer cannot prove it belongs in this cluster.</summary>
public sealed class ClusterAuthenticationException(string message) : ActorNetException(message);
