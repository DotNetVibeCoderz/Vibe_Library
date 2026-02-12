using System;
using System.Threading.Tasks;
using KafkaNet.Network;

namespace KafkaNet.Client
{
    public class ProducerClient : IDisposable
    {
        private readonly KafkaClient _client;

        public ProducerClient(string host = "127.0.0.1", int port = 9092)
        {
            _client = new KafkaClient(host, port);
        }

        public async Task SendAsync(string topic, string key, string value)
        {
            var request = new ProduceRequest
            {
                Topic = topic,
                Key = key,
                Value = value
            };

            var response = await _client.SendRequestAsync(RequestType.Produce, request);
            if (!response.Success)
            {
                throw new Exception($"Failed to send message: {response.Error}");
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
