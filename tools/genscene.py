#!/usr/bin/env python3
"""genscene.py — deterministic generators for ref-approximating scenes.

Each builder emits docs/nimblehold/scenes/ref-XX.json in the shared
scene format (SceneJson). Usage: python3 tools/genscene.py [ref-01 ...]
"""
import json
import math
import os
import sys

ROOT = os.path.join(os.path.dirname(__file__), "..", "docs", "nimblehold", "scenes")


def rng(seed):
    """Deterministic 0..1 hash noise — mirrors Rng.Noise in C#."""
    x = math.sin(seed * 12.9898 + 78.233) * 43758.5453
    return x - math.floor(x)


def blob(cx, cy, r, seed=1.0, n=24, squash=0.82, wob=0.16):
    pts = []
    for i in range(n):
        a = i / n * math.tau
        w = (1.0 + wob * math.sin(a * 2 + seed)
             + wob * 0.62 * math.sin(a * 3 + seed * 1.7)
             + wob * 0.44 * math.sin(a * 5 + seed * 2.3))
        pts.append([round(cx + math.cos(a) * r * w, 1),
                    round(cy + math.sin(a) * r * w * squash, 1)])
    return pts


def catmull(pts, n=80, wob=0.0, seed=1.0):
    """Resample a polyline through a centripetal-ish Catmull-Rom."""
    if len(pts) < 3:
        return [list(p) for p in pts]
    ext = [pts[0]] + list(pts) + [pts[-1]]
    out = []
    segs = len(pts) - 1
    for s in range(segs):
        p0, p1, p2, p3 = ext[s], ext[s + 1], ext[s + 2], ext[s + 3]
        steps = max(2, n // segs)
        for t in (i / steps for i in range(steps)):
            t2, t3 = t * t, t * t * t
            x = 0.5 * ((2 * p1[0]) + (-p0[0] + p2[0]) * t
                       + (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2
                       + (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3)
            y = 0.5 * ((2 * p1[1]) + (-p0[1] + p2[1]) * t
                       + (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2
                       + (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3)
            if wob:
                k = s * steps + t * steps
                x += wob * math.sin(k * 0.9 + seed)
                y += wob * math.sin(k * 1.3 + seed * 2)
            out.append([round(x, 1), round(y, 1)])
    out.append(list(pts[-1]))
    return out


def offset_line(pts, off):
    """Offset a polyline sideways by `off` px (screen-left of direction
    is -off because y grows down)."""
    out = []
    n = len(pts)
    for i, p in enumerate(pts):
        if i == 0:
            d = (pts[1][0] - p[0], pts[1][1] - p[1])
        elif i == n - 1:
            d = (p[0] - pts[i - 1][0], p[1] - pts[i - 1][1])
        else:
            d = (pts[i + 1][0] - pts[i - 1][0], pts[i + 1][1] - pts[i - 1][1])
        l = math.hypot(*d) or 1
        out.append((p[0] - d[1] / l * off, p[1] + d[0] / l * off))
    return out


def bankpoly(pts, gap, depth, side=1):
    """Closed ribbon hugging a river: rim edge `gap` px off the
    centerline, far edge `gap+depth` — a massif top whose wall band
    drops straight into the bank."""
    near = offset_line(pts, gap * side)
    far = offset_line(pts, (gap + depth) * side)
    return [[round(x, 1), round(y, 1)] for x, y in near + far[::-1]]


class Scene:
    def __init__(self, w, h, biome="woodland", mood="day",
                 artpack=None, season=None, name="ref"):
        self.d = {
            "version": 1, "name": name, "width": w, "height": h,
            "biome": biome, "mood": mood, "castShadows": True,
            "shapes": [], "strokes": [], "props": [], "fog": [],
            "fogCoverAll": False, "fogRooms": True,
        }
        if artpack:
            self.d["artPack"] = artpack
        if season:
            self.d["season"] = season
        self._id = 0

    def nid(self):
        self._id += 1
        return self._id

    def terrain(self, pts, height=30, size=46, rough=0, model=0, color=None):
        s = {"id": self.nid(), "kind": "terrain", "points": pts,
             "size": size, "height": height, "roughness": rough,
             "model": model}
        if color:
            s["color"] = color
        self.d["shapes"].append(s)

    def shape(self, kind, pts, model=0, size=0, height=0, rough=0, color=None):
        s = {"id": self.nid(), "kind": kind, "points": pts, "size": size,
             "height": height, "roughness": rough, "model": model}
        if color:
            s["color"] = color
        self.d["shapes"].append(s)

    def stroke(self, kind, pts, width, model=0):
        self.d["strokes"].append({"id": self.nid(), "kind": kind,
                                  "points": pts, "width": width,
                                  "model": model})

    def river(self, pts, w):
        self.stroke("river", catmull(pts, wob=3), w)

    def road(self, pts, w):
        self.stroke("road", catmull(pts, wob=2), w)

    @staticmethod
    def oakseed(x, y):
        """Seed that maps species pick to oak (index 0) but keeps full
        jitter variety: floor(seed*7919) % 3 == 0."""
        h = x * 0.37 + y * 0.53
        k = int(h * 7919) // 3
        return 3 * k / 7919 + (h - int(h)) / 7919

    def house(self, x, y, w=64, h=44, angle=0.0, model=0, kind="house",
              color=None):
        """Building footprint — InteriorPainter draws roof+wall ring."""
        ca, sa = math.cos(angle), math.sin(angle)
        pts = []
        for dx, dy in [(-w/2,-h/2),(w/2,-h/2),(w/2,h/2),(-w/2,h/2)]:
            pts.append([round(x+dx*ca-dy*sa,1), round(y+dx*sa+dy*ca,1)])
        s = {"id": self.nid(), "kind": kind, "points": pts,
             "size": 0, "height": 18, "roughness": 0, "model": model,
             "angle": round(angle, 3)}
        if color:
            s["color"] = color
        self.d["shapes"].append(s)

    def prop(self, kind, x, y, scale=24, rot=0.0, seed=None, model=-1,
             text=None, species=None):
        p = {"id": self.nid(), "kind": kind, "x": round(x, 1),
             "y": round(y, 1), "scale": scale, "rotation": round(rot, 2),
             "seed": seed if seed is not None else round(x * 0.37 + y * 0.53, 2)}
        if model >= 0:
            p["model"] = model
        if species:
            p["species"] = species
        if text:
            p["text"] = text
        self.d["props"].append(p)

    def scatter(self, region, kinds, step=40, density=0.8, smin=0.8, smax=1.2,
                seed=1.0, avoid=None, species=None):
        """Poisson-ish grid scatter inside polygon `region`."""
        xs = [p[0] for p in region]
        ys = [p[1] for p in region]
        y = min(ys)
        while y < max(ys):
            x = min(xs)
            while x < max(xs):
                k = x * 0.41 + y * 0.29 + seed * 7
                if rng(k) < density:
                    px = x + (rng(k + 1) - 0.5) * step
                    py = y + (rng(k + 2) - 0.5) * step
                    if inside(px, py, region) and not (
                            avoid and inside(px, py, avoid)):
                        kind = pick(kinds, rng(k + 3))
                        sd = Scene.oakseed(px, py) if kind == "tree" else None
                        self.prop(kind, px, py, seed=sd, species=species,
                                  scale=round(24 * (smin + rng(k + 4) * (smax - smin)), 1),
                                  rot=(rng(k + 5) - 0.5) * 0.7)
                x += step
            y += step

    def save(self, name):
        self.d["nextId"] = self._id + 1
        path = os.path.join(ROOT, name + ".json")
        json.dump(self.d, open(path, "w"))
        print("wrote", path)


def inside(x, y, poly):
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-9) + xi:
            c = not c
        j = i
    return c


def pick(weighted, roll):
    if isinstance(weighted, str):
        return weighted
    total = sum(w for _, w in weighted)
    acc = 0
    for k, w in weighted:
        acc += w / total
        if roll <= acc:
            return k
    return weighted[-1][0]


# ─────────────────────────────────────────────────────────────────
# Builders — one per ref. Compositions mirror refs/ref-XX.png.


def ref01():
    """River gorge: teal river, twin cliff walls, plank bridge, stairs."""
    s = Scene(1440, 768, name="ref-01")
    # River W→E diagonal through the channel.
    river = [(60, 700), (260, 560), (420, 460), (620, 400), (760, 330),
             (900, 290), (1100, 200), (1330, 90), (1460, 30)]
    s.river(river, 96)
    # Gorge walls hugging both banks — ribbon massifs whose rim runs
    # just off the shoreline so the stone face drops to the water.
    s.terrain(bankpoly(river, 150, 240, side=1), height=24, size=72,
              rough=60)
    s.terrain(bankpoly(river, 150, 240, side=-1), height=26, size=76,
              rough=60)
    # Dirt path approaching the bridge from S, continuing N.
    s.road([(620, 800), (640, 620), (700, 470), (780, 380),
            (880, 250), (960, 90), (990, -40)], 26)
    # Rocks in/around the river.
    for i, (x, y) in enumerate([(200, 590), (250, 560), (520, 430),
                                (840, 310), (1000, 250), (1150, 175),
                                (1240, 130)]):
        s.prop("rock", x, y, scale=14 + (i % 3) * 4)
    # Big inked trees along both banks.
    forest_n = blob(600, 40, 560, 5.5, n=30, squash=0.24, wob=0.14)
    forest_s = blob(760, 820, 620, 7.7, n=30, squash=0.24, wob=0.14)
    s.scatter(forest_n, [("tree", 4), ("bush", 1), ("pine", 1)],
              step=95, density=0.62, smin=2.0, smax=3.0, seed=11)
    s.scatter(forest_s, [("tree", 4), ("bush", 1.4)],
              step=95, density=0.66, smin=2.0, smax=3.0, seed=23)
    # A few hero trees close to the river.
    for x, y, sc in [(350, 480, 2.4), (560, 350, 2.0), (960, 330, 2.2),
                     (1120, 260, 2.0), (300, 640, 2.3)]:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y))
    s.save("ref-01")


def ref02():
    """Terraces + river + dirtpath + stone circle."""
    s = Scene(1180, 756, name="ref-02")
    # Terrace chain top-left and bottom-right.
    s.terrain(blob(300, 120, 380, 2.2, n=30, squash=0.5, wob=0.14),
              height=24, size=52, rough=45)
    s.terrain(blob(320, 130, 240, 4.8, n=26, squash=0.5, wob=0.14),
              height=52, size=48, rough=60)
    s.terrain(blob(880, 560, 420, 6.6, n=30, squash=0.55, wob=0.12),
              height=22, size=50, rough=40)
    s.terrain(blob(900, 570, 250, 8.9, n=26, squash=0.55, wob=0.12),
              height=48, size=46, rough=55)
    s.terrain(blob(950, 140, 300, 1.7, n=26, squash=0.5, wob=0.12),
              height=30, size=52, rough=50)
    # River along the top.
    s.river([(-40, 40), (240, 60), (480, 30), (700, 70),
             (950, 50), (1220, 90)], 70)
    # Dirt path weaving between terraces.
    s.road([(-30, 560), (200, 480), (420, 520), (560, 430),
            (700, 360), (760, 260), (700, 160), (760, 60)], 22)
    s.road([(560, 430), (760, 480), (980, 420), (1200, 380)], 18)
    # Stone circle at center.
    cx, cy, r = 560, 330, 90
    s.prop("stonecircle", cx, cy, scale=24 * 3.6)
    for i in range(10):
        a = i / 10 * math.tau
        s.prop("rock", cx + math.cos(a) * r, cy + math.sin(a) * r * 0.8,
               scale=10 + (i % 3) * 3)
    # Trees.
    s.scatter(blob(180, 560, 260, 12.3, n=24, squash=0.7), "tree",
              step=58, density=0.85, smin=1.7, smax=2.8, seed=31)
    s.scatter(blob(700, 640, 340, 14.1, n=24, squash=0.5), "tree",
              step=58, density=0.75, smin=1.6, smax=2.6, seed=37)
    s.scatter(blob(1080, 300, 200, 16.9, n=20, squash=0.9), "tree",
              step=55, density=0.8, smin=1.7, smax=2.7, seed=41)
    s.scatter(blob(450, 200, 240, 43.1, n=20, squash=0.7), "tree",
              step=58, density=0.75, smin=1.6, smax=2.5, seed=45)
    for x, y in [(140, 320), (260, 380), (60, 240)]:
        s.prop("tree", x, y, scale=24 * 2.5, seed=Scene.oakseed(x, y))
    s.save("ref-02")


def ref03():
    """Caravan camp: wagons, tents, campfire; cliff W, river E."""
    s = Scene(1536, 783, name="ref-03")
    s.terrain(blob(140, 420, 420, 4.4, n=30, squash=0.75, wob=0.12),
              height=26, size=58, rough=50)
    s.river([(1240, -40), (1180, 160), (1210, 380), (1160, 560),
             (1200, 780)], 90)
    # Roads: sandy paths crossing the meadow.
    s.road([(-40, 160), (300, 220), (620, 260), (900, 300), (1180, 330)], 20)
    s.road([(500, 780), (560, 560), (640, 420), (700, 300)], 16)
    s.road([(900, 300), (860, 520), (820, 700)], 14)
    # Wagons + tents + campfire cluster.
    s.prop("cart", 560, 240, scale=96, rot=0.5)
    s.prop("cart", 780, 270, scale=92, rot=-2.6)
    s.prop("cart", 470, 480, scale=88, rot=1.2)
    s.prop("cart", 880, 520, scale=88, rot=-0.8)
    s.prop("tent", 660, 380, scale=80, rot=0.9)
    s.prop("tent", 540, 560, scale=74, rot=-0.4)
    s.prop("tent", 940, 410, scale=76, rot=2.4)
    s.prop("campfire", 700, 490, scale=56)
    for x, y in [(620, 300), (840, 350), (740, 560), (960, 470)]:
        s.prop("crate", x, y, scale=44, rot=rng(x + y) * 3)
    for x, y in [(590, 340), (910, 300)]:
        s.prop("barrel", x, y, scale=42)
    for x, y in [(640, 470), (780, 440), (860, 560), (560, 500)]:
        s.prop("crate", x, y, scale=40, rot=rng(x * y) * 2)
    s.prop("bones", 760, 620, scale=36)
    s.prop("well", 680, 620, scale=34)
    # Trees — border forest + big heroes.
    s.scatter(blob(1300, 500, 360, 18.5, n=24, squash=1.0), "tree",
              step=90, density=0.7, smin=1.8, smax=2.8, seed=51,
              avoid=blob(1200, 420, 200, 19.9))
    s.scatter(blob(700, 720, 700, 20.2, n=30, squash=0.25), "tree",
              step=100, density=0.55, smin=1.8, smax=2.7, seed=53)
    s.scatter(blob(760, 40, 700, 22.7, n=30, squash=0.3), "tree",
              step=100, density=0.5, smin=1.8, smax=2.7, seed=57)
    for x, y, sc in [(320, 600, 2.2), (380, 160, 2.0), (1080, 620, 2.1),
                     (1050, 150, 2.3)]:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y))
    # Rocks at cliff foot + river stones.
    for x, y in [(240, 620), (300, 660), (170, 700), (1150, 200),
                 (1230, 250), (1180, 600)]:
        s.prop("rock", x, y, scale=16)
    s.save("ref-03")


