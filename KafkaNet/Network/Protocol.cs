using System;
using System.Collections.Generic;

namespace KafkaNet.Network
{
    public enum RequestType : byte
    {
        Produce = 0x01,
        Consume = 0x02,
        CreateTopic = 0x03,
        Metadata = 0x04
    }

    public class RequestHeader
    {
        public RequestType Type { get; set; }
        public int Length { get; set; }
    }

    public class Response
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    public class ProduceRequest
    {
        public string Topic { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class ConsumeRequest
    {
        public string Topic { get; set; } = string.Empty;
        public int Partition { get; set; }
        public long Offset { get; set; }
        public int MaxMessages { get; set; }
    }

    public class CreateTopicRequest
    {
        public string Topic { get; set; } = string.Empty;
        public int Partitions { get; set; }
    }
}
