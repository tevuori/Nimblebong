#!/usr/bin/env python3
"""compare.py — side-by-side Nimblehold ref vs our render + metrics.

Usage:
    python3 tools/compare.py ref-01              # build montage + report
    python3 tools/compare.py --all               # all refs present
    python3 tools/compare.py ref-01 --score 72   # record manual score

Reads refs from docs/nimblehold/refs/ref-XX.png, ours from
docs/nimblehold/ours/ref-XX.png (rendered via RefProbe). Writes
docs/nimblehold/compare/ref-XX.png side-by-side montage.
Metrics (mean/median colors, palette match) are a compass only —
the scored artifact is the visual checklist in refs/ref-XX.md.
"""
import json
import math
import os
import sys

from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..", "docs", "nimblehold")
REFS = os.path.join(ROOT, "refs")
OURS = os.path.join(ROOT, "ours")
CMP = os.path.join(ROOT, "compare")
SCORES = os.path.join(ROOT, "scores.json")


def load_scores():
    try:
        return json.load(open(SCORES))
    except OSError:
        return {}


def palette(path, n=8):
    im = Image.open(path).convert("RGB")
    im.thumbnail((320, 320))
    q = im.quantize(colors=n).convert("RGB")
    cols = sorted(q.getcolors(), key=lambda c: -c[0])
    total = sum(c for c, _ in cols) or 1
    return [(round(c / total, 3), col) for c, col in cols]


def sat_lum(col):
    r, g, b = [c / 255 for c in col]
    mx, mn = max(r, g, b), min(r, g, b)
    lum = (mx + mn) / 2
    sat = 0 if mx == mn else (mx - mn) / (1 - abs(2 * lum - 1) or 1)
    return sat, lum


def report(name):
    ref = os.path.join(REFS, name + ".png")
    ours = os.path.join(OURS, name + ".png")
    if not os.path.exists(ref):
        print(f"{name}: missing ref")
        return None
    if not os.path.exists(ours):
        print(f"{name}: no render yet (docs/nimblehold/ours/{name}.png)")
        return None
    os.makedirs(CMP, exist_ok=True)
    a, b = Image.open(ref).convert("RGB"), Image.open(ours).convert("RGB")
    h = 760
    a = a.resize((int(a.width * h / a.height), h))
    b = b.resize((int(b.width * h / b.height), h))
    m = Image.new("RGB", (a.width + b.width + 8, h), (24, 20, 16))
    m.paste(a, (0, 0))
    m.paste(b, (a.width + 8, 0))
    out = os.path.join(CMP, name + ".png")
    m.save(out)

    print(f"=== {name}  (ref left | ours right)  -> {out}")
    rp, op = palette(ref), palette(ours)
    rs = sum(s for s, _ in (sat_lum(c) for _, c in rp)) / len(rp)
    os_ = sum(s for s, _ in (sat_lum(c) for _, c in op)) / len(op)
    rl = sum(l for _, l in (sat_lum(c) for _, c in rp)) / len(rp)
    ol = sum(l for _, l in (sat_lum(c) for _, c in op)) / len(op)
    print(f"  saturation avg: ref {rs:.2f} ours {os_:.2f}   "
          f"lightness avg: ref {rl:.2f} ours {ol:.2f}")
    print("  ref palette :", " ".join(f"#{c[0]:02x}{c[1]:02x}{c[2]:02x}x{f:.0%}"
                                     for f, c in rp[:6]))
    print("  ours palette:", " ".join(f"#{c[0]:02x}{c[1]:02x}{c[2]:02x}x{f:.0%}"
                                     for f, c in op[:6]))
    scores = load_scores()
    if name in scores:
        print(f"  recorded score: {scores[name]}%")
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--score" in sys.argv:
        i = sys.argv.index("--score")
        name, val = args[0], int(sys.argv[i + 1])
        scores = load_scores()
        scores[name] = val
        json.dump(scores, open(SCORES, "w"), indent=1)
        print(f"{name}: score {val}% recorded")
        return
    names = (
        sorted(f[:-4] for f in os.listdir(REFS) if f.endswith(".png"))
        if "--all" in sys.argv
        else args
    )
    for n in names:
        report(n)


if __name__ == "__main__":
    main()