def ref04():
    """Woodland market — thatch stalls, winding paths, cliff ridges."""
    s = Scene(1748, 950, name="ref-04")
    # White rocky ridges — a long broken terrace east, a small mound
    # centre, a low ledge south-west. Stairs appear where paths cross.
    ridge1 = catmull([(980, -30), (1010, 120), (960, 300), (1010, 480),
                      (960, 620)], n=40, wob=0.06, seed=5.1)
    s.terrain(bankpoly(ridge1, -80, 160), height=20, size=54, rough=55)
    ridge2 = catmull([(680, -40), (700, 140), (660, 320), (700, 500)],
                     n=40, wob=0.06, seed=7.7)
    s.terrain(bankpoly(ridge2, -60, 120), height=16, size=46, rough=50)
    s.terrain(blob(580, 620, 150, 11.2, n=22, squash=0.85, wob=0.12),
              height=18, size=44, rough=48)
    s.terrain(blob(1200, 780, 170, 13.6, n=22, squash=0.8, wob=0.12),
              height=22, size=48, rough=50)
    # River south-east corner with a bridge where the path crosses.
    s.river([(1250, 990), (1240, 820), (1340, 660), (1300, 540),
             (1360, 420), (1340, 300), (1420, 200), (1400, 60)], 74)
    # Winding dirt path network — the market lanes.
    s.road([(-40, 260), (240, 300), (480, 380), (700, 420), (960, 460),
            (1240, 520), (1330, 610), (1420, 660), (1560, 680)], 20)
    s.road([(350, -40), (380, 160), (480, 380), (560, 600), (620, 840),
            (700, 990)], 16)
    s.road([(480, 380), (760, 300), (1000, 260), (1260, 240),
            (1500, 220)], 14)
    s.road([(960, 460), (980, 640), (1060, 800), (1180, 900)], 12)
    # Market stalls — big thatched rectangles NW + a second cluster E.
    for i, (x, y, w, h, a) in enumerate([
            (60, 60, 150, 90, -0.1), (150, 180, 140, 85, 0.15),
            (30, 300, 130, 80, -0.05), (220, 60, 120, 75, 0.2),
            (260, 230, 120, 70, -0.25), (80, 420, 110, 70, 0.1),
            (1140, 60, 150, 95, 0.35), (1290, 90, 130, 80, 0.3)]):
        s.house(x, y, w, h, a, model=2, color="#b7a955")
    # Market clutter — stall props, carts, crates, tables.
    for i, (x, y) in enumerate([(330, 150), (180, 360), (300, 420),
                                (1050, 130), (1220, 200)]):
        s.prop("stall", x, y, scale=70 + (i % 3) * 10, rot=rng(i * 11) - 0.5,
               model=i % 3)
    for x, y in [(420, 480), (520, 470), (640, 500), (840, 430),
                 (1080, 500), (240, 500)]:
        s.prop("table", x, y, scale=34, rot=rng(x) * 1.2 - 0.6)
    for x, y, a in [(520, 560, 0.4), (700, 560, -0.9), (900, 380, 0.2),
                    (1150, 300, 1.2)]:
        s.prop("cart", x, y, scale=88, rot=a)
    for x, y in [(600, 460), (740, 380), (380, 440), (1010, 420),
                 (1300, 160)]:
        s.prop("crate", x, y, scale=40)
    s.prop("well", 700, 470, scale=42)
    s.prop("lantern", 640, 430, scale=30)
    s.prop("lantern", 760, 500, scale=30)
    # Red-roof homes south-west + south-east (as in the ref).
    for i, (x, y, a) in enumerate([(200, 830, -0.3), (330, 870, 0.2),
                                   (260, 900, 0.6), (1180, 880, -0.4)]):
        s.house(x, y, 80, 54, a)
    # Scattered woodland — sparse clumps with wide grass clearings
    # between the lanes, like the ref's open market commons.
    s.scatter(blob(600, 150, 300, 41.2, n=20, squash=0.6), "tree",
              step=110, density=0.65, smin=1.5, smax=2.6, seed=61)
    s.scatter(blob(1500, 450, 280, 45.3, n=20, squash=0.8), "tree",
              step=105, density=0.6, smin=1.5, smax=2.5, seed=63)
    s.scatter(blob(250, 650, 280, 47.8, n=18, squash=0.7), "tree",
              step=105, density=0.6, smin=1.5, smax=2.4, seed=67)
    s.scatter(blob(1000, 700, 300, 51.4, n=18, squash=0.7), "tree",
              step=100, density=0.6, smin=1.5, smax=2.4, seed=69)
    s.scatter(blob(1050, 200, 240, 55.1, n=16, squash=0.6), "tree",
              step=100, density=0.55, smin=1.4, smax=2.3, seed=71)
    s.save("ref-04")


