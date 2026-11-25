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
    """River gorge: teal river N→SE, twin cliff walls, plank bridge, stairs."""
    s = Scene(1440, 768, name="ref-01")
    # River enters the top edge around x170-380, meanders south-east
    # and leaves through the bottom-right corner (ref composition).
    river = [(250, -40), (330, 120), (300, 270), (390, 400),
             (560, 500), (820, 590), (1100, 660), (1480, 730)]
    s.river(river, 96)
    # Gorge walls hugging both banks — ribbon massifs whose far edge
    # reaches well offscreen at the map corners so the stone reads as
    # chunky massifs, not contour-lined hills. Small `size` and low
    # roughness keep the faces clean like the ref's pale stone.
    s.terrain(bankpoly(river, 82, 145, side=1), height=24, size=27,
              rough=18)
    s.terrain(bankpoly(river, 82, 145, side=-1), height=26, size=28,
              rough=20)
    # Sandy path: enters at the top-right grass shelf, crosses the
    # river on the plank bridge near the center and ends at the stone
    # stair landing on the west bank. The ref does not continue the
    # lane as a second road beyond the stairs.
    s.road([(790, -40), (755, 120), (705, 245), (635, 335),
            (545, 420), (440, 470), (330, 505), (250, 535)], 22)
    # Rocks in/around the river.
    for i, (x, y) in enumerate([(300, 180), (270, 330), (460, 450),
                                (640, 540), (900, 620), (1050, 650),
                                (1200, 690)]):
        s.prop("rock", x, y, scale=28 + (i % 3) * 7)
    # Large scalloped crowns frame the gorge, but the reference keeps
    # broad open grass around the lane and bridge — clustered edges,
    # not wall-to-wall forest.
    clusters = [
        (170, 105, 310, 0.72, 11), (1250, 120, 350, 0.75, 23),
        (150, 690, 330, 0.65, 27), (1260, 680, 370, 0.66, 31),
    ]
    for cx, cy, rr, squash, seed in clusters:
        s.scatter(blob(cx, cy, rr, seed, n=28, squash=squash, wob=0.14),
                  [("tree", 5), ("bush", 1)],
                  step=132, density=0.34, smin=1.8, smax=2.55,
                  seed=seed, species="oak")
    # A few hero trees close to the river, deliberately sparse.
    for x, y, sc in [(180, 145, 2.5), (515, 255, 2.25), (850, 350, 2.15),
                     (1085, 430, 2.3), (240, 585, 2.35), (705, 115, 1.9),
                     (1140, 560, 2.15)]:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y),
               species="oak")
    s.save("ref-01")


