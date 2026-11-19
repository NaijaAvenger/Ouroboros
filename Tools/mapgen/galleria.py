#!/usr/bin/env python3
"""
Layout generator for the Galleria map (v0.14).

The Galleria is an indoor, two-level mall built around a bank: a central atrium, four concourses,
four anchor wings (Bank, Luxe Wing, Market Hall, Services), four corner blocks of shops with staff
corridors behind them, four yards with a crew gate each, and an upper level of walkways and
secure rooms.

This script is the single source of truth for the map. It draws the plan on a grid of 2 m cells,
checks it (every room reachable from every crew gate, the upper level reachable by ramp, no prop
inside a wall, a door or another prop) and writes Assets/Editor/OuroborosGalleriaLayout.cs, which
the Unity builder (OuroborosGalleriaBuilder.cs) turns into a scene.

Run:  python3 Tools/mapgen/galleria.py            (writes the C# file)
      python3 Tools/mapgen/galleria.py --ascii    (also prints both floor plans)
"""
import math, os, sys
from collections import deque

CELL = 2
XMIN, XMAX, ZMIN, ZMAX = -110, 110, -90, 90
W, H = (XMAX - XMIN) // CELL, (ZMAX - ZMIN) // CELL
SOLID, FLOOR, VOID, DOOR = '#', '.', ' ', 'D'
LEVEL_Y = [0.0, 6.0]

