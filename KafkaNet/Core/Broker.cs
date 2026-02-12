using System;
using System.Collections.Concurrent;
using System.IO;

namespace KafkaNet.Core
{
    public class Broker
    {
        public int Id { get; private set; }
        private readonly string _storagePath;
        private readonly ConcurrentDictionary<string, Topic> _topics;

        public Broker(int id, string storagePath = "./kafka-data")
        {
            Id = id;
            _storagePath = storagePath;
            _topics = new ConcurrentDictionary<string, Topic>();

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }

        public void CreateTopic(string topicName, int partitions = 1)
        {
            if (!_topics.ContainsKey(topicName))
            {
                var newTopic = new Topic(topicName, partitions, _storagePath);
                _topics.TryAdd(topicName, newTopic);
            }
        }

        public Topic? GetTopic(string topicName)
        {
            if (_topics.TryGetValue(topicName, out var topic))
            {
                return topic;
            }
            return null;
        }
        
        public bool TopicExists(string topicName)
        {
             return _topics.ContainsKey(topicName);
        }

        public void PublishToTopic(string topicName, string key, string value)
        {
            if (!_topics.ContainsKey(topicName))
            {
                CreateTopic(topicName, 1);
            }

            if (_topics.TryGetValue(topicName, out var topic))
            {
                topic.Publish(key, value);
            }
        }
    }
}