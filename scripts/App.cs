using System.Reflection;
using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src;
using DiscordBot.scripts._src.Services;
using DiscordBot.scripts.db;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace DiscordBot.scripts;

public class App
{
    public static bool IsTest = false;
    static async Task Main(string[] args)
    {
        IsTest = args.Length >= 1 && args[0] == "test";
        
        // .NET 9.0 최신 방식: HostApplicationBuilder 사용
        var builder = Host.CreateApplicationBuilder(args);

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Info
        };

        var assembly = Assembly.GetExecutingAssembly();

        var serviceTypes = assembly.GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                !type.IsInterface &&
                typeof(ISingleton).IsAssignableFrom(type) &&
                type.Namespace != null)
            .ToList();

        var serviceCollection = builder.Services;
        serviceCollection
            .AddSingleton(new DiscordSocketClient(config))
            .AddQuartz()
            .AddLogging(configure =>
            {
                configure.AddFilter("Quartz", LogLevel.Error);
                configure.AddFilter("Microsoft", LogLevel.Error);
                
            })
            .AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
            
        foreach (var serviceType in serviceTypes)
        {
            serviceCollection.AddSingleton(serviceType);
        }
        
        var host = builder.Build();
        
        foreach (var serviceType in serviceTypes)
        {
            host.Services.GetRequiredService(serviceType);
        }

        // 호스트 빌드
        
        DatabaseController.Init();

        // Quartz 스케줄러 시작 (테스트 모드가 아닐 때만)
        if (!IsTest)
        {
            _ = Task.Run(async () =>
            {
                var schedulerFactory = host.Services.GetRequiredService<ISchedulerFactory>();
                var scheduler = await schedulerFactory.GetScheduler();
                
                // Job 정의
                var job = JobBuilder.Create<CycleJob>()
                    .WithIdentity("cycleJob", "party")
                    .Build();

                // Trigger 정의 (매 분마다 실행)
                var trigger = TriggerBuilder.Create()
                    .WithIdentity("cycleTrigger", "party")
                    .WithCronSchedule("0 * * * * ?") // 매 분 0초에 실행
                    .StartNow()
                    .Build();

                // 스케줄 등록
                await scheduler.ScheduleJob(job, trigger);
                await scheduler.Start();

                Console.WriteLine("[Cycle] Quartz 스케줄러가 시작되었습니다. (매 분마다 실행)");
            });
        }
        
        // 프로그램이 종료되지 않도록 대기
        await host.RunAsync();
    }
}


