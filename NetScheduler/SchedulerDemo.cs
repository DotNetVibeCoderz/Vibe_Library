using Quartz;
using Quartz.Impl.Matchers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetScheduler.Jobs;
using NetScheduler.Listeners;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetScheduler
{
    public class SchedulerDemo : IHostedService
    {
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly ILogger<SchedulerDemo> _logger;
        private readonly GlobalJobListener _globalListener;
        private IScheduler? _scheduler;

        public SchedulerDemo(
            ISchedulerFactory schedulerFactory, 
            ILogger<SchedulerDemo> logger,
            GlobalJobListener globalListener)
        {
            _schedulerFactory = schedulerFactory;
            _logger = logger;
            _globalListener = globalListener;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(1000, cancellationToken);

            _scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            if (_scheduler == null) return;

            _logger.LogInformation("--- NetScheduler Demo Started ---");

            // --- Case 10: Trigger Listener (Global) ---
            _scheduler.ListenerManager.AddJobListener(
                _globalListener, 
                GroupMatcher<JobKey>.GroupEquals("Group1")
            );
            _logger.LogInformation("10. Registered GlobalJobListener for Group1");


            // --- Case 1: Simple Interval Job ---
            var job1 = JobBuilder.Create<SimpleJob>()
                .WithIdentity("SimpleJob", "Group1")
                .Build();

            var trigger1 = TriggerBuilder.Create()
                .WithIdentity("Trigger1", "Group1")
                .StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(5).RepeatForever())
                .Build();

            if (!await _scheduler.CheckExists(job1.Key, cancellationToken))
            {
                 await _scheduler.ScheduleJob(job1, trigger1, cancellationToken);
                 _logger.LogInformation("1. Scheduled SimpleJob (Interval 5s)");
            }

            // --- Case 2: Cron Job ---
            var job2 = JobBuilder.Create<SimpleJob>()
                .WithIdentity("CronJob", "Group1")
                .Build();

            var trigger2 = TriggerBuilder.Create()
                .WithIdentity("Trigger2", "Group1")
                .WithCronSchedule("0 * * * * ?") 
                .Build();

            if (!await _scheduler.CheckExists(job2.Key, cancellationToken))
            {
                await _scheduler.ScheduleJob(job2, trigger2, cancellationToken);
                _logger.LogInformation("2. Scheduled CronJob (0 * * * * ?)");
            }

            // --- Case 3: Job with Data ---
            var job3 = JobBuilder.Create<ParamJob>()
                .WithIdentity("ParamJob", "Group1")
                .UsingJobData("Message", "Hello from Gravicode!")
                .UsingJobData("Count", 100)
                .Build();

            var trigger3 = TriggerBuilder.Create()
                .WithIdentity("Trigger3", "Group1")
                .StartNow()
                .Build(); 

            if (!await _scheduler.CheckExists(job3.Key, cancellationToken))
            {
                await _scheduler.ScheduleJob(job3, trigger3, cancellationToken);
                _logger.LogInformation("3. Scheduled ParamJob (One time execution with data)");
            }

            // --- Case 4: Stateful Job ---
            var job4 = JobBuilder.Create<StatefulJob>()
               .WithIdentity("StatefulJob", "Group1")
               .UsingJobData("ExecutionCount", 0)
               .Build();

            var trigger4 = TriggerBuilder.Create()
                .WithIdentity("Trigger4", "Group1")
                .StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(3).WithRepeatCount(3))
                .Build();

             if (!await _scheduler.CheckExists(job4.Key, cancellationToken))
            {
                await _scheduler.ScheduleJob(job4, trigger4, cancellationToken);
                _logger.LogInformation("4. Scheduled StatefulJob (Persists data between runs)");
            }

            // --- Case 5: Concurrency Control ---
            var job5 = JobBuilder.Create<NoConcurrentJob>()
                .WithIdentity("NoConcurrentJob", "Group1")
                .Build();

            var trigger5 = TriggerBuilder.Create()
                .WithIdentity("Trigger5", "Group1")
                .StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(1).WithRepeatCount(2))
                .Build();

             if (!await _scheduler.CheckExists(job5.Key, cancellationToken))
            {
                await _scheduler.ScheduleJob(job5, trigger5, cancellationToken);
                _logger.LogInformation("5. Scheduled NoConcurrentJob (Job 3s, Trigger 1s - shows queuing)");
            }

            // --- Case 6: Error Handling ---
            var job6 = JobBuilder.Create<FailingJob>()
                .WithIdentity("FailingJob", "Group1")
                .Build();

            var trigger6 = TriggerBuilder.Create()
                .WithIdentity("Trigger6", "Group1")
                .StartAt(DateTimeOffset.Now.AddSeconds(2))
                .Build();

             if (!await _scheduler.CheckExists(job6.Key, cancellationToken))
            {
                await _scheduler.ScheduleJob(job6, trigger6, cancellationToken);
                _logger.LogInformation("6. Scheduled FailingJob (Will throw exception)");
            }

            // --- Case 7: Dependency Injection ---
            var job7 = JobBuilder.Create<DiJob>()
                .WithIdentity("DiJob", "Group1")
                .Build();
            
            var trigger7 = TriggerBuilder.Create()
                .WithIdentity("Trigger7", "Group1")
                .StartAt(DateTimeOffset.Now.AddSeconds(1))
                .Build();

             if (!await _scheduler.CheckExists(job7.Key, cancellationToken))
            {
                await _scheduler.ScheduleJob(job7, trigger7, cancellationToken);
                _logger.LogInformation("7. Scheduled DiJob (Uses Injected Service)");
            }

            // --- Case 8: Misfire Handling ---
            // Fix: Create Job FIRST, then link trigger to it.
            var job8 = JobBuilder.Create<SimpleJob>()
                 .WithIdentity("MisfireJob", "Group1")
                 .StoreDurably() 
                 .Build();

            var trigger8 = TriggerBuilder.Create()
                .WithIdentity("MisfireTrigger", "Group1")
                .StartAt(DateTime.Now.AddSeconds(-10))
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(10)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionFireNow()) 
                .ForJob(job8) // Fixed: Explicitly link trigger to job
                .Build();

             if (!await _scheduler.CheckExists(job8.Key, cancellationToken))
            {
                await _scheduler.AddJob(job8, true, cancellationToken);
                await _scheduler.ScheduleJob(trigger8, cancellationToken);
                _logger.LogInformation("8. Scheduled MisfireJob (Configured to FireNow)");
            }

            // --- Case 9: Calendar ---
            Quartz.Impl.Calendar.HolidayCalendar holidayCalendar = new Quartz.Impl.Calendar.HolidayCalendar();
            holidayCalendar.AddExcludedDate(DateTime.Now.AddSeconds(5).Date); 
            
            try {
                await _scheduler.AddCalendar("myHolidays", holidayCalendar, false, false, cancellationToken);
                _logger.LogInformation("9. Created HolidayCalendar 'myHolidays' (API Demonstration)");
            } catch {}

            _logger.LogInformation("--- All Demo Jobs Scheduled. Watch the console! ---");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