def ref05():
    """Desert canyon + oasis lagoon + round tower interior."""
    s = Scene(1881, 788, name="ref-05", biome="desert")
    # Canyon ring: big massif center, lower dunes.
    s.terrain(blob(950, 420, 520, 5.7, n=34, squash=0.6, wob=0.10),
              height=20, size=58, rough=35)
    s.terrain(blob(960, 430, 330, 7.9, n=28, squash=0.6, wob=0.10),
              height=44, size=62, rough=45)
    s.terrain(blob(1700, 200, 360, 9.5, n=26, squash=0.55, wob=0.12),
              height=24, size=60, rough=40)
    s.terrain(blob(200, 750, 320, 11.3, n=24, squash=0.4, wob=0.12),
              height=18, size=55, rough=38)
    # Oasis lagoon west.
    s.shape("pond", blob(80, 380, 340, 4.2, n=28, squash=0.9, wob=0.08))
    # Tower (round room) at canyon top.
    cx, cy, r = 950, 420, 200
    pts = [[round(cx + math.cos(i / 26 * math.tau) * r, 1),
            round(cy + math.sin(i / 26 * math.tau) * r, 1)]
           for i in range(26)]
    s.shape("room", pts, model=0)
    s.prop("table", cx - 20, cy - 30, scale=26, rot=0.3)
    s.prop("table", cx + 60, cy + 50, scale=24, rot=-0.6)
    s.prop("barrel", cx - 90, cy + 60, scale=20)
    s.prop("barrel", cx - 60, cy + 80, scale=20)
    s.prop("crate", cx + 90, cy - 70, scale=20, rot=0.4)
    s.prop("rug", cx, cy + 20, scale=30)
    s.prop("shelf", cx - 10, cy - 110, scale=22, rot=math.pi)
    s.prop("bed", cx + 80, cy + 90, scale=24, rot=-0.9)
    s.prop("fireplace", cx - 100, cy - 60, scale=22)
    # Palms + desert flora.
    for x, y, sc in [(620, 300, 3.0), (530, 500, 2.6), (750, 620, 2.8),
                     (1250, 260, 3.0), (1330, 560, 2.6), (300, 240, 2.4),
                     (420, 180, 2.2), (480, 420, 2.8), (1350, 400, 2.6),
                     (200, 480, 2.4), (1500, 300, 2.4)]:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y))
    for x, y in [(700, 200), (1150, 640), (550, 680), (1400, 420)]:
        s.prop("bush", x, y, scale=24 * 1.8)
    # Boulders.
    for i, (x, y) in enumerate([(760, 120), (820, 150), (1200, 680),
                                (1280, 720), (1600, 500), (250, 550),
                                (330, 590)]):
        s.prop("rock", x, y, scale=14 + (i % 4) * 4)
    s.save("ref-05")


