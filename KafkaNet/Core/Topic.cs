using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace KafkaNet.Core
{
    public class Topic
    {
        public string Name { get; private set; }
        public int PartitionCount { get; private set; }
        private readonly List<Partition> _partitions;

        public Topic(string name, int partitionCount, string storagePath)
        {
            Name = name;
            PartitionCount = partitionCount;
            _partitions = new List<Partition>();
            
            for (int i = 0; i < partitionCount; i++)
            {
                _partitions.Add(new Partition(name, i, storagePath));
            }
        }

        public void Publish(string key, string value)
        {
            // Simple partitioning strategy: Hash(Key) % PartitionCount
            int partitionIndex = 0;
            if (!string.IsNullOrEmpty(key))
            {
                partitionIndex = Math.Abs(key.GetHashCode()) % PartitionCount;
            }
            // Round-robin fallback if key is null is not implemented for brevity, using 0
            
            var partition = _partitions[partitionIndex];
            var message = new Message(key, value);
            partition.Append(message);
        }

        public Partition GetPartition(int partitionId)
        {
            if (partitionId >= 0 && partitionId < _partitions.Count)
            {
                return _partitions[partitionId];
            }
            throw new ArgumentOutOfRangeException(nameof(partitionId), "Partition does not exist");
        }

        public List<Partition> GetPartitions()
        {
            return _partitions;
        }
    }
}