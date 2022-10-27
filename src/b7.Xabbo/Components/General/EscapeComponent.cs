﻿using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Xabbo.Core;
using Xabbo.Core.Events;
using Xabbo.Core.Game;
using Xabbo.Extension;
using Xabbo.Messages;

using b7.Xabbo.Configuration;

namespace b7.Xabbo.Components;

public class EscapeComponent : Component
{
    private readonly RoomManager _roomManager;

    private readonly GameOptions _gameOptions;

    private bool _canEscapeStaff;
    public bool CanEscapeStaff
    {
        get => _canEscapeStaff;
        set => SetProperty(ref _canEscapeStaff, value);
    }

    private bool _escapeStaff;
    public bool EscapeStaff
    {
        get => _escapeStaff;
        set => SetProperty(ref _escapeStaff, value);
    }

    private bool _canEscapeAmbassadors;
    public bool CanEscapeAmbassadors
    {
        get => _canEscapeAmbassadors;
        set => SetProperty(ref _canEscapeAmbassadors, value);
    }

    private bool _escapeAmbassadors;
    public bool EscapeAmbassadors
    {
        get => _escapeAmbassadors;
        set => SetProperty(ref _escapeAmbassadors, value);
    }

    public EscapeComponent(IExtension extension,
        IConfiguration config,
        IOptions<GameOptions> gameOptions,
        RoomManager roomManager)
        : base(extension)
    {
        _gameOptions = gameOptions.Value;
        _roomManager = roomManager;

        EscapeStaff = config.GetValue("Escape:Staff", false);
        EscapeAmbassadors = config.GetValue("Escape:Ambassadors", false);

        CanEscapeStaff = _gameOptions.StaffList.Any();
        CanEscapeAmbassadors = _gameOptions.AmbassadorList.Any();

        _roomManager.EntitiesAdded += Entities_EntitiesAdded;
    }

    private async void Entities_EntitiesAdded(object? sender, EntitiesEventArgs e)
    {
        bool escape = false;
        string escapeMessage = "";

        foreach (var user in e.Entities.OfType<IRoomUser>())
        {
            string name = user.Name;

            if (EscapeStaff && _gameOptions.StaffList.Contains(name))
            {
                escape = true;
                escapeMessage = $"Escaped from staff! {name}";
                break;
            }
            else if (EscapeAmbassadors && _gameOptions.AmbassadorList.Contains(name))
            {
                escape = true;
                escapeMessage = $"Escaped from ambassador! {name}";
                break;
            }
        }

        if (escape)
        {
            await Task.Delay(500);
            Extension.Send(Out.FlatOpc, (LegacyLong)0, "", -1L);
            Extension.Send(In.SystemBroadcast, escapeMessage);
            return;
        }
    }
}
