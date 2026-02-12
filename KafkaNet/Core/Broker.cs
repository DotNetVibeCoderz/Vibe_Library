using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

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
            
            LoadTopics();
        }

        private void LoadTopics()
        {
            try
            {
                var topicDirs = Directory.GetDirectories(_storagePath);
                foreach (var dir in topicDirs)
                {
                    var topicName = Path.GetFileName(dir);
                    var partitionDirs = Directory.GetDirectories(dir);
                    
                    // Simple logic: number of partitions = number of subdirectories that are integers
                    int partitionCount = 0;
                    foreach(var pDir in partitionDirs)
                    {
                        if (int.TryParse(Path.GetFileName(pDir), out _))
                        {
                            partitionCount++;
                        }
                    }

                    if (partitionCount > 0)
                    {
                        var topic = new Topic(topicName, partitionCount, _storagePath);
                        _topics.TryAdd(topicName, topic);
                        Console.WriteLine($"Loaded topic '{topicName}' with {partitionCount} partitions.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading topics: {ex.Message}");
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
