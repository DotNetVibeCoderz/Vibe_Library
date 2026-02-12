using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace KafkaNet.Core
{
    public class Partition
    {
        public int Id { get; private set; }
        public string TopicName { get; private set; }
        private readonly string _logFilePath;
        private readonly object _lock = new object();
        private long _currentOffset = 0;
        
        private readonly List<Message> _inMemoryBuffer = new List<Message>();

        public Partition(string topicName, int partitionId, string storagePath)
        {
            TopicName = topicName;
            Id = partitionId;
            var directory = Path.Combine(storagePath, topicName, partitionId.ToString());
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            _logFilePath = Path.Combine(directory, "log.dat");
            
            if (File.Exists(_logFilePath))
            {
                var lines = File.ReadAllLines(_logFilePath);
                if (lines.Length > 0)
                {
                    _currentOffset = lines.Length;
                }
            }
        }

        public void Append(Message message)
        {
            lock (_lock)
            {
                message.Offset = _currentOffset++;
                _inMemoryBuffer.Add(message);

                var serialized = JsonConvert.SerializeObject(message);
                File.AppendAllText(_logFilePath, serialized + Environment.NewLine);
            }
        }

        public List<Message> Read(long startOffset, int maxMessages)
        {
            lock (_lock)
            {
                if (_inMemoryBuffer.Count == 0 && File.Exists(_logFilePath))
                {
                     var lines = File.ReadAllLines(_logFilePath);
                     foreach(var line in lines)
                     {
                         if (!string.IsNullOrWhiteSpace(line))
                         {
                            var msg = JsonConvert.DeserializeObject<Message>(line);
                            if (msg != null) _inMemoryBuffer.Add(msg);
                         }
                     }
                }

                return _inMemoryBuffer
                    .Where(m => m.Offset >= startOffset)
                    .Take(maxMessages)
                    .ToList();
            }
        }
        
        public long GetHighWatermark()
        {
            return _currentOffset;
        }
    }
}