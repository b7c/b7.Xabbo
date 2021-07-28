﻿using System;

using Xabbo.Interceptor;
using Xabbo.Messages;

namespace b7.Xabbo.Components
{
    public class AntiWalkComponent : Component
    {
        private bool _faceDirection;
        public bool FaceDirection
        {
            get => _faceDirection;
            set => Set(ref _faceDirection, value);
        }

        public AntiWalkComponent(IInterceptor interceptor)
            : base(interceptor)
        { }

        [InterceptOut(nameof(Outgoing.Move)), RequiredOut(nameof(Outgoing.LookTo))]
        private void OnMove(InterceptArgs e)
        {
            if (IsActive) e.Block();

            if (FaceDirection)
            {
                int x = e.Packet.ReadInt();
                int y = e.Packet.ReadInt();
                Send(Out.LookTo, x, y);
            }
        }
    }
}