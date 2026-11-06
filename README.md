# Emberheart

A single-use, expensive emergency warming item for Vintage Story. Hold right mouse for a
second to crack one open; it burns for about six minutes and holds your core temperature
up while it does. Built to sit alongside [Great Hearths](https://github.com/joyedit/GreatHearths)
rather than replace it: a hearth warms a room, an Emberheart warms the one person caught
out in the open.

Requires Vintage Story 1.22.x (net10.0).

## What it does

Vanilla tracks body temperature in 'EntityBehaviorBodyTemperature'. Every real second the
server recomputes a 'tempChange' figure in **degrees per in-game hour** from the local
climate, your clothing, wetness, wind, and nearby heat sources, then moves your core
temperature by that much. Emberheart adds its own warmth to the same value from its own
one-second server tick, after vanilla has already pulled it down. The two are a genuine
tug of war, which is the whole point: a hard floor would make blizzards meaningless, so
instead severe cold can still out-pull the pellet.

Reference points from vanilla's own formula, worth knowing before you tune anything:

| Threshold | Value | What happens |
|---|---|---|
| Normal | 37 C | 'defaultBodyTemperature' |
| Freezing effect begins | below 35 C | 'freezingEffectStrength' goes positive, frost shader, shiver animation |
| Frost damage accrues | below 33 C | after 3 in-game hours of it, 0.2 damage every 10 real seconds |
| Hard floor | 31 C | vanilla clamps and will not go lower |
| Hard ceiling | 45 C | vanilla clamps and will not go higher |
| Dead zone | 'tempChange' in -0.5..0 | **discarded entirely** - mild cold does nothing at all |

That last row is the single most surprising thing in the vanilla model. At -5 C with even
light clothing your 'tempChange' is about -0.33/hour, which falls in the dead zone, so your
temperature never moves. Cold only starts to bite once 'ambient + clothing' drops below
about -3.

## Crafting

Two steps, and the costly part is deliberate.

**1. Grind step** (3x1 grid) -> 1 Emberheart Powder. Two accepted paths for the iron:
- **8x Iron Ore Nuggets** ('game:nugget-*', limonite / hematite / magnetite) - the
  early-game path. Loose ore stones scatter on the surface above any iron deposit and are
  right-click pickup with bare hands, 2 +/- 1 nuggets each. No bloomery, no pickaxe, no
  smelting.
- **or 4x Iron Bits** ('game:metalbit-iron') - the iron-age path, for anyone already
  running a bloomery. Fewer needed, since it is refined metal rather than ore.

plus, in both cases:
- 2x Salt ('game:salt') - catalyst
- 2x Charcoal Powder ('game:powder-charcoal') - carbon

**2. Seal step** (2x2 grid) -> 1 Emberheart
- 1x Emberheart Powder
- 1x Fired Bowl ('game:bowl-*-fired', any colour) - the shell
- 1x Beeswax ('game:beeswax') - the seal
- 1x Sulfur Powder ('game:powder-sulfur') - the rare one

Sulfur powder is the gate. It is quern-ground from 'ore-sulfur', which spawns only in
specific surface deposits and is already wanted for blasting powder and acid. Quartz was
the other candidate and was rejected: quartz veins are everywhere, so it would not have
cost anything real.

Deliberately, **nothing in the chain needs a bloomery**. The only tech requirements are a
quern (for the sulfur and charcoal powders) and a pit kiln (for the bowl), both of which
come long before metal. The cost is in legwork - finding sulfur, finding a hive, picking
ore off the ground - rather than in tech tier.

Using one leaves a **Spent Emberheart Shell**. Four shells recombine into one blue clay,
and a pulverizer will crush a single shell for the same, so they are worth carrying home.

To retune costs, edit the 'quantity' fields in
'assets/emberheart/recipes/grid/emberheartpowder.json' and '.../emberheart.json'. The
'Recipe*' numbers in the config file are read only for the tooltip; the recipe JSON is
authoritative.

## Config

'ModConfig/emberheart.json', written on first server start.

| Key | Default | Meaning |
|---|---|---|
| 'Mode' | '"boost"' | 'boost' adds heat per second; 'floor' clamps you to at least 'FloorTemperature'. Anything else is read as 'boost'. |
| 'DurationSeconds' | '360' | Burn time in real seconds. 6 minutes = 6 in-game hours at the default calendar. |
| 'BoostPerSecond' | '0.10' | Degrees C per real second at full strength, boost mode. |
| 'FloorTemperature' | '36' | Floor mode only: the temperature held. |
| 'CeilingTemperature' | '37.5' | The effect never contributes above this. |
| 'RampUpSeconds' | '25' | Seconds to reach full strength. |
| 'TaperFraction' | '0.10' | Fraction of the burn spent fading out at the end. |
| 'StackingRefreshesTimer' | 'true' | 'true': a second pellet resets the timer. 'false': it is refused and kept. Heat never doubles either way. |
| 'TickIntervalMs' | '1000' | Server recalculation interval. |
| 'AnnounceTaper' | 'true' | Send the "your Emberheart is cooling" message. |
| 'ShowHud' | 'true' | Remaining-time readout above the hotbar (Ctrl+E toggles). |
| 'ShowParticles' | 'true' | Ember motes on anyone with a lit Emberheart. |

### Why 'BoostPerSecond' is 0.10

Vanilla clamps the *climate* term of 'tempChange' to -6 C per in-game hour. At the default
calendar one in-game hour is one real minute, so -6/hour is -0.10 C per real second. A
full-strength Emberheart therefore exactly cancels the worst the climate alone can throw at
you, and what remains able to beat it is wind and wetness - which is exactly the balance
asked for.

### A calendar caveat

Vanilla's cooling is per in-game hour; Emberheart's warming is per real second. On a
default calendar ('SpeedOfTime' 60, 'CalendarSpeedMul' 1, so 24 real minutes per in-game
day) they are related by exactly 60. **If you run a slowed or accelerated calendar, the
cold scales and the Emberheart does not.** Halve your calendar speed and the Emberheart
becomes twice as strong relative to the cold. '/emberheart status' prints the current
conversion on its first line so you can see where you stand; multiply 'BoostPerSecond' by
your calendar multiplier to keep the balance as shipped.

## Commands

- '/emberheart' or '/emberheart status' - body temperature, the band it falls in, every
  term of vanilla's 'tempChange' broken out, and the Emberheart's current contribution.
  This is the measuring instrument: it prints the net rate directly, so you never have to
  time a drifting number.
- '/emberheart start' / 'stop' - light or snuff one without spending an item. Needs
  'controlserver'.
- '/emberheart mode boost|floor' - switch for this session without restarting. Not written
  to disk; edit the config file if you want it to stick.

## Implementation notes

- **No Harmony patches.** 'EntityBehaviorBodyTemperature.CurBodyTemperature' is a public
  property with a setter and 'NormalBodyTemperature' is a public field, so the warmth is
  applied through the normal API. That also means no patch conflict with Great Hearths,
  which does postfix 'getNearHeatSourceStrength'.
- **State lives in the player's 'WatchedAttributes'** under an 'emberheart' tree, as an
  expiry timestamp in **calendar hours** rather than real time. Calendar hours are saved
  with the world and only advance while it runs, so relog, server restart and a week of
  downtime all behave correctly without any extra code. It also syncs to clients for free,
  which is how the HUD and the particles work with no networking at all.
- **One server tick listener for everyone**, not one per player. A per-player listener has
  to be torn down on expiry, death, disconnect and shutdown, and any missed path leaks a
  closure holding a player reference. A single listener walking the online players has
  nothing to clean up, so there is nothing to forget.
- **The ceiling is a cap on contribution, not on temperature.** If a hearth has already
  carried you past 37.5 C the Emberheart stops adding and never drags you back down - an
  item that made you colder next to a fire would be a bug.
- Death clears the effect. Vanilla's 'OnEntityRevive' resets temperature to 41 C anyway, so
  leaving it running would only hand out a free warm respawn.
