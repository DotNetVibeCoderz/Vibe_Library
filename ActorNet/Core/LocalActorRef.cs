using System;
using System.Threading.Tasks;

namespace ActorNet.Core
{
    public class LocalActorRef : IActorRef
    {
        private readonly string _id;
        private readonly ActorSystem _system;

        public string Id => _id;

        public LocalActorRef(string id, ActorSystem system)
        {
            _id = id;
            _system = system;
        }

        public async Task Tell(object message, string senderId = null)
        {
             await _system.DispatchMessageAsync(_id, message, senderId);
        }

        public async Task<TResponse> Ask<TResponse>(object message, TimeSpan? timeout = null)
        {
            throw new NotImplementedException("Ask pattern requires full Future/Promise implementation.");
        }
    }
}