using System;

namespace KafkaNet.Core
{
    public class Message
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public long Offset { get; set; }
        public DateTime Timestamp { get; set; }

        public Message(string key, string value)
        {
            Key = key;
            Value = value;
            Timestamp = DateTime.UtcNow;
        }
    }
}