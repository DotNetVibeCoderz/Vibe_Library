using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using NetScheduler.Jobs;
using NetScheduler.Listeners;
using System;

namespace NetScheduler
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Jacky the code bender here! NetScheduler is starting...");
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // 1. Add Quartz services
                    services.AddQuartz(q =>
                    {
                        q.UseMicrosoftDependencyInjectionJobFactory();

                        // Register Jobs for DI
                        // Note: In newer Quartz versions, we might not need to explicitly add them if we use DI factory, 
                        // but it's good practice or sometimes required depending on version.
                        // However, we will register them as transient services below manually to be safe.
                    });

                    // 2. Add Quartz Hosted Service (Daemon)
                    services.AddQuartzHostedService(options =>
                    {
                        options.WaitForJobsToComplete = true;
                    });

                    // 3. Register Jobs
                    services.AddTransient<SimpleJob>();
                    services.AddTransient<ParamJob>();
                    services.AddTransient<StatefulJob>();
                    services.AddTransient<NoConcurrentJob>();
                    services.AddTransient<FailingJob>();
                    services.AddTransient<DiJob>();

                    // 4. Register Dependencies
                    services.AddSingleton<IMyService, MyService>();
                    services.AddSingleton<GlobalJobListener>(); // Register Listener

                    // 5. Register our Demo Orchestrator
                    services.AddHostedService<SchedulerDemo>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                });
    }
}
