// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: joining a basic group with its random 32-digit token (zg.joinGroup). The token alone admits
// the caller; it replaces Telegram's invite links, which are disabled in this product.

using Ferrite.Data.Models;
using Ferrite.Data.Repositories;
using Ferrite.Data.Search;
using Ferrite.Services.Zerogram;
using Ferrite.TL;
using Ferrite.TL.baseLayer;
using Ferrite.TL.baseLayer.zg;
using Ferrite.Utils;

namespace Ferrite.Services.Handlers.MessageMethods;

public sealed class ZgJoinGroupHandler : MessagesHandlerBase
{
    private readonly IAuthorizationRepository _authorizationRepository;
    private readonly IChatParticipantsRepository _chatParticipantsRepository;
    private readonly IChatRepository _chatRepository;
    private readonly ZerogramOptions _zerogram;
    private readonly ZgGroupTokens _tokens;
    private readonly ZgStore _store;

    public ZgJoinGroupHandler(IUnitOfWork unitOfWork, IForumTopicsRepository forumTopicsRepository, IMessagingSettingsRepository messagingSettingsRepository, IAuthorizationRepository authorizationRepository, IChannelMessagesRepository channelMessagesRepository, IChatParticipantsRepository chatParticipantsRepository, IChatRepository chatRepository, IMessageRepository messageRepository, IUserRepository userRepository, ISearchEngine search,
        IUpdatesService updates, IUpdatesContextFactory updatesContextFactory,
        ILogger log, IUploadService upload, IPhotoProcessingService photos,
        ICounterFactory counterFactory, IdAllocators ids,
        ChatRowStore chatRows, InviteStore invites,
        PrivacyEvaluator privacy, MessageStore messages, SendPipeline send,
        UpdateFanout fanout, DialogBuilder dialogs,
        ZerogramOptions zerogram, ZgGroupTokens tokens, ZgStore store)
        : base(unitOfWork, forumTopicsRepository, messagingSettingsRepository, authorizationRepository, channelMessagesRepository, chatParticipantsRepository, chatRepository, messageRepository, userRepository, search, updates, updatesContextFactory, log, upload, photos, counterFactory, ids, chatRows, invites, privacy, messages, send, fanout, dialogs)
    {
        _authorizationRepository = authorizationRepository;
        _chatParticipantsRepository = chatParticipantsRepository;
        _chatRepository = chatRepository;
        _zerogram = zerogram;
        _tokens = tokens;
        _store = store;
    }

    [TLFunction(Constructors.baseLayer_JoinGroup)]
    public async Task<TLUpdates> Handle(long authKeyId, TLBytes q)
    {
        var auth = await _authorizationRepository.GetAuthorizationAsync(authKeyId);
        if (auth == null || !auth.Value.AsAuthInfo().LoggedIn)
        {
            return ErrorUpdates("AUTH_KEY_INVALID");
        }

        long userId = auth.Value.AsAuthInfo().UserId;
        string token = ZgGroupTokens.Normalize(System.Text.Encoding.UTF8.GetString(new JoinGroup(q.AsSpan()).Token));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long since = now - 600;
        string who = "u" + userId;
        if (_store.CountEvents("token-fail", who, since) >= _zerogram.GroupTokenMaxFailures)
        {
            long oldest = _store.OldestEvent("token-fail", who, since) ?? now;
            return ErrorUpdates($"FLOOD_WAIT_{Math.Max(1, oldest + 600 - now)}");
        }

        long? chatId = _tokens.FindChat(token);
        if (chatId is null)
        {
            _store.RecordEvent("token-fail", who, now);
            return ErrorUpdates("TOKEN_INVALID");
        }

        byte[] chatBytes;
        using (var chat = await _chatRepository.GetChatAsync(chatId.Value))
        {
            if (chat == null || chat.Value.Type != TLChat.ChatType.Chat || chat.Value.AsChat().Deactivated)
            {
                _store.RecordEvent("token-fail", who, now);
                return ErrorUpdates("TOKEN_INVALID");
            }
            chatBytes = chat.Value.AsSpan().ToArray();
        }

        var existing = await _chatParticipantsRepository.GetParticipantAsync(chatId.Value, userId);
        bool alreadyActive = existing != null && IsActiveParticipant(existing.Value);
        bool banned = existing != null &&
                      existing.Value.AsChatParticipantInfo().Role == (int)ChatParticipantRole.Banned;
        existing?.Dispose();
        if (banned) return ErrorUpdates("USER_BANNED_IN_CHANNEL");
        if (alreadyActive) return ErrorUpdates("USER_ALREADY_PARTICIPANT");

        int members = 0;
        foreach (var participant in await _chatParticipantsRepository.GetParticipantsAsync(chatId.Value))
        {
            if (IsActiveParticipant(participant)) members++;
            participant.Dispose();
        }
        if (members >= _zerogram.GroupMemberLimit) return ErrorUpdates("USERS_TOO_MUCH");

        int date = (int)now;
        long inviter = _tokens.Get(chatId.Value)?.CreatorId ?? 0;
        _log.Information("zerogram: group joined by token");
        return await ImportBasicChatInvite(authKeyId, userId, chatId.Value, inviter, chatBytes, date);
    }
}
