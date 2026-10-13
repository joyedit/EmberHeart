using System;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EmberHeart
{
    /// <summary>
    /// /emberheart - the debug and testing surface.
    /// </summary>
    /// <remarks>
    /// "status" is the important one. Body temperature is almost impossible to reason about
    /// from inside the game: you watch a bar move and cannot tell whether the climate, the
    /// wind, your wet clothes or the Emberheart is responsible. This prints every term of
    /// vanilla's own tempChange formula alongside the mod's contribution, so a tester can
    /// read the net rate straight off instead of timing a drifting number with a stopwatch.
    /// </remarks>
    public static class EmberheartCommand
    {
        public static void Register(ICoreServerAPI api, EmberHeartModSystem mod)
        {
            IChatCommand root = api.ChatCommands
                .Create("emberheart")
                .WithDescription("Emberheart status and testing helpers")
                // Without an explicit privilege the command registers but then throws
                // "Incomplete command - no name or required privilege has been set".
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(args => Status(api, mod, args));

            root.BeginSubCommand("status")
                .WithDescription("Body temperature, the climate pull on it, and Emberheart state")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(args => Status(api, mod, args))
                .EndSubCommand();

            root.BeginSubCommand("start")
                .WithDescription("Light an Emberheart without consuming an item (testing)")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .HandleWith(args => Start(mod, args))
                .EndSubCommand();

            root.BeginSubCommand("stop")
                .WithDescription("End the active Emberheart effect immediately")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .HandleWith(args => Stop(mod, args))
                .EndSubCommand();

            root.BeginSubCommand("mode")
                .WithDescription("Switch between boost and floor for this session")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("mode"))
                .HandleWith(args => Mode(mod, args))
                .EndSubCommand();
        }

        private static TextCommandResult Status(ICoreServerAPI api, EmberHeartModSystem mod,
            TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("Players only.");

            Entity entity = player.Entity;
            EmberheartConfig cfg = mod.Config;
            EmberheartWarmthSystem warmth = mod.Warmth;

            EntityBehaviorBodyTemperature bt =
                entity.GetBehavior<EntityBehaviorBodyTemperature>();
            if (bt == null)
            {
                return TextCommandResult.Error(
                    "This entity has no bodytemperature behavior - nothing to report.");
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- Emberheart status ---");

            double hoursPerRealSec = warmth.HoursPerRealSecond();
            sb.AppendLine(string.Format(
                "Calendar: {0:0.##} in-game hours per real minute (speed {1:0.#} x {2:0.##})",
                hoursPerRealSec * 60.0, api.World.Calendar.SpeedOfTime,
                api.World.Calendar.CalendarSpeedMul));

            sb.AppendLine(string.Format("Body temperature: {0:0.00} C  (normal {1:0.#} C)",
                bt.CurBodyTemperature, bt.NormalBodyTemperature));

            // Vanilla's own hypothermia markers, read off updateBodyTemperature:
            //   freezing effect starts at normal-2 (35 C), frost damage accrues below
            //   normal-4 (33 C), and the value is hard-clamped to 31..45.
            float cur = bt.CurBodyTemperature;
            string band =
                cur >= bt.NormalBodyTemperature - 2f ? "fine"
                : cur > bt.NormalBodyTemperature - 4f ? "FREEZING (visual effect, no damage yet)"
                : "HYPOTHERMIC (frost damage accrues; hits after 3 in-game hours)";
            sb.AppendLine("Band: " + band);

            // ---- the pull on it, every term of vanilla's formula ----
            AppendClimatePull(api, entity, bt, sb, hoursPerRealSec);

            // ---- the mod's contribution ----
            sb.AppendLine("Mode: " + cfg.Mode
                + (cfg.Mode == "floor"
                    ? string.Format(" (floor {0:0.#} C)", cfg.FloorTemperature)
                    : string.Format(" ({0:0.000} C/real-sec at full strength)", cfg.BoostPerSecond)));
            sb.AppendLine(string.Format("Ceiling: {0:0.#} C   Ramp: {1:0.#}s   Taper: last {2:0.#}%",
                cfg.CeilingTemperature, cfg.RampUpSeconds, cfg.TaperFraction * 100f));

            double remaining = warmth.RemainingRealSeconds(entity);
            if (remaining <= 0.0)
            {
                sb.AppendLine("Active: no");
            }
            else
            {
                float strength = warmth.Strength(entity);
                double progress = warmth.Progress(entity);
                double remainingHours = warmth.RemainingHours(entity);

                string phase =
                    progress >= 1.0 - cfg.TaperFraction ? "tapering"
                    : strength < 1f ? "ramping up"
                    : "full strength";

                sb.AppendLine(string.Format(
                    "Active: YES - {0:0.#}s left ({1:0.##} in-game hours), {2:0.#}% elapsed, {3}",
                    remaining, remainingHours, progress * 100.0, phase));
                sb.AppendLine(string.Format(
                    "Strength: {0:0.###} -> contributing {1:+0.0000;-0.0000} C/real-sec ({2:+0.00;-0.00} C per in-game hour)",
                    strength,
                    cfg.BoostPerSecond * strength,
                    cfg.BoostPerSecond * strength / hoursPerRealSec));

                if (cur >= cfg.CeilingTemperature)
                {
                    sb.AppendLine(
                        "  (at or above the ceiling, so the Emberheart is adding nothing "
                        + "right now - it never pulls you back down)");
                }
            }

            return TextCommandResult.Success(sb.ToString());
        }

        /// <summary>
        /// Recomputes vanilla's tempChange term by term, exactly as
        /// EntityBehaviorBodyTemperature.updateBodyTemperature does it.
        /// </summary>
        /// <remarks>
        /// clothingBonus and inEnclosedRoom are private fields on the behavior with no
        /// accessor, so they are read reflectively. If a game update renames them the
        /// readout degrades to "unknown" and says so, rather than throwing - this is a
        /// diagnostic, and it must never be the thing that breaks.
        /// </remarks>
        private static void AppendClimatePull(ICoreServerAPI api, Entity entity,
            EntityBehaviorBodyTemperature bt, StringBuilder sb, double hoursPerRealSec)
        {
            BlockPos pos = entity.Pos.AsBlockPos;

            ClimateCondition climate = api.World.BlockAccessor
                .GetClimateAt(pos, EnumGetClimateMode.NowValues);
            if (climate == null)
            {
                sb.AppendLine("Climate: unavailable here.");
                return;
            }

            ITreeAttribute tempTree = entity.WatchedAttributes.GetTreeAttribute("bodyTemp");
            float nearHeat = tempTree?.GetFloat("nearHeatSourceStrength", 0f) ?? 0f;
            float wetness = entity.WatchedAttributes.GetFloat("wetness", 0f);
            Vec3d wind = api.World.BlockAccessor.GetWindSpeedAt(pos);

            float? clothing = ReadPrivateFloat(bt, "clothingBonus");
            bool? enclosed = ReadPrivateBool(bt, "inEnclosedRoom");
            float resistance = ReadResistance(api);

            sb.AppendLine(string.Format(
                "Climate: {0:0.#} C ambient | clothing {1} | wetness {2:0.00} | wind {3:0.00} | "
                + "nearHeatSourceStrength {4:0.00} | enclosed {5} | resistance {6:0.#}",
                climate.Temperature,
                clothing.HasValue ? clothing.Value.ToString("+0.#;-0.#") : "unknown",
                wetness, wind.Length(), nearHeat,
                enclosed.HasValue ? (enclosed.Value ? "yes" : "no") : "unknown",
                resistance));

            if (!clothing.HasValue || !enclosed.HasValue)
            {
                sb.AppendLine("  (cannot compute tempChange without those - field names changed?)");
                return;
            }

            float wetPenalty = Math.Max(0f, wetness - 0.1f) * 15f;
            float num4 = climate.Temperature + clothing.Value - wetPenalty;
            float num5 = num4 - GameMath.Clamp(num4, resistance, 50f);
            if (num5 == 0f) num5 = Math.Max(num4 - resistance, 0f);
            float num6 = GameMath.Clamp(num5 / 6f, -6f, 6f);

            float windChill = -(float)Math.Max((wind.Length() - 0.15) * 2.0, 0.0);
            float tempChange = nearHeat + (enclosed.Value ? 1f : windChill + num6);

            // The dead zone is the single most surprising thing in vanilla's formula: a
            // tempChange between -0.5 and 0 is thrown away entirely, so mild cold does
            // nothing at all. Worth printing, because it explains a flat baseline.
            float effective = tempChange;
            string note = "";
            if (!(tempChange < -0.5f || tempChange > 0f))
            {
                effective = 0f;
                note = "  <- inside vanilla's -0.5..0 dead zone, discarded";
            }
            else if (tempChange > 0.5f)
            {
                effective = tempChange * 2f;
                note = "  <- vanilla doubles anything above +0.5";
            }

            sb.AppendLine(string.Format(
                "Vanilla tempChange: {0:+0.00;-0.00} per in-game hour -> effective {1:+0.00;-0.00}{2}",
                tempChange, effective, note));
            sb.AppendLine(string.Format(
                "  = {0:+0.0000;-0.0000} C per real second  (climate term {1:+0.00;-0.00}, wind {2:+0.00;-0.00}, wet {3:-0.00;-0.00})",
                effective * hoursPerRealSec, num6, enclosed.Value ? 0f : windChill, -wetPenalty));
        }

        private static float ReadResistance(ICoreServerAPI api)
        {
            string raw = api.World.Config.GetString("bodyTemperatureResistance", null);
            float parsed;
            return float.TryParse(raw, out parsed) ? parsed : 0f;
        }

        private static float? ReadPrivateFloat(object target, string field)
        {
            object v = ReadPrivate(target, field);
            return v is float f ? f : (float?)null;
        }

        private static bool? ReadPrivateBool(object target, string field)
        {
            object v = ReadPrivate(target, field);
            return v is bool b ? b : (bool?)null;
        }

        private static object ReadPrivate(object target, string field)
        {
            try
            {
                FieldInfo fi = target.GetType().GetField(field,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return fi?.GetValue(target);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static TextCommandResult Start(EmberHeartModSystem mod,
            TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player == null) return TextCommandResult.Error("Players only.");

            return mod.Warmth.Activate(player)
                ? TextCommandResult.Success("Emberheart lit.")
                : TextCommandResult.Error(
                    "One is already burning and stackingRefreshesTimer is false.");
        }

        private static TextCommandResult Stop(EmberHeartModSystem mod,
            TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player?.Entity == null) return TextCommandResult.Error("Players only.");

            if (!mod.Warmth.IsActive(player.Entity))
            {
                return TextCommandResult.Success("Nothing was burning.");
            }

            mod.Warmth.Clear(player.Entity);
            return TextCommandResult.Success("Emberheart snuffed.");
        }

        private static TextCommandResult Mode(EmberHeartModSystem mod,
            TextCommandCallingArgs args)
        {
            string requested = args[0] as string;

            if (string.IsNullOrEmpty(requested))
            {
                return TextCommandResult.Success("Mode is " + mod.Config.Mode
                    + ". Use /emberheart mode boost|floor.");
            }

            requested = requested.Trim().ToLowerInvariant();
            if (requested != "boost" && requested != "floor")
            {
                return TextCommandResult.Error("Mode must be 'boost' or 'floor'.");
            }

            mod.Config.Mode = requested;

            // Deliberately not written to disk: this is a testing switch, and an admin who
            // wants it to stick should edit emberheart.json so a restart agrees with them.
            return TextCommandResult.Success("Mode is now " + requested
                + " for this session (not saved to emberheart.json).");
        }
    }
}
