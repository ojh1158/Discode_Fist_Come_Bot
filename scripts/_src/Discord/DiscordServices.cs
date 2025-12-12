using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts._src.Partys;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Discord;


public class DiscordServices : ISingleton
{
    public readonly DiscordSocketClient client;
    public DiscordServices(DiscordSocketClient discord)
    {
        client = discord;
        
        _ = Task.Run(async () =>
        {
            client.Log += LogAsync;
            client.Ready += () => ReadyAsync(client);

            // 봇 로그인 및 시작 - 테스트 모드면 TEST_DISCORD_TOKEN 사용
            var token = App.IsTest 
                ? Environment.GetEnvironmentVariable("TEST_DISCORDTOKEN")
                : Environment.GetEnvironmentVariable("DISCORDTOKEN");
            
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine($"에러: {(App.IsTest ? "TEST_DISCORD_TOKEN" : "DISCORD_TOKEN")} 환경변수가 설정되지 않았습니다.");
                return;
            }

            Console.WriteLine($"[{(App.IsTest ? "테스트" : "프로덕션")} 모드] 봇 시작 중...");
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();
            
            // client.SlashCommandExecuted += HandleSlashCommandAsync;
            // client.ButtonExecuted += HandleButtonAsync;
            // client.ModalSubmitted += HandleModalAsync;
            // client.SelectMenuExecuted += HandleSelectMenuAsync;
            // client.Ready += InitCommands;
        });
    }
    
    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private async Task ReadyAsync(DiscordSocketClient client)
    {
        var commands = await client.GetGlobalApplicationCommandsAsync();

        var array = new[]
        {
            new SlashCommandBuilder()
                .WithName("파티")
                .WithDescription($"파티를 생성합니다. 허용 인원은 {PartyConstant.MIN_COUNT}-{PartyConstant.MAX_COUNT} 입니다.")
                .AddOption("이름", ApplicationCommandOptionType.String, "파티 이름", isRequired: true, minLength: 1, maxLength: PartyConstant.MAX_NAME_COUNT)
                .AddOption("인원", ApplicationCommandOptionType.Integer, "파티 인원", isRequired: true)
                // .AddOption("호출", ApplicationCommandOptionType.Role, "해당 역할 소유자에게 알람을 보냅니다", isRequired: false)
                .AddOption("만료시간", ApplicationCommandOptionType.String, $"파티 만료 시간 ex(15m, 15h, 15분, 15시) 빈 필드: {PartyConstant.MAX_HOUR}시간", isRequired: false)
        };
        
        // 내용이 다르거나 없는 명령어 생성/업데이트
        foreach (var commandBuilder in array)
        {
            var built = commandBuilder.Build();
            var existing = commands.FirstOrDefault(c => c.Name == built.Name.Value);
            
            if (existing == null || !CommandEquals(existing, built))
            {
                if (existing != null)
                {
                    await existing.DeleteAsync();
                }
                await client.CreateGlobalApplicationCommandAsync(built);
            }
        }
        
        // array에 없는 명령어 삭제
        foreach (var socketApplicationCommand in commands.Where(c => !array.Any(f => f.Name == c.Name)))
        {
            await socketApplicationCommand.DeleteAsync();
        }
        
        Console.WriteLine($"{client.CurrentUser.Username} 봇이 준비되었습니다!");
    }

    public async Task UpdateMessage(SocketInteraction component, PartyEntity party, bool isAllMessage, string message)
    {
        // 임베드 메시지 업데이트
        var updatedEmbed = UpdatedEmbed(party);
        var updatedComponent = UpdatedComponent(party);
        
        var originalMessage = await component.Channel.GetMessageAsync(party.MESSAGE_KEY) as IUserMessage;
        if (originalMessage == null)
        {
            if (await client.GetChannelAsync(party.CHANNEL_KEY) is IMessageChannel cl)
            {
                originalMessage = await cl.GetMessageAsync(party.MESSAGE_KEY) as IUserMessage;
            }
        }

        // 원본 메시지 수정
        if (originalMessage != null)
        {
            
            await originalMessage.ModifyAsync(msg =>
            {
                msg.Embed = updatedEmbed;
                msg.Components = updatedComponent;
            });

            if (isAllMessage)
            {
                if (!component.HasResponded)
                {
                    await component.DeferAsync();
                }
                await originalMessage.ReplyAsync(message);
            }
        }
        else
        {
            await component.Channel.SendMessageAsync($"{party.DISPLAY_NAME} 파티에 대한 원본 메세지를 찾을 수 없습니다. 파티를 해산합니다.");
            await PartyService.ExpirePartyAsync(party.MESSAGE_KEY);
        }
    }

    public async Task RespondMessageWithExpire(SocketInteraction component, int time = 10, string? message = null)
    {
        var separator = "\u200B"; // Zero-Width Space
        var exMessage = $"{separator} (해당 메세지는 {time}초 후 삭제됩니다.)";
        
        if (message != null)
        {
            // HasResponded 체크 - 이미 응답했는지 확인
            if (!component.HasResponded)
            {
                await component.RespondAsync(message + exMessage, ephemeral: true);
            }
            else
            {
                await component.ModifyOriginalResponseAsync(m =>
                {
                    m.Content = message + exMessage;
                });
            }
        }
        else
        {
            var originalResponse = await component.GetOriginalResponseAsync();
            if (originalResponse == null)
            {
                // 원본 응답이 없는 경우 (이미 삭제되었거나 존재하지 않음)
                return;
            }
            
            message = originalResponse.Content;
            await component.ModifyOriginalResponseAsync(m =>
            {
                m.Content = message + exMessage;
            });
        }
        
        // 백그라운드에서 삭제
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(time));

            var originalResponseAsync = await component.GetOriginalResponseAsync();
            if (originalResponseAsync == null) return;
            
            var old = originalResponseAsync.Content;
            var s = old.Split(separator)[0];
            if (s != message)
            {
                return;
            }
            try
            {
                await component.DeleteOriginalResponseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RespondMessageWithExpire] 삭제 실패: {ex.Message}");
            }
        });
    }
    
    private bool CommandEquals(SocketApplicationCommand existing, SlashCommandProperties built)
    {
        // Description 비교
        if (existing.Description != built.Description.Value) return false;
        
        // Options 개수 비교
        var builtOptionsCount = built.Options.IsSpecified ? built.Options.Value.Count : 0;
        if (existing.Options.Count != builtOptionsCount) return false;
        
        // Options가 없으면 true
        if (!built.Options.IsSpecified) return existing.Options.Count == 0;
        
        var existingOptions = existing.Options.ToList();
        var builtOptions = built.Options.Value.ToList();
        
        for (int i = 0; i < existingOptions.Count; i++)
        {
            var e = existingOptions[i];
            var b = builtOptions[i];
            
            if (e.Name != b.Name || e.Type != b.Type || 
                e.Description != b.Description)
                return false;
        }
        
        return true;
    }

    public Embed UpdatedEmbed(PartyEntity party)
    {
        var memberList = party.Members.Count > 0 
            ? string.Join("\n", party.Members.Select(info => $"**<@{info.USER_ID}>**"))
            : "없음";

        string state;
        if (party.IS_EXPIRED)
            state = " (만료)";
        else if (party.IS_CLOSED)
            state = " (일시정지)";
        else
            state = "";
        
        var title = $"**{party.DISPLAY_NAME}** [생성자: {party.OWNER_NICKNAME}]{state}";
        var description = $"**참가자: {party.Members.Count}/{party.MAX_COUNT_MEMBER}**\n\n{memberList}";
        if (party.WaitMembers.Count > 0)
        {
            description += $"\n====================\n**대기열: {party.WaitMembers.Count}\n**";

            var array = party.WaitMembers;
            for (var i = 0; i < array.Count; i++)
            {
                var member = array[i];
                description += $"\n순번: {i + 1} | 닉네임: <@{member.USER_ID}>";
            }
        }
        
        // 만료시간 추가 (강조 표시)
        description += $"\n\n\n**만료시간: {party.EXPIRE_DATE:yyyy/MM/dd tt h:mm}**";
        
        var color = Color.Blue;
        if (party.MAX_COUNT_MEMBER == party.Members.Count) color = Color.Green;
        if (party.IS_CLOSED) color = Color.Orange;
        if (party.IS_EXPIRED) color = Color.Red;
        
        var updatedEmbed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .WithFooter($"버그제보(Discord): ojh1158 Version: {PartyConstant.VERSION}")
            .WithCurrentTimestamp()
            .Build();
        
        return updatedEmbed;
    }

    public MessageComponent UpdatedComponent(PartyEntity party)
    {
        var partyKey = party.MESSAGE_KEY;

        var component = new ComponentBuilder();
        var maxFlag = party.MAX_COUNT_MEMBER <= party.Members.Count;

        if (party.IS_EXPIRED) return component.Build();

        if (!party.IS_CLOSED)
        {
            // 인원이 가득 찬 경우
            if (maxFlag)
            {
                component.WithButton("대기하기", $"party_{PartyConstant.JOIN_KEY}_{partyKey}");
            }
            else
            {
                component.WithButton(PartyConstant.JOIN_KEY, $"party_{PartyConstant.JOIN_KEY}_{partyKey}", ButtonStyle.Success);
            }
        }

        component.WithButton(PartyConstant.LEAVE_KEY, $"party_{PartyConstant.LEAVE_KEY}_{partyKey}", ButtonStyle.Danger);

        component.WithButton(PartyConstant.OPTION_KEY, $"party_{PartyConstant.OPTION_KEY}_{partyKey}", ButtonStyle.Secondary);
        
        return component.Build();
    }

    public async Task<bool> ExpirePartyAsync(PartyEntity party, ISocketMessageChannel? channel = null)
    {
        channel ??= await client.GetChannelAsync(party.CHANNEL_KEY) as ISocketMessageChannel;

        if (channel == null) return false;
        
        var result = await PartyService.ExpirePartyAsync(party.MESSAGE_KEY);

        if (!result) return false;
        
        party.IS_EXPIRED = true;
        
        try
        {
            // 메시지를 먼저 가져와서 봇이 작성한 메시지인지 확인
            var message = await channel.GetMessageAsync(party.MESSAGE_KEY);
            if (message == null || message.Author.Id != client.CurrentUser.Id)
            {
                // 봇이 작성한 메시지가 아니거나 메시지가 없는 경우
                Console.WriteLine($"[ExpirePartyAsync] 메시지를 수정할 수 없습니다. (MESSAGE_KEY: {party.MESSAGE_KEY}, Author: {message?.Author?.Id})");
                return true; // DB 업데이트는 성공했으므로 true 반환
            }
            
            var embed = UpdatedEmbed(party);
            
            await channel.ModifyMessageAsync(party.MESSAGE_KEY, msg =>
            {
                msg.Embed = embed;
                msg.Components = null;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExpirePartyAsync] 메시지 수정 중 오류 발생: {ex.Message}");
            return true; // DB 업데이트는 성공했으므로 true 반환
        }
        
        return true;
    }
}