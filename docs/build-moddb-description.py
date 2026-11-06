#!/usr/bin/env python3
"""Builds the paste-ready ModDB description for Emberheart.

mods.vintagestory.at strips <style> blocks and ignores class= in a mod description, but it
keeps inline style="..." attributes. So a page that keeps its design in a stylesheet arrives
on the ModDB as a wall of unstyled text. This script emits the content with every rule
inlined, and wraps the same fragment in a standalone document for checking it in a browser.

    python3 docs/build-moddb-description.py

Writes:
    docs/emberheart-moddb.html   the fragment to paste into the ModDB description field
    docs/emberheart.html         the same fragment in a full page, for local preview

Size is the binding constraint. The site's editor re-serialises whatever you paste before it
measures it, and that rewrite inflates inline CSS by roughly 60%: every shorthand expands,
every #hex becomes rgb(r, g, b), and every declaration gains its spaces back. The ModDB
refused a 53KB file as "85KB". So the budget is this file's weight times about 1.6, and the
target here is 32KB generated, which lands near 50KB stored.

What that buys: no tables except the figure strip under the masthead - a cell carries its own
padding and border and both treble in the rewrite, so feature lists, recipes and commands are
plain <ul>. Styling sits on the parent wherever colour, line-height and font-size can
inherit. <code> is monospace in every browser without being told.

Other house rules:
  - Content lives here and nowhere else; both files are generated, so they cannot drift.
  - No backtick characters anywhere in the text, apostrophes are written &#39; as you go
    (never regex-swept - a sweep eats the f' that opens every f-string), and the AI
    disclosure stays last, verbatim.
  - No author byline and no source link: Jay links the source elsewhere on each mod page.

Derived from ~/Dev/Quartermaster/docs/build-moddb-description.py - the fleet template. Only
the accent colour changes: ember on parchment.
"""
import os
import re

PAPER   = "#F2EEE5"
SURFACE = "#FAF7F0"
INK     = "#1B1915"
INK2    = "#4A4539"
INK3    = "#7A746A"
RULE    = "#DCD5C6"
RULES   = "#BEB5A3"
EMBER   = "#B3421A"

# Short on purpose: a font stack is stamped into every element that names it.
BODY = "-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif"
DISP = "Georgia,'Times New Roman',serif"
MONO = "ui-monospace,Menlo,Consolas,monospace"

VERSION = "0.1.0"
GAME    = "1.22"

BUDGET = 32 * 1024


def p(text, mt="0", mb="14px", color=None, size=None):
    extra = f" font-size:{size};" if size else ""
    extra += f" color:{color};" if color else ""
    return f'<p style="margin:{mt} 0 {mb};{extra}">{text}</p>'


def h2(text):
    return (f'<h2 style="font-family:{DISP}; font-size:1.6em; line-height:1.15; margin:36px 0 14px;'
            f' padding-top:24px; border-top:1px solid {RULE};">{text}</h2>')


def h3(text):
    return f'<h3 style="font-family:{DISP}; font-size:1.15em; margin:24px 0 8px;">{text}</h3>'


def code(t):
    """Monospace comes from the browser; only the colour is worth a declaration."""
    return f'<code style="color:{EMBER};">{t}</code>'


def key(t):
    """A click or a keystroke. A bordered box costs three declarations that treble in the
    editor&#39;s rewrite, so it is set in bold monospace instead."""
    return f'<code style="font-weight:700;">{t}</code>'


def term(t):
    return f"<em>{t}</em>"


def readout(lines):
    """A crafting grid laid out as the game shows it - one box, however many rows."""
    inner = "<br>".join(lines)
    return (f'<div style="font-family:{MONO}; font-size:0.82em; color:{INK2}; background:{SURFACE};'
            f' border-left:3px solid {RULES}; padding:9px 12px; margin:12px 0; line-height:1.6;">'
            f'{inner}</div>')


def note(label, text):
    return (f'<div style="background:{SURFACE}; border-left:4px solid {EMBER}; padding:12px 16px;'
            f' margin:20px 0;">'
            f'<div style="font-family:{MONO}; font-size:0.68em; font-weight:700; letter-spacing:0.14em;'
            f' text-transform:uppercase; color:{EMBER}; margin-bottom:6px;">{label}</div>'
            f'<div>{text}</div></div>')


