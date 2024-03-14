﻿using System;
using System.Threading.Tasks;

using Xabbo.Messages;
using Xabbo.Interceptor;
using Xabbo.Core;
using Xabbo.Core.Game;

namespace b7.Xabbo.Commands;

public class TurnCommand : CommandModule
{
    private readonly ProfileManager _profileManager;
    private readonly RoomManager _roomManager;

    public TurnCommand(ProfileManager profileManager, RoomManager roomManager)
    {
        _profileManager = profileManager;
        _roomManager = roomManager;
    }

    [Command("turn", "t"), RequiredOut(nameof(Outgoing.LookTo))]
    protected async Task HandleLookCommand(CommandArgs args)
    {
        if (args.Count == 0) return;

        int dir = -1;
        switch (args[0].ToLower())
        {
            case "n": dir = 0; break;
            case "ne": dir = 1; break;
            case "e": dir = 2; break;
            case "se": dir = 3; break;
            case "s": dir = 4; break;
            case "sw": dir = 5; break;
            case "w": dir = 6; break;
            case "nw": dir = 7; break;
            default: break;
        }

        if (dir > -1)
        {
            bool sendInverse = true;

            if (_roomManager.Room is not null &&
                _roomManager.Room.TryGetUserById(_profileManager.UserData?.Id ?? -1, out IRoomUser? user))
            {
                if (user.Direction == dir)
                    return;
                // Server doesn't let you turn 45 degrees so we need to face
                // the opposite way first if we're only turning 45 degrees
                int phi = Math.Abs(user.Direction - dir) % 8;
                sendInverse = (phi > 4 ? (8 - phi) : phi) <= 1; // angle difference <= 45 degrees
            }

            int x, y;
            if (sendInverse)
            {
                (x, y) = H.GetMagicVector((dir + 4) % 8);
                Send(Out.LookTo, x, y);
                await Task.Delay(100);
            }
            (x, y) = H.GetMagicVector(dir);
            Send(Out.LookTo, x, y);
        }
    }
}