def ref02():
    """Terraces + central-S river + dirtpath + stone circle."""
    s = Scene(1180, 756, name="ref-02")
    # River — a central S: enters at the top-right corner, winds
    # through the middle of the map and leaves at the bottom-left.
    river = [(1150, -40), (980, 70), (820, 140), (720, 240),
             (680, 350), (560, 430), (400, 500), (240, 580),
             (80, 680), (-60, 750)]
    s.river(river, 78)
    # Pale terrace ridges hugging the river bends — ribbons on the
    # outer banks of the two big curves, plus stacked terrace blobs
    # in the top-left and bottom-right corners.
    s.terrain(bankpoly(river[1:6], 110, 200, side=1), height=22,
              size=32, rough=20)
    s.terrain(bankpoly(river[4:9], 110, 200, side=-1), height=22,
              size=32, rough=20)
    s.terrain(blob(280, 90, 340, 2.2, n=30, squash=0.55, wob=0.14),
              height=24, size=34, rough=22)
    s.terrain(blob(300, 100, 200, 4.8, n=26, squash=0.55, wob=0.14),
              height=48, size=30, rough=26)
    s.terrain(blob(950, 600, 360, 6.6, n=30, squash=0.6, wob=0.12),
              height=22, size=32, rough=20)
    s.terrain(blob(960, 610, 210, 8.9, n=26, squash=0.6, wob=0.12),
              height=46, size=30, rough=24)
    # Dirt paths weaving between the terraces; the main lane crosses
    # the river at the lower meander → plank bridge.
    s.road([(80, -30), (220, 120), (380, 240), (520, 330),
            (600, 420), (620, 520), (560, 630), (480, 790)], 22)
    s.road([(600, 420), (780, 460), (980, 430), (1220, 400)], 18)
    s.road([(-30, 420), (180, 400), (380, 470), (560, 630)], 16)
    # Stone circle (menhirs) top-left, oval stone-walled enclosure
    # with a tree in the lower middle — both as in the ref.
    cx, cy, r = 230, 130, 85
    s.prop("stonecircle", cx, cy, scale=24 * 3.4)
    for i in range(10):
        a = i / 10 * math.tau
        s.prop("rock", cx + math.cos(a) * r, cy + math.sin(a) * r * 0.8,
               scale=10 + (i % 3) * 3)
    ex, ey = 420, 610
    oval = [[round(ex + math.cos(i / 22 * math.tau) * 120, 1),
             round(ey + math.sin(i / 22 * math.tau) * 80, 1)]
            for i in range(23)]
    s.stroke("wall", oval, 7)
    s.prop("tree", ex + 20, ey - 10, scale=24 * 1.8,
           seed=Scene.oakseed(ex, ey), species="oak")
    # Dense oak forest on both banks — the ref packs big scalloped
    # crowns in several green tones over most of the map.
    s.scatter(blob(150, 350, 280, 12.3, n=24, squash=0.9), "tree",
              step=62, density=0.85, smin=1.7, smax=2.8, seed=31,
              species="oak")
    s.scatter(blob(700, 650, 380, 14.1, n=24, squash=0.5), "tree",
              step=62, density=0.8, smin=1.6, smax=2.6, seed=37,
              species="oak")
    s.scatter(blob(1050, 250, 240, 16.9, n=20, squash=0.9), "tree",
              step=58, density=0.8, smin=1.7, smax=2.7, seed=41,
              species="oak")
    s.scatter(blob(470, 150, 260, 43.1, n=20, squash=0.7), "tree",
              step=62, density=0.75, smin=1.6, smax=2.5, seed=45,
              species="oak")
    s.scatter(blob(850, 350, 200, 47.3, n=18, squash=0.8), "tree",
              step=60, density=0.7, smin=1.6, smax=2.5, seed=49,
              species="oak")
    for x, y in [(140, 320), (260, 380), (60, 240)]:
        s.prop("tree", x, y, scale=24 * 2.5, seed=Scene.oakseed(x, y),
               species="oak")
    s.save("ref-02")


def ref03():
    """Caravan camp: wagons, tents, campfire; cliff W, river E."""
    s = Scene(1536, 783, name="ref-03")
    s.terrain(blob(140, 420, 420, 4.4, n=30, squash=0.75, wob=0.12),
              height=26, size=58, rough=50)
    s.river([(1240, -40), (1180, 160), (1210, 380), (1160, 560),
             (1200, 780)], 90)
    # Roads: sandy paths crossing the meadow.
    s.road([(-40, 160), (300, 220), (620, 260), (900, 300), (1180, 330),
            (1300, 320), (1440, 300)], 20)
    s.road([(500, 780), (560, 560), (640, 420), (700, 300)], 16)
    s.road([(900, 300), (860, 520), (820, 700)], 14)
    # Wagons + tents + campfire cluster — the ref packs the meadow
    # with a caravan ring.
    s.prop("cart", 560, 240, scale=110, rot=0.5)
    s.prop("cart", 780, 270, scale=104, rot=-2.6)
    s.prop("cart", 470, 480, scale=100, rot=1.2)
    s.prop("cart", 880, 520, scale=100, rot=-0.8)
    s.prop("cart", 640, 180, scale=96, rot=2.9)
    s.prop("cart", 990, 330, scale=92, rot=1.8)
    s.prop("cart", 420, 340, scale=92, rot=-1.9)
    s.prop("tent", 660, 380, scale=88, rot=0.9)
    s.prop("tent", 540, 560, scale=80, rot=-0.4)
    s.prop("tent", 940, 410, scale=84, rot=2.4)
    s.prop("tent", 730, 260, scale=74, rot=-1.2)
    s.prop("tent", 850, 620, scale=76, rot=0.6)
    s.prop("stall", 780, 420, scale=78, rot=0.3)
    s.prop("stall", 500, 400, scale=70, rot=-0.7)
    s.prop("campfire", 700, 490, scale=60)
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
              avoid=blob(1200, 420, 200, 19.9), species="oak")
    s.scatter(blob(700, 720, 700, 20.2, n=30, squash=0.25), "tree",
              step=100, density=0.55, smin=1.8, smax=2.7, seed=53,
              species="oak")
    s.scatter(blob(760, 40, 700, 22.7, n=30, squash=0.3), "tree",
              step=100, density=0.5, smin=1.8, smax=2.7, seed=57,
              species="oak")
    for x, y, sc in [(320, 600, 2.2), (380, 160, 2.0), (1080, 620, 2.1),
                     (1050, 150, 2.3)]:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y))
    # Rocks at cliff foot + river stones.
    for x, y in [(240, 620), (300, 660), (170, 700), (1150, 200),
                 (1230, 250), (1180, 600)]:
        s.prop("rock", x, y, scale=16)
    s.save("ref-03")


