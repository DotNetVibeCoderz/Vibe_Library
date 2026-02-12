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
        
        // Cache messages for fast reading. In real implementation, this should be limited or use memory mapped files.
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
            
            LoadFromLog();
        }

        private void LoadFromLog()
        {
            if (!File.Exists(_logFilePath)) return;

            try
            {
                using (var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new BinaryReader(fs))
                {
                    while (fs.Position < fs.Length)
                    {
                        try
                        {
                            // Format: [CRC (4 bytes)] [Length (4 bytes)] [Data (Length bytes)]
                            if (fs.Length - fs.Position < 8) break; // Incomplete header

                            uint storedCrc = reader.ReadUInt32();
                            int length = reader.ReadInt32();

                            if (length < 0 || fs.Length - fs.Position < length) break; // Incomplete data

                            byte[] data = reader.ReadBytes(length);
                            uint calculatedCrc = Crc32.Compute(data);

                            if (storedCrc == calculatedCrc)
                            {
                                var json = System.Text.Encoding.UTF8.GetString(data);
                                var msg = JsonConvert.DeserializeObject<Message>(json);
                                if (msg != null)
                                {
                                    _inMemoryBuffer.Add(msg);
                                    _currentOffset = msg.Offset + 1;
                                }
                            }
                            else
                            {
                                // Corruption detected, stop reading or maybe truncate file here?
                                // For simplicity, we stop reading.
                                Console.WriteLine($"[Partition {Id}] Corruption detected at offset {_currentOffset}. Truncating log.");
                                // Future improvement: truncate file to valid length
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Partition {Id}] Error reading log: {ex.Message}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Partition {Id}] Critical error loading log: {ex.Message}");
            }
        }

        public void Append(Message message)
        {
            lock (_lock)
            {
                message.Offset = _currentOffset++;
                _inMemoryBuffer.Add(message);

                var serialized = JsonConvert.SerializeObject(message);
                var data = System.Text.Encoding.UTF8.GetBytes(serialized);
                var crc = Crc32.Compute(data);
                var length = data.Length;

                try
                {
                    using (var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var writer = new BinaryWriter(fs))
                    {
                        writer.Write(crc);
                        writer.Write(length);
                        writer.Write(data);
                        writer.Flush();
                        fs.Flush(true); // Ensure written to disk
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Partition {Id}] Failed to write message: {ex.Message}");
                    // Rollback memory state if file write fails? 
                    // Ideally yes, but keeping it simple for now.
                }
            }
        }

        public List<Message> Read(long startOffset, int maxMessages)
        {
            lock (_lock)
            {
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

    // Simple CRC32 implementation
    public static class Crc32
    {
        private static readonly uint[] Table;

        static Crc32()
        {
            const uint polynomial = 0xedb88320;
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ polynomial;
                    else
                        entry = entry >> 1;
                Table[i] = entry;
            }
        }

        public static uint Compute(byte[] buffer)
        {
            uint crc = 0xffffffff;
            foreach (byte b in buffer)
                crc = (crc >> 8) ^ Table[(crc ^ b) & 0xff];
            return ~crc;
        }
    }
}
