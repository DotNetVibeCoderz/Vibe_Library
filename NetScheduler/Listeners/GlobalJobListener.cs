using Quartz;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace NetScheduler.Listeners
{
    public class GlobalJobListener : IJobListener
    {
        private readonly ILogger<GlobalJobListener> _logger;

        public GlobalJobListener(ILogger<GlobalJobListener> logger)
        {
            _logger = logger;
        }

        public string Name => "GlobalJobListener";

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug($"[Listener] Job {context.JobDetail.Key} is about to execute.");
            return Task.CompletedTask;
        }

        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug($"[Listener] Job {context.JobDetail.Key} was vetoed.");
            return Task.CompletedTask;
        }

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            if (jobException != null)
            {
                _logger.LogError($"[Listener] Job {context.JobDetail.Key} failed with: {jobException.Message}");
            }
            else
            {
                _logger.LogDebug($"[Listener] Job {context.JobDetail.Key} finished successfully.");
            }
            return Task.CompletedTask;
        }
    }
}
