// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: RPC entry points of the authenticator-account API (zg.*).

using System.Text;
using Ferrite.Data.Repositories;
using Ferrite.Services.Zerogram;
using Ferrite.TL;
using Ferrite.TL.baseLayer;
using Ferrite.TL.baseLayer.zg;

namespace Ferrite.Core.Execution.Functions.BaseLayer.Zerogram;

internal static class ZgRpc
{
    public static TLBytes? Error(ZgStatus status, int floodSeconds, TLExecutionContext ctx)
    {
        (int code, string text) = status switch
        {
            ZgStatus.UsernameInvalid => (400, "USERNAME_INVALID"),
            ZgStatus.UsernameOccupied => (400, "USERNAME_OCCUPIED"),
            ZgStatus.DisplayNameInvalid => (400, "FIRSTNAME_INVALID"),
            ZgStatus.EmailInvalid => (400, "EMAIL_INVALID"),
            ZgStatus.CodeInvalid => (400, "CODE_INVALID"),
            ZgStatus.Flood => (420, $"FLOOD_WAIT_{floodSeconds}"),
            ZgStatus.OwnerDisabled => (400, "OWNER_DISABLED"),
            ZgStatus.OwnerInvalid => (400, "OWNER_INVALID"),
            ZgStatus.Forbidden => (403, "CHAT_ADMIN_REQUIRED"),
            ZgStatus.NotFound => (400, "CHAT_ID_INVALID"),
            _ => (500, "INTERNAL"),
        };
        using var error = RpcErrorGenerator.GenerateError(code, Encoding.UTF8.GetBytes(text));
        return RpcResultGenerator.Generate(error, ctx.MessageId);
    }

    public static TLBytes? Registration(ZgRegistration r, TLExecutionContext ctx)
    {
        using TLRegistration result = Ferrite.TL.baseLayer.zg.Registration.Builder()
            .Issuer(Encoding.UTF8.GetBytes(r.Issuer))
            .Username(Encoding.UTF8.GetBytes(r.Username))
            .DisplayName(Encoding.UTF8.GetBytes(r.DisplayName))
            .OtpauthUri(Encoding.UTF8.GetBytes(r.OtpAuthUri))
            .ManualKey(Encoding.UTF8.GetBytes(r.ManualKey))
            .Period(r.Period)
            .Digits(r.Digits)
            .Build();
        return RpcResultGenerator.Generate(result, ctx.MessageId);
    }
}

[TLFunction(Constructors.baseLayer_Register)]
public sealed class ZgRegisterFunc : ITLFunction
{
    private readonly ZgAccountService _accounts;
    public ZgRegisterFunc(ZgAccountService accounts) => _accounts = accounts;

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        var request = new Register(q.AsSpan());
        var result = await _accounts.RegisterAsync(ctx.CurrentAuthKeyId, ctx.IP,
            Encoding.UTF8.GetString(request.Username), Encoding.UTF8.GetString(request.DisplayName),
            Encoding.UTF8.GetString(request.Email));
        return result.Ok
            ? ZgRpc.Registration(result.Value!, ctx)
            : ZgRpc.Error(result.Status, result.FloodSeconds, ctx);
    }
}

[TLFunction(Constructors.baseLayer_Activate)]
public sealed class ZgActivateFunc : ITLFunction
{
    private readonly ZgAccountService _accounts;
    public ZgActivateFunc(ZgAccountService accounts) => _accounts = accounts;

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        var request = new Activate(q.AsSpan());
        var result = await _accounts.ActivateAsync(ctx.CurrentAuthKeyId, ctx.IP,
            Encoding.UTF8.GetString(request.Username), Encoding.UTF8.GetString(request.Code));
        if (!result.Ok) return ZgRpc.Error(result.Status, result.FloodSeconds, ctx);

        using var authorization = result.Value!.Authorization;
        VectorOfString codes = BuildStrings(result.Value.RecoveryCodes);
        using TLActivated activated = Ferrite.TL.baseLayer.zg.Activated.Builder()
            .RecoveryCodes(codes)
            .Authorization(authorization.AsSpan())
            .Build();
        return RpcResultGenerator.Generate(activated, ctx.MessageId);
    }

    internal static VectorOfString BuildStrings(IReadOnlyList<string> values)
    {
        var vector = new VectorOfString();
        foreach (string value in values) vector.AppendTLBytes(Encoding.UTF8.GetBytes(value));
        return vector;
    }
}

[TLFunction(Constructors.baseLayer_Login)]
public sealed class ZgLoginFunc : ITLFunction
{
    private readonly ZgAccountService _accounts;
    public ZgLoginFunc(ZgAccountService accounts) => _accounts = accounts;

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        var request = new Login(q.AsSpan());
        var result = await _accounts.LoginAsync(ctx.CurrentAuthKeyId, ctx.IP,
            Encoding.UTF8.GetString(request.Username), Encoding.UTF8.GetString(request.Code));
        if (!result.Ok) return ZgRpc.Error(result.Status, result.FloodSeconds, ctx);
        using var authorization = result.Value!;
        return RpcResultGenerator.Generate(authorization, ctx.MessageId);
    }
}