def ref04():
    """Dense woodland + thatch village + river with footbridges."""
    s = Scene(1748, 950, name="ref-04")
    # Pale cliff massifs inside the forest — a broken ridge along the
    # east third (stairs where the lane crosses), a round terrace at
    # the centre and a curved ledge south-west of it.
    ridge1 = catmull([(1180, -30), (1200, 120), (1150, 280),
                      (1200, 420)], n=40, wob=0.06, seed=5.1)
    s.terrain(bankpoly(ridge1, -70, 150), height=20, size=32, rough=20)
    s.terrain(blob(1030, 560, 160, 11.2, n=22, squash=0.85, wob=0.12),
              height=18, size=30, rough=18)
    s.terrain(blob(1030, 560, 95, 12.8, n=20, squash=0.85, wob=0.12),
              height=34, size=28, rough=22)
    s.terrain(blob(420, 720, 190, 13.6, n=22, squash=0.7, wob=0.12),
              height=20, size=30, rough=18)
    # River along the south-east — enters the right edge mid-height,
    # bends through the corner and exits the bottom edge. Lanes cross
    # it twice → small wooden footbridges.
    s.river([(1790, 470), (1620, 560), (1500, 680), (1420, 800),
             (1360, 990)], 70)
    # Winding sandy path network with crossings, as in the ref.
    s.road([(-40, 300), (240, 320), (520, 380), (820, 430),
            (1120, 470), (1380, 520), (1520, 600), (1640, 660),
            (1790, 700)], 20)
    s.road([(300, -40), (330, 160), (420, 360), (520, 580),
            (640, 800), (720, 990)], 16)
    s.road([(520, 380), (820, 300), (1120, 250), (1420, 220),
            (1700, 200)], 14)
    s.road([(1120, 470), (1150, 660), (1260, 820), (1400, 900)], 12)
    s.road([(-40, 620), (200, 600), (420, 640), (640, 800)], 12)
    # Village NW — a cluster of thatched homes along the lane,
    # with carts, crates and a well.
    homes = [
        (70, 70, 120, 76, -0.1), (200, 90, 110, 70, 0.12),
        (320, 70, 105, 66, 0.2), (110, 190, 115, 72, 0.05),
        (250, 200, 105, 66, -0.15), (370, 180, 95, 60, 0.3),
        (160, 300, 100, 64, -0.05),
    ]
    for x, y, w, h, a in homes:
        s.house(x, y, w, h, a, model=2, color="#b7a955")
    for x, y in [(260, 150), (180, 250), (340, 260), (90, 140)]:
        s.prop("crate", x, y, scale=38)
    s.prop("cart", 300, 320, scale=84, rot=0.5)
    s.prop("cart", 120, 350, scale=80, rot=-1.1)
    s.prop("well", 230, 260, scale=40)
    # Red-roof homes bottom-left + bottom-right (roof variant).
    for i, (x, y, a) in enumerate([(180, 830, -0.3), (300, 870, 0.2),
                                   (230, 910, 0.6), (1500, 860, -0.4),
                                   (1620, 890, 0.25)]):
        s.house(x, y, 84, 56, a)
    # Stone circle on the central terrace.
    s.prop("stonecircle", 1030, 545, scale=24 * 2.6)
    for i in range(8):
        a = i / 8 * math.tau
        s.prop("rock", 1030 + math.cos(a) * 70, 545 + math.sin(a) * 55,
               scale=9 + (i % 3) * 2)
    # Dense forest — hundreds of overlapping scalloped oak crowns in
    # several green tones covering almost the whole map; only the
    # lanes, village and river read as open space.
    s.scatter(blob(870, 460, 800, 41.2, n=30, squash=0.55), "tree",
              step=72, density=0.82, smin=1.5, smax=2.6, seed=61,
              species="oak")
    s.scatter(blob(870, 470, 560, 45.3, n=26, squash=0.7), "tree",
              step=70, density=0.8, smin=1.5, smax=2.5, seed=63,
              species="oak")
    s.scatter(blob(250, 650, 300, 47.8, n=18, squash=0.7), "tree",
              step=72, density=0.78, smin=1.5, smax=2.4, seed=67,
              species="oak")
    s.scatter(blob(1500, 350, 300, 51.4, n=18, squash=0.8), "tree",
              step=70, density=0.75, smin=1.5, smax=2.4, seed=69,
              species="oak")
    s.scatter(blob(700, 100, 340, 55.1, n=16, squash=0.5), "tree",
              step=72, density=0.75, smin=1.4, smax=2.3, seed=71,
              species="oak")
    s.scatter(blob(1300, 800, 300, 57.9, n=18, squash=0.6), "tree",
              step=72, density=0.7, smin=1.4, smax=2.3, seed=73,
              species="oak")
    s.scatter(blob(600, 800, 260, 59.2, n=16, squash=0.6), "tree",
              step=74, density=0.7, smin=1.4, smax=2.4, seed=79,
              species="oak")
    # Undergrowth bushes threaded between the trunks.
    s.scatter(blob(870, 460, 700, 61.7, n=26, squash=0.6), "bush",
              step=110, density=0.5, smin=1.0, smax=1.8, seed=83)
    s.save("ref-04")


