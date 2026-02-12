using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KafkaNet.Core;

namespace KafkaNet.Client
{
    public class ConsumerClient
    {
        private readonly Broker _broker;
        private readonly string _consumerGroup;
        // Key: "Topic-PartitionID", Value: Offset
        private readonly ConcurrentDictionary<string, long> _partitionOffsets;
        private readonly List<string> _subscribedTopics;
        public bool IsRunning { get; private set; } = true;

        public ConsumerClient(Broker broker, string consumerGroup)
        {
            _broker = broker;
            _consumerGroup = consumerGroup;
            _partitionOffsets = new ConcurrentDictionary<string, long>();
            _subscribedTopics = new List<string>();
        }

        public void Subscribe(string topic)
        {
            if (!_subscribedTopics.Contains(topic))
            {
                _subscribedTopics.Add(topic);
            }
        }

        public async Task PollAsync(TimeSpan timeout, Action<string, Message> onMessageReceived)
        {
            if (!IsRunning) return;

            bool messageFound = false;

            foreach (var topicName in _subscribedTopics)
            {
                var topic = _broker.GetTopic(topicName);
                if (topic == null) continue;

                foreach (var partition in topic.GetPartitions())
                {
                    string key = $"{topicName}-{partition.Id}";
                    long currentOffset = _partitionOffsets.GetOrAdd(key, 0);

                    // Read batch
                    var messages = partition.Read(currentOffset, 10); // Small batch for responsiveness
                    
                    if (messages.Count > 0)
                    {
                        messageFound = true;
                        foreach (var msg in messages)
                        {
                            onMessageReceived(topicName, msg);
                            // Update offset to next
                            currentOffset = msg.Offset + 1;
                        }
                        _partitionOffsets[key] = currentOffset;
                    }
                }
            }

            if (!messageFound)
            {
                await Task.Delay(timeout);
            }
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}