def bullets(items, mb="14px"):
    lis = "".join(f"<li>{i}</li>" for i in items)
    return (f'<ul style="margin:10px 0 {mb}; padding-left:22px; color:{INK2}; line-height:1.65;">'
            f"{lis}</ul>")


def specs(items):
    cells = []
    for n, unit, label in items:
        u = (f' <span style="font-size:0.5em; color:{INK3}; font-style:italic;">{unit}</span>'
             if unit else "")
        cells.append(
            f'<td style="padding:15px 14px; vertical-align:top; width:25%;">'
            f'<div style="font-family:{MONO}; font-size:1.8em; font-weight:600; line-height:1;">{n}{u}</div>'
            f'<div style="font-size:0.78em; color:{INK3}; margin-top:6px; line-height:1.4;">{label}</div></td>')
    return (f'<table style="width:100%; border-top:1px solid {RULES}; border-bottom:1px solid {RULES};'
            f' background:{SURFACE}; margin:0 0 28px; text-align:left;" cellspacing="0" cellpadding="0">'
            f'<tr>{"".join(cells)}</tr></table>')


def chips(words):
    return (f'<div style="font-family:{MONO}; font-size:0.78em; color:{INK3}; margin:14px 0 4px;">'
            + " &nbsp;&middot;&nbsp; ".join(words) + "</div>")


H = []
a = H.append

a(f'<div style="font-family:{BODY}; font-size:16px; line-height:1.75; color:{INK};'
  f' background:{PAPER}; max-width:860px; margin:0 auto; padding:30px 26px 34px;">')

# ---- masthead ----
a(f'<div style="font-family:{MONO}; font-size:0.68em; font-weight:600; letter-spacing:0.16em;'
  f' text-transform:uppercase; color:{EMBER}; margin-bottom:10px;">Vintage Story &middot; mod guide</div>')
a(f'<h1 style="font-family:{DISP}; font-size:2.9em; line-height:1; margin:0 0 12px;">Emberheart</h1>')
a(f'<p style="font-size:1.1em; line-height:1.6; color:{INK2}; max-width:34em; margin:0 0 18px;">'
  f'A single-use emergency warmer for when the cold is about to kill you. Crack one open and it '
  f'burns warm for minutes, holding your core temperature up while you get back to shelter.</p>')
a(f'<div style="font-family:{MONO}; font-size:0.76em; color:{INK3}; margin:0 0 26px;">v{VERSION} '
  f'&nbsp;&middot;&nbsp; built for Vintage Story {GAME} &nbsp;&middot;&nbsp; singleplayer or server</div>')

a(specs([("6", "minutes", "of warmth from one pellet, by default"),
         ("1", "use", "gone when it is spent, leaving a shell to reclaim"),
         ("2", "models", "boost lets the weather fight back, floor holds a minimum"),
         ("0", "Harmony patches", "body temperature moved through the public API")]))

a(note("In development",
       'Balance is still being tested in cold biomes and the numbers below may move before '
       'release. Feedback on how it feels in a real winter is welcome.'))

a(p('A sealed clay-and-wax pellet packed with ground iron, salt and charcoal. Hold right-click for '
    'a second to crack it open and it burns warm for minutes, holding your core temperature up while '
    'you get back to shelter. Expensive to make and gone when it is used &mdash; an emergency, not a '
    'habit.'))

# ---- how it works ----
a(h2("How it works"))
a(p(f'Hold {key("right mouse")} for about a second. The pellet cracks with a hiss, one is consumed, '
    f'and you are left holding a <b>Spent Emberheart Shell</b>. For the next six minutes or so the '
    f'Emberheart feeds warmth into your core body temperature, ramping up over the first half-minute '
    f'and fading out over the last tenth of its burn. Ember motes drift off you while it is lit, and a '
    f'small readout above the hotbar counts down the time left.'))