def ref06():
    """Hilltop graveyard — stone plot rows, ossuary mound, dead trees."""
    s = Scene(1228, 685, name="ref-06")
    # Low pale terraces like the ref's grassy mounds.
    s.terrain(blob(300, 160, 210, 2.9, n=26, squash=0.8, wob=0.10),
              height=14, size=44, rough=30)
    s.terrain(blob(880, 140, 230, 4.5, n=26, squash=0.8, wob=0.10),
              height=16, size=46, rough=32)
    s.terrain(blob(430, 540, 200, 7.8, n=26, squash=0.75, wob=0.10),
              height=14, size=44, rough=28)
    s.terrain(blob(950, 480, 190, 9.3, n=24, squash=0.8, wob=0.10),
              height=12, size=42, rough=26)
    # Ossuary mound centre — stone circle, skull fountain, bone piles.
    s.prop("stonecircle", 610, 330, scale=24 * 2.4)
    s.prop("fountain", 610, 330, scale=38)
    s.prop("bones", 580, 300, scale=40)
    s.prop("bones", 650, 360, scale=36)
    s.prop("pillar", 560, 350, scale=40, rot=0.3)
    s.prop("pillar", 665, 315, scale=36, rot=-0.5)
    # Grave plot rows — small stone-edged rectangles everywhere,
    # loosely following the lane directions like the ref.
    plots = [
        (80, 120), (140, 110), (200, 130), (90, 230), (160, 240),
        (220, 220), (60, 340), (130, 350), (700, 60), (770, 70),
        (840, 60), (910, 80), (980, 70), (760, 160), (830, 170),
        (900, 160), (1020, 140), (1090, 150), (60, 500), (130, 520),
        (200, 500), (700, 520), (770, 540), (840, 520), (910, 540),
        (1000, 300), (1070, 320), (1130, 300), (990, 400), (1060, 420),
        (1130, 410), (280, 90), (340, 100), (150, 600), (220, 620),
    ]
    for i, (x, y) in enumerate(plots):
        w, h = 58 + (i % 3) * 8, 30 + (i % 2) * 8
        pts = [[x, y], [x + w, y], [x + w, y + h], [x, y + h], [x, y]]
        s.stroke("wall", pts, 7)
        if i % 3 == 0:
            s.prop("grave", x + w * 0.3, y + h * 0.5, scale=26)
    # Coffins + leaning gravestones near the mound.
    s.prop("grave", 480, 250, scale=30, rot=0.4)
    s.prop("grave", 520, 220, scale=28, rot=-0.7)
    s.prop("grave", 700, 400, scale=30, rot=1.1)
    s.prop("chest", 560, 260, scale=30, rot=0.5)  # coffin stand-in
    s.prop("bones", 460, 350, scale=32)
    s.prop("ruin", 690, 300, scale=38, rot=0.6)
    # Dirt paths crossing the yard.
    s.road([(-30, 420), (220, 380), (480, 340), (700, 300), (980, 260),
            (1260, 240)], 16)
    s.road([(620, -30), (630, 160), (610, 330), (640, 500), (660, 690)], 14)
    s.road([(80, 690), (240, 560), (480, 460), (610, 330)], 12)
    # Bare + broadleaf trees dotting the mounds.
    for i, (x, y) in enumerate([(240, 150), (400, 190), (880, 170),
                                (1000, 200), (350, 480), (500, 560),
                                (1100, 480), (150, 440), (760, 120),
                                (560, 120), (960, 580), (60, 250),
                                (430, 620), (1050, 60), (300, 320)]):
        s.prop("tree", x, y, scale=24 * (1.5 + (i % 3) * 0.3),
               seed=Scene.oakseed(x, y), model=i % 4)
    s.prop("statue", 610, 290, scale=42)
    s.save("ref-06")


