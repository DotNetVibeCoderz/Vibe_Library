using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KafkaNet.Core;
using KafkaNet.Network;
using Newtonsoft.Json;

namespace KafkaNet.Client
{
    public class ConsumerClient : IDisposable
    {
        private readonly KafkaClient _client;
        private readonly string _groupId;
        private string _topic;
        private readonly Dictionary<int, long> _offsets = new Dictionary<int, long>();
        public bool IsRunning { get; private set; } = true;

        public ConsumerClient(string host, int port, string groupId = "default-group")
        {
            _client = new KafkaClient(host, port);
            _groupId = groupId;
        }

        public void Subscribe(string topic)
        {
            _topic = topic;
            if (!_offsets.ContainsKey(0)) _offsets[0] = 0; // Default offset 0 partition 0
        }

        public async Task PollAsync(TimeSpan timeout, Action<string, Message> onMessage)
        {
            if (!IsRunning) return;
            if (string.IsNullOrEmpty(_topic)) throw new InvalidOperationException("Subscribe to a topic first.");

            int partition = 0; // Simplified
            long currentOffset = _offsets.ContainsKey(partition) ? _offsets[partition] : 0;

            var request = new ConsumeRequest
            {
                Topic = _topic,
                Partition = partition,
                Offset = currentOffset,
                MaxMessages = 10 
            };

            try 
            {
                var response = await _client.SendRequestAsync(RequestType.Consume, request);
                if (response.Success && !string.IsNullOrEmpty(response.Data))
                {
                    var messages = JsonConvert.DeserializeObject<List<Message>>(response.Data);
                    if (messages != null && messages.Count > 0)
                    {
                        foreach (var msg in messages)
                        {
                            onMessage(_topic, msg);
                            // Update local offset to next message
                            currentOffset = msg.Offset + 1;
                        }
                        _offsets[partition] = currentOffset;
                    }
                }
            }
            catch (Exception ex)
            {
                // Simple logging
                // Console.WriteLine($"Error polling: {ex.Message}");
            }
            
            await Task.Delay(100); 
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
            _client.Dispose();
        }
    }
}
