// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: first-release scope. Private text chats, small basic groups, delivery state and local
// deletion only. Calls, channels, bots, file transfer, secret chats and phone/SMS/password sign-in are
// refused with 403 METHOD_DISABLED before any handler runs.

using System.Collections.Frozen;
using Ferrite.TL;
using Ferrite.TL.Schema;

namespace Ferrite.Core.Execution;

public static class ZerogramScope
{
    private static readonly string[] BlockedNamespaces =
    [
        "phone", "channels", "upload", "bots", "payments", "stories", "premium", "fragment",
        "smsjobs", "chatlists", "stickers", "communities", "ephemeral", "aicompose",
    ];

    private static readonly string[] BlockedMethods =
    [
        // Phone / SMS / e-mail / Firebase / passkey / QR-token sign-in: replaced by zg.*
        "auth.sendCode", "auth.resendCode", "auth.cancelCode", "auth.signIn", "auth.signUp",
        "auth.exportLoginToken", "auth.importLoginToken", "auth.acceptLoginToken",
        "auth.exportAuthorization", "auth.importAuthorization", "auth.checkPassword",
        "auth.requestPasswordRecovery", "auth.recoverPassword", "auth.checkRecoveryPassword",
        "auth.resetLoginEmail", "auth.requestFirebaseSms", "auth.reportMissingCode",
        "auth.importWebTokenAuthorization", "auth.importBotAuthorization",
        // Fixed passwords and phone-number changes do not exist in this product.
        "account.updatePasswordSettings", "account.confirmPasswordEmail", "account.resendPasswordEmail",
        "account.cancelPasswordEmail", "account.resetPassword", "account.declinePasswordReset",
        "account.sendChangePhoneCode", "account.changePhone", "account.sendConfirmPhoneCode",
        "account.confirmPhone", "account.sendVerifyPhoneCode", "account.verifyPhone",
        "account.sendVerifyEmailCode", "account.verifyEmail", "account.deleteAccount",
        // File and media transfer.
        "messages.sendMedia", "messages.sendMultiMedia", "messages.uploadMedia",
        "messages.uploadImportedMedia", "messages.forwardMessages", "messages.editMessage",
        "photos.uploadProfilePhoto", "photos.updateProfilePhoto", "photos.uploadContactProfilePhoto",
        "account.uploadWallPaper", "account.uploadTheme", "account.uploadRingtone",
        "account.uploadWallPaper", "messages.startBot", "messages.setInlineBotResults",
        // Secret chats (end-to-end encryption is not offered).
        "messages.getDhConfig", "messages.requestEncryption", "messages.acceptEncryption",
        "messages.discardEncryption", "messages.setEncryptedTyping", "messages.readEncryptedHistory",
        "messages.sendEncrypted", "messages.sendEncryptedFile", "messages.sendEncryptedService",
        "messages.receivedQueue", "messages.reportEncryptedSpam",
        // Channels, supergroups and invite links (links would bypass the group size limit).
        "messages.migrateChat", "messages.createForumTopic", "messages.importChatInvite",
        "messages.exportChatInvite", "messages.checkChatInvite", "messages.editExportedChatInvite",
        "messages.deleteExportedChatInvite", "messages.getExportedChatInvites",
        "messages.getExportedChatInvite", "messages.getAdminsWithInvites",
        "messages.getChatInviteImporters", "messages.hideChatJoinRequest",
        "messages.hideAllChatJoinRequests", "messages.deleteRevokedExportedChatInvites",
        // Reactions, polls, auto-delete timers and URL previews (server-side fetch) are out of scope.
        "messages.sendReaction", "messages.getMessagesReactions", "messages.getMessageReactionsList",
        "messages.setChatAvailableReactions", "messages.readReactions", "messages.getUnreadReactions",
        "messages.sendVote", "messages.getPollResults", "messages.getPollVotes",
        "messages.setHistoryTTL", "messages.setDefaultHistoryTTL", "messages.getWebPagePreview",
        "messages.getWebPage", "messages.scheduleMessage", "messages.sendScheduledMessages",
    ];

    private static readonly int[] LegacyBlocked =
    [
        Constructors.layer104_AuthSignUp,
        LegacyConstructors.SignIn,
        LegacyConstructors.ResendCode,
    ];

    public static FrozenSet<int> Blocked { get; } = Resolve();

    public static bool IsBlocked(int constructor) => Blocked.Contains(constructor);

    private static FrozenSet<int> Resolve()
    {
        LayerSchema schema = LayerSchemaCatalog.Create(LayerSchemaCatalog.BaseLayer);
        var ids = new HashSet<int>(LegacyBlocked);
        foreach (LayerConstructor method in schema.Methods)
        {
            int dot = method.Name.IndexOf('.');
            if (dot > 0 && BlockedNamespaces.Contains(method.Name[..dot])) ids.Add(method.Id);
        }
        foreach (string name in BlockedMethods)
        {
            if (schema.TryGetMethod(name, out LayerConstructor method)) ids.Add(method.Id);
        }
        return ids.ToFrozenSet();
    }
}