def ref05():
    """Desert oasis — flagstone arena ringed by a red-rock mesa."""
    s = Scene(1881, 788, name="ref-05", biome="desert")
    # One dominant orange massif looming behind the arena (ref top),
    # lower dunes framing the sand.
    s.terrain(blob(1000, 280, 460, 5.7, n=32, squash=0.55, wob=0.10),
              height=34, size=64, rough=45)
    s.terrain(blob(1750, 160, 380, 9.5, n=26, squash=0.55, wob=0.12),
              height=24, size=60, rough=40)
    s.terrain(blob(300, 720, 300, 11.3, n=24, squash=0.4, wob=0.12),
              height=18, size=55, rough=38)
    s.terrain(blob(1600, 700, 260, 6.1, n=22, squash=0.5, wob=0.12),
              height=16, size=50, rough=36)
    # Oasis lagoon west.
    s.shape("pond", blob(60, 380, 360, 4.2, n=28, squash=0.9, wob=0.08))
    # Flagstone arena — a paved disc with a low ring of rock chunks.
    cx, cy, r = 950, 420, 200
    pts = [[round(cx + math.cos(i / 26 * math.tau) * r, 1),
            round(cy + math.sin(i / 26 * math.tau) * r, 1)]
           for i in range(26)]
    s.shape("floor", pts, model=2, size=56, height=6)
    for i in range(14):
        a = i / 14 * math.tau + 0.11
        s.prop("rock", cx + math.cos(a) * (r + 14),
               cy + math.sin(a) * (r + 14), scale=9 + (i % 3) * 2)
    # A table and supplies inside the ring — the ref's meeting spot.
    s.prop("table", cx - 10, cy - 20, scale=34, rot=0.3)
    s.prop("crate", cx + 70, cy + 60, scale=26, rot=0.4)
    s.prop("barrel", cx - 80, cy + 50, scale=24)
    s.prop("rug", cx + 30, cy + 30, scale=28)
    s.prop("campfire", cx + 60, cy - 60, scale=30)
    # Palm ring hugging the arena's foot + lagoon edge.
    for x, y, sc in [(700, 260, 2.8), (640, 460, 2.5), (750, 600, 2.6),
                     (1180, 560, 2.7), (1230, 300, 2.9), (1100, 180, 2.4),
                     (840, 620, 2.3), (1330, 430, 2.4), (420, 200, 2.2),
                     (320, 500, 2.5), (230, 300, 2.3), (1500, 260, 2.6),
                     (1560, 520, 2.4), (520, 680, 2.2)]:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y),
               species="palm")
    for x, y in [(880, 160), (1180, 660), (560, 380), (1450, 380)]:
        s.prop("bush", x, y, scale=24 * 1.8)
    # Boulders.
    for i, (x, y) in enumerate([(760, 120), (820, 150), (1200, 680),
                                (1280, 720), (1600, 500), (250, 550),
                                (330, 590)]):
        s.prop("rock", x, y, scale=14 + (i % 4) * 4)
    s.save("ref-05")


