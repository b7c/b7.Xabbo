﻿using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

using Xabbo.Interceptor;
using Xabbo.Core;
using Xabbo.Core.GameData;
using Xabbo.Core.Extensions;

namespace b7.Xabbo.Services;

/// <summary>
/// Manages resources for the local hotel.
/// </summary>
public class HotelResourceManager : IHostedService
{
    private readonly IUriProvider<HabboEndpoints> _uriProvider;
    private readonly IGameDataManager _gameDataManager;

    private CancellationTokenSource? _ctsLoad;

    public HotelResourceManager(
        IInterceptor interceptor,
        IUriProvider<HabboEndpoints> uriProvider,
        IGameDataManager gameDataManager)
    {
        interceptor.Connected += OnGameConnected;

        _uriProvider = uriProvider;
        _gameDataManager = gameDataManager;

        _gameDataManager.Loaded += OnGameDataLoaded;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnGameConnected(object? sender, GameConnectedEventArgs e)
    {
        _ctsLoad?.Cancel();
        _ctsLoad = new CancellationTokenSource();
        CancellationToken ct = _ctsLoad.Token;

        try
        {
            Hotel hotel = Hotel.FromGameHost(e.Host);
            _uriProvider.Host = hotel.Host;
            Task.Run(() => _gameDataManager.LoadAsync(hotel, ct));
        }
        catch (Exception ex)
        {
            // Failed to find hotel
        }
    }

    private void OnGameDataLoaded()
    {
        FurniData? furni = _gameDataManager.Furni;
        ExternalTexts? texts = _gameDataManager.Texts;

        if (furni is null || texts is null) return;

        XabboCoreExtensions.Initialize(furni, texts);
    }
}
