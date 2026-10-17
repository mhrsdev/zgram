// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: RPC entry points of the authenticator-account API (zg.*).

using System.Text;
using Ferrite.Services.Zerogram;
using Ferrite.TL;
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
