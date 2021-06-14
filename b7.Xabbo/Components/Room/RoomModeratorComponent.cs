﻿using System;
using System.Threading.Tasks;

using Xabbo.Messages;
using Xabbo.Interceptor;

using Xabbo.Core;
using Xabbo.Core.Game;

namespace b7.Xabbo.Components
{
    public class RoomModeratorComponent : Component
    {
        private readonly RoomManager _roomManager;

        public bool HasRights => _roomManager.HasRights;
        public bool CanMute => _roomManager.CanMute;
        public bool CanKick => _roomManager.CanKick;
        public bool CanBan => _roomManager.CanBan;
        public bool IsOwner => _roomManager.IsOwner;

        public RoomModeratorComponent(IInterceptor interceptor, RoomManager roomManager)
            : base(interceptor)
        {
            _roomManager = roomManager;

            _roomManager.Entered += (s, e) => UpdatePermissions();
            _roomManager.RoomDataUpdated += (s, e) => UpdatePermissions();
            _roomManager.RightsUpdated += (s, e) => UpdatePermissions();
            _roomManager.Left += (s, e) => UpdatePermissions();
        }

        private void UpdatePermissions()
        {
            RaisePropertyChanged(nameof(HasRights));
            RaisePropertyChanged(nameof(CanMute));
            RaisePropertyChanged(nameof(CanKick));
            RaisePropertyChanged(nameof(CanBan));
            RaisePropertyChanged(nameof(IsOwner));
        }

        [RequiredOut(nameof(Outgoing.RoomMuteUser))]
        public bool MuteUser(Entity e, int minutes)
        {
            if (!_roomManager.CanMute || e == null)
                return false;
            Send(Out.RoomMuteUser, e.Id, _roomManager.CurrentRoomId, minutes);
            return true;
        }

        [RequiredOut(nameof(Outgoing.KickUser))]
        public bool KickUser(Entity e)
        {
            if (!_roomManager.CanKick || e == null)
                return false;
            Send(Out.KickUser, e.Id);
            return true;
        }

        [RequiredOut(nameof(Outgoing.RoomBanWithDuration))]
        public bool BanUser(Entity e, BanDuration duration)
        {
            if (!_roomManager.CanBan || e == null)
                return false;
            Send(Out.RoomBanWithDuration, e.Id, _roomManager.CurrentRoomId, duration.GetValue());
            return true;
        }

        [RequiredOut(nameof(Outgoing.RoomUnbanUser))]
        public bool UnbanUser(Entity e)
        {
            if (!_roomManager.IsOwner || e == null)
                return false;
            Send(Out.RoomUnbanUser, e.Id, _roomManager.CurrentRoomId);
            return true;
        }
    }
}
