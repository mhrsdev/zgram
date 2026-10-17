// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: owner overview (connection message + all group tokens) and per-group token access for the
// group creator.

using System.Text;
using Ferrite.Data.Models;
using Ferrite.Data.Repositories;
using Ferrite.TL;
using Ferrite.TL.baseLayer;
using Ferrite.TL.baseLayer.dto;

namespace Ferrite.Services.Zerogram;

public sealed record ZgGroupInfo(long ChatId, string Title, string Token, int Members, long Date);

public sealed record ZgOwnerInfo(string ServerName, string ConnectMessage, IReadOnlyList<ZgGroupInfo> Groups);

public sealed class ZgGroupAdmin
{
    private readonly ZgGroupTokens _tokens;
    private readonly ZgOwnerService _owner;
    private readonly ZerogramOptions _options;
    private readonly IChatRepository _chats;
    private readonly IChatParticipantsRepository _participants;

    public ZgGroupAdmin(ZgGroupTokens tokens, ZgOwnerService owner, ZerogramOptions options, IChatRepository chats,
        IChatParticipantsRepository participants)
    {
        _tokens = tokens;
        _owner = owner;
        _options = options;
        _chats = chats;
        _participants = participants;
    }

    public async ValueTask<ZgResult<ZgOwnerInfo>> OwnerInfoAsync(long userId)
    {
        if (!_owner.IsOwner(userId)) return ZgResult<ZgOwnerInfo>.Fail(ZgStatus.Forbidden);
        var groups = new List<ZgGroupInfo>();
        foreach (ZgGroupToken token in _tokens.All())
        {
            if (await DescribeAsync(token) is { } info) groups.Add(info);
        }
        return ZgResult<ZgOwnerInfo>.Success(new ZgOwnerInfo(_options.ServerName, _owner.BuildConnectMessage(), groups));
    }

    /// <summary>Token of a group for its creator or the owner; <paramref name="rotate"/> issues a new one.</summary>
    public async ValueTask<ZgResult<ZgGroupInfo>> GroupTokenAsync(long userId, long chatId, bool rotate)
    {
        using var chat = await _chats.GetChatAsync(chatId);
        if (chat is not { } stored || stored.Type != TLChat.ChatType.Chat || stored.AsChat().Deactivated)
            return ZgResult<ZgGroupInfo>.Fail(ZgStatus.NotFound);
        bool isOwner = _owner.IsOwner(userId);
        bool isCreator = false;
        if (!isOwner)
        {
            using var me = await _participants.GetParticipantAsync(chatId, userId);
            isCreator = me is { } p && p.AsChatParticipantInfo().Role == (int)ChatParticipantRole.Creator;
        }
        if (!isOwner && !isCreator) return ZgResult<ZgGroupInfo>.Fail(ZgStatus.Forbidden);

        ZgGroupToken? token = rotate ? _tokens.Rotate(chatId) : null;
        token ??= _tokens.Ensure(chatId, userId);
        return await DescribeAsync(token) is { } info
            ? ZgResult<ZgGroupInfo>.Success(info)
            : ZgResult<ZgGroupInfo>.Fail(ZgStatus.NotFound);
    }

    private async ValueTask<ZgGroupInfo?> DescribeAsync(ZgGroupToken token)
    {
        using var chat = await _chats.GetChatAsync(token.ChatId);
        if (chat is not { } stored || stored.Type != TLChat.ChatType.Chat || stored.AsChat().Deactivated) return null;
        var view = stored.AsChat();
        return new ZgGroupInfo(token.ChatId, Encoding.UTF8.GetString(view.Title), token.Token,
            view.ParticipantsCount, token.CreatedAt);
    }
}
