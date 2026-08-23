#!/usr/bin/env python3
"""WCAG contrast for the Hearthstone palette.

Warm-on-warm is the direction's stated risk, so every foreground/background pair that ships has
to be computed rather than eyeballed. 4.5:1 is the floor, including disabled text -- a control
faded until it is unreadable reads as a rendering fault at 0.95 m, not as unavailable.
"""

def lin(c):
    c /= 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def lum(h):
    h = h.lstrip("#")
    r, g, b = (int(h[i:i+2], 16) for i in (0, 2, 4))
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)

def ratio(fg, bg):
    a, b = lum(fg), lum(bg)
    hi, lo = max(a, b), min(a, b)
    return (hi + 0.05) / (lo + 0.05)

# Surfaces darkened from the direction sketch. Warm mid-tones need headroom above them, and a
# darker panel also throws less light at the eye in an unlit cave.
SURFACES = {
    "panel":   "#2A241E",
    "header":  "#1E1915",
    "control": "#372F27",
    "hover":   "#4A3F34",
    "muted":   "#241F1A",
    "void":    "#120F0B",
    "ember":   "#E2703A",
}

# Signal colours are all earth pigments so they stay in the family: ember (action), moss
# (confirmed), ochre (refused), oxide (rejected). Each ships in two values -- a fill and a
# lighter text tone -- because the fill values are too dark to read as type on a dark panel.
FOREGROUNDS = {
    "ink":         "#EDE2D0",
    "ink-soft":    "#CBB89B",
    "dim":         "#B9A88F",
    "disabled":    "#A5947C",
    "ember-text":  "#F5A374",
    "moss-text":   "#A3B472",
    "ochre-text":  "#E0B475",
    "oxide-text":  "#E08878",
    "on-ember":    "#201408",
}

PAIRS = [
    ("ink", "panel"), ("ink", "header"), ("ink", "control"), ("ink", "hover"), ("ink", "muted"),
    ("ink-soft", "panel"), ("ink-soft", "control"), ("ink-soft", "hover"),
    ("dim", "panel"), ("dim", "header"), ("dim", "control"), ("dim", "void"),
    ("disabled", "muted"), ("disabled", "panel"),
    ("ember-text", "panel"), ("ember-text", "header"), ("ember-text", "void"), ("ember-text", "control"),
    ("moss-text", "panel"), ("ochre-text", "panel"), ("oxide-text", "panel"),
    ("moss-text", "header"), ("ochre-text", "header"), ("oxide-text", "header"),
    ("on-ember", "ember"),
]

print(f"{'foreground':<10} on {'surface':<9} {'ratio':>6}   verdict")
print("-" * 46)
fails = []
for f, s in PAIRS:
    r = ratio(FOREGROUNDS[f], SURFACES[s])
    ok = r >= 4.5
    if not ok:
        fails.append((f, s, r))
    print(f"{f:<10} on {s:<9} {r:>6.2f}   {'ok' if ok else 'FAILS 4.5:1'}")

if fails:
    print("\nBelow the floor -- these need lightening before they ship:")
    for f, s, r in fails:
        print(f"  {f} on {s}: {r:.2f}")
else:
    print("\nEvery pair clears 4.5:1.")
