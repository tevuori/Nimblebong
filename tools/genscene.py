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
    drops straight into the bank. Densify + wobble the centerline so
    the cliff lip is hand-drawn and crenellated instead of a smooth
    survey ribbon."""
    # Already-dense hand-built ridges must not be resampled again —
    # nesting Catmull here explodes a 40-point ridge into hundreds of
    # vertices and makes the polygon clipper crawl.
    dense = (catmull(pts, n=max(40, len(pts) * 6), wob=5.5,
                     seed=gap * 0.37 + depth * 0.11 + side)
             if len(pts) <= 12 else [list(p) for p in pts])
    near = offset_line(dense, gap * side)
    far = offset_line(dense, (gap + depth) * side)
    return [[round(x, 1), round(y, 1)] for x, y in near + far[::-1]]


def tube(pts, halfwidth, wob=0.0, seed=1.0):
    """Closed polygonal corridor around a polyline, used both for
    visible banks and as a scatter exclusion mask."""
    dense = catmull(pts, n=max(36, len(pts) * 7), wob=wob, seed=seed)
    left = offset_line(dense, halfwidth)
    right = offset_line(dense, -halfwidth)
    return [[round(x, 1), round(y, 1)] for x, y in left + right[::-1]]


def inside_any(x, y, polys):
    """Point-in-polygon test accepting one polygon or a list of them."""
    if not polys:
        return False
    if isinstance(polys[0][0], (int, float)):
        return inside(x, y, polys)
    return any(inside(x, y, poly) for poly in polys)


def arc(cx, cy, rx, ry, a0, a1, n=12):
    """Polyline arc; angles in radians."""
    return [[round(cx + math.cos(a0 + (a1 - a0) * i / (n - 1)) * rx, 1),
             round(cy + math.sin(a0 + (a1 - a0) * i / (n - 1)) * ry, 1)]
            for i in range(n)]


def rough_rect(x, y, w, h, seed=1.0, jitter=8):
    """Slightly uneven rectangular field/plot footprint."""
    pts = []
    corners = [(x, y), (x + w, y), (x + w, y + h), (x, y + h)]
    for i, (px, py) in enumerate(corners):
        pts.append([round(px + (rng(seed + i * 3.1) - 0.5) * jitter, 1),
                    round(py + (rng(seed + i * 5.7) - 0.5) * jitter, 1)])
    return pts


def rough_patch(x, y, w, h, seed=1.0, jitter=10):
    """Eight-point uneven rectangle — rectangular land use, but with
    hand-laid edges instead of a CAD grid."""
    base = [(x, y), (x + w * 0.52, y), (x + w, y),
            (x + w, y + h * 0.52), (x + w, y + h),
            (x + w * 0.48, y + h), (x, y + h), (x, y + h * 0.48)]
    pts = []
    for i, (px, py) in enumerate(base):
        edge_mid = i % 2 == 1
        amp = jitter * (1.25 if edge_mid else 0.65)
        pts.append([round(px + (rng(seed + i * 3.7) - 0.5) * amp, 1),
                    round(py + (rng(seed + i * 6.1) - 0.5) * amp, 1)])
    return pts


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
                            avoid and inside_any(px, py, avoid)):
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
    s.terrain(bankpoly(river, 82, 145, side=1), height=36, size=34,
              rough=18)
    s.terrain(bankpoly(river, 82, 145, side=-1), height=40, size=36,
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
                  step=128, density=0.34, smin=2.25, smax=3.0,
                  seed=seed, species="oak")
    # A few hero trees close to the river, deliberately sparse.
    for x, y, sc in [(180, 145, 2.85), (515, 255, 2.65), (850, 350, 2.55),
                     (1085, 430, 2.7), (240, 585, 2.75), (705, 115, 2.25),
                     (1140, 560, 2.55)]:
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
    roads = [
        ([(80, -30), (220, 120), (380, 240), (520, 330),
          (600, 420), (620, 520), (560, 630), (480, 790)], 22),
        ([(600, 420), (780, 460), (980, 430), (1220, 400)], 18),
        ([(-30, 420), (180, 400), (380, 470), (560, 630)], 16),
    ]
    for pts, width in roads:
        s.road(pts, width)
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
    # Dense oak forest is pushed to the outer frame. Broad exclusion
    # corridors keep the S-river, all three lanes, the menhir circle
    # and the walled meadow open — the reference reads as forested
    # edges around a grassy terraced centre, not a continuous canopy.
    clearings = [
        tube(river, 108, wob=4.0, seed=201),
        tube(roads[0][0], 48, wob=2.0, seed=203),
        tube(roads[1][0], 40, wob=2.0, seed=205),
        tube(roads[2][0], 40, wob=2.0, seed=207),
        blob(cx, cy, 168, 15.7, n=20, squash=0.86, wob=0.10),
        blob(ex, ey, 165, 17.1, n=20, squash=0.74, wob=0.10),
    ]
    edge_forests = [
        (blob(35, 300, 250, 12.3, n=24, squash=1.0, wob=0.13), 72, 0.82, 31),
        (blob(360, -10, 320, 43.1, n=24, squash=0.45, wob=0.13), 74, 0.78, 45),
        (blob(1125, 230, 260, 16.9, n=22, squash=1.0, wob=0.13), 70, 0.80, 41),
        (blob(1040, 725, 330, 14.1, n=24, squash=0.55, wob=0.13), 74, 0.78, 37),
        (blob(150, 760, 260, 47.3, n=20, squash=0.45, wob=0.12), 76, 0.74, 49),
    ]
    for region, step, density, seed in edge_forests:
        s.scatter(region, "tree", step=step, density=density,
                  smin=1.22, smax=2.05, seed=seed,
                  avoid=clearings, species="oak")
    # A few medium hero crowns overlap the grass/forest transition,
    # deliberately smaller than the first iteration's giant canopy.
    for x, y, sc in [(105, 310, 1.9), (235, 365, 1.8), (60, 225, 1.95),
                     (1015, 250, 1.85), (1085, 520, 1.9),
                     (850, 690, 1.75)]:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y),
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
    s.prop("tent", 660, 380, scale=88, rot=0.9, model=0)
    s.prop("tent", 540, 560, scale=80, rot=-0.4, model=0)
    s.prop("tent", 940, 410, scale=84, rot=2.4, model=0)
    s.prop("tent", 730, 260, scale=74, rot=-1.2, model=0)
    s.prop("tent", 850, 620, scale=76, rot=0.6, model=0)
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
              step=118, density=0.42, smin=1.55, smax=2.25, seed=51,
              avoid=blob(1200, 420, 200, 19.9), species="oak")
    s.scatter(blob(700, 720, 700, 20.2, n=30, squash=0.25), "tree",
              step=122, density=0.3, smin=1.55, smax=2.2, seed=53,
              species="oak")
    s.scatter(blob(760, 40, 700, 22.7, n=30, squash=0.3), "tree",
              step=122, density=0.28, smin=1.55, smax=2.2, seed=57,
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
    river = [(1790, 470), (1620, 560), (1500, 680), (1420, 800),
             (1360, 990)]
    s.river(river, 70)
    # Winding sandy path network with crossings, as in the ref.
    roads = [
        ([(-40, 300), (240, 320), (520, 380), (820, 430),
          (1120, 470), (1380, 520), (1520, 600), (1640, 660),
          (1790, 700)], 20),
        ([(300, -40), (330, 160), (420, 360), (520, 580),
          (640, 800), (720, 990)], 16),
        ([(520, 380), (820, 300), (1120, 250), (1420, 220),
          (1700, 200)], 14),
        ([(1120, 470), (1150, 660), (1260, 820), (1400, 900)], 12),
        ([(-40, 620), (200, 600), (420, 640), (640, 800)], 12),
    ]
    for pts, width in roads:
        s.road(pts, width)
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
    # Dense woodland, but structured into separate massed canopy
    # islands. Every travel line, the NW village, the stone-circle
    # terrace and the river/bridges sit in wide deterministic clearings;
    # this preserves the ref's dense green mass without the first
    # iteration's wall-to-wall overlap and its 476-prop cost.
    clearings = [
        tube(river, 92, wob=4.0, seed=301),
        blob(220, 180, 305, 23.7, n=24, squash=0.72, wob=0.12),
        blob(1030, 545, 195, 24.9, n=20, squash=0.88, wob=0.10),
        blob(230, 875, 145, 26.1, n=18, squash=0.72, wob=0.10),
        blob(1560, 875, 155, 28.3, n=18, squash=0.72, wob=0.10),
    ]
    clearings += [tube(pts, 43 if width >= 16 else 35,
                       wob=2.0, seed=305 + i * 2)
                  for i, (pts, width) in enumerate(roads)]
    woods = [
        (blob(280, 620, 305, 47.8, n=20, squash=0.72, wob=0.13), 60, 0.74, 67),
        (blob(690, 105, 340, 55.1, n=20, squash=0.55, wob=0.13), 60, 0.74, 71),
        (blob(735, 430, 330, 45.3, n=22, squash=0.72, wob=0.13), 62, 0.72, 63),
        (blob(1495, 330, 330, 51.4, n=20, squash=0.82, wob=0.13), 60, 0.74, 69),
        (blob(1305, 795, 300, 57.9, n=20, squash=0.62, wob=0.13), 62, 0.70, 73),
        (blob(610, 825, 255, 59.2, n=18, squash=0.62, wob=0.13), 62, 0.68, 79),
        (blob(1130, 160, 250, 41.2, n=18, squash=0.68, wob=0.13), 64, 0.68, 61),
    ]
    for region, step, density, seed in woods:
        s.scatter(region, "tree", step=step, density=density,
                  smin=1.45, smax=2.35, seed=seed,
                  avoid=clearings, species="oak")
    # Undergrowth stays sparse and is kept out of the same corridors.
    s.scatter(blob(870, 460, 700, 61.7, n=26, squash=0.6, wob=0.12),
              "bush", step=122, density=0.42, smin=1.0, smax=1.7,
              seed=83, avoid=clearings)
    s.save("ref-04")


def ref05():
    """Desert shrine island — layered sandstone plateau, broken round
    sanctuary, scattered palms and rock shelves (not a flat paved disc)."""
    s = Scene(1881, 788, name="ref-05", biome="desert")
    # Broad sandstone island built from several uneven, overlapping
    # plateaus. The stacked masses step upward toward the sanctuary and
    # their wobbled footprints leave organic sand bays around the edges.
    s.terrain(blob(900, 455, 425, 5.7, n=34, squash=0.63, wob=0.15),
              height=14, size=58, rough=42)
    s.terrain(blob(870, 420, 330, 7.1, n=32, squash=0.67, wob=0.16),
              height=30, size=54, rough=44)
    s.terrain(blob(890, 392, 235, 8.4, n=30, squash=0.72, wob=0.15),
              height=48, size=48, rough=46)
    # Satellite shelves and dunes break the silhouette instead of one
    # symmetrical mesa; the biggest red masses stay at the outer frame.
    s.terrain(blob(520, 505, 185, 10.2, n=24, squash=0.72, wob=0.16),
              height=22, size=48, rough=42)
    s.terrain(blob(1215, 355, 175, 11.6, n=24, squash=0.76, wob=0.15),
              height=24, size=48, rough=42)
    s.terrain(blob(1715, 145, 390, 9.5, n=28, squash=0.52, wob=0.13),
              height=24, size=60, rough=40)
    s.terrain(blob(300, 735, 310, 11.3, n=24, squash=0.38, wob=0.13),
              height=16, size=55, rough=38)
    s.terrain(blob(1600, 720, 260, 6.1, n=22, squash=0.48, wob=0.13),
              height=15, size=50, rough=36)
    # Oasis lagoon west, clipped by the map edge as in the reference.
    s.shape("pond", blob(55, 380, 360, 4.2, n=28, squash=0.9, wob=0.08))

    # Sanctuary on the upper plateau: a modest irregular stone floor,
    # broken ring wall, pillar stubs and two small annex floor patches.
    cx, cy = 890, 382
    floor = blob(cx, cy, 96, 13.8, n=28, squash=0.92, wob=0.07)
    s.shape("floor", floor, model=2, size=34, height=8)
    # Broken perimeter — separate arcs with wide gaps south-west and
    # north-east, so it reads as a ruin rather than a perfect arena.
    for a0, a1 in [(-0.25, 1.25), (1.55, 2.75), (3.15, 4.35),
                   (4.75, 5.65)]:
        s.stroke("wall", arc(cx, cy, 103, 96, a0, a1, n=11), 8)
    # Worn entry causeway stepping down the south slope.
    for i, (x, y, w, h) in enumerate([(858, 480, 72, 34),
                                      (846, 520, 84, 32),
                                      (832, 560, 98, 30)]):
        pts = rough_rect(x, y, w, h, seed=401 + i * 3, jitter=7)
        s.shape("floor", pts, model=2, size=26, height=3 + i)
    # Collapsed side cells / broken annexes add architectural detail
    # without enlarging the central chamber into an empty disk.
    s.shape("floor", rough_rect(755, 340, 72, 54, seed=417, jitter=9),
            model=2, size=24, height=4)
    s.shape("floor", rough_rect(956, 322, 82, 48, seed=421, jitter=9),
            model=2, size=24, height=4)
    # Column stubs and fallen stones around the ring.
    pillars = [(805, 350, 24), (835, 302, 20), (910, 292, 22),
               (977, 350, 24), (965, 430, 18), (820, 430, 18)]
    for i, (x, y, sc) in enumerate(pillars):
        if i not in (2, 4):  # missing pieces make the ring ruinous
            s.prop("pillar", x, y, scale=sc, rot=(i - 2) * 0.12)
    for i, (x, y) in enumerate([(780, 390), (822, 466), (942, 462),
                                (1005, 397), (870, 285), (1015, 330)]):
        s.prop("rock", x, y, scale=12 + (i % 3) * 4)
    s.prop("ruin", 772, 426, scale=40, rot=-0.4)
    s.prop("ruin", 990, 302, scale=34, rot=0.7)
    # Furnishings are scaled up enough to read inside the small chamber.
    s.prop("table", cx - 8, cy - 5, scale=48, rot=0.28)
    s.prop("rug", cx + 18, cy + 28, scale=38, rot=-0.2)
    s.prop("crate", cx + 56, cy + 42, scale=34, rot=0.4)
    s.prop("barrel", cx - 55, cy + 38, scale=32)
    s.prop("campfire", cx + 48, cy - 48, scale=36)
    s.prop("statue", cx - 47, cy - 42, scale=34, rot=0.5)

    # Palms cling to the plateau foot and outer sand in uneven groups —
    # no regular ring. Larger hero palms overlap the rock lip like the
    # reference, while smaller ones trail toward the lagoon.
    palms = [(705, 285, 3.0), (650, 450, 2.75), (735, 590, 2.55),
             (1125, 548, 2.75), (1225, 318, 2.85), (1105, 205, 2.4),
             (830, 640, 2.25), (1325, 450, 2.35), (420, 215, 2.25),
             (325, 510, 2.5), (235, 310, 2.3), (1495, 270, 2.55),
             (1565, 520, 2.35), (520, 675, 2.2), (1015, 610, 2.2),
             (620, 185, 2.1)]
    for x, y, sc in palms:
        s.prop("tree", x, y, scale=24 * sc, seed=Scene.oakseed(x, y),
               species="palm")
    for x, y, sc in [(875, 155, 1.9), (1195, 665, 1.8), (555, 380, 1.9),
                     (1450, 385, 1.7), (340, 610, 1.6), (1265, 240, 1.6)]:
        s.prop("bush", x, y, scale=24 * sc)
    # Boulders scattered over separate sand shelves, clustered in twos
    # and threes rather than forming a geometric border.
    rocks = [(735, 120), (790, 145), (1215, 675), (1285, 715),
             (1605, 500), (250, 555), (330, 595), (1425, 585),
             (1510, 610), (545, 260), (590, 285), (1080, 155),
             (1730, 345), (180, 620)]
    for i, (x, y) in enumerate(rocks):
        s.prop("rock", x, y, scale=13 + (i % 4) * 4)
    s.save("ref-05")

def ref06():
    """Irrigated terrace sanctuary — uneven stacked paddies, narrow
    retaining stone lips, branching paths and small farmsteads."""
    s = Scene(1228, 733, name="ref-06")
    # Terraces are long, uneven shelves rather than concentric road-like
    # rings. Low outer shelves plus smaller upper lips give readable
    # elevation steps while leaving most of the surface for crops.
    shelves = [
        (250, 155, 285, 0.38, 2.9, 12), (270, 150, 190, 0.40, 3.9, 26),
        (905, 145, 285, 0.40, 4.5, 12), (890, 142, 185, 0.42, 5.5, 26),
        (260, 535, 285, 0.42, 7.8, 12), (275, 535, 180, 0.44, 8.6, 25),
        (940, 535, 270, 0.42, 9.3, 12), (925, 532, 170, 0.45, 10.2, 25),
        (610, 350, 205, 0.72, 5.6, 14), (610, 345, 128, 0.75, 6.8, 30),
    ]
    for cx, cy, rr, squash, sd, height in shelves:
        s.terrain(blob(cx, cy, rr, sd, n=28, squash=squash, wob=0.17),
                  height=height, size=28, rough=18)

    # Filled paddies. The crop polygon does the visual work; retaining
    # walls are narrow, sometimes incomplete and occasionally absent,
    # so the plots read as cultivated soil instead of empty stone pens.
    def add_plot(x, y, w, h, seed, walled=True):
        pts = rough_patch(x, y, w, h, seed=seed, jitter=9)
        s.shape("field", pts)
        if walled:
            closed = pts + [pts[0]]
            s.stroke("wall", closed, 4)

    blocks = [
        (42, 58, 5, 2, 84, 64, 500),
        (704, 56, 5, 2, 86, 62, 540),
        (52, 292, 4, 2, 88, 64, 580),
        (782, 295, 5, 2, 82, 64, 620),
        (145, 565, 5, 1, 88, 60, 660),
        (742, 565, 5, 1, 84, 60, 700),
    ]
    for x0, y0, cols, rows, dx, dy, seed0 in blocks:
        for row in range(rows):
            for col in range(cols):
                seed = seed0 + row * 17 + col * 7
                if rng(seed) < 0.10:
                    continue
                x = x0 + col * dx + (rng(seed + 1) - 0.5) * 16
                y = y0 + row * dy + (rng(seed + 2) - 0.5) * 12
                w = 62 + rng(seed + 3) * 26
                h = 34 + rng(seed + 4) * 18
                add_plot(x, y, w, h, seed, walled=rng(seed + 5) > 0.18)

    # Narrow irrigation channels sit inside the field belts. They stay
    # parallel to lanes and stop before junctions, avoiding spurious
    # bridge/crossing artifacts while still reading as watered paddies.
    s.river([(25, 218), (170, 204), (330, 216), (470, 204)], 10)
    s.river([(735, 218), (890, 202), (1045, 214), (1160, 206)], 10)
    s.river([(555, 465), (540, 540), (558, 625), (548, 720)], 9)
    s.river([(690, 465), (705, 540), (688, 625), (700, 720)], 9)

    # Branching sandy lanes remain broad and readable; fields may meet
    # their verges but the lanes are not encircled by terrace rings.
    roads = [
        ([(-30, 445), (170, 412), (360, 378), (520, 352),
          (700, 318), (930, 282), (1260, 256)], 15),
        ([(620, -30), (625, 125), (610, 285), (620, 445),
          (640, 580), (660, 740)], 13),
        ([(80, 740), (220, 595), (420, 485), (610, 350)], 11),
        ([(205, 412), (300, 290), (420, 155)], 10),
        ([(700, 318), (820, 420), (950, 490)], 10),
    ]
    for pts, width in roads:
        s.road(pts, width)

    # Central shrine: a small stone sanctuary on the upper terrace,
    # with broken ornament and standing stones rather than roundabout
    # rings. The clearing stays compact and architecturally detailed.
    cx, cy = 610, 332
    s.shape("floor", blob(cx, cy, 82, 12.8, n=22, squash=0.82, wob=0.10),
            model=2, size=28, height=6)
    for a0, a1 in [(0.2, 1.5), (1.9, 3.0), (3.55, 4.8), (5.2, 6.0)]:
        s.stroke("wall", arc(cx, cy, 88, 70, a0, a1, n=9), 5)
    s.prop("stonecircle", cx, cy + 8, scale=24 * 2.0)
    s.prop("statue", cx, cy - 12, scale=52)
    for i, (dx, dy) in enumerate([(-65, -15), (-35, -52), (38, -48),
                                  (68, -8), (48, 48), (-48, 46)]):
        if i != 4:
            s.prop("pillar", cx + dx, cy + dy, scale=18 + (i % 2) * 4)
    s.prop("ruin", cx + 74, cy + 48, scale=32, rot=0.4)

    # Small farmsteads sit beside lane junctions and field ends. Their
    # footprints vary but stay modest — working homesteads, not a town.
    homes = [
        (112, 260, 82, 52, -0.15), (190, 272, 74, 48, 0.18),
        (1015, 260, 84, 52, 0.12), (1100, 292, 72, 46, -0.2),
        (312, 495, 78, 50, 0.25), (872, 475, 76, 48, -0.25),
        (532, 558, 72, 46, 0.15), (694, 548, 70, 44, -0.2),
    ]
    for x, y, w, h, a in homes:
        s.house(x, y, w, h, a, model=2, color="#b7a955")
    # Work props clustered near the homesteads and shrine.
    for x, y, kind, sc, rot in [
        (155, 295, "cart", 66, 0.5), (1045, 322, "cart", 62, -0.8),
        (350, 522, "bale", 34, 0.2), (895, 505, "bale", 32, -0.3),
        (560, 282, "crate", 30, -0.7), (662, 380, "crate", 28, 0.2),
        (575, 378, "chest", 28, 0.5), (690, 302, "chest", 26, 1.1),
    ]:
        s.prop(kind, x, y, scale=sc, rot=rot)
    # Occasional stones and broadleaf trees soften the terrace lips,
    # but the field parcels remain the dominant texture.
    for i, (x, y) in enumerate([(320, 312), (385, 245), (535, 215),
                                (760, 220), (885, 312), (960, 375),
                                (240, 550), (400, 612), (720, 500),
                                (860, 570), (1040, 332), (140, 352)]):
        s.prop("rock", x, y, scale=10 + (i % 3) * 3)
    trees = [(240, 155), (400, 198), (880, 178), (1000, 208),
             (350, 500), (500, 590), (1100, 500), (150, 455),
             (760, 125), (560, 125), (960, 610), (60, 260),
             (430, 650), (1050, 65), (300, 332)]
    for i, (x, y) in enumerate(trees):
        s.prop("tree", x, y, scale=24 * (1.35 + (i % 3) * 0.25),
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
    roads = [
        ([(60, 300), (300, 340), (420, 380), (620, 360), (900, 330),
          (1100, 340)], 20),
        ([(-40, 820), (180, 800), (330, 830), (470, 860),
          (660, 840), (880, 800), (1120, 780)], 18),
        ([(80, 1150), (280, 1120), (420, 1180), (700, 1160),
          (1040, 1180)], 18),
    ]
    for pts, width in roads:
        s.road(pts, width)
    # Village — dense rows of golden thatched homes packed along
    # both banks of the branching river, plus a red-tile manor NW.
    homes = [
        # East bank, upper village — long thatched footprints packed
        # into irregular rows, but kept off the east-west lane at y≈340.
        (600, 80, 132, 84, 0.08), (850, 80, 138, 86, -0.12),
        (720, 155, 132, 84, 0.14), (980, 150, 126, 80, -0.08),
        (610, 240, 136, 88, 0.18), (840, 245, 142, 90, -0.10),
        (990, 250, 130, 82, 0.16),
        (620, 460, 136, 86, 0.10), (770, 515, 142, 90, -0.18),
        (930, 460, 136, 86, 0.16), (1025, 545, 126, 82, -0.22),
        # East bank of the lower arm, split by the y≈820 bridge lane.
        (790, 640, 142, 88, -0.12), (945, 690, 136, 86, 0.15),
        (830, 895, 126, 82, 0.24), (970, 950, 142, 90, -0.12),
        (820, 1045, 136, 86, 0.18), (955, 1095, 128, 82, -0.18),
        # Island wedge — fewer, diagonal large houses following the fork.
        (475, 720, 126, 82, 0.10),
        (430, 936, 124, 80, 0.22), (445, 1060, 118, 76, -0.12),
        # West bank lower village, clustered tightly along the water.
        (70, 640, 132, 86, -0.20), (170, 725, 132, 90, 0.12),
        (85, 900, 136, 88, 0.24), (115, 1015, 146, 92, -0.14),
        # South village, spread along both lower banks but still dense.
        (205, 1248, 136, 88, 0.15), (690, 1305, 138, 94, -0.18),
        (820, 1238, 138, 88, 0.24), (955, 1315, 132, 84, -0.15),
    ]
    for x, y, w, h, a in homes:
        # Individual hand-set dimensions replace the former repeating
        # small-box size; rotations stay narrow so the rows read organic.
        s.house(x, y, w, h, a + rng(x * 0.07 + y * 0.11) * 0.12 - 0.06,
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
    # Trees sprinkled along the banks and behind the homes. The same
    # deterministic exclusion masks protect the widened house rows,
    # both river arms and the bridge lanes, so vegetation frames the
    # village instead of swallowing its readable structure.
    village_clearings = [
        tube(upper, 56, wob=3.0, seed=601),
        tube(arm_w, 52, wob=3.0, seed=603),
        tube(arm_e, 52, wob=3.0, seed=605),
    ]
    village_clearings += [tube(pts, 30 if width >= 18 else 24,
                               wob=2.0, seed=607 + i * 2)
                          for i, (pts, width) in enumerate(roads)]
    village_clearings += [sh["points"] for sh in s.d["shapes"]
                          if sh["kind"] == "house"]
    s.scatter(blob(950, 200, 220, 15.3, n=20, squash=0.8), "tree",
              step=85, density=0.65, smin=1.7, smax=2.6, seed=81,
              avoid=village_clearings, species="oak")
    s.scatter(blob(120, 1250, 220, 17.5, n=20, squash=0.8), "tree",
              step=85, density=0.7, smin=1.7, smax=2.6, seed=83,
              avoid=village_clearings, species="oak")
    s.scatter(blob(540, 960, 180, 19.8, n=18, squash=0.9), "tree",
              step=80, density=0.6, smin=1.6, smax=2.4, seed=87,
              avoid=village_clearings, species="oak")
    s.scatter(blob(980, 1150, 200, 21.4, n=18, squash=0.8), "tree",
              step=82, density=0.6, smin=1.6, smax=2.4, seed=89,
              avoid=village_clearings, species="oak")
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
    s.terrain(bankpoly(river, 112, 150, side=1), height=22, size=34,
              rough=20, model=2)
    s.terrain(bankpoly(river, 112, 138, side=-1), height=22, size=34,
              rough=20, model=2)
    # Only a small clipped shelf remains in the south-west; the lower
    # canyon is predominantly jungle grass in the reference.
    s.terrain(blob(65, 1710, 270, 7.4, n=24, squash=0.65, wob=0.12),
              height=15, size=34, rough=22)
    # Footpaths converge on the west-bank camp instead of crossing the
    # river; keeping them out of the channel preserves the rapid read.
    camp_paths = [
        ([(-40, 1190), (130, 1215), (285, 1265), (410, 1330),
          (505, 1405)], 14),
        ([(80, 1930), (135, 1710), (220, 1535), (330, 1405),
          (410, 1330)], 12),
        ([(410, 1330), (475, 1245), (510, 1160)], 10),
    ]
    for pts, width in camp_paths:
        s.road(pts, width)
    # Jungle: palms massed on the canyon rims but the ref leaves the
    # grey rock faces and the river reading through — mixed species
    # and looser packing so the canyon still shows. Explicit masks hold
    # back canopy from the water, the rapid, paths and the camp meadow.
    jungle_clearings = [
        tube(river, 142, wob=4.0, seed=501),
        blob(445, 1360, 190, 43.3, n=20, squash=0.78, wob=0.12),
    ]
    jungle_clearings += [tube(pts, 34 if width >= 12 else 28,
                              wob=2.0, seed=505 + i * 2)
                         for i, (pts, width) in enumerate(camp_paths)]
    s.scatter(blob(280, 380, 330, 21.6, n=22, squash=1.0), "tree",
              step=78, density=0.6, smin=1.6, smax=2.6, seed=91,
              avoid=jungle_clearings, species="palm")
    s.scatter(blob(280, 1350, 330, 23.4, n=22, squash=1.0), "tree",
              step=78, density=0.6, smin=1.6, smax=2.6, seed=93,
              avoid=jungle_clearings, species="palm")
    s.scatter(blob(850, 1500, 300, 25.9, n=20, squash=0.9), "tree",
              step=78, density=0.6, smin=1.6, smax=2.6, seed=97,
              avoid=jungle_clearings, species="palm")
    s.scatter(blob(880, 300, 280, 33.1, n=20, squash=1.0), "tree",
              step=80, density=0.55, smin=1.5, smax=2.3, seed=99, avoid=jungle_clearings)
    s.scatter(blob(860, 900, 280, 38.9, n=20, squash=0.9), "tree",
              step=80, density=0.55, smin=1.5, smax=2.4, seed=101,
              avoid=jungle_clearings, species="palm")
    s.scatter(blob(260, 800, 300, 39.7, n=20, squash=0.95), "tree",
              step=80, density=0.55, smin=1.5, smax=2.4, seed=103,
              avoid=jungle_clearings, species="palm")
    for x, y in [(200, 700), (280, 1050), (820, 800), (900, 1150),
                 (160, 1750), (950, 350), (740, 480), (350, 560),
                 (880, 1000), (240, 350), (700, 1550), (940, 600),
                 (150, 500), (400, 1250), (860, 700), (960, 1650),
                 (200, 950), (780, 1750), (660, 380), (340, 850)]:
        s.prop("bush", x, y, scale=24 * 2.0, model=1)
    s.scatter(blob(700, 500, 200, 41.3, n=18, squash=0.9), "tree",
              step=78, density=0.55, smin=1.5, smax=2.3, seed=105,
              avoid=jungle_clearings, species="palm")
    # Compact camp on the west bank meadow below the bend — tents and
    # supplies form one legible cluster around the path junction, close
    # to the bank but never inside the river corridor.
    s.prop("tent", 410, 1258, scale=102, rot=0.4, model=0)
    s.prop("tent", 520, 1332, scale=92, rot=-0.8, model=0)
    s.prop("tent", 382, 1422, scale=86, rot=1.9, model=0)
    s.prop("cart", 145, 1495, scale=100, rot=1.5)
    s.prop("campfire", 470, 1428, scale=64)
    s.prop("crate", 455, 1328, scale=52)
    s.prop("crate", 392, 1348, scale=46)
    s.prop("barrel", 515, 1240, scale=50)
    s.prop("barrel", 535, 1428, scale=46)
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
    """Large clipped river fort — courtyard stronghold with integrated
    corner towers, multiple interior zones and readable tavern-scale
    furniture."""
    s = Scene(1080, 1288, name="ref-09")
    # River and the high rocky north bank stay confined to the top strip.
    river = [(-40, 60), (300, 90), (600, 60), (900, 110), (1120, 80)]
    s.terrain(blob(540, -42, 700, 6.8, n=32, squash=0.25, wob=0.10),
              height=18, size=34, rough=22)
    s.river(river, 80)
    s.terrain(bankpoly(river, 92, 120, side=1), height=15, size=30,
              rough=20)
    for x, y, sc in [(110, 170, 18), (245, 190, 15), (760, 180, 17),
                     (935, 185, 20), (1015, 155, 14)]:
        s.prop("rock", x, y, scale=sc)

    # The fort dominates the lower two thirds and is deliberately cut by
    # the bottom edge. A single stone-paved courtyard floor establishes
    # the large mass before smaller interior zones are layered over it.
    courtyard = [[170, 350], [885, 350], [925, 515], [910, 1305],
                 [185, 1315], [145, 1165], [160, 545]]
    s.shape("floor", courtyard, model=2, size=42, height=5)

    # Interior zones: several smaller room/floor footprints instead of
    # one undifferentiated hall. Their overlaps are intentional zones in
    # the courtyard plan, each with a distinct function.
    s.shape("room", rough_patch(305, 455, 330, 245, seed=701, jitter=7))
    s.shape("room", rough_patch(300, 735, 230, 205, seed=707, jitter=7))
    s.shape("room", rough_patch(555, 735, 215, 185, seed=711, jitter=7))
    s.shape("floor", rough_patch(625, 948, 235, 245, seed=717, jitter=10),
            model=2, size=28, height=3)
    s.shape("floor", rough_patch(250, 980, 285, 245, seed=719, jitter=10),
            model=2, size=28, height=3)
    s.shape("floor", rough_patch(680, 430, 165, 220, seed=723, jitter=8),
            model=2, size=26, height=3)

    # Crenellated curtain wall. It runs through the tower centres so the
    # round towers are structurally part of the corners rather than
    # separate circular buildings parked behind the fort.
    curtain = [[180, 360], [875, 360], [912, 520], [900, 1215],
               [865, 1315], [205, 1315], [160, 1185], [168, 540],
               [180, 360]]
    s.stroke("wall", curtain, 18, model=1)
    # Interior partition lines read as masonry walls around the hall and
    # service rooms, with openings implied by the gaps.
    s.stroke("wall", [[300, 728], [535, 728], [535, 945]], 8)
    s.stroke("wall", [[548, 728], [548, 925], [775, 925]], 8)
    s.stroke("wall", [[642, 430], [642, 655]], 7)

    # Four round red-roof towers integrated into the corners. Bottom
    # towers clip out of frame; the larger NE tower mirrors the ref.
    towers = [
        (185, 395, 98, 3.4), (875, 420, 132, 4.1),
        (195, 1195, 126, 5.2), (875, 1190, 132, 6.3),
    ]
    for cx, cy, rr, sd in towers:
        s.shape("house", blob(cx, cy, rr, sd, n=24, squash=1.0,
                              wob=0.025), model=1, color="#b04a38")

    # Main hall furniture — scaled up so individual pieces survive the
    # much larger footprint instead of reading as scattered tokens.
    s.prop("table", 430, 525, scale=52, rot=0.2)
    s.prop("table", 435, 635, scale=48, rot=-0.12)
    s.prop("table", 575, 555, scale=44, rot=1.4)
    s.prop("bed", 350, 492, scale=42, rot=math.pi / 2)
    s.prop("shelf", 595, 475, scale=40, rot=math.pi)
    s.prop("fireplace", 465, 468, scale=42)
    s.prop("rug", 470, 590, scale=64)
    s.prop("rug", 380, 670, scale=42)
    for x, y in [(365, 575), (500, 575), (365, 685), (505, 685)]:
        s.prop("crate", x, y, scale=27, rot=(x + y) * 0.01)
    # Kitchen/service room south-west.
    s.prop("table", 390, 820, scale=42, rot=0.85)
    s.prop("shelf", 330, 780, scale=36, rot=math.pi / 2)
    s.prop("fireplace", 350, 890, scale=36)
    for x, y in [(455, 785), (485, 825), (450, 890)]:
        s.prop("barrel", x, y, scale=30)
    # Storage/armoury south-east.
    s.prop("shelf", 725, 785, scale=38, rot=math.pi)
    s.prop("table", 650, 835, scale=40, rot=-0.35)
    for x, y in [(725, 875), (755, 875), (740, 900), (610, 785)]:
        s.prop("crate", x, y, scale=28)
    for x, y in [(600, 885), (630, 890), (660, 890)]:
        s.prop("barrel", x, y, scale=27)
    # Doors/windows anchored around the main room shell.
    s.prop("door", 470, 702, scale=40)
    s.prop("door", 642, 585, scale=36, rot=math.pi / 2)
    s.prop("window", 375, 455, scale=32)
    s.prop("window", 555, 455, scale=32)

    # Courtyard zones: smithy/stores, stable clutter, a central statue
    # and a well-like fountain. Larger props keep the broad floor legible.
    s.prop("fountain", 535, 1005, scale=52)
    s.prop("statue", 520, 1090, scale=48)
    s.prop("cart", 770, 1115, scale=105, rot=0.65)
    s.prop("bale", 700, 1030, scale=44, rot=-0.25)
    s.prop("bale", 760, 1015, scale=40, rot=0.4)
    s.prop("stall", 335, 1105, scale=82, rot=-0.2)
    s.prop("crate", 410, 1145, scale=34)
    s.prop("barrel", 455, 1165, scale=32)
    s.prop("tent", 115, 1005, scale=95, rot=-0.6)
    # Tower contents peeking through the larger NE tower.
    s.prop("shelf", 850, 390, scale=34, rot=0.2)
    s.prop("table", 895, 440, scale=38, rot=-0.5)
    s.prop("crate", 835, 465, scale=28)

    # Sparse edge trees only; the fort is the compositional mass now.
    s.scatter(blob(990, 720, 145, 27.3, n=18, squash=1.1), "tree",
              step=92, density=0.55, smin=1.6, smax=2.3, seed=101,
              species="oak")
    s.scatter(blob(70, 650, 145, 29.1, n=18, squash=1.1), "tree",
              step=90, density=0.52, smin=1.6, smax=2.3, seed=103,
              species="oak")
    s.save("ref-09")

def ref10():
    """2mt artpack gorge: cracked cliffs, cyan water, dark bridge."""
    s = Scene(1080, 1440, name="ref-10", artpack="ink")
    # Water in the gorge — a channel crossing the map west→east
    # through the middle, as in the ref screenshot.
    river = [(-40, 880), (220, 820), (460, 760), (700, 660),
             (900, 540), (1120, 430)]
    s.river(river, 130)
    # Gorge walls hugging the channel — narrow stone ribbons so the
    # mossy grass keeps the upper and lower thirds; small `size` +
    # low roughness avoid mechanical contour lines while the ink
    # crack texture (AlbedoPainter 2mt mesh) does the detail work.
    s.terrain(bankpoly(river, 122, 190, side=1), height=24, size=32,
              rough=20, model=2)
    s.terrain(bankpoly(river, 122, 190, side=-1), height=24, size=32,
              rough=20, model=2)
    s.terrain(blob(950, 1250, 320, 12.4, n=26, squash=0.8, wob=0.12),
              height=20, size=30, rough=20, model=2)
    # Road crossing the gorge N→S at the narrows — BridgePainter
    # spans it where the road meets the water: the dark plank bridge.
    s.road([(380, -40), (390, 240), (370, 480), (390, 700),
            (420, 950), (400, 1200), (430, 1480)], 19)
    s.road([(380, 240), (560, 200), (760, 180), (1000, 150)], 16)
    # Rocks in water with ripple rings — slightly larger than the first
    # pass so they survive the strong cyan water and ink cliff texture.
    for x, y, sc in [(250, 670, 21), (500, 710, 23), (750, 650, 20),
                     (950, 680, 22), (600, 680, 19), (835, 705, 18)]:
        s.prop("rock", x, y, scale=sc)
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