def ref07():
    """Village + river fork + waterfalls + bridges (portrait)."""
    s = Scene(1080, 1421, name="ref-07")
    # Terraces across the middle — river crosses their edges → falls.
    s.terrain(blob(540, 640, 560, 6.4, n=32, squash=0.35, wob=0.10),
              height=22, size=56, rough=45)
    s.terrain(blob(540, 1000, 600, 8.2, n=32, squash=0.32, wob=0.10),
              height=22, size=56, rough=45)
    # River: single channel N, forks around a mid island, joins S.
    s.river([(420, -40), (430, 200), (460, 420), (440, 560)], 66)
    s.river([(440, 560), (300, 720), (240, 900), (280, 1100), (340, 1300),
             (360, 1460)], 60)
    s.river([(440, 560), (640, 700), (700, 900), (640, 1100),
             (560, 1280), (520, 1460)], 60)
    # Roads crossing rivers at bridges.
    s.road([(60, 300), (300, 340), (420, 380), (620, 360), (900, 330),
            (1100, 340)], 20)
    s.road([(80, 1150), (280, 1120), (420, 1180), (700, 1160),
            (1040, 1180)], 18)
    # Village — thatch homes hugging the banks.
    homes = [(200, 200, 2.0), (320, 160, 2.2), (150, 480, 1.9),
             (660, 200, 2.0), (800, 250, 1.8), (920, 430, 1.9),
             (760, 600, 1.8), (160, 800, 1.8), (850, 850, 2.0),
             (350, 1250, 1.9), (700, 1300, 1.8),
             # Fork cluster — the thatch knot on the island banks.
             (430, 560, 2.1), (370, 700, 2.0), (300, 620, 1.9),
             (780, 720, 2.0), (860, 640, 1.9), (240, 900, 2.0),
             (900, 920, 1.9), (380, 940, 2.1)]
    for i, (x, y, sc) in enumerate(homes):
        s.house(x, y, 52 * sc, 38 * sc, rng(i * 11) * 1.2 - 0.6,
                model=2 if i % 4 else 0, color="#b7a955")
    for x, y in [(260, 260), (700, 320), (180, 560), (800, 520),
                 (400, 1200), (760, 1250)]:
        s.prop("crate", x, y, scale=24)
    s.prop("well", 500, 240, scale=30)
    # Trees sprinkled.
    s.scatter(blob(950, 200, 220, 15.3, n=20, squash=0.8), "tree",
              step=85, density=0.65, smin=1.7, smax=2.6, seed=81)
    s.scatter(blob(120, 1250, 220, 17.5, n=20, squash=0.8), "tree",
              step=85, density=0.7, smin=1.7, smax=2.6, seed=83)
    s.scatter(blob(540, 960, 180, 19.8, n=18, squash=0.9), "tree",
              step=80, density=0.6, smin=1.6, smax=2.4, seed=87)
    s.save("ref-07")