a(p(f'It is a {term("contest")}, not a cheat. The warmth is added on top of whatever the climate is '
    f'doing to you, so in ordinary cold it will carry you back toward normal and hold you there '
    f'&mdash; but a hard enough blizzard, or being soaked through in deep winter, can still out-pull '
    f'it. That is deliberate. An Emberheart buys you time to reach shelter; it is not a shelter.'))
a(p('It will never cook you either. The effect stops contributing once you reach a ceiling just above '
    'normal body temperature, and it never pulls you back down &mdash; so warming yourself at a fire '
    'or a Great Hearth while one is burning works exactly as you would expect.'))

# ---- crafting ----
a(h2("Crafting"))
a(p('<b>Emberheart Powder</b> (3&times;1 grid) &mdash; the grind step:'))
a(readout(["iron &nbsp;&middot;&nbsp; salt &times;2 &nbsp;&middot;&nbsp; charcoal powder &times;2"]))
a(p('Two accepted sources for the iron, so this is not gated behind a bloomery:'))
a(bullets([
    '<b>Iron ore nuggets &times;8</b> &mdash; limonite, hematite or magnetite. The loose ore stones '
    'that scatter on the surface above an iron deposit are picked up by hand, no tool required, and '
    'give 2&ndash;3 nuggets each. Available from day one.',
    '<b>or iron bits &times;4</b> &mdash; if you already run a bloomery, use smithing scrap instead '
    'and save yourself the walk.']))

a(p('<b>Emberheart</b> (2&times;2 grid) &mdash; the seal step:'))
a(readout(["emberheart powder &nbsp;&middot;&nbsp; fired bowl",
           "beeswax &nbsp;&middot;&nbsp; sulfur powder"]))
a(bullets([
    '<b>Fired bowl</b> &mdash; any colour; this becomes the shell',
    '<b>Beeswax</b> &mdash; the seal',
    '<b>Sulfur powder</b> &mdash; quern-ground from sulfur ore']))
a(p('Sulfur is the real gate. It spawns only in specific deposits and is already wanted for blasting '
    'powder and acid, so every Emberheart costs you something you would rather have kept. Nothing in '
    'the chain needs metalworking &mdash; a quern and a pit kiln are the only tools involved.'))

a(h3("Reclaiming shells"))
a(p('A 2&times;2 shapeless recipe turns four Spent Emberheart Shells back into one blue clay, and a '
    'pulverizer will crush a single shell for the same. Worth carrying home rather than dropping.'))

# ---- behaviour ----
a(h2("Behaviour"))
a(bullets([
    '<b>Duration</b> &mdash; about 6 real minutes by default (6 in-game hours at default calendar '
    'speed)',
    f'<b>Activation</b> &mdash; roughly 1 second of held {key("right-click")}, with a progress ring',
    '<b>Ramp</b> &mdash; climbs to full strength over about 25 seconds rather than snapping on',
    '<b>Taper</b> &mdash; fades out over the final 10% of the burn, with a warning message',
    f'<b>No stacking</b> &mdash; cracking a second one while the first still burns '
    f'{term("refreshes the timer")}. The heat never doubles.',
    '<b>Survives relog and server restart</b> &mdash; the effect is stored against world time, so '
    'logging out, restarting the server, or stopping for the night all resume correctly with the '
    'right time remaining, and it does not burn away while the server is offline.',
    '<b>Ends on death</b> &mdash; no free warm respawn.',
    '<b>Cannot be cracked open while swimming</b> &mdash; a soaked pellet is the whole reason it is '
    'sealed in clay.']))

# ---- config ----
a(h2("Configurable"))
a(p(f'An {code("emberheart.json")} config file, auto-generated in {code("ModConfig")}, exposes '
    f'everything: burn duration, warmth per second, the temperature ceiling, ramp-up time, taper '
    f'fraction, tick interval, and whether a second pellet refreshes the timer. The HUD readout and '
    f'the ember particles can each be switched off.'))
a(p(f'Two warming models are included. <b>Boost</b>, the default, adds heat per second and lets '
    f'severe weather fight back. <b>Floor</b> instead holds your core temperature at or above a set '
    f'minimum. Switch with one config value, or in game with '
    f'{code("/emberheart mode boost|floor")}.'))

