using Discord.WebSocket;
using DiscordBot.scripts._src.Discord;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

public class SlashCommandServices : BaseServices
{
    public SlashCommandServices(DiscordServices services) : base(services)
    {
        Services.client.SlashCommandExecuted += HandleSlashCommandAsync;
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        var commandName = command.Data.Name;
        
        if (command.Channel is SocketGuildChannel guildChannel)
        {
            // 봇의 현재 권한 가져오기
            var permissions = guildChannel.Guild.CurrentUser.GetPermissions(guildChannel);

            // 필요한 권한 체크 (채널 보기 & 메시지 보내기)
            if (!permissions.ViewChannel)
            {
                await command.RespondAsync("🚫 이 채널에 대한 접근 권한이 없습니다. 권한을 확인해주세요.", ephemeral: true);
                return;
            }

            if (!permissions.SendMessages)
            {
                await command.RespondAsync("🚫 이 채널에 대한 메시지 전송 권한이 없습니다. 권한을 확인해주세요.", ephemeral: true);
                return;
            }

            // 메시지 기록 보기 권한 체크
            if (!permissions.ReadMessageHistory)
            {
                await command.RespondAsync("🚫 이 채널의 '메시지 기록 보기' 권한이 없습니다.", ephemeral: true);
                return;
            }
            
            if (!await GuildService.GuildCheckAsync(guildChannel.Id, guildChannel.Guild.Name))
            {
                await command.RespondAsync("🚫 이 채널을 검증할 수 없거나 제한되었습니다.", ephemeral: true);
                return;
            }
        }
        else
        {
            await command.RespondAsync("서버에서만 사용 가능합니다.", ephemeral: true);
            return;
        }
        
        if (commandName != "파티")
        {
            await command.RespondAsync("알 수 없는 명령입니다.", ephemeral: true);
            return;
        }
        
        var commandOptions = command.Data.Options;
        var nameOption = commandOptions.FirstOrDefault(x => x.Name == "이름");
        var countOption = commandOptions.FirstOrDefault(x => x.Name == "인원");
        var timeOption = commandOptions.FirstOrDefault(x => x.Name == "만료시간");
        // var collOption = commandOptions.FirstOrDefault(x => x.Name == "호출");
        
        if (nameOption?.Value == null || countOption?.Value == null || !int.TryParse(countOption.Value.ToString(), out var count))
        {
            await command.RespondAsync("명령어에 오류가 있습니다.", ephemeral: true);
            return;
        }

        if (count is < PartyConstant.MIN_COUNT or > PartyConstant.MAX_COUNT)
        {
            await command.RespondAsync($"파티 인원은 최소 {PartyConstant.MIN_COUNT} 최대 {PartyConstant.MAX_COUNT}까지만 지정할 수 있습니다.", ephemeral: true);
            return;
        }
                
        var partyName = nameOption.Value.ToString()!;
        
        if (await PartyService.IsPartyExistsAsync(partyName, (ulong)command.GuildId!))
        {
            await command.RespondAsync("해당 파티 이름이 이미 있습니다.", ephemeral: true);
            return;
        }

        var time = TimeSpan.FromHours(PartyConstant.MAX_HOUR);

        if (timeOption?.Value != null)
        {
            var timeString = timeOption.Value.ToString()!.ToLower();
            if (!int.TryParse(timeString[..1], out var number))
            {
                await command.RespondAsync("시간 형식이 알맞지 않습니다!", ephemeral: true);
                return;
            }
            
            switch (timeString[^1])
            {
                case 'm' or '분' :
                    time = TimeSpan.FromMinutes(number);
                    break;
                case 'h' or '시':
                    time = TimeSpan.FromHours(number);
                    break;
                default:
                    await command.RespondAsync("시간 형식이 알맞지 않습니다!", ephemeral: true);
                    return;
            }
        }
        

        if (time >= TimeSpan.FromHours(PartyConstant.MAX_HOUR))
        {
            time = TimeSpan.FromHours(PartyConstant.MAX_HOUR);
        }
        
        await command.RespondAsync("초기화 중입니다...");
        var message = await command.GetOriginalResponseAsync();
        
        var now = DateTime.Now;
        var party = new PartyEntity
        {
            DISPLAY_NAME = partyName,
            PARTY_KEY = Guid.NewGuid().ToString(),
            MAX_COUNT_MEMBER = count,
            MESSAGE_KEY = message.Id,
            GUILD_KEY = (ulong)command.GuildId!,
            CHANNEL_KEY = (ulong)command.ChannelId!,
            OWNER_KEY = command.User.Id,
            OWNER_NICKNAME = command.User is SocketGuildUser user
                ? user.DisplayName
                : command.User.Username,
            EXPIRE_DATE = now.AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond).Add(time),
        };
        
        if (!await PartyService.CreatePartyAsync(party))
        {
            await message.DeleteAsync();
            await command.FollowupAsync("파티 생성에 실패하였습니다.", ephemeral: true);
            await Services.RespondMessageWithExpire(command);
            return;
        }

        var updatedEmbed = Services.UpdatedEmbed(party);
        var component = Services.UpdatedComponent(party);
        
        await message.ModifyAsync(m =>
        {
            m.Embed = updatedEmbed;
            m.Components = component;
            m.Content = "";
        });

        var me = await command.FollowupAsync("파티를 생성하였습니다!", ephemeral: true);
    }
}