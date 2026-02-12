using System;
using System.Threading.Tasks;
using KafkaNet.Core;

namespace KafkaNet.Client
{
    public class ProducerClient
    {
        private readonly Broker _broker;

        public ProducerClient(Broker broker)
        {
            _broker = broker;
        }

        public async Task SendAsync(string topic, string key, string value)
        {
            // Simulate network latency
            await Task.Delay(5); // 5ms simulated latency
            _broker.PublishToTopic(topic, key, value);
        }
    }
}