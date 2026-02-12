using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace KafkaNet.Network
{
    public class KafkaClient : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;

        public KafkaClient(string host = "127.0.0.1", int port = 9092)
        {
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
        }

        public async Task<Response> SendRequestAsync<TRequest>(RequestType type, TRequest request)
        {
            var json = JsonConvert.SerializeObject(request);
            var payload = Encoding.UTF8.GetBytes(json);
            
            // Send Header
            var header = new byte[5];
            header[0] = (byte)type;
            var lengthBytes = BitConverter.GetBytes(payload.Length);
            Array.Copy(lengthBytes, 0, header, 1, 4);
            
            await _stream.WriteAsync(header, 0, 5);
            await _stream.WriteAsync(payload, 0, payload.Length);
            await _stream.FlushAsync();

            // Receive Response
            var respLenBuffer = new byte[4];
            await ReadFullAsync(respLenBuffer, 4);
            int respLen = BitConverter.ToInt32(respLenBuffer, 0);

            var respBuffer = new byte[respLen];
            await ReadFullAsync(respBuffer, respLen);

            var respJson = Encoding.UTF8.GetString(respBuffer);
            return JsonConvert.DeserializeObject<Response>(respJson) ?? new Response { Success = false, Error = "Failed to deserialize response" };
        }

        private async Task ReadFullAsync(byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = await _stream.ReadAsync(buffer, total, count - total);
                if (read == 0) throw new Exception("Connection closed unexpectedly");
                total += read;
            }
        }

        public void Dispose()
        {
            _client?.Close();
            _client?.Dispose();
        }
    }
}
