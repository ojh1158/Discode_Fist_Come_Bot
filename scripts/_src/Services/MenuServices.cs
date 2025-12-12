using Discord.WebSocket;
using DiscordBot.scripts._src.Discord;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

public class MenuServices : BaseServices
{
    public MenuServices(DiscordServices services) : base(services)
    {
        Services.client.SelectMenuExecuted += HandleSelectMenuAsync;
    }
    private async Task HandleSelectMenuAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        // CustomId 파싱: "party_{JOIN_AUTO_KEY}_{messageId}"
        var parts = customId.Split('_');
        if (parts.Length < 3 || parts[0] != "party")
            return;
        
        var action = parts[1]; // "인원추가"
        var messageId = ulong.Parse(parts[2]);
        
        // 선택된 값들 가져오기 (SelectMenu는 여러 값 선택 가능)
        var selectedValues = component.Data.Values; // string[] 배열
        
        // 첫 번째 선택된 값 사용 (또는 모든 값 처리)
        var selectedValue = selectedValues.FirstOrDefault();
        
        if (action == PartyConstant.JOIN_AUTO_KEY)
        {
            // 여기서 선택된 유저를 파티에 추가하는 로직 구현
            await component.RespondAsync($"선택된 값: {selectedValue}", ephemeral: true);
            
            // 파티 정보 가져오기
            var partyEntity = await PartyService.GetPartyEntityAsync(messageId);
            if (partyEntity == null)
            {
                await component.RespondAsync("파티를 찾을 수 없습니다.", ephemeral: true);
                return;
            }
            
            // 선택된 유저 ID를 파싱하여 파티에 추가
            // 예: selectedValue가 "123456789" (ulong)라면
            if (ulong.TryParse(selectedValue, out var userId))
            {
                // 파티에 유저 추가 로직
                // await PartyService.JoinPartyAsync(partyEntity, userId, "추가된 유저");
            }
        }
    }
}