def ref06():
    """Terraced fields — round terraces, hilltop shrine, stone-walled
    field plots, branching dirt paths."""
    s = Scene(1228, 685, name="ref-06")
    # Round terraces in stacked pairs → 3+ readable levels, pale
    # faces with stones on the rims.
    for bx, by, br, sd in [(300, 160, 210, 2.9), (880, 140, 230, 4.5),
                           (430, 540, 200, 7.8), (950, 480, 190, 9.3),
                           (610, 330, 240, 5.6)]:
        s.terrain(blob(bx, by, br, sd, n=26, squash=0.8, wob=0.10),
                  height=14, size=30, rough=16)
        s.terrain(blob(bx + 8, by + 6, br * 0.62, sd + 1.7, n=24,
                       squash=0.8, wob=0.10),
                  height=30, size=28, rough=20)
    # Shrine on the central terrace — carved stone circle ornament
    # with a standing stone head, ringed by rocks.
    s.prop("stonecircle", 610, 320, scale=24 * 2.6)
    s.prop("statue", 610, 310, scale=44)
    for i in range(8):
        a = i / 8 * math.tau + 0.2
        s.prop("rock", 610 + math.cos(a) * 95, 320 + math.sin(a) * 75,
               scale=9 + (i % 3) * 2)
    # Field plots — stone-edged rectangles in loose rows scattered
    # over the flats, their green fill reading as crops.
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
    # Supplies + boulders on the terraces.
    s.prop("chest", 560, 260, scale=30, rot=0.5)
    s.prop("chest", 700, 400, scale=28, rot=1.1)
    s.prop("cart", 480, 250, scale=70, rot=0.4)
    s.prop("crate", 520, 220, scale=30, rot=-0.7)
    s.prop("crate", 660, 370, scale=28)
    for i, (x, y) in enumerate([(320, 300), (380, 240), (540, 200),
                                (760, 210), (880, 300), (960, 360),
                                (240, 520), (400, 580), (720, 480),
                                (860, 540), (1040, 320), (140, 340)]):
        s.prop("rock", x, y, scale=12 + (i % 3) * 3)
    # Branching sandy path network.
    s.road([(-30, 420), (220, 380), (480, 340), (700, 300), (980, 260),
            (1260, 240)], 16)
    s.road([(620, -30), (630, 160), (610, 330), (640, 500), (660, 690)], 14)
    s.road([(80, 690), (240, 560), (480, 460), (610, 330)], 12)
    s.road([(220, 380), (300, 240), (420, 140)], 10)
    s.road([(700, 300), (810, 400), (950, 470)], 10)
    # Broadleaf oaks dotting the terraces and field edges.
    for i, (x, y) in enumerate([(240, 150), (400, 190), (880, 170),
                                (1000, 200), (350, 480), (500, 560),
                                (1100, 480), (150, 440), (760, 120),
                                (560, 120), (960, 580), (60, 250),
                                (430, 620), (1050, 60), (300, 320)]):
        s.prop("tree", x, y, scale=24 * (1.5 + (i % 3) * 0.3),
               seed=Scene.oakseed(x, y), species="oak")
    s.save("ref-06")


