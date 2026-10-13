using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace EmberHeart
{
    public class EmberHeartModSystem : ModSystem
    {
        public const string ConfigFile = "emberheart.json";

        public EmberheartConfig Config { get; private set; } = new EmberheartConfig();
        public EmberheartWarmthSystem Warmth { get; private set; }

        private ICoreServerAPI sapi;
        private ICoreClientAPI capi;
        private EmberheartHud hud;
        private EmberheartParticles particles;
        private bool configFileUnreadable;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterCollectibleBehaviorClass("EmberheartActivation",
                typeof(CollectibleBehaviorEmberheart));

            LoadConfig(api);
        }

        /// <summary>
        /// The config is loaded on both sides. The server owns every decision, but the
        /// client needs Mode, DurationSeconds and the temperatures to render an honest
        /// tooltip and HUD without a round trip.
        /// </summary>
        private void LoadConfig(ICoreAPI api)
        {
            // LoadModConfig returns null when the file is simply absent (a normal first
            // run) and throws when it exists but will not parse. Those need different
            // handling: a first run should get a written-out default file, but a file with
            // a typo in it must be left exactly as the admin saved it, or we would discard
            // their settings with no sign in game of why.
            try
            {
                EmberheartConfig loaded = api.LoadModConfig<EmberheartConfig>(ConfigFile);
                if (loaded != null) Config = loaded;
            }
            catch (Exception ex)
            {
                configFileUnreadable = true;
                Mod.Logger.Error(
                    "[Emberheart] " + ConfigFile + " could not be parsed and has been LEFT "
                    + "UNCHANGED. Running on defaults for this session. Fix the file and "
                    + "restart. Error: {0}", ex);
            }

            Config.Sanitise();

            if (!configFileUnreadable && api.Side == EnumAppSide.Server)
            {
                api.StoreModConfig(Config, ConfigFile);
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            Warmth = new EmberheartWarmthSystem(api, Config);
            Warmth.Start();

            EmberheartCommand.Register(api, this);

            Mod.Logger.Notification(
                "[Emberheart] mode={0} duration={1}s boost={2}/s floor={3}C ceiling={4}C "
                + "ramp={5}s taper={6} tick={7}ms",
                Config.Mode, Config.DurationSeconds, Config.BoostPerSecond,
                Config.FloorTemperature, Config.CeilingTemperature, Config.RampUpSeconds,
                Config.TaperFraction, Config.TickIntervalMs);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;

            if (Config.ShowHud)
            {
                api.Input.RegisterHotKey(EmberheartHud.ToggleHotkey,
                    "Toggle Emberheart timer readout", GlKeys.E,
                    HotkeyType.HelpAndOverlays, false, true, false);

                hud = new EmberheartHud(api, this);
                api.Gui.RegisterDialog(hud);
            }

            if (Config.ShowParticles)
            {
                particles = new EmberheartParticles(api);
                particles.Start();
            }
        }

        /// <summary>
        /// Whether this entity has a live Emberheart, answerable on either side.
        /// </summary>
        /// <remarks>
        /// The state lives in WatchedAttributes, which the server syncs to every client
        /// that can see the entity, so the client can read it directly - no packet, no
        /// channel, and it works for other players' entities too.
        /// </remarks>
        public bool IsActiveClientside(Entity entity)
        {
            return RemainingRealSeconds(entity) > 0.0;
        }

        /// <summary>Seconds of burn left, from the synced attributes. Works on both sides.</summary>
        public double RemainingRealSeconds(Entity entity)
        {
            if (entity == null) return 0.0;

            ITreeAttribute tree = entity.WatchedAttributes
                .GetTreeAttribute(EmberheartWarmthSystem.AttrTree);
            if (tree == null) return 0.0;

            double expiry = tree.GetDouble("expiryTotalHours", 0.0);
            if (expiry <= 0.0) return 0.0;

            IGameCalendar cal = entity.World.Calendar;
            double remainingHours = expiry - cal.TotalHours;
            if (remainingHours <= 0.0) return 0.0;

            double gameSecondsPerRealSecond = cal.SpeedOfTime * cal.CalendarSpeedMul;
            if (gameSecondsPerRealSecond <= 0.0) gameSecondsPerRealSecond = 60.0;

            return remainingHours * 3600.0 / gameSecondsPerRealSecond;
        }

        public override void Dispose()
        {
            Warmth?.Stop();
            particles?.Stop();
            base.Dispose();
        }
    }
}
