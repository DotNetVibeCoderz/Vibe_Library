using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using KafkaNet.Client;
using KafkaNet.Core;

namespace KafkaNet.Streams
{
    public class StreamProcessor
    {
        private readonly ConsumerClient _consumer;
        private readonly Subject<Message> _subject;
        private CancellationTokenSource _cts;
        private Task _pollingTask;

        public StreamProcessor(ConsumerClient consumer)
        {
            _consumer = consumer;
            _subject = new Subject<Message>();
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _pollingTask = Task.Run(async () =>
            {
                while (_consumer.IsRunning && !_cts.Token.IsCancellationRequested)
                {
                    await _consumer.PollAsync(TimeSpan.FromMilliseconds(100), (topic, msg) =>
                    {
                        _subject.OnNext(msg);
                    });
                }
            }, _cts.Token);
        }

        public IObservable<Message> FromTopic(string topicName)
        {
            _consumer.Subscribe(topicName);
            return _subject.AsObservable().Where(m => true);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _consumer.Stop();
            try { _pollingTask?.Wait(); } catch {}
            _subject.OnCompleted();
            _subject.Dispose();
        }
    }
}