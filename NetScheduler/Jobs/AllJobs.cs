using Quartz;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace NetScheduler.Jobs
{
    // 1. Basic Job
    public class SimpleJob : IJob
    {
        private readonly ILogger<SimpleJob> _logger;
        public SimpleJob(ILogger<SimpleJob> logger)
        {
            _logger = logger;
        }

        public Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation($"[SimpleJob] Executed at {DateTime.Now}");
            return Task.CompletedTask;
        }
    }

    // 2. Job with Parameters (Data Map)
    public class ParamJob : IJob
    {
        private readonly ILogger<ParamJob> _logger;
        public ParamJob(ILogger<ParamJob> logger) => _logger = logger;

        public Task Execute(IJobExecutionContext context)
        {
            JobDataMap dataMap = context.JobDetail.JobDataMap;
            string? message = dataMap.GetString("Message"); // Nullable fix
            int count = dataMap.GetInt("Count");

            _logger.LogInformation($"[ParamJob] Received Message: {message}, Count: {count}");
            return Task.CompletedTask;
        }
    }

    // 3. Stateful Job (PersistJobDataAfterExecution)
    [PersistJobDataAfterExecution]
    public class StatefulJob : IJob
    {
        private readonly ILogger<StatefulJob> _logger;
        public StatefulJob(ILogger<StatefulJob> logger) => _logger = logger;

        public Task Execute(IJobExecutionContext context)
        {
            JobDataMap data = context.JobDetail.JobDataMap;
            int executeCount = data.GetInt("ExecutionCount");
            executeCount++;
            data.Put("ExecutionCount", executeCount);

            _logger.LogInformation($"[StatefulJob] Execution Count is now: {executeCount}");
            return Task.CompletedTask;
        }
    }

    // 4. Concurrency Control (DisallowConcurrentExecution)
    [DisallowConcurrentExecution]
    public class NoConcurrentJob : IJob
    {
        private readonly ILogger<NoConcurrentJob> _logger;
        public NoConcurrentJob(ILogger<NoConcurrentJob> logger) => _logger = logger;

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation($"[NoConcurrentJob] Started... (This takes 3 seconds)");
            await Task.Delay(3000); // Simulate long running task
            _logger.LogInformation($"[NoConcurrentJob] Finished.");
        }
    }

    // 5. Failing Job (For Retry/Error Handling)
    public class FailingJob : IJob
    {
        private readonly ILogger<FailingJob> _logger;
        public FailingJob(ILogger<FailingJob> logger) => _logger = logger;

        public Task Execute(IJobExecutionContext context)
        {
            _logger.LogWarning($"[FailingJob] OOPS! Something went wrong.");
            
            // Fix: Create exception properly
            var ex = new JobExecutionException("Simulated Failure");
            ex.RefireImmediately = false; // Do not retry immediately
            throw ex;
        }
    }

    // 6. DI Job (Service Injection)
    public interface IMyService { void DoWork(); }
    public class MyService : IMyService
    {
        public void DoWork() => Console.WriteLine("    -> [MyService] Logic executed via Dependency Injection!");
    }

    public class DiJob : IJob
    {
        private readonly IMyService _service;
        private readonly ILogger<DiJob> _logger;

        public DiJob(IMyService service, ILogger<DiJob> logger)
        {
            _service = service;
            _logger = logger;
        }

        public Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("[DiJob] Calling injected service...");
            _service.DoWork();
            return Task.CompletedTask;
        }
    }
}