[TLFunction(Constructors.baseLayer_Recover)]
public sealed class ZgRecoverFunc : ITLFunction
{
    private readonly ZgAccountService _accounts;
    public ZgRecoverFunc(ZgAccountService accounts) => _accounts = accounts;

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        var request = new Recover(q.AsSpan());
        var result = await _accounts.RecoverAsync(ctx.CurrentAuthKeyId, ctx.IP,
            Encoding.UTF8.GetString(request.Username), Encoding.UTF8.GetString(request.RecoveryCode));
        return result.Ok
            ? ZgRpc.Registration(result.Value!, ctx)
            : ZgRpc.Error(result.Status, result.FloodSeconds, ctx);
    }
}

[TLFunction(Constructors.baseLayer_GetPolicy)]
public sealed class ZgGetPolicyFunc : ITLFunction
{
    private readonly ZgAccountService _accounts;
    public ZgGetPolicyFunc(ZgAccountService accounts) => _accounts = accounts;

    public ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        using TLPolicy policy = Policy.Builder()
            .GroupMemberLimit(_accounts.Options.GroupMemberLimit)
            .QueueTtlDays((int)_accounts.Options.QueueTtl.TotalDays)
            .SingleDevice(true)
            .Build();
        return ValueTask.FromResult<TLBytes?>(RpcResultGenerator.Generate(policy, ctx.MessageId));
    }
}


[TLFunction(Constructors.baseLayer_AckMessages)]
public sealed class ZgAckMessagesFunc : ITLFunction
{
    private readonly ZgRetention _retention;
    private readonly IAuthorizationRepository _authorizations;

    public ZgAckMessagesFunc(ZgRetention retention, IAuthorizationRepository authorizations)
    {
        _retention = retention;
        _authorizations = authorizations;
    }

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        using var auth = await _authorizations.GetAuthorizationAsync(ctx.CurrentAuthKeyId);
        if (auth is not { } info || !info.AsAuthInfo().LoggedIn)
        {
            using var denied = RpcErrorGenerator.GenerateError(401, "AUTH_KEY_UNREGISTERED"u8);
            return RpcResultGenerator.Generate(denied, ctx.MessageId);
        }

        var request = new AckMessages(q.AsSpan());
        VectorOfInt vector = request.Ids;
        var ids = new List<int>(vector.Count);
        for (int i = 0; i < vector.Count; i++) ids.Add(vector[i]);

        int acked = await _retention.AckAsync(info.AsAuthInfo().UserId, ids);
        using TLAckResult result = AckResult.Builder().Acked(acked).Build();
        return RpcResultGenerator.Generate(result, ctx.MessageId);
    }
}

[TLFunction(Constructors.baseLayer_GetDeliveryState)]
public sealed class ZgGetDeliveryStateFunc : ITLFunction
{
    private readonly ZgRetention _retention;
    private readonly IAuthorizationRepository _authorizations;

    public ZgGetDeliveryStateFunc(ZgRetention retention, IAuthorizationRepository authorizations)
    {
        _retention = retention;
        _authorizations = authorizations;
    }

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        using var auth = await _authorizations.GetAuthorizationAsync(ctx.CurrentAuthKeyId);
        if (auth is not { } info || !info.AsAuthInfo().LoggedIn)
        {
            using var denied = RpcErrorGenerator.GenerateError(401, "AUTH_KEY_UNREGISTERED"u8);
            return RpcResultGenerator.Generate(denied, ctx.MessageId);
        }

        var marks = new Vector();
        foreach (var (peerType, peerId, maxId) in _retention.Queue.DeliveryMarks(info.AsAuthInfo().UserId))
        {
            using TLPeer peer = Ferrite.Services.Users.PeerResolver.BuildPeer((TLPeer.PeerType)peerType, peerId);
            using TLDeliveryMark mark = DeliveryMark.Builder().Peer(peer.AsSpan()).MaxId(maxId).Build();
            marks.AppendTLObject(mark.AsSpan());
        }
        using TLDeliveryState state = DeliveryState.Builder().Marks(marks).Build();
        return RpcResultGenerator.Generate(state, ctx.MessageId);
    }
}


[TLFunction(Constructors.baseLayer_OwnerHello)]
public sealed class ZgOwnerHelloFunc : ITLFunction
{
    private readonly ZgOwnerService _owner;
    public ZgOwnerHelloFunc(ZgOwnerService owner) => _owner = owner;

    public ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        var request = new OwnerHello(q.AsSpan());
        var result = _owner.Hello(ctx.CurrentAuthKeyId, ctx.IP, request.ClientNonce.ToArray());
        if (!result.Ok) return ValueTask.FromResult(ZgRpc.Error(result.Status, result.FloodSeconds, ctx));
        ZgOwnerChallenge c = result.Value!;
        using TLOwnerChallenge challenge = OwnerChallenge.Builder()
            .ServerNonce(c.ServerNonce)
            .Salt(c.Salt)
            .Iterations(c.Iterations)
            .ServerProof(c.ServerProof)
            .Build();
        return ValueTask.FromResult<TLBytes?>(RpcResultGenerator.Generate(challenge, ctx.MessageId));
    }
}

