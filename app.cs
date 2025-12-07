using DiscodeBot.scripts._src;
using DiscodeBot.scripts.db;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DiscodeBot;

class App
{
    // 클라이언트, 앱 초기화
    private DiscordSocketClient? _client;
    private Controller? _controller;

    static async Task Main(string[] args)
    {
        var program = new Program();
        await program.RunAsync(args);
    }

    public async Task RunAsync(string[] args)
    {
        var isTestMode = args.Length >= 1 && args[0] == "test";

        // 서비스 컬렉션 설정
        var services = ConfigureServices();

        // Discord 클라이언트 초기화
        _client = services.GetRequiredService<DiscordSocketClient>();
        _controller = services.GetRequiredService<Controller>();

        // 이벤트 핸들러 등록
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;

        // 봇 로그인 및 시작 - 테스트 모드면 TEST_DISCORD_TOKEN 사용
        var token = isTestMode 
            ? Environment.GetEnvironmentVariable("TEST_DISCORDTOKEN")
            : Environment.GetEnvironmentVariable("DISCORDTOKEN");
            
        if (string.IsNullOrEmpty(token))
            return Console.WriteLine($"에러: {(isTestMode ? "TEST_DISCORD_TOKEN" : "DISCORD_TOKEN")} 환경변수가 설정되지 않았습니다.");

        Console.WriteLine($"[{(isTestMode ? "테스트" : "프로덕션")} 모드] 봇 시작 중...");
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        DatabaseController.Init();

        // 명령어 핸들러 초기화
        _controller.Init();

        // 프로그램이 종료되지 않도록 대기
        await Task.Delay(-1);
    }

    private ServiceProvider ConfigureServices()
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Info
        };

        return new ServiceCollection()
            .AddSingleton(new DiscordSocketClient(config))
            .AddSingleton<Controller>()
            .AddSingleton<DatabaseController>()  // DatabaseController 추가
            .BuildServiceProvider();
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        Console.WriteLine($"{_client?.CurrentUser.Username} 봇이 준비되었습니다!");
        return Task.CompletedTask;
    }
}


