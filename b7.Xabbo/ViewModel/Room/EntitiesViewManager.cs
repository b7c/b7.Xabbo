﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

using GalaSoft.MvvmLight.Command;

using Xabbo.Messages;
using Xabbo.Interceptor;

using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Core.Events;

using b7.Xabbo.Components;
using b7.Xabbo.Services;
using b7.Xabbo.Configuration;

namespace b7.Xabbo.ViewModel
{
    public class EntitiesViewManager : ComponentViewModel
    {
        private const string IMAGE_STAFF = "/b7.Xabbo;component/Resources/Images/staff.png";
        private const string IMAGE_AMB = "/b7.Xabbo;component/Resources/Images/ambassador.png";

        private readonly Color COLOR_AMB = Colors.Cyan;
        private readonly Color COLOR_STAFF = Colors.Yellow;
        private readonly Color COLOR_NONE = Colors.Transparent;

        private readonly ILogger _logger;
        private readonly IUiContext _uiContext;
        private readonly HashSet<string> _staffList, _ambassadorList;

        private readonly RoomManager _roomManager;

        private readonly ObservableCollection<EntityViewModel> _entities = new();
        private readonly ConcurrentDictionary<int, EntityViewModel> _entitiesByIndex = new();
        private readonly ConcurrentDictionary<long, EntityViewModel> _usersById = new();

        public ICollectionView Entities { get; }

        private EntityViewModel? selectedItem;
        public EntityViewModel? SelectedEntity
        {
            get => selectedItem;
            set
            {
                if (Set(ref selectedItem, value))
                {
                    RaisePropertyChanged(nameof(IsSelectedEntityUser));
                }
            }
        }

        public bool IsSelectedEntityUser => SelectedEntity?.Entity.Type == EntityType.User;

        private bool showBots;
        public bool ShowBots
        {
            get => showBots;
            set
            {
                if (Set(ref showBots, value))
                    RefreshList();
            }
        }

        private bool showPets;
        public bool ShowPets
        {
            get => showPets;
            set
            {
                if (Set(ref showPets, value))
                    RefreshList();
            }
        }

        public RoomModeratorComponent Moderation { get; }

        public ICommand FindCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand OpenProfileCommand { get; }
        public ICommand MuteCommand { get; }
        public ICommand KickCommand { get; }
        public ICommand BanCommand { get; }
        public ICommand BounceCommand { get; }

