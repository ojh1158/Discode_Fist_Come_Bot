using Discord.WebSocket;
using DiscordBot.scripts._src.Discord;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts._src.Partys;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

public class ModalServices : BaseServices
{
    public ModalServices(DiscordServices services) : base(services)
    {
        Services.client.ModalSubmitted += HandleModalAsync;
    }
    
    private async Task HandleModalAsync(SocketModal modal)
    {
        var customId = modal.Data.CustomId;
        
        var parts = customId.Split('_');
        if (parts[0] != "party")
            return;
        
        if (!ulong.TryParse(parts[2], out var messageId))
            return;

        var partyEntity = await PartyService.GetPartyEntityAsync(messageId);

        var partyClass = new PartyClass();
        partyClass.Init(partyEntity, modal);
        var party = partyClass.Entity;

        var message = "";

        await modal.RespondAsync("작업 중....", ephemeral: true);

        var renameOk = true;
        var resizeOk = true;
        
        // 입력값 가져오기
        var countInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "count");
        int newCount = party.MAX_COUNT_MEMBER;
        if (countInput == null || !int.TryParse(countInput.Value, out newCount))
        {
            message += $"인원 오류: 유호한 숫자를 입력해주세요.\n";
            resizeOk = false;
        }

        if (party.MAX_COUNT_MEMBER != newCount)
        {
            // 범위 체크
            if (newCount < 1 || newCount > PartyConstant.MAX_COUNT)
            {
                message += $"인원 오류: 파티 인원은 {1}~{PartyConstant.MAX_COUNT} 사이여야 합니다.\n";
                resizeOk = false;
            }

            if (partyClass is { isOwner: false, isAdmin: false })
            {
                message += $"인원 오류: 파티장 또는 관리자만 인원을 변경할 수 있습니다.\n";
                resizeOk = false;
            }

            if (resizeOk)
            {
                var (members, waitMember) = await PartyService.ResizePartyAsync(party, newCount);

                party.Members = members;
                party.WaitMembers = waitMember;
                party.MAX_COUNT_MEMBER = newCount;
                message += $"인원: 인원을 변경하였습니다.\n";
            }
        }
                
        var nameInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "name");
        var name = nameInput?.Value ?? "";
        if (string.IsNullOrEmpty(name))
        {
            renameOk = false;
        }

        if (renameOk && name != party.DISPLAY_NAME)
        {
            if (await PartyService.PartyRename(messageId, name))
            {
                message += "제목: 제목을 변경하였습니다.\n";
                party.DISPLAY_NAME = name;
            }
            else
            {
                message += "제목 오류: 제목을 변경할 수 없었습니다.\n";
            }
        }

        if (message == "")
        {
            message = "설정이 취소되었습니다.";
        }
        await modal.ModifyOriginalResponseAsync(m => m.Content = message);
        _ = Services.RespondMessageWithExpire(modal);
        
        await Services.UpdateMessage(modal, party, false, "");
    }
}