def ref08():
    """River canyon + rapid + jungle camp (portrait)."""
    s = Scene(1080, 1920, name="ref-08")
    # River N→S center-right.
    river = [(560, -40), (580, 300), (540, 620), (600, 900),
             (560, 1200), (620, 1500), (580, 1960)]
    s.river(river, 110)
    # Canyon walls hugging both banks — ribbon massifs whose rim runs
    # just off the shoreline so the stone face drops to the water.
    s.terrain(bankpoly(river, 175, 420, side=1), height=22, size=90,
              rough=60)
    s.terrain(bankpoly(river, 175, 420, side=-1), height=22, size=90,
              rough=60)
    s.terrain(blob(200, 1600, 420, 7.4, n=26, squash=0.8, wob=0.10),
              height=18, size=56, rough=50)
    # Jungle: dense forced palms hugging both canyon rims + fern
    # undergrowth — the ref packs the tops edge-to-edge.
    s.scatter(blob(280, 380, 330, 21.6, n=22, squash=1.0), "tree",
              step=60, density=0.85, smin=1.8, smax=2.8, seed=91,
              species="palm")
    s.scatter(blob(280, 1350, 330, 23.4, n=22, squash=1.0), "tree",
              step=60, density=0.85, smin=1.8, smax=2.8, seed=93,
              species="palm")
    s.scatter(blob(850, 1500, 300, 25.9, n=20, squash=0.9), "tree",
              step=60, density=0.85, smin=1.8, smax=2.8, seed=97,
              species="palm")
    s.scatter(blob(880, 300, 280, 33.1, n=20, squash=1.0), "tree",
              step=62, density=0.8, smin=1.6, smax=2.4, seed=99,
              species="palm")
    s.scatter(blob(860, 900, 280, 38.9, n=20, squash=0.9), "tree",
              step=62, density=0.8, smin=1.6, smax=2.6, seed=101,
              species="palm")
    s.scatter(blob(260, 800, 300, 39.7, n=20, squash=0.95), "tree",
              step=62, density=0.8, smin=1.6, smax=2.6, seed=103,
              species="palm")
    for x, y in [(200, 700), (280, 1050), (820, 800), (900, 1150),
                 (160, 1750), (950, 350), (740, 480), (350, 560),
                 (880, 1000), (240, 350), (700, 1550), (940, 600),
                 (150, 500), (400, 1250), (860, 700), (960, 1650),
                 (200, 950), (780, 1750), (660, 380), (340, 850)]:
        s.prop("bush", x, y, scale=24 * 2.0, model=1)
    s.scatter(blob(700, 500, 200, 41.3, n=18, squash=0.9), "tree",
              step=65, density=0.75, smin=1.7, smax=2.5, seed=105,
              species="palm")
    # Camp by the east bank.
    s.prop("tent", 850, 1250, scale=95, rot=0.4)
    s.prop("tent", 940, 1340, scale=85, rot=-0.8)
    s.prop("tent", 780, 1400, scale=80, rot=1.9)
    s.prop("cart", 120, 1480, scale=95, rot=1.5)
    s.prop("campfire", 880, 1420, scale=62)
    s.prop("crate", 800, 1330, scale=52)
    s.prop("crate", 750, 1280, scale=46)
    s.prop("barrel", 830, 1200, scale=50)
    s.prop("barrel", 960, 1420, scale=46)
    # River rocks + a rapid line across the channel at the top
    # meander (foam reads off the ripple rings the water pass draws).
    for x, y in [(540, 200), (600, 420), (550, 700), (610, 1050),
                 (570, 1350), (600, 1700)]:
        s.prop("rock", x, y, scale=15)
    rapid = [(522, 318), (548, 322), (575, 325), (602, 330),
             (625, 336), (648, 340)]
    for x, y in rapid:
        s.prop("rock", x, y, scale=17)
    # Rapid foam line — a hair-thin shelf crossing the channel makes
    # the water drop a level: CrossingPainter draws the white lip.
    s.terrain([(500, 330), (540, 322), (600, 330), (680, 345),
               (685, 380), (600, 368), (540, 360), (498, 366)],
              height=7, size=26, rough=30)
    s.save("ref-08")