def ref07():
    """Village + river fork + waterfalls + bridges (portrait)."""
    s = Scene(1080, 1421, name="ref-07")
    # River: single channel N, forks around a mid island, both arms
    # run south and leave the bottom edge.
    upper = [(420, -40), (430, 200), (460, 420), (440, 560)]
    arm_w = [(440, 560), (300, 720), (240, 900), (280, 1100),
             (340, 1300), (360, 1460)]
    arm_e = [(440, 560), (640, 700), (700, 900), (640, 1100),
             (560, 1280), (520, 1460)]
    s.river(upper, 66)
    s.river(arm_w, 60)
    s.river(arm_e, 60)
    # Pale cliff bands hugging the upper channel's banks — where the
    # river crosses their edges the water drops in small falls, and
    # the fork wedge reads as a high bank.
    s.terrain(bankpoly(upper, 110, 170, side=1), height=20, size=30,
              rough=18)
    s.terrain(bankpoly(upper, 110, 170, side=-1), height=20, size=30,
              rough=18)
    s.terrain(bankpoly(arm_w[:4], 100, 150, side=-1), height=18,
              size=28, rough=16)
    s.terrain(bankpoly(arm_e[:4], 100, 150, side=1), height=18,
              size=28, rough=16)
    # Roads: along both banks and across the arms — each crossing
    # spawns a wooden footbridge, like the ref's paired bridges at
    # the fork.
    s.road([(60, 300), (300, 340), (420, 380), (620, 360), (900, 330),
            (1100, 340)], 20)
    s.road([(-40, 820), (180, 800), (330, 830), (470, 860),
            (660, 840), (880, 800), (1120, 780)], 18)
    s.road([(80, 1150), (280, 1120), (420, 1180), (700, 1160),
            (1040, 1180)], 18)
    s.road([(520, 60), (560, 260), (600, 480), (640, 700)], 12)
    # Village — dense rows of golden thatched homes packed along
    # both banks of the branching river, plus a red-tile manor NW.
    homes = [
        # Right bank, upper — the ref's densest row.
        (560, 120, 0.1), (680, 100, -0.2), (800, 140, 0.15),
        (920, 110, -0.1), (620, 240, 0.2), (740, 220, -0.15),
        (860, 250, 0.1), (980, 230, -0.2), (580, 360, 0.25),
        (700, 350, -0.1), (830, 380, 0.15), (950, 360, -0.25),
        (640, 480, 0.1), (770, 500, -0.2), (900, 490, 0.2),
        # Right bank, lower arm.
        (780, 640, -0.15), (900, 700, 0.1), (820, 800, 0.25),
        (950, 860, -0.1), (760, 950, 0.15), (890, 1020, -0.2),
        # Island wedge between the arms.
        (420, 640, 0.4), (400, 780, -0.3), (430, 920, 0.2),
        (440, 1060, -0.1),
        # Left bank, lower.
        (90, 700, -0.2), (200, 730, 0.1), (80, 850, 0.3),
        (170, 880, -0.15), (90, 990, 0.2), (190, 1020, -0.1),
        # South village.
        (350, 1250, 0.15), (480, 1280, -0.2), (620, 1250, 0.3),
        (750, 1300, -0.15), (200, 1150, 0.25), (880, 1150, -0.2),
    ]
    for i, (x, y, a) in enumerate(homes):
        w = 95 + rng(i * 7 + 3) * 30
        h = 62 + rng(i * 13 + 5) * 18
        s.house(x, y, w, h, a + rng(i * 17) * 0.3 - 0.15,
                model=2, color="#b7a955")
    # Red-tile manor NW corner + one red-roof home SW (as in the ref).
    for i, (x, y, a) in enumerate([(40, 60, 0.05), (170, 40, -0.1),
                                   (60, 140, 0.15), (290, 70, 0.3)]):
        s.house(x, y, 120 - i * 12, 80 - i * 6, a, model=0)
    s.house(90, 1330, 100, 68, 0.2, model=0)
    for x, y in [(260, 260), (700, 320), (180, 560), (800, 520),
                 (400, 1200), (760, 1250), (150, 350), (420, 700),
                 (560, 700), (950, 750), (300, 480), (640, 440)]:
        s.prop("crate", x, y, scale=24)
    s.prop("well", 500, 240, scale=30)
    s.prop("cart", 460, 400, scale=80, rot=0.8)
    s.prop("cart", 200, 620, scale=76, rot=-1.4)
    s.prop("cart", 850, 580, scale=78, rot=0.4)
    # Trees sprinkled along the banks and behind the homes.
    s.scatter(blob(950, 200, 220, 15.3, n=20, squash=0.8), "tree",
              step=85, density=0.65, smin=1.7, smax=2.6, seed=81,
              species="oak")
    s.scatter(blob(120, 1250, 220, 17.5, n=20, squash=0.8), "tree",
              step=85, density=0.7, smin=1.7, smax=2.6, seed=83,
              species="oak")
    s.scatter(blob(540, 960, 180, 19.8, n=18, squash=0.9), "tree",
              step=80, density=0.6, smin=1.6, smax=2.4, seed=87,
              species="oak")
    s.scatter(blob(980, 1150, 200, 21.4, n=18, squash=0.8), "tree",
              step=82, density=0.6, smin=1.6, smax=2.4, seed=89,
              species="oak")
    s.save("ref-07")


