using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ActorNet.Core.Network
{
    // Simple TCP Node Listener for Remote Actor Communication
    public class NodeListener
    {
        private TcpListener _listener;
        private readonly ActorSystem _system;
        private CancellationTokenSource _cts;

        public NodeListener(ActorSystem system)
        {
            _system = system;
        }

        public void Start(int port)
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Task.Run(() => AcceptClientsAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts.Cancel();
            _listener.Stop();
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = HandleClientAsync(client, token);
                }
                catch
                {
                    // Listener stopped
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    try
                    {
                        var envelope = JsonConvert.DeserializeObject<MessageEnvelope>(json);
                        if (envelope != null)
                        {
                            // In a real system, we deserialize payload based on TypeName using a binder
                            // Here we just pass the envelope to a special handler or try to guess.
                            // For the demo, we assume the payload is a simple JObject or Dictionary if complex type unknown.
                            
                            // NOTE: Deserialization vulnerability risk in production. Use a whitelist or specific binder.
                            
                            // Dispatch to local actor
                             await _system.DispatchMessageAsync(envelope.TargetActorId, envelope, envelope.SenderActorId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Network] Error processing message: {ex.Message}");
                    }
                }
            }
        }
    }
}