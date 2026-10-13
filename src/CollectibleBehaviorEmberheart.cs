using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EmberHeart
{
    /// <summary>
    /// Hold right mouse to crack an Emberheart open.
    /// </summary>
    /// <remarks>
    /// Structured after vanilla's CollectibleBehaviorHealingItem, which is the engine's own
    /// hold-to-use item: PreventDefault on start so no swing animation plays, a progress
    /// bar while held, and the payload applied in OnHeldInteractStop only once secondsUsed
    /// has actually reached the application time and only on the server.
    /// </remarks>
    public class CollectibleBehaviorEmberheart : CollectibleBehavior
    {
        private ICoreAPI api;
        private IProgressBar progressBar;

        /// <summary>Seconds of held RMB before it cracks.</summary>
        public float ApplicationTimeSec { get; private set; } = 1.0f;

        /// <summary>What is left in your hand afterwards. Null or empty leaves nothing.</summary>
        public string ShellCode { get; private set; } = "emberheart:emberheartshell";

        public AssetLocation ActivationSound { get; private set; }
            = new AssetLocation("game:sounds/sizzle");

        public float SoundRange { get; private set; } = 12f;

        public CollectibleBehaviorEmberheart(CollectibleObject collObj) : base(collObj)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            if (!properties.Exists) return;

            ApplicationTimeSec = properties["applicationTimeSec"].AsFloat(ApplicationTimeSec);
            ShellCode = properties["shellCode"].AsString(ShellCode);
            SoundRange = properties["soundRange"].AsFloat(SoundRange);

            string sound = properties["sound"].AsString(null);
            if (!string.IsNullOrEmpty(sound)) ActivationSound = new AssetLocation(sound);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            this.api = api;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel, bool firstEvent,
            ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            EmberHeartModSystem mod = api?.ModLoader.GetModSystem<EmberHeartModSystem>();
            EmberheartConfig config = mod?.Config;

            // Refusing a second one has to happen before the hold starts, or the player
            // spends a second cracking it and gets told no at the end.
            if (config != null && !config.StackingRefreshesTimer && mod.IsActiveClientside(byEntity))
            {
                if (api is ICoreClientAPI capi)
                {
                    capi.TriggerIngameError(this, "emberheartactive",
                        Lang.Get("emberheart:ingameerror-alreadyactive"));
                }
                return;
            }

            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventSubsequent;

            if (api is ICoreClientAPI clientApi)
            {
                ModSystemProgressBar bars = clientApi.ModLoader.GetModSystem<ModSystemProgressBar>();
                if (bars != null)
                {
                    bars.RemoveProgressbar(progressBar);
                    progressBar = bars.AddProgressbar();
                }
            }
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot,
            EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
            ref EnumHandling handling)
        {
            handling = EnumHandling.PreventSubsequent;

            float progress = ApplicationTimeSec <= 0f ? 1f : secondsUsed / ApplicationTimeSec;
            if (progressBar != null) progressBar.Progress = progress;

            // Cracking it open is a two-handed fiddle: being in water is the one state that
            // should stop you, since a soaked pellet is the whole reason this is a sealed
            // clay shell in the first place.
            if (byEntity.Swimming)
            {
                if (api is ICoreClientAPI capi)
                {
                    capi.TriggerIngameError(this, "emberheartswimming",
                        Lang.Get("emberheart:ingameerror-swimming"));
                }
                return false;
            }

            return progress < 1f;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot,
            EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
            EnumItemUseCancelReason cancelReason, ref EnumHandling handling)
        {
            RemoveProgressBar();
            return base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel,
                entitySel, cancelReason, ref handling);
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot,
            EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
            ref EnumHandling handling)
        {
            RemoveProgressBar();
            handling = EnumHandling.PreventSubsequent;

            if (secondsUsed < ApplicationTimeSec) return;
            if (byEntity.World.Side != EnumAppSide.Server) return;
            if (slot.Empty) return;

            EmberHeartModSystem mod = api.ModLoader.GetModSystem<EmberHeartModSystem>();
            EmberheartWarmthSystem warmth = mod?.Warmth;
            if (warmth == null) return;

            IServerPlayer player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
            if (player == null) return;

            // Activate first: if it refuses, the item is not spent.
            if (!warmth.Activate(player)) return;

            if (ActivationSound != null)
            {
                byEntity.World.PlaySoundAt(ActivationSound, byEntity, null, false,
                    SoundRange);
            }

            slot.TakeOut(1);
            slot.MarkDirty();

            GiveShell(byEntity);
        }

        private void GiveShell(EntityAgent byEntity)
        {
            if (string.IsNullOrEmpty(ShellCode)) return;

            Item shell = byEntity.World.GetItem(new AssetLocation(ShellCode));
            if (shell == null)
            {
                api.Logger.Warning("[Emberheart] shellCode '{0}' matched no item; no shell returned.",
                    ShellCode);
                return;
            }

            ItemStack stack = new ItemStack(shell);

            // A full inventory must not silently eat the shell.
            if (!byEntity.TryGiveItemStack(stack))
            {
                byEntity.World.SpawnItemEntity(stack, byEntity.Pos.XYZ.AddCopy(0, 0.5, 0));
            }
        }

        private void RemoveProgressBar()
        {
            if (progressBar == null) return;

            (api as ICoreClientAPI)?.ModLoader
                .GetModSystem<ModSystemProgressBar>()?.RemoveProgressbar(progressBar);
            progressBar = null;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc,
            IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            EmberheartConfig config = api?.ModLoader
                .GetModSystem<EmberHeartModSystem>()?.Config;
            if (config == null) return;

            int mins = (int)Math.Round(config.DurationSeconds / 60f);

            // Also stated in in-game hours, because every other warmth number the player
            // sees - vanilla's own tempChange, clothing warmth - is per in-game hour.
            double igHours = config.DurationSeconds
                * (world.Calendar.SpeedOfTime * world.Calendar.CalendarSpeedMul) / 3600.0;

            dsc.AppendLine(Lang.Get("emberheart:iteminfo-duration",
                mins, igHours.ToString("0.#")));

            dsc.AppendLine(config.Mode == "floor"
                ? Lang.Get("emberheart:iteminfo-floor",
                    config.FloorTemperature.ToString("0.#"))
                : Lang.Get("emberheart:iteminfo-boost",
                    config.BoostPerSecond.ToString("0.00"),
                    config.CeilingTemperature.ToString("0.#")));

            dsc.AppendLine(Lang.Get("emberheart:iteminfo-singleuse"));
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot,
            ref EnumHandling handling)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "emberheart:heldhelp-crack",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}