def ref08():
    """River canyon + rapid + jungle camp (portrait)."""
    s = Scene(1080, 1920, name="ref-08")
    # River N→S center, bending east in the lower half and leaving
    # through the right edge, as in the ref.
    river = [(560, -40), (580, 300), (540, 620), (600, 900),
             (640, 1100), (780, 1250), (940, 1330), (1120, 1380)]
    s.river(river, 110)
    # Stone banks hugging the channel — narrow ribbons so the grass
    # keeps most of the map; small `size` + low roughness avoid the
    # mechanical contour lines and let the ink cracks read.
    s.terrain(bankpoly(river, 120, 220, side=1), height=22, size=34,
              rough=20, model=2)
    s.terrain(bankpoly(river, 120, 220, side=-1), height=22, size=34,
              rough=20, model=2)
    s.terrain(blob(200, 1600, 420, 7.4, n=26, squash=0.8, wob=0.10),
              height=18, size=34, rough=22)
    # Jungle: palms massed on the canyon rims but the ref leaves the
    # grey rock faces and the river reading through — mixed species
    # and looser packing so the canyon still shows.
    s.scatter(blob(280, 380, 330, 21.6, n=22, squash=1.0), "tree",
              step=78, density=0.6, smin=1.6, smax=2.6, seed=91,
              species="palm")
    s.scatter(blob(280, 1350, 330, 23.4, n=22, squash=1.0), "tree",
              step=78, density=0.6, smin=1.6, smax=2.6, seed=93,
              species="palm")
    s.scatter(blob(850, 1500, 300, 25.9, n=20, squash=0.9), "tree",
              step=78, density=0.6, smin=1.6, smax=2.6, seed=97,
              species="palm")
    s.scatter(blob(880, 300, 280, 33.1, n=20, squash=1.0), "tree",
              step=80, density=0.55, smin=1.5, smax=2.3, seed=99)
    s.scatter(blob(860, 900, 280, 38.9, n=20, squash=0.9), "tree",
              step=80, density=0.55, smin=1.5, smax=2.4, seed=101,
              species="palm")
    s.scatter(blob(260, 800, 300, 39.7, n=20, squash=0.95), "tree",
              step=80, density=0.55, smin=1.5, smax=2.4, seed=103)
    for x, y in [(200, 700), (280, 1050), (820, 800), (900, 1150),
                 (160, 1750), (950, 350), (740, 480), (350, 560),
                 (880, 1000), (240, 350), (700, 1550), (940, 600),
                 (150, 500), (400, 1250), (860, 700), (960, 1650),
                 (200, 950), (780, 1750), (660, 380), (340, 850)]:
        s.prop("bush", x, y, scale=24 * 2.0, model=1)
    s.scatter(blob(700, 500, 200, 41.3, n=18, squash=0.9), "tree",
              step=78, density=0.55, smin=1.5, smax=2.3, seed=105,
              species="palm")
    # Camp on the west bank meadow below the bend — tents, a cart at
    # the bottom-left corner, campfire and supplies.
    s.prop("tent", 560, 1250, scale=95, rot=0.4)
    s.prop("tent", 440, 1330, scale=85, rot=-0.8)
    s.prop("tent", 640, 1400, scale=80, rot=1.9)
    s.prop("cart", 120, 1480, scale=95, rot=1.5)
    s.prop("campfire", 540, 1420, scale=62)
    s.prop("crate", 470, 1330, scale=52)
    s.prop("crate", 420, 1280, scale=46)
    s.prop("barrel", 500, 1200, scale=50)
    s.prop("barrel", 630, 1420, scale=46)
    # River rocks + a rapid line across the channel at the top
    # meander (foam reads off the ripple rings the water pass draws).
    for x, y in [(540, 200), (600, 420), (550, 700), (610, 1050),
                 (700, 1200), (860, 1300)]:
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
              height=18, size=34, rough=22)
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
    # Bar counter along the east wall + stools — the ref's tavern.
    s.prop("table", 640, 500, scale=30, rot=math.pi / 2)
    s.prop("table", 640, 610, scale=28, rot=math.pi / 2)
    for x, y in [(590, 480), (590, 560), (590, 640)]:
        s.prop("barrel", x, y, scale=16)
    s.prop("shelf", 380, 460, scale=24, rot=math.pi / 2)
    s.prop("fireplace", 380, 560, scale=24)
    s.prop("table", 500, 700, scale=26, rot=0.9)
    for x, y in [(460, 660), (540, 660), (500, 740)]:
        s.prop("crate", x, y, scale=18)
    # Courtyard props.
    s.prop("fountain", 530, 880, scale=30)
    s.prop("cart", 850, 1080, scale=84, rot=0.8)
    s.prop("tent", 180, 1020, scale=72, rot=-0.6)
    # Road south.
    s.road([(540, 1290), (560, 1150), (600, 1020)], 18)
    # Trees.
    s.scatter(blob(950, 700, 180, 27.3, n=18, squash=1.1), "tree",
              step=90, density=0.65, smin=1.7, smax=2.5, seed=101,
              species="oak")
    s.scatter(blob(120, 600, 160, 29.1, n=18, squash=1.1), "tree",
              step=85, density=0.6, smin=1.7, smax=2.4, seed=103,
              species="oak")
    s.save("ref-09")


