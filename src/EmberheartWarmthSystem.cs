using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EmberHeart
{
    /// <summary>
    /// Server-side owner of the warming effect: one tick listener, all book-keeping in the
    /// player's own WatchedAttributes.
    /// </summary>
    /// <remarks>
    /// <para>Two decisions worth knowing about.</para>
    ///
    /// <para><b>One global listener, not one per player.</b> A per-player listener has to be
    /// unregistered on expiry, death, disconnect and shutdown, and any path that is missed
    /// leaks a closure holding a player reference for the life of the server. A single
    /// listener that walks the online players has no such path - there is nothing to clean
    /// up, so there is nothing to forget.</para>
    ///
    /// <para><b>Expiry is stored as a calendar timestamp, not a real-world one.</b> Real
    /// wall-clock time is not saved with the world and keeps running while the server is
    /// off, so a real-time expiry would burn away overnight. Calendar hours are saved,
    /// advance only while the world is running, and are what vanilla's own temperature code
    /// already measures in. Relog, server restart and sleeping through it all behave
    /// correctly for free.</para>
    /// </remarks>
    public class EmberheartWarmthSystem
    {
        /// <summary>WatchedAttributes tree on the player entity. Synced to clients, hence the HUD.</summary>
        public const string AttrTree = "emberheart";

        private const string AttrStart = "startTotalHours";
        private const string AttrExpiry = "expiryTotalHours";
        private const string AttrTapered = "taperWarned";

        private readonly ICoreServerAPI sapi;
        private readonly EmberheartConfig config;
        private long listenerId;

        public EmberheartWarmthSystem(ICoreServerAPI sapi, EmberheartConfig config)
        {
            this.sapi = sapi;
            this.config = config;
        }

        public void Start()
        {
            listenerId = sapi.World.RegisterGameTickListener(OnTick, config.TickIntervalMs);

            sapi.Event.PlayerDeath += OnPlayerDeath;
            sapi.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        }

        public void Stop()
        {
            if (listenerId != 0)
            {
                sapi.World.UnregisterGameTickListener(listenerId);
                listenerId = 0;
            }

            sapi.Event.PlayerDeath -= OnPlayerDeath;
            sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
        }

        // ---------------------------------------------------------------- calendar helpers

        /// <summary>
        /// In-game hours that pass per real second at the world's current calendar speed.
        /// SpeedOfTime defaults to 60 and CalendarSpeedMul to 1, giving 1/60 - one in-game
        /// hour per real minute.
        /// </summary>
        public double HoursPerRealSecond()
        {
            IGameCalendar cal = sapi.World.Calendar;
            double gameSecondsPerRealSecond = cal.SpeedOfTime * cal.CalendarSpeedMul;

            // A paused or frozen calendar would otherwise divide by zero and park every
            // active Emberheart forever. Fall back to the stock rate.
            if (gameSecondsPerRealSecond <= 0.0) gameSecondsPerRealSecond = 60.0;

            return gameSecondsPerRealSecond / 3600.0;
        }

        private double RealSecondsToHours(double realSeconds)
        {
            return realSeconds * HoursPerRealSecond();
        }

        private double HoursToRealSeconds(double hours)
        {
            double perSecond = HoursPerRealSecond();
            return perSecond <= 0.0 ? 0.0 : hours / perSecond;
        }

        // ---------------------------------------------------------------- state accessors

        private static ITreeAttribute Tree(Entity entity, bool create)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(AttrTree);
            if (tree == null && create)
            {
                tree = new TreeAttribute();
                entity.WatchedAttributes.SetAttribute(AttrTree, tree);
            }
            return tree;
        }

        public bool IsActive(Entity entity)
        {
            return RemainingHours(entity) > 0.0;
        }

        public double RemainingHours(Entity entity)
        {
            ITreeAttribute tree = Tree(entity, false);
            if (tree == null) return 0.0;

            double expiry = tree.GetDouble(AttrExpiry, 0.0);
            if (expiry <= 0.0) return 0.0;

            return Math.Max(0.0, expiry - sapi.World.Calendar.TotalHours);
        }

        public double RemainingRealSeconds(Entity entity)
        {
            return HoursToRealSeconds(RemainingHours(entity));
        }

        /// <summary>0 at activation, 1 at expiry. Returns 1 when nothing is active.</summary>
        public double Progress(Entity entity)
        {
            ITreeAttribute tree = Tree(entity, false);
            if (tree == null) return 1.0;

            double start = tree.GetDouble(AttrStart, 0.0);
            double expiry = tree.GetDouble(AttrExpiry, 0.0);
            double span = expiry - start;
            if (span <= 0.0) return 1.0;

            return GameMath.Clamp((sapi.World.Calendar.TotalHours - start) / span, 0.0, 1.0);
        }

        /// <summary>
        /// The ramp-and-taper multiplier on the effect's strength, 0..1.
        /// </summary>
        /// <remarks>
        /// Both ends are computed from the stored start/expiry rather than from the live
        /// config, so changing DurationSeconds mid-burn rescales an already-lit Emberheart
        /// smoothly instead of making it jump or hang.
        /// </remarks>
        public float Strength(Entity entity)
        {
            ITreeAttribute tree = Tree(entity, false);
            if (tree == null) return 0f;

            double start = tree.GetDouble(AttrStart, 0.0);
            double expiry = tree.GetDouble(AttrExpiry, 0.0);
            double span = expiry - start;
            if (span <= 0.0) return 0f;

            double now = sapi.World.Calendar.TotalHours;
            if (now >= expiry) return 0f;

            double elapsed = Math.Max(0.0, now - start);
            float strength = 1f;

            double rampHours = RealSecondsToHours(config.RampUpSeconds);
            if (rampHours > 0.0 && elapsed < rampHours)
            {
                strength = (float)(elapsed / rampHours);
            }

            if (config.TaperFraction > 0f)
            {
                double taperHours = span * config.TaperFraction;
                double taperStart = expiry - taperHours;
                if (taperHours > 0.0 && now > taperStart)
                {
                    strength *= (float)((expiry - now) / taperHours);
                }
            }

            return GameMath.Clamp(strength, 0f, 1f);
        }

        // ---------------------------------------------------------------- activation

        /// <summary>
        /// Lights an Emberheart on this player. Returns false only when one is already
        /// burning and the config forbids a refresh.
        /// </summary>
        public bool Activate(IServerPlayer player)
        {
            Entity entity = player.Entity;
            if (entity == null) return false;

            bool wasActive = IsActive(entity);
            if (wasActive && !config.StackingRefreshesTimer) return false;

            double now = sapi.World.Calendar.TotalHours;
            ITreeAttribute tree = Tree(entity, true);

            tree.SetDouble(AttrStart, now);
            tree.SetDouble(AttrExpiry, now + RealSecondsToHours(config.DurationSeconds));
            tree.SetBool(AttrTapered, false);
            entity.WatchedAttributes.MarkPathDirty(AttrTree);

            Tell(player, wasActive ? "emberheart:msg-refreshed" : "emberheart:msg-lit",
                FormatRealSeconds(config.DurationSeconds));

            return true;
        }

        /// <summary>Ends the effect and removes every trace of it from the entity.</summary>
        /// <remarks>
        /// Zeroes the values before removing the tree, and marks dirty after each step.
        /// Removing a whole attribute does not always reach clients as reliably as
        /// changing a value inside one does, and a client that missed the removal would
        /// leave the HUD counting down against an expiry that no longer exists. Zeroing
        /// first means even a missed removal reads as "not active" everywhere.
        /// </remarks>
        public void Clear(Entity entity)
        {
            if (entity == null) return;

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(AttrTree);
            if (tree == null) return;

            tree.SetDouble(AttrStart, 0.0);
            tree.SetDouble(AttrExpiry, 0.0);
            tree.SetBool(AttrTapered, false);
            entity.WatchedAttributes.MarkPathDirty(AttrTree);

            entity.WatchedAttributes.RemoveAttribute(AttrTree);
            entity.WatchedAttributes.MarkPathDirty(AttrTree);
        }

        // ---------------------------------------------------------------- the tick

        private void OnTick(float dt)
        {
            IPlayer[] players = sapi.World.AllOnlinePlayers;

            for (int i = 0; i < players.Length; i++)
            {
                IServerPlayer player = players[i] as IServerPlayer;
                if (player == null) continue;

                // A player still loading has an entity whose body-temperature behavior has
                // not initialised its attribute tree yet. Vanilla guards its own update the
                // same way.
                if (player.ConnectionState != EnumClientState.Playing) continue;

                Entity entity = player.Entity;
                if (entity == null || !entity.Alive) continue;

                ITreeAttribute tree = Tree(entity, false);
                if (tree == null) continue;

                double expiry = tree.GetDouble(AttrExpiry, 0.0);
                if (expiry <= 0.0) { Clear(entity); continue; }

                double now = sapi.World.Calendar.TotalHours;

                if (now >= expiry)
                {
                    Clear(entity);
                    Tell(player, "emberheart:msg-expired");
                    continue;
                }

                if (config.AnnounceTaper && !tree.GetBool(AttrTapered, false)
                    && config.TaperFraction > 0f)
                {
                    double start = tree.GetDouble(AttrStart, 0.0);
                    double taperStart = expiry - (expiry - start) * config.TaperFraction;
                    if (now >= taperStart)
                    {
                        tree.SetBool(AttrTapered, true);
                        entity.WatchedAttributes.MarkPathDirty(AttrTree);
                        Tell(player, "emberheart:msg-cooling");
                    }
                }

                ApplyWarmth(entity, dt);
            }
        }

        /// <summary>
        /// The actual heat. Runs after vanilla has already pulled the temperature down this
        /// second, so the two are a genuine tug of war - which is what lets a blizzard
        /// still win.
        /// </summary>
        private void ApplyWarmth(Entity entity, float dt)
        {
            EntityBehaviorBodyTemperature bt = entity.GetBehavior<EntityBehaviorBodyTemperature>();
            if (bt == null) return;

            float strength = Strength(entity);
            if (strength <= 0f) return;

            float current = bt.CurBodyTemperature;

            // Guard against the uninitialised case: the behavior reports 0 until its tree
            // exists, and treating 0 as "freezing" would slam the player to the ceiling.
            if (current <= 0f) return;

            float ceiling = config.CeilingTemperature;

            // Already warmer than we would ever make them - a hearth, a fire, a hot spring.
            // Stop, rather than clamping them down to our ceiling. An item that makes you
            // colder next to a fire would be a bug, not a balance decision.
            if (current >= ceiling) return;

            float target;

            if (config.Mode == "floor")
            {
                // Floor mode: hold them at or above a floor that climbs from
                // FloorTemperature toward normal as the ramp completes, and sinks back as
                // it tapers.
                float normal = bt.NormalBodyTemperature;
                float floorNow = config.FloorTemperature
                    + (normal - config.FloorTemperature) * strength;
                target = Math.Min(floorNow, ceiling);
                if (current >= target) return;
            }
            else
            {
                target = ceiling;
            }

            // Rate-limited in both modes, so floor mode rises gently instead of snapping,
            // and so a long tick (lag spike, 2s interval) is worth proportionally more.
            float add = config.BoostPerSecond * strength * dt;
            if (add <= 0f) return;

            bt.CurBodyTemperature = Math.Min(target, current + add);
        }

        // ---------------------------------------------------------------- events

        private void OnPlayerDeath(IServerPlayer player, DamageSource source)
        {
            // Vanilla's OnEntityRevive resets body temperature to normal+4 anyway, so
            // leaving the effect running would hand back a free warm respawn.
            if (player?.Entity == null) return;

            if (IsActive(player.Entity))
            {
                Clear(player.Entity);
                Tell(player, "emberheart:msg-died");
            }
            else
            {
                Clear(player.Entity);
            }
        }

        private void OnPlayerNowPlaying(IServerPlayer player)
        {
            if (player?.Entity == null) return;

            // Nothing to resume: the tick listener picks up any still-valid effect on its
            // own, because the state was saved with the player. This is only the courtesy
            // message, and the chance to tidy an expired tree left over from last session.
            double remaining = RemainingRealSeconds(player.Entity);

            if (remaining <= 0.0)
            {
                Clear(player.Entity);
                return;
            }

            Tell(player, "emberheart:msg-resumed", FormatRealSeconds((float)remaining));
        }

        // ---------------------------------------------------------------- output

        public string FormatRealSeconds(float seconds)
        {
            int total = (int)Math.Round(seconds);
            int mins = total / 60;
            int secs = total % 60;
            return mins > 0 ? mins + "m " + secs + "s" : secs + "s";
        }

        private void Tell(IServerPlayer player, string langKey, params object[] args)
        {
            player.SendMessage(GlobalConstants.InfoLogChatGroup,
                Lang.Get(langKey, args), EnumChatType.Notification);
        }
    }
}