def cx(x): return int((x - XMIN) // CELL)
def cz(z): return int((z - ZMIN) // CELL)
def wx(c): return XMIN + c * CELL + CELL / 2
def wz(c): return ZMIN + c * CELL + CELL / 2

grid = [[[SOLID] * W for _ in range(H)] for _ in range(2)]   # grid[level][cz][cx]
zone_of = [[[None] * W for _ in range(H)] for _ in range(2)]
zones, doors, ramps, items = [], [], [], []
problems = []

def norm(x0, z0, x1, z1): return min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1)

def carve(level, x0, z0, x1, z1, zone=None, poi=False, kind=FLOOR):
    """Turn a rectangle (metres) into floor. A zone name also registers a named area for the HUD."""
    x0, z0, x1, z1 = norm(x0, z0, x1, z1)
    assert x0 >= XMIN + CELL and x1 <= XMAX - CELL and z0 >= ZMIN + CELL and z1 <= ZMAX - CELL, (zone, x0, z0, x1, z1)
    for c in range(cz(z0), cz(z1)):
        for r in range(cx(x0), cx(x1)):
            grid[level][c][r] = kind
            if zone and kind != VOID: zone_of[level][c][r] = zone
    if zone and kind != VOID:
        zones.append((zone, level, (x0 + x1) / 2, (z0 + z1) / 2, x1 - x0, z1 - z0, poi))

def void(x0, z0, x1, z1): carve(1, x0, z0, x1, z1, kind=VOID)

def door(level, x0, z0, x1, z1, kind='Open', name='', arg=''):
    """Punch a doorway through a wall strip. The strip is one cell thick; the door runs along its long side."""
    x0, z0, x1, z1 = norm(x0, z0, x1, z1)
    along_x = (x1 - x0) >= (z1 - z0)
    for c in range(cz(z0), cz(z1)):
        for r in range(cx(x0), cx(x1)):
            grid[level][c][r] = DOOR
    width = (x1 - x0) if along_x else (z1 - z0)
    doors.append(dict(kind=kind, level=level, x=(x0 + x1) / 2, z=(z0 + z1) / 2, along_x=along_x, width=width, name=name, arg=arg))

def ramp(name, x0, z0, x1, z1, width):
    """A walkable ramp from level 0 at (x0, z0) up to level 1 at (x1, z1). The upper floor is opened above it."""
    ramps.append(dict(name=name, x0=x0, z0=z0, x1=x1, z1=z1, width=width))
    if abs(x1 - x0) > abs(z1 - z0): void(min(x0, x1), z0 - width / 2, max(x0, x1), z0 + width / 2)
    else: void(x0 - width / 2, min(z0, z1), x0 + width / 2, max(z0, z1))

def put(kind, level, x, z, yaw=0, arg='', foot=None, solid=True):
    items.append(dict(kind=kind, level=level, x=x, z=z, yaw=yaw, arg=arg, foot=foot, solid=solid))

# ------------------------------------------------------------------ level 1: the concourse level

carve(0, -22, -22, 22, 22, 'Atrium', poi=True)
carve(0, -8, 22, 8, 54, 'North Concourse')
carve(0, -8, -54, 8, -22, 'South Concourse')
carve(0, 22, -8, 70, 8, 'East Concourse')
carve(0, -70, -8, -22, 8, 'West Concourse')

# North anchor: the Bank
carve(0, -38, 56, 38, 68, 'Bank Lobby', poi=True)
door(0, -6, 54, 6, 56)
carve(0, -4, 70, 4, 76, 'Vault Corridor', poi=True)
door(0, -2, 68, 2, 70, 'Laser', 'Vault Corridor Lasers')
carve(0, -12, 78, 12, 88, 'Main Vault', poi=True)
# (v0.21) four Grand Vaults, one per wing: Grand:<ways in>:<perk>:<weak wall>. Was 'Passcode'.
door(0, -2, 76, 2, 78, 'Vault', 'Main Vault', 'Grand:Key:IronSkin:Main Vault Wall')
carve(0, 16, 70, 38, 88, 'Safe Deposit', poi=True)
door(0, 24, 68, 28, 70, 'Vault', 'Safe Deposit', 'Keycard:Red')
carve(0, -38, 70, -14, 88, 'Counting Room', poi=True)
door(0, -28, 68, -24, 70, 'Maglock', 'Counting Room Door')
door(0, -14, 80, -12, 84, 'WeakWall', 'Main Vault Wall')

# South anchor: Services
carve(0, -38, -68, 38, -56, 'Loading Dock', poi=True)
door(0, -6, -56, 6, -54)
carve(0, -14, -88, 14, -70, 'Truck Bay', poi=True)
door(0, -6, -70, 6, -68)
carve(0, 16, -88, 38, -70, 'Cash Cage', poi=True)
door(0, 24, -70, 28, -68, 'Vault', 'Cash Cage', 'Grand:Drill:QuickHands:')   # (v0.21) was 'Keycard:Blue'
carve(0, -38, -88, -16, -70, 'Power Room')
door(0, -28, -70, -24, -68)

# East anchor: the Luxe Wing
carve(0, 72, -10, 108, 10, 'Art Gallery', poi=True)
door(0, 70, -6, 72, 6)
carve(0, 74, 12, 106, 38, 'Jewellers', poi=True)
door(0, 80, 10, 84, 12, 'Shutter', 'Jewellers Shutter West')
door(0, 96, 10, 100, 12, 'Shutter', 'Jewellers Shutter East')
carve(0, 74, -24, 106, -12, 'Auction House', poi=True)
carve(0, 74, -38, 88, -24, 'Auction House')
door(0, 88, -12, 92, -10)
carve(0, 90, -38, 106, -26, 'Auction Strongroom', poi=True)
door(0, 96, -26, 100, -24, 'Vault', 'Auction Strongroom', 'Grand:Crack:HotShots:Strongroom Wall')   # (v0.21) was 'Lockdown'
door(0, 88, -34, 90, -30, 'WeakWall', 'Strongroom Wall')

# West anchor: the Market Hall
carve(0, -108, -10, -72, 10, 'Food Court', poi=True)
door(0, -72, -6, -70, 6)
carve(0, -106, 12, -74, 26, 'Supermarket')
carve(0, -88, 26, -74, 38, 'Supermarket')
door(0, -100, 10, -96, 12)
door(0, -84, 10, -80, 12)
carve(0, -106, 28, -90, 38, 'Stockroom', poi=True)
door(0, -90, 30, -88, 34, 'Maglock', 'Stockroom Door')
carve(0, -106, -24, -74, -12, 'Dispensary')
carve(0, -88, -38, -74, -24, 'Dispensary')
door(0, -92, -12, -88, -10)
carve(0, -106, -38, -90, -26, 'Cold Store', poi=True)
door(0, -100, -26, -96, -24, 'Vault', 'Cold Store', 'Grand:Key+Drill+Crack:SecondWind:')   # (v0.21) was 'PlayerCard'

SHOPS = {
    (1, 1):   ['Tailor', 'Electronics', 'Cafe', 'Barber'],
    (-1, 1):  ['Bookshop', 'Florist', 'Phone Repair', 'Shoe Shop'],
    (1, -1):  ['Spice Merchant', 'Record Store', 'Pawn Shop', 'Bakery'],
    (-1, -1): ['Camera Shop', 'Perfumery', 'Luggage', 'Hardware'],
}
HALLS = {(1, 1): 'Arcade', (-1, 1): 'Cinema Foyer', (1, -1): 'Gym', (-1, -1): 'Toy Store'}
UPPER = {(1, 1): 'Security HQ', (-1, 1): "Manager's Office", (1, -1): 'Server Room', (-1, -1): 'VIP Lounge'}
CORNER = {(1, 1): 'North-East', (-1, 1): 'North-West', (1, -1): 'South-East', (-1, -1): 'South-West'}
CREW = {(-1, -1): 'TeamAlpha', (1, -1): 'TeamBravo', (-1, 1): 'TeamCharlie', (1, 1): 'TeamDelta'}
CREW_NAME = {'TeamAlpha': 'Lagos', 'TeamBravo': 'Nairobi', 'TeamCharlie': 'Accra', 'TeamDelta': 'Joburg'}

def quadrant(sx, sz):
    def R(level, x0, z0, x1, z1, zone=None, poi=False): carve(level, sx * x0, sz * z0, sx * x1, sz * z1, zone, poi)
    def D(level, x0, z0, x1, z1, kind='Open', name='', arg=''): door(level, sx * x0, sz * z0, sx * x1, sz * z1, kind, name, arg)
    shops, corner = SHOPS[(sx, sz)], CORNER[(sx, sz)]

    # Shops on the two concourses, each with a front door and a staff door at the back
    R(0, 10, 24, 28, 38, shops[0]); D(0, 8, 30, 10, 34)
    R(0, 10, 40, 28, 54, shops[1]); D(0, 8, 44, 10, 48)
    R(0, 24, 10, 44, 22, shops[2]); D(0, 32, 8, 36, 10)
    R(0, 46, 10, 70, 22, shops[3]); D(0, 56, 8, 60, 10)
    # Staff corridors behind them, in an L
    R(0, 30, 24, 36, 54, corner + ' Staff Corridor')
    R(0, 36, 24, 72, 30, corner + ' Staff Corridor')
    D(0, 28, 26, 30, 30); D(0, 28, 48, 30, 52); D(0, 38, 22, 42, 24); D(0, 62, 22, 66, 24)
    # The hall in the middle of the block
    R(0, 38, 32, 72, 54, HALLS[(sx, sz)], poi=True)
    D(0, 52, 30, 56, 32); D(0, 36, 40, 38, 44)
    # Yard and crew gate
    R(0, 40, 56, 84, 88, corner + ' Yard')
    R(0, 74, 40, 108, 64, corner + ' Yard')
    crew = CREW[(sx, sz)]
    R(0, 86, 66, 108, 88, CREW_NAME[crew] + ' Gate')
    D(0, 84, 74, 86, 78); D(0, 94, 64, 98, 66)
    D(0, 52, 54, 56, 56); D(0, 72, 44, 74, 48)
    D(0, 38, 60, 40, 64)                                   # yard into the anchor hall on this side
    # Staff corridor into the anchor hall (north: the bank keeps its staff door locked)
    D(0, 30, 54, 34, 56, 'Maglock' if sz > 0 else 'Open', corner + ' Staff Door')
    # Staff corridor into the east or west anchor
    if sx > 0 and sz > 0: D(0, 72, 26, 74, 30, 'WeakWall', 'Jewellers Wall')
    elif sx > 0:          D(0, 72, 26, 74, 30, 'Maglock', 'Auction Staff Door')
    else:                 D(0, 72, 26, 74, 30)

    # ---- upper level over this block
    up = UPPER[(sx, sz)]
    R(1, 10, 24, 36, 54, corner + ' Upper Mall')
    R(1, 24, 10, 72, 30, corner + ' Upper Mall')
    D(1, 8, 28, 10, 34); D(1, 8, 44, 10, 50); D(1, 30, 8, 36, 10); D(1, 56, 8, 62, 10)
    D(1, 12, 22, 16, 24); D(1, 22, 12, 24, 16)
    R(1, 38, 32, 72, 54, up, poi=True)
    kind = {'Security HQ': 'Shutter', "Manager's Office": 'Shutter', 'Server Room': 'Laser', 'VIP Lounge': 'Open'}[up]
    D(1, 52, 30, 56, 32, kind, up + ' Door A')
    D(1, 36, 40, 38, 44, kind if up != "Manager's Office" else 'Maglock', up + ' Door B')
    # Staff stairs: up the corridor behind the shops
    ramp(corner + ' Staff Stairs', sx * 34, sz * 30, sx * 34, sz * 42, 4)

for q in SHOPS: quadrant(*q)

# ------------------------------------------------------------------ level 2: walkways over the concourses
for x in range(-22, 22, CELL):
    for z in range(-22, 22, CELL):
        ring = max(abs(x + 1), abs(z + 1)) > 18
        grid[1][cz(z)][cx(x)] = FLOOR if ring else VOID
        if ring: zone_of[1][cz(z)][cx(x)] = 'Sky Gallery'
zones.append(('Sky Gallery', 1, 0, 0, 44, 44, False))

def arm_upper(axis, sign, name):
    """Walkways down both sides of a concourse, a void between them, and two bridges."""
    for a in range(22, 54 if axis == 'z' else 70, CELL):
        for b in range(-8, 8, CELL):
            side = abs(b + 1) > 4
            x, z = (b, sign * (a + 1) - 1) if axis == 'z' else (sign * (a + 1) - 1, b)
            grid[1][cz(z)][cx(x)] = FLOOR if side else VOID
            if side: zone_of[1][cz(z)][cx(x)] = name
    length = 54 if axis == 'z' else 70
    zones.append((name, 1, 0 if axis == 'z' else sign * (22 + length) / 2, sign * (22 + length) / 2 if axis == 'z' else 0,
                  16 if axis == 'z' else length - 22, length - 22 if axis == 'z' else 16, False))
    def bridge(a0, a1):
        if axis == 'z': carve(1, -4, sign * a0, 4, sign * a1)
        else: carve(1, sign * a0, -4, sign * a1, 4)
    bridge(28, 32)
    bridge(length - 6, length - 2)
    # The escalator: up the middle of the concourse, landing on the inner bridge
    if axis == 'z': ramp(name + ' Escalator', 0, sign * 44, 0, sign * 32, 4)
    else: ramp(name + ' Escalator', sign * 44, 0, sign * 32, 0, 4)

arm_upper('z', 1, 'North Gallery'); arm_upper('z', -1, 'South Gallery')
arm_upper('x', 1, 'East Gallery'); arm_upper('x', -1, 'West Gallery')

# ------------------------------------------------------------------ checks: is everything reachable?
def is_open(level, c, r):
    return 0 <= c < H and 0 <= r < W and grid[level][c][r] in (FLOOR, DOOR)

def flood(starts):
    seen = set(starts)
    queue = deque(starts)
    links = {}
    for rp in ramps:
        a, b = (0, cz(rp['z0']), cx(rp['x0'])), (1, cz(rp['z1'] + (1 if rp['z1'] < rp['z0'] else -1) * 0 - 0.01 if False else rp['z1']), cx(rp['x1']))
        # the top of a ramp lands on the cell just past its end
        dz = (rp['z1'] > rp['z0']) - (rp['z1'] < rp['z0']); dx = (rp['x1'] > rp['x0']) - (rp['x1'] < rp['x0'])
        top = (1, cz(rp['z1'] + dz * 1), cx(rp['x1'] + dx * 1))
        bottom = (0, cz(rp['z0'] - dz * 1), cx(rp['x0'] - dx * 1))
        links.setdefault(bottom, []).append(top); links.setdefault(top, []).append(bottom)
        rp['top'], rp['bottom'] = top, bottom
    while queue:
        level, c, r = queue.popleft()
        near = [(level, c + 1, r), (level, c - 1, r), (level, c, r + 1), (level, c, r - 1)] + links.get((level, c, r), [])
        for n in near:
            if n not in seen and is_open(*n):
                seen.add(n); queue.append(n)
    return seen

gate = (0, cz(77), cx(97))
reached = flood([gate])
for rp in ramps:
    for end in ('top', 'bottom'):
        if not is_open(*rp[end]): problems.append(f"ramp '{rp['name']}' {end} is not on a floor: {rp[end]}")
for level in range(2):
    for c in range(H):
        for r in range(W):
            if grid[level][c][r] in (FLOOR, DOOR) and (level, c, r) not in reached:
                problems.append(f"unreachable floor at level {level + 1} ({wx(r)}, {wz(c)}) zone={zone_of[level][c][r]}")
                break
        else: continue
        break

# ------------------------------------------------------------------ what goes in the rooms
door_cells = [(d['level'], d['x'], d['z'], d['width']) for d in doors]
placed = []   # (level, x0, z0, x1, z1) of solid things already down

def clear(level, x, z, w, d, yaw=0, door_gap=1.2, must_floor=True):
    if yaw in (90, 270): w, d = d, w
    x0, z0, x1, z1 = x - w / 2, z - d / 2, x + w / 2, z + d / 2
    for c in range(cz(z0 + 0.01), cz(z1 - 0.01) + 1):
        for r in range(cx(x0 + 0.01), cx(x1 - 0.01) + 1):
            if not (0 <= c < H and 0 <= r < W) or grid[level][c][r] != FLOOR: return 'not on open floor'
    for (dl, dx_, dz_, dw) in door_cells:
        if dl == level and abs(dx_ - x) < w / 2 + dw / 2 + door_gap and abs(dz_ - z) < d / 2 + dw / 2 + door_gap: return 'blocks a door'
    for rp in ramps:
        rx0, rx1 = min(rp['x0'], rp['x1']) - rp['width'] / 2 - 1, max(rp['x0'], rp['x1']) + rp['width'] / 2 + 1
        rz0, rz1 = min(rp['z0'], rp['z1']) - rp['width'] / 2 - 1, max(rp['z0'], rp['z1']) + rp['width'] / 2 + 1
        dz = (rp['z1'] > rp['z0']) - (rp['z1'] < rp['z0']); dx = (rp['x1'] > rp['x0']) - (rp['x1'] < rp['x0'])
        # keep the foot and the head of every ramp clear as well
        rx0 -= 3 * abs(dx); rx1 += 3 * abs(dx); rz0 -= 3 * abs(dz); rz1 += 3 * abs(dz)
        if x1 > rx0 and x0 < rx1 and z1 > rz0 and z0 < rz1: return 'in the way of a ramp'
    for (pl, a0, b0, a1, b1) in placed:
        if pl == level and x1 > a0 - 0.5 and x0 < a1 + 0.5 and z1 > b0 - 0.5 and z0 < b1 + 0.5: return 'overlaps another piece'
    return None

FOOT = {'Shelf': (6, 0.9), 'Counter': (4, 0.9), 'Table': (2.2, 2.2), 'Bench': (2.0, 0.6), 'Planter': (2.2, 2.2), 'Kiosk': (3, 3),
        'Rack': (2.4, 0.8), 'Mannequin': (0.7, 0.7), 'Sofa': (2.6, 1.0), 'Desk': (2.4, 1.2), 'Servers': (0.8, 1.0), 'Crate': (1.2, 1.2),
        'CrateStack': (2.4, 1.3), 'Pallet': (1.2, 1.0), 'Barrels': (1.6, 1.6), 'Forklift': (1.4, 2.6), 'Van': (2.2, 5.0),
        'Truck': (2.8, 7.0), 'Stall': (3.0, 2.0), 'Fountain': (5.4, 5.4), 'Lockers': (3.6, 0.6), 'Detector': (1.6, 0.4),
        'Arcade': (1.0, 1.0), 'Treadmill': (1.0, 2.0), 'Seats': (4.0, 1.0), 'Plinth': (1.1, 1.1),
        'B:Crate': (1.1, 1.1), 'B:DisplayCase': (1.2, 1.2), 'B:VendingMachine': (1.1, 0.9), 'B:Atm': (0.9, 0.7), 'B:CashRegister': (0.6, 0.6),
        'B:Locker': (1.2, 0.6), 'B:StrongBox': (1.1, 0.9), 'B:Safe': (0.9, 0.9), 'B:AirdropCrate': (1.6, 1.6),
        'Turret': (1.0, 1.0), 'Power': (0.8, 0.8), 'Terminal': (1.0, 0.5), 'Vault': (1.5, 1.5),
        'CashOut': (1.6, 1.2), 'VaultPerk': (1.5, 1.5), 'VaultLock': (0.9, 0.5)}
skipped = []

def place(kind, level, x, z, yaw=0, arg='', strict=False, gap=1.2):
    """Put down a solid piece if the spot is free. Strict pieces are gameplay: failing to place one is an error."""
    w, d = FOOT[kind]
    why = clear(level, x, z, w, d, yaw, door_gap=gap)
    if why:
        (problems if strict else skipped).append(f"{kind} at L{level + 1} ({x}, {z}): {why}")
        return False
    if yaw in (90, 270): w, d = d, w
    placed.append((level, x - w / 2, z - d / 2, x + w / 2, z + d / 2))
    put(kind, level, x, z, yaw, arg)
    return True

def mark(kind, level, x, z, yaw=0, arg=''):
    """Something with no footprint: a spawn, an exit, a patrol point, a camera on the ceiling."""
    c, r = cz(z), cx(x)
    if not (0 <= c < H and 0 <= r < W) or grid[level][c][r] not in (FLOOR, DOOR):
        problems.append(f"{kind} at L{level + 1} ({x}, {z}) is not on a floor")
    put(kind, level, x, z, yaw, arg, solid=False)

# ---- extraction, spawns
mark('Exit', 0, 0, 0, arg='Central Getaway|central')
mark('Exit', 0, 34, 62, arg='Bank Staff Exit')
mark('Exit', 0, 104, 0, arg='Gallery Exit')
mark('Exit', 0, 0, -84, arg='Truck Bay Exit')
mark('Exit', 0, -104, 0, arg='Metro Exit')
for (sx, sz), crew in CREW.items():
    mark('Spawn', 0, sx * 97, sz * 77, arg=crew)

# ---- cash-out stations (v0.16): public places between the vaults and the exits, two on each level,
# each reachable from several sides so a cash-out can be attacked as well as held
place('CashOut', 0, -5, 38, 90, strict=True)    # North Concourse, beside the escalator to the Bank
place('CashOut', 0, -5, -38, 90, strict=True)   # South Concourse, beside the escalator to the Truck Bay
place('CashOut', 1, 66, 0, 90, strict=True)     # upper level, east
place('CashOut', 1, -66, 0, 90, strict=True)    # upper level, west

# ---- the atrium: the emblem fountain is off-centre so the getaway stays open ground
for sx in (-1, 1):
    for sz in (-1, 1):
        place('Planter', 0, sx * 14, sz * 14)
        place('Bench', 0, sx * 14, sz * 9, 90)
        mark('Lift', 0, sx * 16.5, sz * 5, arg='6')
place('Kiosk', 0, 0, 14); place('Kiosk', 0, 0, -14)
mark('Gravity', 0, 20.6, 20.6, arg='Atrium Gravity|0|6|0|36|12|36')
mark('Power', 0, -20, 20, arg='Atrium Power')

# ---- concourses: vending machines, cash machines, benches, a gate that can be shut
def concourse(axis, sign):
    def at(a, b): return (b, sign * a) if axis == 'z' else (sign * a, b)
    yaw_side = 90 if axis == 'z' else 0
    for a, b, kind, arg in [(26, -6.9, 'B:VendingMachine', 'Common|0|60'), (38, 6.9, 'B:Atm', 'Common|0|160'), (50, -6.9, 'B:VendingMachine', 'Common|0|50')]:
        x, z = at(a, b)
        yaw = (90 if b < 0 else 270) if axis == 'z' else (0 if b < 0 else 180)
        place(kind, 0, x, z, yaw, arg)
    for a in (24, 48):
        x, z = at(a, 5.5); place('Bench', 0, x, z, yaw_side)
        x, z = at(a, -5.5); place('Planter', 0, x, z)
    x, z = at(23, 0); mark('Gate', 0, x, z, 0 if axis == 'z' else 90, arg='Concourse Gate')
concourse('z', 1); concourse('z', -1); concourse('x', 1); concourse('x', -1)

# ---- the Bank
for x in (-30, -20, -10, 10, 20): place('Counter', 0, x, 66.2, 0)
for x in (-34, 30): place('B:Atm', 0, x, 57, 0, 'Common|0|180')
for x in (-3, 3): place('Detector', 0, x, 58, 0)
place('Sofa', 0, -18, 58, 0); place('Sofa', 0, 18, 58, 0); place('Planter', 0, -36, 62, 0, 'shrub'); place('Desk', 0, 12, 60, 0)
mark('Camera', 0, 0, 57, arg='0|64'); mark('Camera', 0, -36, 67, arg='-20|58'); mark('Camera', 0, 0, 75, arg='0|70')
place('Turret', 0, 10, 79.5, 0, strict=True)
# [v0.20] place('Vault', 0, 0, 84, 0, 'Main Vault|Legendary|2|true|10|3|Main Vault|Main Vault Wall', strict=True)
place('VaultPerk', 0, 0, 84, 0, 'Main Vault', strict=True)   # (v0.21) the room is the vault; this is its perk pedestal
for x in (-9, 9):
    for z in (80, 86): place('B:StrongBox', 0, x, z, 90, 'Rare|1|150')
place('Lockers', 0, -4, 87.3, 0); place('Lockers', 0, 4, 87.3, 0)
place('Vault', 0, 27, 82, 0, 'Safe Deposit|Rare|3|false|7|1|Safe Deposit|', strict=True)
for x in (18.5, 22, 32, 35.5): place('B:Locker', 0, x, 87.3, 0, 'Common|1|40')
for z in (74, 78, 82): place('B:Locker', 0, 37.3, z, 90, 'Common|0|90')
place('Desk', 0, -30, 80, 90); place('Desk', 0, -22, 80, 90)
place('B:Safe', 0, -36, 86, 0, 'Rare|1|200'); place('B:StrongBox', 0, -18, 86, 0, 'Common|1|220'); place('Lockers', 0, -27, 87.3, 0)
mark('Post', 0, -8, 60, arg='elite'); mark('Post', 0, 8, 60); mark('Post', 0, 0, 73, arg='elite')
mark('Post', 0, -2.5, 71.5)   # (v0.21) the Main Vault's second guard: its key falls when both are down

# ---- Services: loading dock, truck bay, cash cage, power room
for x in (-30, -22, 22, 30): place('CrateStack', 0, x, -58, 0)
for x in (-14, 14): place('Pallet', 0, x, -66, 0)
place('Forklift', 0, -32, -64, 90); place('Barrels', 0, 34, -64); place('B:Crate', 0, -18, -58, 0, 'Common|1|0')
place('B:Crate', 0, 18, -58, 0, 'Common|0|80'); place('B:Crate', 0, 31, -66.8, 0, 'Common|1|0')
place('Truck', 0, 0, -80, 0)
for x, z in ((-8, -76), (8, -76), (-8, -84)): place('B:StrongBox', 0, x, z, 0, 'Rare|1|250|dormant')
place('Van', 0, 9, -84, 0, 'white'); place('Barrels', 0, -11, -72)
place('Turret', 0, 11, -72, 0, strict=True)
# [v0.20] place('Vault', 0, 27, -80, 0, 'Cash Cage|Rare|3|true|7|1|Cash Cage|', strict=True)
place('VaultPerk', 0, 27, -80, 0, 'Cash Cage', strict=True)
for x in (18, 22, 32, 36): place('B:StrongBox', 0, x, -87, 0, 'Common|0|260')
place('Power', 0, -36, -86, 0, 'Services Power', strict=True)
for x in (-30, -26, -22): place('Servers', 0, x, -87.2, 0)
place('Lockers', 0, -37.3, -78, 90); place('B:Locker', 0, -17.3, -80, 90, 'Common|1|0'); place('Desk', 0, -26, -76, 0)
mark('Camera', 0, 0, -57, arg='0|-64'); mark('Camera', 0, 12, -71, arg='0|-80'); mark('Post', 0, 0, -62); mark('Post', 0, -27, -74)
mark('Post', 0, 26, -64.5, arg='elite')   # (v0.21) on the Cash Cage door: whoever drills it has to get past this one

# ---- the Luxe Wing: gallery, jewellers, auction house
for x in (80, 88, 96): 
    for z in (-6, 6): place('B:DisplayCase', 0, x, z, 0, 'Rare|1|0' if x == 88 else 'Common|1|0')
place('Plinth', 0, 100, 0, 0, 'Leopard'); place('Bench', 0, 84, 0, 0); place('Bench', 0, 92, 0, 0)
for x in (78, 86, 94, 102):
    for z in (20, 30): place('B:DisplayCase', 0, x, z, 0, 'Rare|1|0' if (x + z) % 16 == 0 else 'Common|1|60')
place('Counter', 0, 90, 36.5, 0); place('B:CashRegister', 0, 96, 36.5, 0, 'Common|0|140'); place('B:Safe', 0, 104.5, 36.5, 0, 'Rare|1|120')
place('Turret', 0, 76, 36, 0, strict=True)
mark('Camera', 0, 90, 13, arg='90|26'); mark('Camera', 0, 105, 0, arg='90|0')
for x in (80, 86, 92, 98): place('Seats', 0, x, -16, 0)
place('Desk', 0, 90, -22, 0); place('Plinth', 0, 102, -22, 0, 'Sword'); place('Sofa', 0, 81, -27, 0)
place('B:Safe', 0, 76, -37, 0, 'Rare|0|180')   # [v0.20] ; mark('Keycard', 0, 80, -36, arg='Blue'): the Cash Cage is drilled now
# [v0.20] place('Vault', 0, 98, -33, 0, 'Auction Strongroom|Rare|3|true|8|2|Auction Strongroom|Strongroom Wall', strict=True)
place('VaultPerk', 0, 98, -33, 0, 'Auction Strongroom', strict=True)
place('VaultLock', 0, 94.5, -22.8, 0, 'Auction Strongroom', strict=True, gap=0.2)   # the lock console, outside, beside the door
for z in (-37, -29): place('B:StrongBox', 0, 104.5, z, 90, 'Rare|1|0')
mark('Post', 0, 76, 0); mark('Post', 0, 94, -20, arg='elite'); mark('Camera', 0, 75, -13, arg='90|-20')

# ---- the Market Hall: food court, supermarket, stockroom, dispensary, cold store
for x in (-100, -92, -84):
    for z in (-5, 5): place('Table', 0, x, z)
place('Stall', 0, -104, 8, 90, 'TeamCharlie'); place('Stall', 0, -104, -8, 90, 'TeamAlpha')
place('B:VendingMachine', 0, -74, 9.3, 180, 'Common|0|70'); place('B:VendingMachine', 0, -74, -9.3, 0, 'Common|0|70')
for z in (16, 22): 
    for x in (-100, -90, -80): place('Shelf', 0, x, z, 0)
place('B:CashRegister', 0, -78, 13, 0, 'Common|0|120'); place('B:CashRegister', 0, -92, 13, 0, 'Common|0|120')
place('Shelf', 0, -81, 32, 90); place('B:Crate', 0, -76, 36, 0, 'Common|1|0')
place('Vault', 0, -98, 33, 0, 'Stockroom|Common|4|false|5|1||', strict=True)
for x in (-104, -100, -94): place('B:Crate', 0, x, 37, 0, 'Common|1|0' if x != -100 else 'Common|0|90')
place('CrateStack', 0, -104, 30, 90)
for x in (-100, -90, -80): place('Shelf', 0, x, -18, 0)
place('Counter', 0, -84, -13, 0); place('B:CashRegister', 0, -78, -13, 0, 'Common|0|130'); place('B:Locker', 0, -75, -36, 90, 'Common|1|0')
place('Shelf', 0, -81, -32, 90)
# [v0.20] place('Vault', 0, -98, -32, 0, 'Cold Store|Rare|3|false|7|1|Cold Store|', strict=True)
place('VaultPerk', 0, -98, -32, 0, 'Cold Store', strict=True)
place('VaultLock', 0, -94.5, -22.8, 0, 'Cold Store', strict=True, gap=0.2)
for x in (-104, -92): place('B:Crate', 0, x, -37, 0, 'Rare|1|0' if x == -104 else 'Common|1|0')
mark('Power', 0, -107, 9, arg='Market Power'); mark('Camera', 0, -90, 13, arg='-90|20'); mark('Camera', 0, -107, 0, arg='-90|0')
# [v0.20] mark('Post', 0, -76, 0); mark('Post', 0, -98, -22)
mark('Post', 0, -76, 0); mark('Post', 0, -98, -22, arg='elite'); mark('Post', 0, -93, -21)   # (v0.21) the Cold Store's two guards hold its key
mark('Power', 0, 107, 9, arg='Luxe Power'); mark('Power', 0, 37, 67, arg='Bank Power')

# ---- the four corner blocks
def block(sx, sz):
    def P(kind, level, x, z, yaw=0, arg='', strict=False):
        if sx < 0: yaw = (360 - yaw) % 360 if yaw in (90, 270) else yaw
        if sz < 0: yaw = (180 - yaw) % 360 if yaw in (0, 180) else yaw
        return place(kind, level, sx * x, sz * z, yaw, arg, strict)
    def M(kind, level, x, z, arg=''): mark(kind, level, sx * x, sz * z, arg=arg)
    hall, up = HALLS[(sx, sz)], UPPER[(sx, sz)]

    # Shops: a counter with a till, shelves or racks, and something worth breaking
    P('Counter', 0, 16, 25.2, 0); P('B:CashRegister', 0, 22, 25.2, 0, 'Common|0|110'); P('Rack', 0, 14, 34, 0); P('Rack', 0, 22, 34, 0); P('Mannequin', 0, 12, 27)
    P('Shelf', 0, 19, 52, 0); P('Shelf', 0, 19, 42, 0); P('B:CashRegister', 0, 24, 46, 0, 'Common|0|110'); P('B:DisplayCase', 0, 13, 47, 0, 'Common|1|0')
    P('Table', 0, 28, 18); P('Table', 0, 40, 18); P('Counter', 0, 27.5, 11.2, 0); P('B:CashRegister', 0, 41, 11.2, 0, 'Common|0|90'); P('B:VendingMachine', 0, 43, 16, 270, 'Common|0|60')
    P('Seats', 0, 52, 19, 0); P('Seats', 0, 64, 19, 0); P('Counter', 0, 50, 11.2, 0); P('B:CashRegister', 0, 66, 11.2, 0, 'Common|0|100')
    # Staff corridor: lockers, crates and a delivery left where it was dropped
    P('B:Locker', 0, 31, 52, 90, 'Common|1|0'); P('B:Crate', 0, 46, 28.5, 0, 'Common|0|70'); P('CrateStack', 0, 68, 28.5, 0); P('B:Crate', 0, 58, 28.5, 0, 'Common|1|0')
    # The hall
    if hall == 'Arcade':
        for x in (42, 46, 50, 60, 64, 68): P('Arcade', 0, x, 52.5, 0)
        for x in (44, 50, 62): P('Arcade', 0, x, 40, 0)
    elif hall == 'Cinema Foyer':
        for z in (38, 42, 46): P('Seats', 0, 62, z, 0)
        P('Counter', 0, 44, 52.5, 0); P('B:CashRegister', 0, 50, 52.5, 0, 'Common|0|150')
    elif hall == 'Gym':
        for x in (42, 46, 50, 62, 66): P('Treadmill', 0, x, 51.5, 0)
        P('Lockers', 0, 62, 33, 0); P('B:Locker', 0, 68, 33, 0, 'Common|1|0')
    else:
        for x in (42, 50, 62): P('Shelf', 0, x, 50, 0)
        P('Shelf', 0, 46, 40, 0); P('Shelf', 0, 64, 40, 0)
    P('B:VendingMachine', 0, 39, 36, 90, 'Common|0|80'); P('B:Atm', 0, 70.5, 36, 270, 'Common|0|150')
    P('Vault', 0, 56, 44, 0, hall + ' Safe|Common|2|false|5|1||', strict=True)
    # Yard: a car park with cover, and the way home
    P('Van', 0, 50, 70, 0, 'grey'); P('Van', 0, 62, 80, 90, 'blue'); P('Barrels', 0, 44, 84); P('CrateStack', 0, 78, 60, 0)
    P('Van', 0, 90, 46, 90, 'red'); P('Planter', 0, 80, 52); P('Planter', 0, 60, 60); P('Crate', 0, 100, 60); P('B:Crate', 0, 70, 86, 0, 'Common|1|0')
    P('Crate', 0, 104, 70); P('CrateStack', 0, 90, 86, 0)

    # ---- upper level
    for x, z in ((20, 30), (20, 46), (26, 51)): P('Planter', 1, x, z, 0, 'shrub')
    P('Kiosk', 1, 22, 38); P('Bench', 1, 28, 28, 0); P('B:VendingMachine', 1, 11, 53, 0, 'Common|0|60')
    P('Kiosk', 1, 48, 20); P('Bench', 1, 40, 14, 0); P('Bench', 1, 64, 14, 0); P('B:Atm', 1, 70.6, 20, 270, 'Common|0|160'); P('Planter', 1, 60, 26, 0, 'shrub')
    if up == 'Security HQ':
        for x in (44, 50, 56): P('Desk', 1, x, 52, 0)
        P('Terminal', 1, 62, 52.5, 0, 'Security Terminal', True); P('Turret', 1, 68, 36, 0, '', True)
        P('B:StrongBox', 1, 40, 36, 0, 'Rare|1|0'); P('Lockers', 1, 70.6, 46, 90); M('Keycard', 1, 46, 51, 'Red'); M('Post', 1, 56, 42, 'elite')
    elif up == "Manager's Office":
        P('Desk', 1, 56, 50, 0); P('Sofa', 1, 44, 36, 0); P('Sofa', 1, 66, 36, 0); P('B:Safe', 1, 70.5, 52, 270, 'Rare|1|250')
        P('Shelf', 1, 46, 52.5, 0); M('Post', 1, 50, 44)   # [v0.20] M('Note', 1, 56, 51, 'Main Vault'): no passcode any more
    elif up == 'Server Room':
        for x in (44, 48, 52, 60, 64, 68):
            for z in (38, 48): P('Servers', 1, x, z, 90)
        P('Vault', 1, 56, 43, 0, 'Server Vault|Common|3|true|6|1||', True); P('Power', 1, 71, 53, 0, 'Upper Power', True); M('Post', 1, 56, 36)
    else:
        for x, z in ((44, 38), (44, 48), (66, 38), (66, 48)): P('Sofa', 1, x, z, 90)
        P('Counter', 1, 56, 52.5, 0); P('B:CashRegister', 1, 62, 52.5, 0, 'Common|0|200')
        for x in (50, 62): P('B:DisplayCase', 1, x, 43, 0, 'Rare|1|0')
        M('Post', 1, 56, 38, 'elite')
    M('Camera', 1, 36, 29, arg=f'{sx * 20}|{sz * 40}'); M('Camera', 0, 71, 33, arg=f'{sx * 56}|{sz * 44}'); M('Camera', 0, 41, 87, arg=f'{sx * 60}|{sz * 70}')

for q in SHOPS: block(*q)

# ---- security on the move: one loop round the concourses, one round the upper walkways
for i, (x, z) in enumerate([(0, 30), (15, 15), (40, 0), (15, -15), (0, -40), (-15, -15), (-40, 0), (-15, 15), (0, 48), (60, 0), (0, -48), (-60, 0)]):
    mark('Patrol', 0, x, z, arg=f'A|{i}')
for i, (x, z) in enumerate([(6, 40), (20, 20), (40, 6), (20, -20), (6, -40), (-20, -20), (-40, -6), (-20, 20)]):
    mark('Patrol', 1, x, z, arg=f'B|{i}')
for x, z in ((0, 20), (20, 0), (0, -20), (-20, 0)): mark('GuardSpawn', 0, x, z, arg='A')
for x, z in ((6, 26), (-6, -26)): mark('GuardSpawn', 1, x, z, arg='B')
mark('Sniper', 1, 20, -20, arg='0|0'); mark('Sniper', 1, -20, 20, arg='0|0')

# ---- map events: where and when the building changes
mark('Event', 0, 0, -80, arg='Delivery|Armoured delivery|Truck Bay|150|truck')
mark('Event', 0, 0, 8, arg='Delivery|Supply drop|Atrium|300|airdrop')
mark('Event', 0, 0, 77, arg='TimeLock|Time lock released|Main Vault|480|Main Vault')
mark('Event', 0, 0, 0, arg='Blackout|Power failure|Atrium|390|Atrium Power')
for x, z in ((-5, 8), (5, 8)): place('B:AirdropCrate', 0, x, z, 0, 'Rare|1|200|dormant|airdrop', strict=True, gap=0)

# ------------------------------------------------------------------ loot economy
# Containers are a trickle, vaults are the haul. Loose cash is scaled down, and every second plain
# container (crate, locker) holds cash instead of a named item, so smashing vending machines never
# reaches the getaway quota on its own.
CASH_SCALE = 0.4
thin = 0
for it in items:
    if not it['kind'].startswith('B:'): continue
    a = it['arg'].split('|')
    if len(a) < 3: continue
    tier, count, cash = a[0], int(a[1]), int(a[2])
    if tier == 'Common' and count > 0 and it['kind'] in ('B:Crate', 'B:Locker'):
        thin += 1
        if thin % 2 == 0: count, cash = 0, 120
    cash = int(round(cash * CASH_SCALE / 5.0)) * 5
    it['arg'] = '|'.join([tier, str(count), str(cash)] + a[3:])

# ------------------------------------------------------------------ greedy boxes
def boxes(level, wanted):
    used = [[False] * W for _ in range(H)]
    out = []
    for c in range(H):
        for r in range(W):
            if used[c][r] or grid[level][c][r] not in wanted: continue
            r1 = r
            while r1 + 1 < W and not used[c][r1 + 1] and grid[level][c][r1 + 1] in wanted: r1 += 1
            c1 = c
            while c1 + 1 < H and all(not used[c1 + 1][k] and grid[level][c1 + 1][k] in wanted for k in range(r, r1 + 1)): c1 += 1
            for cc in range(c, c1 + 1):
                for k in range(r, r1 + 1): used[cc][k] = True
            out.append((XMIN + r * CELL, ZMIN + c * CELL, XMIN + (r1 + 1) * CELL, ZMIN + (c1 + 1) * CELL))
    return out

walls1 = boxes(0, (SOLID,))
mass2 = boxes(1, (SOLID,))
slab2 = boxes(1, (FLOOR, DOOR))
floor1 = boxes(0, (FLOOR, DOOR))

# Floor tints (v0.17). A zone used to be tinted as one rectangle, which on the upper level painted a floor
# across the atrium, the voids between the walkways and every stairwell. Tints are now cut from the cells
# that are really floor. A bridge carved through a zone after it was named takes that zone's tint.
for (zname, zlevel, zx, zz, zw, zd, _poi) in zones:
    for c in range(cz(zz - zd / 2), cz(zz + zd / 2)):
        for r in range(cx(zx - zw / 2), cx(zx + zw / 2)):
            if grid[zlevel][c][r] in (FLOOR, DOOR) and zone_of[zlevel][c][r] is None: zone_of[zlevel][c][r] = zname

def zone_boxes(level, name):
    used = [[False] * W for _ in range(H)]
    ok = lambda c, r: not used[c][r] and grid[level][c][r] in (FLOOR, DOOR) and zone_of[level][c][r] == name
    out = []
    for c in range(H):
        for r in range(W):
            if not ok(c, r): continue
            r1 = r
            while r1 + 1 < W and ok(c, r1 + 1): r1 += 1
            c1 = c
            while c1 + 1 < H and all(ok(c1 + 1, k) for k in range(r, r1 + 1)): c1 += 1
            for cc in range(c, c1 + 1):
                for k in range(r, r1 + 1): used[cc][k] = True
            out.append((XMIN + r * CELL, ZMIN + c * CELL, XMIN + (r1 + 1) * CELL, ZMIN + (c1 + 1) * CELL))
    return out

zone_floors, seen_zones = [], set()
for (zname, zlevel, *_rest) in zones:
    if (zname, zlevel) in seen_zones: continue
    seen_zones.add((zname, zlevel))
    for b in zone_boxes(zlevel, zname): zone_floors.append((zname, zlevel) + b)
for (zname, zlevel, x0, z0, x1, z1) in zone_floors:
    for c in range(cz(z0), cz(z1)):
        for r in range(cx(x0), cx(x1)):
            if grid[zlevel][c][r] == VOID: problems.append(f"floor tint for {zname} covers an opening at L{zlevel + 1} ({wx(r)}, {wz(c)})")

# Rails where an upper floor meets a drop
rails = []
for c in range(H):
    for r in range(W):
        if grid[1][c][r] not in (FLOOR, DOOR): continue
        x0, z0 = XMIN + r * CELL, ZMIN + c * CELL
        for dc, dr, seg in ((1, 0, (x0, z0 + CELL, x0 + CELL, z0 + CELL)), (-1, 0, (x0, z0, x0 + CELL, z0)),
                            (0, 1, (x0 + CELL, z0, x0 + CELL, z0 + CELL)), (0, -1, (x0, z0, x0, z0 + CELL))):
            n = (c + dc, r + dr)
            if 0 <= n[0] < H and 0 <= n[1] < W and grid[1][n[0]][n[1]] == VOID:
                # not across the head of a ramp
                mx, mz = (seg[0] + seg[2]) / 2, (seg[1] + seg[3]) / 2
                if any(abs(mx - rp['x1']) <= rp['width'] / 2 + 0.1 and abs(mz - rp['z1']) <= rp['width'] / 2 + 0.1 for rp in ramps): continue
                rails.append(seg)

def merge_rails(segs):
    """Join collinear rail pieces into long runs."""
    horizontal = sorted([s for s in segs if s[1] == s[3]], key=lambda s: (s[1], s[0]))
    vertical = sorted([s for s in segs if s[0] == s[2]], key=lambda s: (s[0], s[1]))
    out = []
    for group, ax in ((horizontal, 0), (vertical, 1)):
        cur = None
        for s in group:
            if cur and ((ax == 0 and s[1] == cur[1] and s[0] == cur[2]) or (ax == 1 and s[0] == cur[0] and s[1] == cur[3])):
                cur = (cur[0], cur[1], s[2], s[3])
            else:
                if cur: out.append(cur)
                cur = s
        if cur: out.append(cur)
    return out
rails = merge_rails(rails)

# ------------------------------------------------------------------ report
if '--ascii' in sys.argv:
    for level in range(2):
        print(f'--- level {level + 1} (north at the top, one character = {CELL} m)')
        for c in range(H - 1, -1, -1): print(''.join(grid[level][c]))
print(f'cells {W}x{H}  zones {len(zones)}  doors {len(doors)}  ramps {len(ramps)}  items {len(items)}  '
      f'boxes: walls {len(walls1)} upper {len(mass2)} slabs {len(slab2)} floors {len(floor1)} rails {len(rails)} tints {len(zone_floors)}')
if skipped: print(f'{len(skipped)} optional pieces skipped:'); [print('   ', s) for s in skipped]
if problems:
    print(f'{len(problems)} PROBLEMS:'); [print('   ', p) for p in problems]
    sys.exit(1)

# ------------------------------------------------------------------ write the C# data file
def f(v):
    s = ('%.2f' % v).rstrip('0').rstrip('.')
    return (s if s not in ('', '-0') else '0') + 'f'
def q(s): return '"' + str(s).replace('\\', '\\\\').replace('"', '\\"') + '"'
def rows(values, per=4, cols=6):
    lines, row = [], []
    for i in range(0, len(values), per):
        row.append(', '.join(f(v) for v in values[i:i + per]))
        if len(row) == cols: lines.append('            ' + ',  '.join(row) + ','); row = []
    if row: lines.append('            ' + ',  '.join(row) + ',')
    return '\n'.join(lines)
def flat(bs): return [v for b in bs for v in b]

out = []
out.append('// GENERATED by Tools/mapgen/galleria.py. Do not edit by hand: change the script and run it again.')
out.append('namespace Ouroboros.EditorTools\n{')
out.append('    /// <summary>Layout data for the Galleria map: boxes in metres as x0, z0, x1, z1, plus zones, doors, ramps and items.</summary>')
out.append('    public static partial class OuroborosDevSceneBuilder\n    {')
out.append(f'        private const float GalleriaHalfX = {f(XMAX)}, GalleriaHalfZ = {f(ZMAX)}, GalleriaUpperY = {f(LEVEL_Y[1])};')
for name, data in (('GalleriaFloors', floor1), ('GalleriaWalls', walls1), ('GalleriaUpperMass', mass2), ('GalleriaUpperSlabs', slab2), ('GalleriaRails', rails)):
    out.append(f'\n        private static readonly float[] {name} =\n        {{\n{rows(flat(data))}\n        }};')
out.append('\n        private static readonly GZone[] GalleriaZones =\n        {')
for (n, l, x, z, w, d, poi) in zones: out.append(f'            new GZone({q(n)}, {l}, {f(x)}, {f(z)}, {f(w)}, {f(d)}, {"true" if poi else "false"}),')
out.append('        };')
out.append('\n        // Tinted floor per zone, cut to the cells that are floor: name, level, x0, z0, x1, z1.')
out.append('        private static readonly GTile[] GalleriaZoneFloors =\n        {')
for (n, l, x0, z0, x1, z1) in zone_floors: out.append(f'            new GTile({q(n)}, {l}, {f(x0)}, {f(z0)}, {f(x1)}, {f(z1)}),')
out.append('        };')
out.append('\n        private static readonly GDoor[] GalleriaDoors =\n        {')
for d in doors: out.append(f'            new GDoor({q(d["kind"])}, {d["level"]}, {f(d["x"])}, {f(d["z"])}, {"true" if d["along_x"] else "false"}, {f(d["width"])}, {q(d["name"])}, {q(d["arg"])}),')
out.append('        };')
out.append('\n        private static readonly GRamp[] GalleriaRamps =\n        {')
for r in ramps: out.append(f'            new GRamp({q(r["name"])}, {f(r["x0"])}, {f(r["z0"])}, {f(r["x1"])}, {f(r["z1"])}, {f(r["width"])}),')
out.append('        };')
out.append('\n        private static readonly GItem[] GalleriaItems =\n        {')
for i in items: out.append(f'            new GItem({q(i["kind"])}, {i["level"]}, {f(i["x"])}, {f(i["z"])}, {f(i["yaw"])}, {q(i["arg"])}),')
out.append('        };\n    }\n}')
target = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Editor', 'OuroborosGalleriaLayout.cs')
open(target, 'w').write('\n'.join(out) + '\n')
print('wrote', os.path.normpath(target))
