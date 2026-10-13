using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace EmberHeart
{
    /// <summary>
    /// Ember motes drifting off anyone carrying a lit Emberheart.
    /// </summary>
    /// <remarks>
    /// Client-side on purpose. The active flag is synced in WatchedAttributes already, so
    /// every client can work out for itself which players are glowing - no particle packets
    /// from the server, and other players' Emberhearts show up for free.
    /// </remarks>
    public class EmberheartParticles
    {
        private const int IntervalMs = 250;

        /// <summary>Blocks from the camera within which other players' embers are drawn.</summary>
        private const double RangeSq = 32 * 32;

        private readonly ICoreClientAPI capi;
        private readonly EmberHeartModSystem mod;
        private long listenerId;

        private readonly SimpleParticleProperties embers;

        public EmberheartParticles(ICoreClientAPI capi)
        {
            this.capi = capi;
            mod = capi.ModLoader.GetModSystem<EmberHeartModSystem>();

            embers = new SimpleParticleProperties(
                1, 2,
                ColorUtil.ToRgba(180, 60, 150, 255),
                new Vec3d(), new Vec3d(),
                new Vec3f(-0.08f, 0.05f, -0.08f),
                new Vec3f(0.08f, 0.22f, 0.08f),
                1.2f,
                -0.015f,
                0.12f, 0.28f,
                EnumParticleModel.Quad);

            embers.VertexFlags = 128;          // full-bright, so they read as live coals
            embers.ShouldDieInLiquid = true;
            embers.SelfPropelled = false;
            embers.AddPos.Set(0.35, 0.8, 0.35);
        }

        public void Start()
        {
            listenerId = capi.Event.RegisterGameTickListener(OnTick, IntervalMs);
        }

        public void Stop()
        {
            if (listenerId == 0) return;
            capi.Event.UnregisterGameTickListener(listenerId);
            listenerId = 0;
        }

        private void OnTick(float dt)
        {
            IClientPlayer self = capi.World.Player;
            if (self?.Entity == null) return;

            Vec3d eye = self.Entity.Pos.XYZ;

            foreach (Entity entity in capi.World.LoadedEntities.Values)
            {
                if (!(entity is EntityPlayer)) continue;
                if (!entity.Alive) continue;
                if (entity.Pos.XYZ.SquareDistanceTo(eye) > RangeSq) continue;
                if (!mod.IsActiveClientside(entity)) continue;

                embers.MinPos.Set(entity.Pos.X - 0.175, entity.Pos.Y + 0.4, entity.Pos.Z - 0.175);
                capi.World.SpawnParticles(embers);
            }
        }
    }
}
