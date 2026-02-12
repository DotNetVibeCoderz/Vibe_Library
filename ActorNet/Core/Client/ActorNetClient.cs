using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ActorNet.Core.Client
{
    public class ActorNetClient
    {
        private readonly string _host;
        private readonly int _port;

        public ActorNetClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public async Task SendMessageAsync(string targetActorId, object message, string senderId = "client")
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port);
                
                using var stream = client.GetStream();
                
                var envelope = new MessageEnvelope
                {
                    TargetActorId = targetActorId,
                    SenderActorId = senderId, // Client ID
                    MessageType = message.GetType().FullName + ", " + message.GetType().Assembly.GetName().Name,
                    PayloadJson = JsonConvert.SerializeObject(message),
                    Timestamp = DateTime.UtcNow
                };

                var json = JsonConvert.SerializeObject(envelope);
                var bytes = Encoding.UTF8.GetBytes(json);
                
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client] Error sending message: {ex.Message}");
            }
        }
    }
}