# ---- commands ----
a(h2("Commands"))
a(bullets([
    f'{code("/emberheart status")} &mdash; your body temperature, which danger band it falls in, every '
    f'factor currently pushing it up or down, and what the Emberheart is contributing right now. '
    f'Useful on its own for understanding why you are freezing.',
    f'{code("/emberheart start")} / {code("stop")} &mdash; light or snuff one without spending an item '
    f'(admin).',
    f'{code("/emberheart mode boost|floor")} &mdash; switch models without a restart (admin).']))

# ---- compatibility ----
a(h2("Compatibility"))
a(bullets([
    f'Built for <b>Vintage Story {GAME}+</b>',
    'Universal &mdash; required on client and server, and safe for multiplayer. All warming is decided '
    'server-side.',
    '<b>No Harmony patches.</b> Body temperature is adjusted through the public API, so this will not '
    'fight other temperature mods over the same patched methods.',
    'Designed as a companion to <b>Great Hearths</b> &mdash; a hearth warms a room, an Emberheart '
    'warms the one person caught out in the open. Using both together is fine; warming at a hearth '
    'will not be capped or cancelled by a burning Emberheart.']))
a(chips(["no dependencies", "singleplayer or server", "no Harmony patches",
         f"built for Vintage Story {GAME}"]))

# ---- footer + disclosure ----
a(f'<div style="margin-top:32px; padding-top:16px; border-top:1px solid {RULES};">')
a(f'<p style="font-family:{MONO}; font-size:0.74em; color:{INK3}; margin:0 0 12px;">'
  f'Emberheart v{VERSION} &middot; built for Vintage Story {GAME}</p>')
a(f'<p style="font-size:0.88em; line-height:1.7; color:{INK3}; margin:0;">'
  f'AI Disclosure: This mod was developed with the assistance of AI tools, and its full source code is '
  f'linked on this page for anyone who wants to review it. If AI-assisted content isn&#39;t for you, please '
  f'filter or skip this mod. The comment section is reserved for bug reports, compatibility questions, and '
  f'gameplay feedback.</p>')
a("</div>")
a("</div>")

fragment = "\n".join(H)

# Whitespace inside a style attribute is only there for the reader of this script, and it is
# counted twice over once the site's editor rewrites the page. Nothing outside style="..." is
# touched, so the prose keeps its own semicolons and colons.
fragment = re.sub(r'style="([^"]*)"',
                  lambda m: 'style="' + re.sub(r"\s*([:;])\s*", r"\1", m.group(1)).rstrip(";") + '"',
                  fragment)

here = os.path.dirname(os.path.abspath(__file__))

moddb = os.path.join(here, "emberheart-moddb.html")
with open(moddb, "w") as f:
    f.write(fragment)

page = os.path.join(here, "emberheart.html")
with open(page, "w") as f:
    f.write("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
            "<title>Emberheart</title>\n<!-- Generated by docs/build-moddb-description.py "
            "- do not edit; edit the script. -->\n</head>\n"
            f'<body style="margin:0; background:{PAPER};">\n' + fragment + "\n</body>\n</html>\n")

# What the site measures is this file after its editor has expanded every shorthand and turned
# every #hex into rgb(r, g, b). Estimate that here rather than finding out on upload.
decls = sum(len([d for d in s.split(";") if d.strip()])
            for s in re.findall(r'style="([^"]*)"', fragment))
hexes = len(re.findall(r"#[0-9A-Fa-f]{6}", fragment))
borders = len(re.findall(r"border[a-z-]*:[^;\"]+", fragment))
pasted = len(fragment) + decls * 3 + hexes * 11 + borders * 60

print(f"written {moddb} {len(fragment)} bytes ({len(fragment) / 1024:.1f}KB)")
print(f"written {page}")
print(f"estimated once pasted: ~{pasted / 1000:.0f}KB - the ModDB counts that, not the file")
if len(fragment) > BUDGET:
    print(f"WARNING: over the {BUDGET // 1024}KB budget by {(len(fragment) - BUDGET) / 1024:.1f}KB. "
          f"Drop a whole aside, never add a table.")
