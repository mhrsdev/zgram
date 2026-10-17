// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2022-2026 Aykut Alparslan KOC

using System.Text;
using Ferrite.TL;
using Ferrite.TL.baseLayer;

namespace Ferrite.Services.Handlers.HelpMethods;

public sealed class GetConfigHandler
{
    private readonly IDataCenter _dataCenter;
    private readonly Zerogram.ZerogramOptions _zerogram;

    public GetConfigHandler(IDataCenter dataCenter, Zerogram.ZerogramOptions zerogram)
    {
        _dataCenter = dataCenter;
        _zerogram = zerogram;
    }

    [TLFunction(Constructors.baseLayer_GetConfig)]
    public ValueTask<TLBytes> Handle(long authKeyId, TLBytes q)
    {
        Vector dcOptions = new();
        for (int i = 1; i < 3; i++)
        {
            byte[] ip = Encoding.UTF8.GetBytes(_dataCenter.IpAddress);
            using var option = DcOption.Builder().Id(i)
                .IpAddress(ip)
                .Port(_dataCenter.Port)
                .MediaOnly(_dataCenter.MediaOnly)
                .Build();
            dcOptions.AppendTLObject(option.ToReadOnlySpan());
        }

        var config = Config.Builder()
            .DefaultP2pContacts(true)
            .PreloadFeaturedStickers(false)
            .RevokePmInbox(false)
            .BlockedMode(false)
            .Date((int)DateTimeOffset.Now.ToUnixTimeSeconds())
            .Expires((int)DateTimeOffset.Now.AddSeconds(90).ToUnixTimeSeconds())
            .TestMode(false)
            .ThisDc(1)
            .DcOptions(dcOptions)
            .DcTxtDomainName("localhost"u8)
            .ChatSizeMax(_zerogram.GroupMemberLimit)
            .MegagroupSizeMax(100000)
            .ForwardedCountMax(100)
            .OnlineUpdatePeriodMs(120000)
            .OfflineBlurTimeoutMs(5000)
            .OfflineIdleTimeoutMs(30000)
            .OnlineCloudTimeoutMs(300000)
            .NotifyCloudDelayMs(30000)
            .NotifyDefaultDelayMs(1500)
            .PushChatPeriodMs(60000)
            .PushChatLimit(2)
            .EditTimeLimit(0) // Zerogram: no editing of sent messages
            .RevokeTimeLimit(0) // Zerogram: deletion is local to each device
            .RevokePmTimeLimit(0)
            .RatingEDecay(2419200)
            .StickersRecentLimit(200)
            .ChannelsReadMediaPeriod(604800)
            .CallReceiveTimeoutMs(20000)
            .CallRingTimeoutMs(90000)
            .CallConnectTimeoutMs(30000)
            .CallPacketTimeoutMs(10000)
            .MeUrlPrefix("localhost"u8)
            .GifSearchUsername("gif"u8)
            .VenueSearchUsername("foursquare"u8)
            .ImgSearchUsername("bing"u8)
            .CaptionLengthMax(1024)
            .MessageLengthMax(4096)
            .WebfileDcId(1)
            .Build();
        return ValueTask.FromResult(config.TLBytes!.Value);
    }
}