def ref09():
    """Fort/tavern: walls, round towers, interior, courtyard."""
    s = Scene(1080, 1288, name="ref-09")
    # River + bank at top.
    s.terrain(blob(540, -40, 700, 6.8, n=32, squash=0.25, wob=0.10),
              height=18, size=52, rough=50)
    s.river([(-40, 60), (300, 90), (600, 60), (900, 110), (1120, 80)], 80)
    # Building — open main hall (room = interior view) + two round
    # towers with red hip roofs + stone courtyard.
    s.shape("floor", [[280, 560], [800, 560], [800, 980], [280, 980]],
            model=2)
    s.shape("room", [[360, 420], [720, 420], [720, 780], [360, 780]])
    # Crenellated parapet ring hugging the hall perimeter (model=1 =
    # merlon ticks) — the fort curtain wall read.
    s.stroke("wall", [[352, 412], [728, 412], [728, 788], [352, 788],
                      [352, 412]], 14, model=1)
    for cx in (300, 780):
        s.shape("house", blob(cx, 880, 110, 3.4 + cx, n=20, squash=1.0,
                              wob=0.02), model=1, color="#b04a38")
    # Interior furniture.
    s.prop("table", 480, 520, scale=30, rot=0.2)
    s.prop("table", 480, 640, scale=28, rot=-0.15)
    s.prop("table", 620, 560, scale=26, rot=1.4)
    s.prop("bed", 400, 470, scale=26, rot=math.pi / 2)
    s.prop("bed", 690, 700, scale=26, rot=math.pi)
    s.prop("shelf", 690, 460, scale=24, rot=math.pi)
    s.prop("shelf", 380, 700, scale=24, rot=math.pi / 2)
    s.prop("table", 620, 680, scale=24, rot=-0.5)
    s.prop("barrel", 680, 740, scale=20)
    s.prop("barrel", 650, 750, scale=20)
    s.prop("barrel", 620, 750, scale=20)
    s.prop("crate", 400, 740, scale=20)
    s.prop("crate", 430, 745, scale=18)
    s.prop("fireplace", 540, 440, scale=26)
    s.prop("rug", 540, 600, scale=40)
    s.prop("rug", 450, 690, scale=26)
    s.prop("door", 540, 782, scale=30)
    s.prop("window", 430, 421, scale=22)
    s.prop("window", 640, 421, scale=22)
    # Courtyard props.
    s.prop("fountain", 530, 880, scale=30)
    s.prop("cart", 850, 1080, scale=84, rot=0.8)
    s.prop("tent", 180, 1020, scale=72, rot=-0.6)
    # Road south.
    s.road([(540, 1290), (560, 1150), (600, 1020)], 18)
    # Trees.
    s.scatter(blob(950, 700, 180, 27.3, n=18, squash=1.1), "tree",
              step=90, density=0.65, smin=1.7, smax=2.5, seed=101)
    s.scatter(blob(120, 600, 160, 29.1, n=18, squash=1.1), "tree",
              step=85, density=0.6, smin=1.7, smax=2.4, seed=103)
    s.save("ref-09")


