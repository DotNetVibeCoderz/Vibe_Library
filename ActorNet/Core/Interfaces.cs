using System;
using System.Threading.Tasks;

namespace ActorNet.Core
{
    // Base interface for all messages
    public interface IMessage { }

    // Standard envelope for distributed communication
    public class MessageEnvelope
    {
        public string TargetActorId { get; set; }
        public string SenderActorId { get; set; }
        public string MessageType { get; set; }
        public string PayloadJson { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // Context provided to actors during processing
    public interface IActorContext
    {
        string SelfId { get; }
        IActorSystem System { get; }
        void Send(string targetId, object message);
        void Reply(object message);
    }

    // Interface for Actors
    public interface IActor
    {
        string Id { get; }
        Task ActivateAsync();
        Task DeactivateAsync();
        Task ReceiveAsync(IActorContext context, object message);
    }

    // Abstract reference to an actor
    public interface IActorRef 
    {
        string Id { get; }
        Task Tell(object message, string senderId = null);
        Task<TResponse> Ask<TResponse>(object message, TimeSpan? timeout = null);
    }

    // The main engine interface
    public interface IActorSystem
    {
        void Start(int port);
        void Stop();
        IActorRef GetActor<T>(string id) where T : VirtualActor, new();
        Task SendMessageAsync(string targetId, object message, string senderId = null);
    }
}