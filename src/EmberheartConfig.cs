namespace EmberHeart
{
    /// <summary>
    /// Everything tunable, serialised to ModConfig/emberheart.json.
    /// </summary>
    /// <remarks>
    /// On units: vanilla works in degrees per <em>in-game hour</em>. Its
    /// updateBodyTemperature applies tempChange * (hours elapsed since the last update),
    /// so the climate's pull on you scales with calendar speed. This mod deliberately
    /// works in degrees per <em>real second</em> instead, because the duration is a real
    /// wall-clock number the player feels ("six minutes of warmth"). At the default
    /// calendar - 60x speed, 24h days, so one in-game hour per real minute - the two
    /// scales differ by exactly 60: BoostPerSecond 0.10 is worth 6 C per in-game hour.
    /// That matters if you run a slowed or sped-up calendar: the cold scales, the
    /// Emberheart does not. See the README.
    /// </remarks>
    public class EmberheartConfig
    {
        /// <summary>
        /// "boost" adds heat per second and lets severe cold partly win.
        /// "floor" clamps body temperature to at least FloorTemperature instead.
        /// Anything unrecognised is treated as "boost".
        /// </summary>
        public string Mode = "boost";

        /// <summary>How long one Emberheart burns, in real seconds. 360 = 6 minutes.</summary>
        public float DurationSeconds = 360f;

        /// <summary>
        /// Degrees C added per real second at full strength, in boost mode.
        /// 0.10 is calibrated against vanilla's own numbers: vanilla clamps the climate
        /// term of tempChange to -6 C per in-game hour, which is -0.10 C per real second
        /// at the default calendar. So a full-strength Emberheart exactly cancels the
        /// worst the *climate* alone can do, and what can still beat it is wind and
        /// wetness - which is the balance the spec asked for.
        /// </summary>
        public float BoostPerSecond = 0.10f;

        /// <summary>Floor mode only: the temperature the effect holds you at or above.</summary>
        public float FloorTemperature = 36f;

        /// <summary>
        /// The effect never pushes you above this. It is a cap on what the Emberheart
        /// contributes, not a cap on your temperature: if a fire or a Great Hearth has
        /// already carried you past it, the Emberheart simply stops adding and never
        /// drags you back down.
        /// </summary>
        public float CeilingTemperature = 37.5f;

        /// <summary>Seconds to reach full strength after activation.</summary>
        public float RampUpSeconds = 25f;

        /// <summary>Fraction of the duration spent fading out at the end. 0.10 = last 10%.</summary>
        public float TaperFraction = 0.10f;

        /// <summary>
        /// True: using a second Emberheart while one burns resets the timer to full.
        /// False: the second one is refused and kept, with a message.
        /// Either way the heat never doubles.
        /// </summary>
        public bool StackingRefreshesTimer = true;

        /// <summary>How often the server recalculates, in milliseconds.</summary>
        public int TickIntervalMs = 1000;

        /// <summary>Seconds of warning before the taper begins, for the "cooling" message.</summary>
        public bool AnnounceTaper = true;

        /// <summary>Show the remaining-time readout above the hotbar.</summary>
        public bool ShowHud = true;

        /// <summary>Spawn ember particles on a player with an active Emberheart.</summary>
        public bool ShowParticles = true;

        /// <summary>
        /// Recipe amounts. Changing these rewrites nothing on disk - the grid recipes in
        /// assets/emberheart/recipes/grid/ hold the real quantities, and these values are
        /// only read to keep the tooltip honest. See the README for which file to edit.
        /// </summary>
        public int RecipeIronBits = 4;
        public int RecipeSalt = 2;
        public int RecipeCharcoalPowder = 2;
        public int RecipeSulfurPowder = 1;
        public int RecipeBeeswax = 1;
        public int RecipeOutputQuantity = 1;

        /// <summary>Clamps anything an admin has typed into the file into a workable range.</summary>
        public void Sanitise()
        {
            if (Mode == null) Mode = "boost";
            Mode = Mode.Trim().ToLowerInvariant();
            if (Mode != "boost" && Mode != "floor") Mode = "boost";

            if (DurationSeconds < 1f) DurationSeconds = 1f;
            if (BoostPerSecond < 0f) BoostPerSecond = 0f;
            if (RampUpSeconds < 0f) RampUpSeconds = 0f;

            // A ramp longer than the burn would mean the effect never reaches full
            // strength and the taper maths runs on a span that has already ended.
            if (RampUpSeconds > DurationSeconds) RampUpSeconds = DurationSeconds;

            if (TaperFraction < 0f) TaperFraction = 0f;
            if (TaperFraction > 0.9f) TaperFraction = 0.9f;

            // Vanilla hard-clamps body temperature to 31..45 in updateBodyTemperature.
            // Values outside that are not reachable, so accepting them would only make
            // the status readout lie.
            if (CeilingTemperature < 31f) CeilingTemperature = 31f;
            if (CeilingTemperature > 45f) CeilingTemperature = 45f;
            if (FloorTemperature < 31f) FloorTemperature = 31f;
            if (FloorTemperature > CeilingTemperature) FloorTemperature = CeilingTemperature;

            if (TickIntervalMs < 50) TickIntervalMs = 50;
            if (TickIntervalMs > 10000) TickIntervalMs = 10000;
        }
    }
}