def ref10():
    """2mt artpack gorge: cracked cliffs, cyan water, dark bridge."""
    s = Scene(1080, 1440, name="ref-10", artpack="ink")
    # Water in the gorge — zig-zag channel.
    river = [(560, -40), (620, 300), (560, 620), (640, 900),
             (580, 1200), (620, 1480)]
    s.river(river, 120)
    # Gorge walls hugging the channel — ink-pack crack texture does
    # the rest (AlbedoPainter draws the 2mt crack mesh).
    s.terrain(bankpoly(river, 185, 460, side=1), height=24, size=110,
              rough=70)
    s.terrain(bankpoly(river, 185, 460, side=-1), height=24, size=110,
              rough=70)
    s.terrain(blob(250, 1350, 380, 12.4, n=26, squash=0.9, wob=0.12),
              height=20, size=58, rough=55)
    # Road crosses the channel E->W at the gorge narrows — BridgePainter
    # spans it where the road meets the water.
    s.road([(240, 360), (400, 340), (560, 370), (700, 400),
            (820, 520), (860, 800), (800, 1100)], 26)
    # Rocks in water.
    for x, y in [(600, 500), (560, 760), (620, 1040), (590, 1250)]:
        s.prop("rock", x, y, scale=16)
    # Trees — 2mt style (fine ink crowns).
    s.scatter(blob(180, 200, 280, 31.8, n=22, squash=1.0), "tree",
              step=95, density=0.6, smin=1.8, smax=2.7, seed=111)
    s.scatter(blob(880, 1100, 260, 33.5, n=20, squash=1.0), "tree",
              step=95, density=0.6, smin=1.8, smax=2.6, seed=113)
    s.scatter(blob(950, 200, 200, 35.2, n=18, squash=1.0), "tree",
              step=90, density=0.55, smin=1.7, smax=2.5, seed=117)
    for x, y in [(300, 800), (820, 700), (200, 1150)]:
        s.prop("bush", x, y, scale=22)
    s.save("ref-10")


BUILDERS = {"ref-01": ref01, "ref-02": ref02, "ref-03": ref03,
            "ref-04": ref04, "ref-05": ref05, "ref-06": ref06,
            "ref-07": ref07, "ref-08": ref08, "ref-09": ref09,
            "ref-10": ref10}

if __name__ == "__main__":
    names = sys.argv[1:] or list(BUILDERS)
    for n in names:
        BUILDERS[n]()
