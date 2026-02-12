using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KafkaNet.Core;
using Newtonsoft.Json;

namespace KafkaNet.Network
{
    public class KafkaServer
    {
        private readonly Broker _broker;
        private readonly TcpListener _listener;
        private bool _isRunning;

        public KafkaServer(Broker broker, int port = 9092)
        {
            _broker = broker;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public async Task StartAsync()
        {
            _listener.Start();
            _isRunning = true;
            Console.WriteLine($"KafkaNet Server started on port 9092...");

            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    if (_isRunning) Console.WriteLine($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
            using (client)
            using (var stream = client.GetStream())
            {
                while (client.Connected)
                {
                    try
                    {
                        // Protocol: [Type: byte] [Length: int] [Payload: json]
                        var headerBuffer = new byte[5];
                        int bytesRead = await ReadFullAsync(stream, headerBuffer, 5);
                        if (bytesRead == 0) break;

                        var type = (RequestType)headerBuffer[0];
                        var length = BitConverter.ToInt32(headerBuffer, 1);

                        var payloadBuffer = new byte[length];
                        await ReadFullAsync(stream, payloadBuffer, length);

                        var payloadJson = Encoding.UTF8.GetString(payloadBuffer);
                        var response = ProcessRequest(type, payloadJson);

                        var responseJson = JsonConvert.SerializeObject(response);
                        var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                        var responseLength = BitConverter.GetBytes(responseBytes.Length);

                        await stream.WriteAsync(responseLength, 0, 4);
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling client: {ex.Message}");
                        break;
                    }
                }
            }
            Console.WriteLine("Client disconnected.");
        }

        private async Task<int> ReadFullAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, total, count - total);
                if (read == 0) return 0; // End of stream
                total += read;
            }
            return total;
        }

        private Response ProcessRequest(RequestType type, string payload)
        {
            try
            {
                switch (type)
                {
                    case RequestType.Produce:
                        var produceReq = JsonConvert.DeserializeObject<ProduceRequest>(payload);
                        if (produceReq != null)
                        {
                            _broker.PublishToTopic(produceReq.Topic, produceReq.Key, produceReq.Value);
                            return new Response { Success = true };
                        }
                        break;

                    case RequestType.Consume:
                        var consumeReq = JsonConvert.DeserializeObject<ConsumeRequest>(payload);
                        if (consumeReq != null)
                        {
                            var topic = _broker.GetTopic(consumeReq.Topic);
                            if (topic != null)
                            {
                                var partition = topic.GetPartition(consumeReq.Partition); // Using specific partition
                                var messages = partition.Read(consumeReq.Offset, consumeReq.MaxMessages);
                                return new Response { Success = true, Data = JsonConvert.SerializeObject(messages) };
                            }
                            else
                            {
                                return new Response { Success = false, Error = "Topic not found" };
                            }
                        }
                        break;

                    case RequestType.CreateTopic:
                        var createReq = JsonConvert.DeserializeObject<CreateTopicRequest>(payload);
                        if (createReq != null)
                        {
                            _broker.CreateTopic(createReq.Topic, createReq.Partitions);
                            return new Response { Success = true };
                        }
                        break;
                        
                    case RequestType.Metadata:
                        // Placeholder for metadata request
                        return new Response { Success = true, Data = "[]" };
                }
            }
            catch (Exception ex)
            {
                return new Response { Success = false, Error = ex.Message };
            }
            
            return new Response { Success = false, Error = "Invalid request" };
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
        }
    }
}
