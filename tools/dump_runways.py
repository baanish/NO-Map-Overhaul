"""Print every airbase's runways from the game's asset files, offline.

Usage (any Python with UnityPy and TypeTreeGeneratorAPI installed):
    python tools/dump_runways.py "D:/SteamLibrary/steamapps/common/Nuclear Option"

For each airbase it prints the runway's serialized name, flags, width, and both ends in world space,
plus the heading the game would turn into a runway number. Use it to check map-specific quirks
(custom names, one-way or overlapping runways) without flying to the field.
"""

import math
import sys
from pathlib import Path

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

UNITY_VERSION = "2022.3.62f1"


def world_position(transform):
    """Walk the parent chain, applying each local rotation and scale."""
    x, y, z = (transform.m_LocalPosition.x, transform.m_LocalPosition.y, transform.m_LocalPosition.z)
    parent = transform.m_Father
    while parent and parent.path_id:
        p = parent.read()
        q = p.m_LocalRotation
        sx, sy, sz = p.m_LocalScale.x, p.m_LocalScale.y, p.m_LocalScale.z
        x, y, z = rotate((q.x, q.y, q.z, q.w), (x * sx, y * sy, z * sz))
        x, y, z = x + p.m_LocalPosition.x, y + p.m_LocalPosition.y, z + p.m_LocalPosition.z
        parent = p.m_Father
    return x, y, z


def rotate(q, v):
    qx, qy, qz, qw = q
    vx, vy, vz = v
    # v' = v + 2w(q x v) + 2 q x (q x v)
    cx, cy, cz = qy * vz - qz * vy, qz * vx - qx * vz, qx * vy - qy * vx
    cx2, cy2, cz2 = qy * cz - qz * cy, qz * cx - qx * cz, qx * cy - qy * cx
    return vx + 2 * (qw * cx + cx2), vy + 2 * (qw * cy + cy2), vz + 2 * (qw * cz + cz2)


def heading(a, b):
    """Unity yaw of a -> b in degrees, the angle Airbase.Runway.GetName rounds to a number."""
    return math.degrees(math.atan2(b[0] - a[0], b[2] - a[2])) % 360


def main(game_dir):
    data = Path(game_dir) / "NuclearOption_Data"
    generator = TypeTreeGenerator(UNITY_VERSION)
    generator.load_local_game(str(game_dir))
    env = UnityPy.load(str(data / "globalgamemanagers.assets"), *[str(p) for p in sorted(data.glob("sharedassets*.assets"))], *[str(p) for p in sorted(data.glob("level*"))])
    env.typetree_generator = generator

    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        if "runways" not in tree or "center" not in tree:
            continue

        name = tree.get("m_Name") or ""
        print(f"\n== {obj.assets_file.name} path_id={obj.path_id} {name}")
        behaviour = obj.read()
        for index, runway in enumerate(behaviour.runways):
            try:
                start = world_position(runway.Start.read())
                end = world_position(runway.End.read())
            except Exception as error:
                print(f"  [{index}] unreadable ends: {error}")
                continue
            forward = heading(start, end)
            length = math.dist((start[0], start[2]), (end[0], end[2]))
            print(
                f"  [{index}] name={runway.name!r} rev={runway.Reversable} landing={runway.Landing} "
                f"takeoff={runway.Takeoff} width={runway.width:.0f} length={length:.0f} "
                f"heading={forward:.1f} (#{round(forward / 10) or 36:02d}) "
                f"reverse={(forward + 180) % 360:.1f} (#{round(((forward + 180) % 360) / 10) or 36:02d}) "
                f"start=({start[0]:.0f},{start[2]:.0f}) end=({end[0]:.0f},{end[2]:.0f})"
            )


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else r"D:\SteamLibrary\steamapps\common\Nuclear Option")