def ref10():
    """2mt artpack gorge: cracked cliffs, cyan water, dark bridge."""
    s = Scene(1080, 1440, name="ref-10", artpack="ink")
    # Water in the gorge — a channel crossing the map west→east
    # through the middle, as in the ref screenshot.
    river = [(-40, 700), (220, 660), (460, 700), (700, 640),
             (900, 690), (1120, 660)]
    s.river(river, 130)
    # Gorge walls hugging the channel — narrow stone ribbons so the
    # mossy grass keeps the upper and lower thirds; small `size` +
    # low roughness avoid mechanical contour lines while the ink
    # crack texture (AlbedoPainter 2mt mesh) does the detail work.
    s.terrain(bankpoly(river, 130, 240, side=1), height=24, size=32,
              rough=20, model=2)
    s.terrain(bankpoly(river, 130, 240, side=-1), height=24, size=32,
              rough=20, model=2)
    s.terrain(blob(950, 1250, 320, 12.4, n=26, squash=0.8, wob=0.12),
              height=20, size=30, rough=20, model=2)
    # Road crossing the gorge N→S at the narrows — BridgePainter
    # spans it where the road meets the water: the dark plank bridge.
    s.road([(380, -40), (390, 240), (370, 480), (390, 700),
            (420, 950), (400, 1200), (430, 1480)], 26)
    s.road([(380, 240), (560, 200), (760, 180), (1000, 150)], 16)
    # Rocks in water with ripple rings.
    for x, y in [(250, 670), (500, 710), (750, 650), (950, 680),
                 (600, 680)]:
        s.prop("rock", x, y, scale=16)
    # Trees — 2mt style fine ink crowns, broadleaf only.
    s.scatter(blob(180, 200, 280, 31.8, n=22, squash=1.0), "tree",
              step=95, density=0.6, smin=1.8, smax=2.7, seed=111,
              species="oak")
    s.scatter(blob(880, 1100, 260, 33.5, n=20, squash=1.0), "tree",
              step=95, density=0.6, smin=1.8, smax=2.6, seed=113,
              species="oak")
    s.scatter(blob(950, 200, 200, 35.2, n=18, squash=1.0), "tree",
              step=90, density=0.55, smin=1.7, smax=2.5, seed=117,
              species="oak")
    s.scatter(blob(200, 1150, 240, 37.6, n=18, squash=1.0), "tree",
              step=92, density=0.55, smin=1.7, smax=2.5, seed=119,
              species="oak")
    for x, y in [(300, 500), (820, 450), (200, 950), (650, 1000),
                 (480, 350)]:
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
