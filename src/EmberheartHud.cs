using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace EmberHeart
{
    /// <summary>
    /// A one-line readout above the hotbar while an Emberheart burns: time left, and a
    /// marker once it starts to cool.
    /// </summary>
    /// <remarks>
    /// Costs nothing to run. The expiry timestamp is already synced to this client in the
    /// player's "emberheart" WatchedAttributes tree, so this reads one double twice a
    /// second and only redraws when the displayed text actually changes.
    /// </remarks>
    public class EmberheartHud : HudElement
    {
        public const string ToggleHotkey = "emberhearthud";

        public override string ToggleKeyCombinationCode => ToggleHotkey;

        private const string TextKey = "emberheart";

        /// <summary>Pixels down from the top of the screen, clearing a default minimap.</summary>
        private const int MinimapClearance = 330;

        private readonly EmberHeartModSystem mod;
        private string lastText = "";
        private long listenerId;

        public EmberheartHud(ICoreClientAPI capi, EmberHeartModSystem mod) : base(capi)
        {
            this.mod = mod;
        }

        public override void OnOwnPlayerDataReceived()
        {
            base.OnOwnPlayerDataReceived();

            Compose();
            TryOpen();

            if (listenerId == 0) listenerId = capi.Event.RegisterGameTickListener(OnTick, 500);
        }

        private void Compose()
        {
            ElementBounds text = ElementBounds.Fixed(0, 0, 210, 22);

            ElementBounds dialog = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightTop)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, MinimapClearance);

            SingleComposer = capi.Gui
                .CreateCompo("emberheart:hud", dialog)
                .AddDynamicText("",
                    CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right),
                    text, TextKey)
                .Compose();
        }

        private void OnTick(float dt)
        {
            if (!IsOpened()) return;

            double remaining = mod.RemainingRealSeconds(capi.World.Player?.Entity);

            // Nothing burning: blank the line rather than hiding the dialog, so there is no
            // layout flicker as it comes and goes.
            string text = remaining <= 0.0 ? "" : Render(remaining);

            if (text == lastText) return;
            lastText = text;

            SingleComposer.GetDynamicText(TextKey).SetNewText(text);
        }

        private string Render(double remaining)
        {
            int total = (int)Math.Round(remaining);
            string clock = (total / 60) + ":" + (total % 60).ToString("00");

            bool cooling = mod.Config.TaperFraction > 0f
                && remaining <= mod.Config.DurationSeconds * mod.Config.TaperFraction;

            return cooling
                ? Lang.Get("emberheart:hud-cooling", clock)
                : Lang.Get("emberheart:hud-active", clock);
        }

        public override void Dispose()
        {
            if (listenerId != 0)
            {
                capi.Event.UnregisterGameTickListener(listenerId);
                listenerId = 0;
            }
            base.Dispose();
        }
    }
}
