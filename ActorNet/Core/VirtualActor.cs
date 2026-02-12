using System;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ActorNet.Core
{
    public abstract class VirtualActor : IActor
    {
        public string Id { get; private set; }
        public IActorSystem System { get; private set; } // Public getter for context access
        
        private readonly Channel<MessageEnvelope> _mailbox;
        private Task _processingLoop;
        private readonly CancellationTokenSource _cts = new();

        protected object State { get; set; }

        protected VirtualActor()
        {
            _mailbox = Channel.CreateUnbounded<MessageEnvelope>();
        }

        public void Initialize(string id, ActorSystem system)
        {
            Id = id;
            System = system;
            _processingLoop = Task.Run(ProcessMailboxAsync);
        }

        public virtual Task ActivateAsync()
        {
            // Lifecycle hook
            return Task.CompletedTask;
        }

        public virtual Task DeactivateAsync()
        {
            _cts.Cancel();
            return Task.CompletedTask;
        }

        public async Task PushMessageAsync(object message, string senderId)
        {
            string payloadJson;
            string messageType;
            
            if (message is MessageEnvelope env)
            {
                payloadJson = env.PayloadJson;
                messageType = env.MessageType;
                senderId = env.SenderActorId;
            }
            else
            {
                payloadJson = JsonConvert.SerializeObject(message);
                // Use fully qualified name to help type resolution
                messageType = message.GetType().FullName + ", " + message.GetType().Assembly.GetName().Name;
            }

            var envelope = new MessageEnvelope
            {
                TargetActorId = Id,
                SenderActorId = senderId,
                MessageType = messageType,
                PayloadJson = payloadJson,
                Timestamp = DateTime.UtcNow
            };
            
            await _mailbox.Writer.WriteAsync(envelope);
        }

        private async Task ProcessMailboxAsync()
        {
            try 
            {
                while (await _mailbox.Reader.WaitToReadAsync(_cts.Token))
                {
                    while (_mailbox.Reader.TryRead(out var envelope))
                    {
                        try 
                        {
                            object messageObject = null;
                            
                            // Try to resolve type
                            Type type = Type.GetType(envelope.MessageType);
                            if (type == null)
                            {
                                // Fallback: Iterate loaded assemblies
                                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                                {
                                    type = asm.GetType(envelope.MessageType.Split(',')[0]);
                                    if (type != null) break;
                                }
                            }

                            if (type != null)
                            {
                                messageObject = JsonConvert.DeserializeObject(envelope.PayloadJson, type);
                            }
                            else
                            {
                                // Fallback to JObject if type unknown
                                messageObject = JObject.Parse(envelope.PayloadJson);
                            }

                            if (messageObject != null)
                            {
                                await ReceiveAsync(new ActorContext(this, envelope), messageObject);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Error] Actor {Id} failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
        }

        public abstract Task ReceiveAsync(IActorContext context, object message);
    }
    
    public class ActorContext : IActorContext
    {
        private readonly VirtualActor _actor;
        private readonly MessageEnvelope _envelope;

        public string SelfId => _actor.Id;
        public IActorSystem System => _actor.System;

        public ActorContext(VirtualActor actor, MessageEnvelope envelope)
        {
            _actor = actor;
            _envelope = envelope;
        }

        public void Send(string targetId, object message)
        {
            _ = System.SendMessageAsync(targetId, message, SelfId);
        }

        public void Reply(object message)
        {
            if (!string.IsNullOrEmpty(_envelope.SenderActorId))
            {
                Send(_envelope.SenderActorId, message);
            }
        }
    }
}