        public EntitiesViewManager(IInterceptor interceptor,
            ILogger<EntitiesViewManager> logger,
            IUiContext uiContext,
            IConfiguration config,
            IOptions<GameOptions> gameOptions,
            RoomManager roomManager,
            RoomModeratorComponent moderation)
            : base(interceptor)
        {
            _logger = logger;
            _uiContext = uiContext;

            _staffList = gameOptions.Value.StaffList;
            _ambassadorList = gameOptions.Value.AmbassadorList;

            _roomManager = roomManager;
            Moderation = moderation;

            Entities = CollectionViewSource.GetDefaultView(_entities);
            Entities.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EntityViewModel.VisualGroupName)));
            Entities.SortDescriptions.Add(new SortDescription(nameof(EntityViewModel.VisualGroupSort), ListSortDirection.Ascending));
            Entities.SortDescriptions.Add(new SortDescription(nameof(EntityViewModel.Name), ListSortDirection.Ascending));
            Entities.Filter = FilterEntity;

            FindCommand = new RelayCommand(OnFindEntity);
            CopyCommand = new RelayCommand<string>(OnCopy);
            OpenProfileCommand = new RelayCommand<string>(OnOpenProfile);
            MuteCommand = new RelayCommand<string>(OnMute);
            KickCommand = new RelayCommand(OnKick);
            BanCommand = new RelayCommand<string>(OnBan);
            BounceCommand = new RelayCommand(OnBounce);

            _roomManager.RoomDataUpdated += RoomManager_RoomDataUpdated;
            _roomManager.Left += RoomManager_Left;

            _roomManager.EntitiesAdded += OnEntitiesAdded;
            _roomManager.EntityRemoved += OnEntityRemoved;
            _roomManager.EntitiesUpdated += OnEntitiesUpdated;
            _roomManager.UserDataUpdated += OnUserDataUpdated;
            _roomManager.EntityNameChanged += OnEntityNameChanged;
            _roomManager.EntityIdle += OnEntityIdle;
        }

        private bool FilterEntity(object obj)
        {
            if (obj is not EntityViewModel viewModel) return false;

            if (!ShowPets && viewModel.Entity.Type == EntityType.Pet)
                return false;

            if (!ShowBots &&
                (viewModel.Entity.Type == EntityType.PublicBot ||
                 viewModel.Entity.Type == EntityType.PrivateBot))
                return false;

            return true;
        }

        private void AddEntity(EntityViewModel entityViewModel)
        {
            if (!_uiContext.IsSynchronized)
            {
                _uiContext.InvokeAsync(() => AddEntity(entityViewModel));
                return;
            }

            _entities.Add(entityViewModel);
            _entitiesByIndex.TryAdd(entityViewModel.Index, entityViewModel);
            if (entityViewModel.Entity.Type == EntityType.User)
                _usersById.TryAdd(entityViewModel.Id, entityViewModel);
        }

        private void AddEntities(IEnumerable<EntityViewModel> entityViewModels)
        {
            if (!_uiContext.IsSynchronized)
            {
                _uiContext.InvokeAsync(() => AddEntities(entityViewModels));
                return;
            }

            foreach (var entity in entityViewModels)
                AddEntity(entity);
        }

        private void RemoveEntity(EntityViewModel entityViewModel)
        {
            if (!_uiContext.IsSynchronized)
            {
                _uiContext.InvokeAsync(() => RemoveEntity(entityViewModel));
                return;
            }

            _entities.Remove(entityViewModel);
            _entitiesByIndex.TryRemove(entityViewModel.Index, out _);
            if (entityViewModel.Entity.Type == EntityType.User)
                _usersById.TryRemove(entityViewModel.Id, out _);
        }

        private void ClearEntities()
        {
            if (!_uiContext.IsSynchronized)
            {
                _uiContext.InvokeAsync(() => ClearEntities());
                return;
            }

            _entities.Clear();
            _entitiesByIndex.Clear();
            _usersById.Clear();
        }

        private void RefreshList()
        {
            if (!_uiContext.IsSynchronized)
            {
                _uiContext.InvokeAsync(() => RefreshList());
                return;
            }

            _logger.LogTrace("Refreshing collection view");
            Entities.Refresh();
        }

        #region - Events -
        private void RoomManager_RoomDataUpdated(object? sender, RoomDataEventArgs e)
        {
            if (_usersById.TryGetValue(e.Data.OwnerId, out EntityViewModel? user))
            {
                if (!user.IsRoomOwner)
                {
                    _logger.LogTrace("Room owner detected: {owner}", user.Entity);
                    user.IsRoomOwner = true;
                    RefreshList();
                }
            }
        }

        private void RoomManager_Left(object? sender, EventArgs e)
        {
            ClearEntities();
        }

        private void OnEntitiesAdded(object? sender, EntitiesEventArgs e)
        {
            try
            {
                if (!_roomManager.IsLoadingRoom && !_roomManager.IsInRoom)
                    return;

                var entityViewModels = e.Entities.Select(x => new EntityViewModel(x)).ToArray();
                foreach (var vm in entityViewModels)
                {
                    if (vm.Entity.Type != EntityType.User)
                    {
                        vm.ImageSource = "";
                        vm.HeaderColor = COLOR_NONE;
                        continue;
                    }

                    if (vm.Id == _roomManager.Data?.OwnerId)
                    {
                        _logger.LogTrace("Room owner detected: {owner}", vm.Entity);
                        vm.IsRoomOwner = true;
                    }

                    if (vm.IsStaff = _staffList.Contains(vm.Name))
                    {
                        vm.ImageSource = IMAGE_STAFF;
                        vm.HeaderColor = COLOR_STAFF;
                    }
                    else if (vm.IsAmbassador = _ambassadorList.Contains(vm.Name))
                    {
                        vm.ImageSource = IMAGE_AMB;
                        vm.HeaderColor = COLOR_AMB;
                    }
                    else
                    {
                        vm.ImageSource = "";
                        vm.HeaderColor = COLOR_NONE;
                    }
                }

                AddEntities(entityViewModels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in OnEntitiesAdded: {message}", ex.Message);
            }
        }

        private void OnEntityRemoved(object? sender, EntityEventArgs e)
        {
            if (_entitiesByIndex.TryGetValue(e.Entity.Index, out EntityViewModel? entityViewModel))
                RemoveEntity(entityViewModel);
        }

        private void OnEntitiesUpdated(object? sender, EntitiesEventArgs e)
        {
            bool refresh = false;
            foreach (var user in e.Entities.OfType<IRoomUser>())
            {
                if (!_usersById.TryGetValue(user.Id, out EntityViewModel? vm))
                    continue;

                // TODO trade status

                IEntityStatusUpdate? update = user.CurrentUpdate;
                if (update is not null && update.IsController != vm.HasRights)
                {
                    vm.HasRights = update.IsController;
                    _logger.LogTrace("Updating rights for {user} = {hasRights} (level:level)", vm.Entity, vm.HasRights, update.ControlLevel);
                    if (!vm.IsStaff && !vm.IsAmbassador && !vm.IsRoomOwner)
                        refresh = true;
                }
            }

            if (refresh)
            {
                RefreshList();
            }
        }

        private void OnUserDataUpdated(object? sender, UserDataUpdatedEventArgs e)
        {
            if (!_usersById.TryGetValue(e.User.Id, out EntityViewModel? vm))
                return;

            if (e.FigureUpdated)
                vm.Figure = e.User.Figure;
            if (e.MottoUpdated)
                vm.Motto = e.User.Motto;
        }

        private void OnEntityNameChanged(object? sender, EntityNameChangedEventArgs e)
        {
            if (!_entitiesByIndex.TryGetValue(e.Entity.Index, out EntityViewModel? vm))
                return;

            vm.Name = e.Entity.Name;
        }

        private void OnEntityIdle(object? sender, EntityIdleEventArgs e)
        {
            if (e.WasIdle == e.Entity.IsIdle) return;

            if (!_entitiesByIndex.TryGetValue(e.Entity.Index, out EntityViewModel? vm))
                return;

            vm.IsIdle = e.Entity.IsIdle;
        }
        #endregion

        #region - Commands -
        private void OnFindEntity()
        {
            if (SelectedEntity == null) return;

            Send(In.Whisper,
                (LegacyLong)SelectedEntity.Index,
                "(click here to find)", 0, 0, 0, 0
            );
        }

        private void OnCopy(string arg)
        {
            if (SelectedEntity == null) return;

            string? text = null;

            switch (arg)
            {
                case "index": text = SelectedEntity.Index.ToString(); break;
                case "id": text = SelectedEntity.Id.ToString(); break;
                case "name": text = SelectedEntity.Name; break;
                case "motto": text = SelectedEntity.Motto; break;
                case "figure": text = SelectedEntity.Figure; break;
                default: break;
            }

            if (text is null) return;

            try { Clipboard.SetText(text); }
            catch (Exception ex) { /*Dialog.ShowError($"Failed to set clipboard text: {ex.Message}");*/ }
        }

        private void OnOpenProfile(string arg)
        {
            if (SelectedEntity == null ||
                SelectedEntity.Entity.Type != EntityType.User) return;

            try
            {
                // TODO dynamic domain

                switch (arg)
                {
                    case "game":
                        Send(Out.GetExtendedProfile, SelectedEntity.Id, true);
                        break;
                    case "web":
                        Process.Start($"https://www.habbo.com/profile/{WebUtility.UrlEncode(SelectedEntity.Name)}");
                        break;
                    case "widgets":
                        Process.Start($"http://www.habbowidgets.com/habinfo/com/{WebUtility.UrlEncode(SelectedEntity.Name)}");
                        break;
                    default:
                        break;
                }
            }
            catch { }
        }

        private async void OnMute(string arg)
        {
            if (SelectedEntity == null ||
                SelectedEntity.Entity.Type != EntityType.User) return;

            if (!int.TryParse(arg, out int minutes))
                return;
           
            await SendAsync(Out.RoomMuteUser,
                (LegacyLong)SelectedEntity.Id,
                (LegacyLong)_roomManager.CurrentRoomId,
                minutes
            );
        }

        private async void OnKick()
        {
            if (SelectedEntity == null ||
                SelectedEntity.Entity.Type != EntityType.User) return;

            await SendAsync(Out.KickUser, (LegacyLong)SelectedEntity.Id);
        }

        private async void OnBan(string arg)
        {
            if (SelectedEntity == null ||
                SelectedEntity.Entity.Type != EntityType.User) return;

            BanDuration banDuration;

            switch (arg)
            {
                case "hour": banDuration = BanDuration.Hour; break;
                case "day": banDuration = BanDuration.Day; break;
                case "perm": banDuration = BanDuration.Permanent; break;
                default: return;
            }

            await SendAsync(Out.RoomBanWithDuration,
                (LegacyLong)SelectedEntity.Id,
                (LegacyLong)_roomManager.CurrentRoomId,
                banDuration.GetValue()
            );
        }

        private async void OnBounce()
        {
            if (SelectedEntity == null ||
                SelectedEntity.Entity.Type != EntityType.User) return;

            long userId = SelectedEntity.Id;
            long roomId = _roomManager.CurrentRoomId;

            await SendAsync(Out.RoomBanWithDuration, (LegacyLong)userId, (LegacyLong)roomId, BanDuration.Hour.GetValue());
            await Task.Delay(200);
            await SendAsync(Out.RoomUnbanUser, (LegacyLong)userId, (LegacyLong)roomId);
        }
#endregion
    }
}