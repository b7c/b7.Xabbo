﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

using GalaSoft.MvvmLight.Command;

using Xabbo.Messages;
using Xabbo.Interceptor;
using Xabbo.Core.Game;

using b7.Xabbo.Services;
using Microsoft.Extensions.Configuration;
using MaterialDesignThemes.Wpf;

namespace b7.Xabbo.ViewModel
{
    public class BanListViewManager : ComponentViewModel
    {
        private readonly IUiContext _context;
        private readonly ISnackbarMessageQueue _snackbarMq;
        private readonly RoomManager _roomManager;

        private CancellationTokenSource? _cts;

        private readonly ObservableCollection<BannedUserViewModel> _users = new();
        private readonly ConcurrentDictionary<long, BannedUserViewModel> _userIdMap = new();

        private readonly int _banInterval;

        public ICollectionView Users { get; }

        private bool _isAvailable;
        public bool IsAvailable
        {
            get => _isAvailable;
            set => Set(ref _isAvailable, value);
        }

        private bool _isInRoom;
        public bool IsInRoom
        {
            get => _isInRoom;
            set => Set(ref _isInRoom, value);
        }

        private bool _isWorking;
        public bool IsWorking
        {
            get => _isWorking;
            set => Set(ref _isWorking, value);
        }

        private bool _isUnbanning;
        public bool IsUnbanning
        {
            get => _isUnbanning;
            set => Set(ref _isUnbanning, value);
        }

        private bool _isCancelling;
        public bool IsCancelling
        {
            get => _isCancelling;
            set => Set(ref _isCancelling, value);
        }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (Set(ref _filterText, value))
                    RefreshUsers();
            }
        }

        public ICommand CancelCommand { get; }
        public ICommand LoadCommand { get; }
        public ICommand UnbanCommand { get; }

        public BanListViewManager(
            IInterceptor interceptor,
            IConfiguration config,
            ISnackbarMessageQueue snackbarMq,
            IUiContext context,
            RoomManager roomManager)
            : base(interceptor)
        {
            _context = context;
            _snackbarMq = snackbarMq;
            _roomManager = roomManager;

            _banInterval = config.GetValue("BanList:Interval", 600);

            Interceptor.Disconnected += OnGameDisconnected;
            _roomManager.Entered += RoomManager_Entered;
            _roomManager.Left += RoomManager_Left;

            Users = CollectionViewSource.GetDefaultView(_users);
            Users.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            Users.Filter = FilterUsers;

            CancelCommand = new RelayCommand(Cancel);
            LoadCommand = new RelayCommand(Load);
            UnbanCommand = new RelayCommand<IList>(Unban);
        }

        #region - Commands -
        private void Cancel()
        {
            if (!IsWorking || IsCancelling) return;

            IsCancelling = true;
            _cts?.Cancel();
        }

        private async void Load()
        {
            if (IsWorking) return;

            await LoadAsync();
        }

        private async void Unban(IList list)
        {
            if (list == null || IsWorking) return;

            await UnbanAsync(list.OfType<BannedUserViewModel>());
        }
        #endregion

        #region - ViewModel logic -
        private void Reset()
        {
            IsInRoom = false;
            Cancel();
            Clear();
        }

        private bool FilterUsers(object obj)
        {
            if (obj is not BannedUserViewModel user)
                return false;

            return user.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshUsers()
        {
            if (!_context.IsSynchronized)
            {
                _context.InvokeAsync(() => RefreshUsers());
                return;
            }

            Users.Refresh();
        }

        private void Clear()
        {
            if (!_context.IsSynchronized)
            {
                _context.InvokeAsync(() => Clear());
                return;
            }

            _users.Clear();
            _userIdMap.Clear();
        }

        private void AddUser(BannedUserViewModel user)
        {
            if (!_context.IsSynchronized)
            {
                _context.InvokeAsync(() => AddUser(user));
                return;

            }

            _users.Add(user);
        }

        private void RemoveUser(BannedUserViewModel user)
        {
            if (!_context.IsSynchronized)
            {
                _context.InvokeAsync(() => RemoveUser(user));
                return;
            }

            _users.Remove(user);
        }
        #endregion

        #region - Events -
        private void OnGameDisconnected(object? sender, EventArgs e) => Reset();
        private void RoomManager_Entered(object? sender, EventArgs e) => IsInRoom = true;
        private void RoomManager_Left(object? sender, EventArgs e) => Reset();
        #endregion

        #region - Logic -
        [InterceptIn(nameof(Incoming.UserUnbannedFromRoom))]
        protected void HandleRoomUserUnbanned(InterceptArgs e)
        {
            long roomId = e.Packet.ReadLegacyLong();
            if (roomId != _roomManager.CurrentRoomId)
                return;

            int userId = e.Packet.ReadInt();
            if (_userIdMap.TryRemove(userId, out BannedUserViewModel? user))
                RemoveUser(user);
        }

        private async Task LoadAsync() 
        {
            if (!_roomManager.IsInRoom || IsWorking)
                return;

            _cts = new CancellationTokenSource();

            try
            {
                IsWorking = true;

                Clear();

                await Interceptor.SendAsync(Out.GetBannedUsers, (LegacyLong)_roomManager.CurrentRoomId);
                var packet = await Interceptor.ReceiveAsync(In.UsersBannedFromRoom, 4000);

                long roomId = packet.ReadLegacyLong();
                if (roomId != _roomManager.CurrentRoomId)
                    throw new Exception($"Room ID mismatch (expected {_roomManager.CurrentRoomId}, received {roomId}).");

                short n = packet.ReadLegacyShort();
                for (int i = 0; i < n; i++)
                {
                    long userId = packet.ReadLegacyLong();
                    string userName = packet.ReadString();

                    var userViewModel = new BannedUserViewModel(userId, userName);
                    if (_userIdMap.TryAdd(userId, userViewModel))
                        AddUser(userViewModel);
                }
            }
            catch (OperationCanceledException)
            {
                _snackbarMq.Enqueue("Failed to load the banned users list.");
            }
            catch { }
            finally
            {
                _cts.Dispose();
                _cts = null;

                IsWorking =
                IsCancelling = false;
            }
        }

        private async Task UnbanAsync(IEnumerable<BannedUserViewModel> users)
        {
            if (!_roomManager.IsInRoom || IsWorking)
                return;

            _cts = new CancellationTokenSource();

            try
            {
                long roomId = _roomManager.CurrentRoomId;
                if (roomId <= 0) return;

                IsWorking =
                IsUnbanning = true;

                var array = users.ToArray();
                for (int i = 0; i < array.Length; i++)
                {
                    var user = array[i];
                    await Interceptor.SendAsync(Out.RoomUnbanUser, (LegacyLong)user.Id, (LegacyLong)roomId);
                    await Task.Delay(_banInterval, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            when (_cts.IsCancellationRequested)
            { }
            catch (Exception ex)
            {
                Dialog.ShowError(ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;

                IsWorking =
                IsUnbanning =
                IsCancelling = false;
            }
        }
        #endregion
    }
}
