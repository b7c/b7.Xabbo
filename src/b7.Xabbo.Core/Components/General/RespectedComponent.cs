﻿using Microsoft.Extensions.Configuration;

using Xabbo;
using Xabbo.Messages;
using Xabbo.Extension;

using Xabbo.Core;
using Xabbo.Core.Game;

namespace b7.Xabbo.Components;

public class RespectedComponent : Component
{
    private readonly RoomManager _roomManager;

    private DateTime _lastRespect = DateTime.MinValue;
    private int _lastRespecterIndex = -1;

    private bool _showWhoRespected = true;
    public bool ShowWhoRespected
    {
        get => _showWhoRespected;
        set => Set(ref _showWhoRespected, value);
    }

    private bool _showTotalRespects = true;
    public bool ShowTotalRespects
    {
        get => _showTotalRespects;
        set => Set(ref _showTotalRespects, value);
    }

    public RespectedComponent(IExtension extension,
        IConfiguration config,
        RoomManager roomManager)
        : base(extension)
    {
        ShowWhoRespected = config.GetValue("Respect:ShowWhoRespected", true);
        ShowTotalRespects = config.GetValue("Respect:ShowTotalRespects", true);

        _roomManager = roomManager;
        roomManager.Left += Room_Left;
    }

    private void Room_Left(object? sender, EventArgs e)
    {
        _lastRespect = DateTime.MinValue;
        _lastRespecterIndex = -1;
    }

    [Receive(nameof(Incoming.RoomExpression))]
    private void InRoomUserAction(IReadOnlyPacket packet)
    {
        if (!_roomManager.IsInRoom)
            return;

        int index = packet.ReadInt();
        Actions action = (Actions)packet.ReadInt();

        if (action == Actions.ThumbsUp)
        {
            _lastRespect = DateTime.Now;
            _lastRespecterIndex = index;
        }
    }

    [InterceptIn(nameof(Incoming.RespectNotification))]
    private void HandleRespectNotification(InterceptArgs e)
    {
        IRoom? room = _roomManager.Room;
        if (room is null || (!ShowWhoRespected && !ShowTotalRespects))
            return;

        int id = e.Packet.ReadInt();
        int totalRespects = e.Packet.ReadInt();

        IRoomUser? respectee = room.GetEntityById<IRoomUser>(id);
        if (respectee == null)
            return;

        e.Block();

        string message = $"{respectee.Name} was respected";

        if (_lastRespecterIndex > -1 && (DateTime.Now - _lastRespect).TotalMilliseconds < 500)
        {
            if (ShowWhoRespected)
            {
                IRoomUser? respecter = room.GetEntity<IRoomUser>(_lastRespecterIndex);
                if (respecter is not null)
                    message += $" by {respecter.Name}";
            }

            _lastRespecterIndex = -1;
        }

        message += "!";
        if (ShowTotalRespects)
            message += $" ({totalRespects})";

        Extension.Send(In.Whisper, respectee.Index, message, 0, 1, 0, 0);
    }
}