[TLFunction(Constructors.baseLayer_OwnerLogin)]
public sealed class ZgOwnerLoginFunc : ITLFunction
{
    private readonly ZgOwnerService _owner;
    public ZgOwnerLoginFunc(ZgOwnerService owner) => _owner = owner;

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        var request = new OwnerLogin(q.AsSpan());
        var result = await _owner.LoginAsync(ctx.CurrentAuthKeyId, ctx.IP, request.ClientNonce.ToArray(),
            request.ServerNonce.ToArray(), request.ClientProof.ToArray());
        if (!result.Ok) return ZgRpc.Error(result.Status, result.FloodSeconds, ctx);
        using var authorization = result.Value!;
        return RpcResultGenerator.Generate(authorization, ctx.MessageId);
    }
}

internal static class ZgGroupRpc
{
    public static TLBytes Unauthorized(TLExecutionContext ctx)
    {
        using var denied = RpcErrorGenerator.GenerateError(401, "AUTH_KEY_UNREGISTERED"u8);
        return RpcResultGenerator.Generate(denied, ctx.MessageId);
    }

    public static TLGroupToken Build(ZgGroupInfo g) => GroupToken.Builder()
        .ChatId(g.ChatId)
        .Title(Encoding.UTF8.GetBytes(g.Title))
        .Token(Encoding.UTF8.GetBytes(g.Token))
        .Members(g.Members)
        .Date((int)g.Date)
        .Build();
}

[TLFunction(Constructors.baseLayer_GetOwnerInfo)]
public sealed class ZgGetOwnerInfoFunc : ITLFunction
{
    private readonly ZgGroupAdmin _admin;
    private readonly IAuthorizationRepository _authorizations;

    public ZgGetOwnerInfoFunc(ZgGroupAdmin admin, IAuthorizationRepository authorizations)
    {
        _admin = admin;
        _authorizations = authorizations;
    }

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        using var auth = await _authorizations.GetAuthorizationAsync(ctx.CurrentAuthKeyId);
        if (auth is not { } info || !info.AsAuthInfo().LoggedIn) return ZgGroupRpc.Unauthorized(ctx);
        var result = await _admin.OwnerInfoAsync(info.AsAuthInfo().UserId);
        if (!result.Ok) return ZgRpc.Error(result.Status, result.FloodSeconds, ctx);
        var groups = new Vector();
        foreach (ZgGroupInfo g in result.Value!.Groups)
        {
            using TLGroupToken token = ZgGroupRpc.Build(g);
            groups.AppendTLObject(token.AsSpan());
        }
        using TLOwnerInfo owner = OwnerInfo.Builder()
            .ServerName(Encoding.UTF8.GetBytes(result.Value.ServerName))
            .ConnectMessage(Encoding.UTF8.GetBytes(result.Value.ConnectMessage))
            .Groups(groups)
            .Build();
        return RpcResultGenerator.Generate(owner, ctx.MessageId);
    }
}

public abstract class ZgGroupTokenFuncBase : ITLFunction
{
    private readonly ZgGroupAdmin _admin;
    private readonly IAuthorizationRepository _authorizations;
    private readonly bool _rotate;

    protected ZgGroupTokenFuncBase(ZgGroupAdmin admin, IAuthorizationRepository authorizations, bool rotate)
    {
        _admin = admin;
        _authorizations = authorizations;
        _rotate = rotate;
    }

    protected abstract long ChatId(TLBytes q);

    public async ValueTask<TLBytes?> Process(TLBytes q, TLExecutionContext ctx)
    {
        using var auth = await _authorizations.GetAuthorizationAsync(ctx.CurrentAuthKeyId);
        if (auth is not { } info || !info.AsAuthInfo().LoggedIn) return ZgGroupRpc.Unauthorized(ctx);
        var result = await _admin.GroupTokenAsync(info.AsAuthInfo().UserId, ChatId(q), _rotate);
        if (!result.Ok) return ZgRpc.Error(result.Status, result.FloodSeconds, ctx);
        using TLGroupToken token = ZgGroupRpc.Build(result.Value!);
        return RpcResultGenerator.Generate(token, ctx.MessageId);
    }
}

[TLFunction(Constructors.baseLayer_GetGroupToken)]
public sealed class ZgGetGroupTokenFunc : ZgGroupTokenFuncBase
{
    public ZgGetGroupTokenFunc(ZgGroupAdmin admin, IAuthorizationRepository authorizations)
        : base(admin, authorizations, rotate: false) { }

    protected override long ChatId(TLBytes q) => new GetGroupToken(q.AsSpan()).ChatId;
}

[TLFunction(Constructors.baseLayer_RotateGroupToken)]
public sealed class ZgRotateGroupTokenFunc : ZgGroupTokenFuncBase
{
    public ZgRotateGroupTokenFunc(ZgGroupAdmin admin, IAuthorizationRepository authorizations)
        : base(admin, authorizations, rotate: true) { }

    protected override long ChatId(TLBytes q) => new RotateGroupToken(q.AsSpan()).ChatId;
}
