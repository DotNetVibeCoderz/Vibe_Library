using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ActorNet.Core.Network;

namespace ActorNet.Core
{
    public class ActorSystem : IActorSystem
    {
        private readonly ConcurrentDictionary<string, VirtualActor> _activeActors = new();
        private readonly ConcurrentDictionary<string, Type> _actorTypeRegistry = new();
        
        private readonly string _nodeId;
        private int _port;
        private NodeListener _listener;

        public string NodeId => _nodeId;

        public ActorSystem(string nodeId, int port)
        {
            _nodeId = nodeId;
            _port = port;
        }

        public void Start(int port)
        {
            _port = port;
            Start();
        }

        public void Start()
        {
            _listener = new NodeListener(this);
            _listener.Start(_port);
            Console.WriteLine($"[ActorSystem] Node {_nodeId} started on port {_port}");
        }

        public void Stop()
        {
            _listener?.Stop();
            foreach(var actor in _activeActors.Values)
            {
                // In a real system, we'd await these gracefully
                actor.DeactivateAsync().Wait();
            }
            _activeActors.Clear();
            Console.WriteLine("[ActorSystem] Stopped.");
        }

        public void RegisterActorType<T>() where T : VirtualActor
        {
            _actorTypeRegistry.TryAdd(typeof(T).Name, typeof(T));
        }

        public IActorRef ActorOf<T>(string key) where T : VirtualActor, new()
        {
            RegisterActorType<T>();
            var typeName = typeof(T).Name;
            var fullId = $"{typeName}/{key}";
            return new LocalActorRef(fullId, this);
        }

        public async Task DispatchMessageAsync(string targetId, object message, string senderId = null)
        {
            // 1. Parse ID
            var parts = targetId.Split('/');
            if (parts.Length < 2) 
            {
                Console.WriteLine($"[System] Invalid Actor ID: {targetId}");
                return; 
            }

            var typeName = parts[0];
            
            // 2. Get or Activate Actor
            // Uses a lock-free approach mostly, but uses a lock for activation to avoid double activation
            // However, GetOrAdd is atomic. The factory might run multiple times concurrently though.
            // For simplicity, we assume single-threaded activation per key is handled by the dictionary implementation roughly correctly.
            
            // Note: In high-concurrency scenarios, we need lazy initialization.
            // Here, we just do it directly.
            
            if (!_activeActors.TryGetValue(targetId, out var actor))
            {
                if (_actorTypeRegistry.TryGetValue(typeName, out var type))
                {
                    // Double check locking pattern would be better here but dictionary handles insertion race
                    actor = _activeActors.GetOrAdd(targetId, _ => 
                    {
                        var newActor = (VirtualActor)Activator.CreateInstance(type);
                        newActor.Initialize(targetId, this);
                        Task.Run(() => newActor.ActivateAsync());
                        return newActor;
                    });
                }
                else
                {
                    Console.WriteLine($"[System] Actor type '{typeName}' not registered.");
                    return;
                }
            }

            if (actor != null)
            {
                await actor.PushMessageAsync(message, senderId);
            }
        }

        public IActorRef GetActor<T>(string id) where T : VirtualActor, new()
        {
            return ActorOf<T>(id);
        }

        public async Task SendMessageAsync(string targetId, object message, string senderId = null)
        {
            await DispatchMessageAsync(targetId, message, senderId);
        }
    }
}