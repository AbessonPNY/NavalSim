# Run by tools/blender-zone.js inside Blender (--background): reads where every
# top-level object of the .blend stands and writes it out in the GAME's frame
# (Y up, metres, origin at the zone centre) for the game to apply as retouches.
#   blender --background <file.blend> --python zone_relever.py -- <out.json>
#
# Blender is Z-up and its glTF importer maps game (x, y, z) to Blender (x, -z, y);
# a turn about Blender +Z is the same angle about game +Y.
import sys
import json
import math
import bpy
from mathutils import Quaternion

out = sys.argv[sys.argv.index("--") + 1]
objets = []
for o in bpy.context.scene.objects:
    if o.parent is not None or o.name in ("terre", "mer"):
        continue
    if o.type not in ('MESH', 'EMPTY'):
        continue
    m = o.matrix_world
    loc, rot, sc = m.decompose()
    cx = m.col[0].xyz
    scale = (sc.x + sc.y + sc.z) / 3.0
    cap = math.atan2(cx.y, cx.x)
    # the full turn in the game's frame: the axis (x, y, z) of Blender is (x, z, -y) there
    q = Quaternion((rot.w, rot.x, rot.z, -rot.y))
    # what is left once the heading is taken out: the lean, applied after the heading
    lean = q @ Quaternion((0.0, 1.0, 0.0), -cap)
    if lean.w < 0:
        lean.negate()
    objets.append({
        "nom": o.name,
        "x": loc.x, "y": loc.z, "z": -loc.y,
        "cap": cap, "echelle": scale,
        "penche": [lean.x, lean.y, lean.z, lean.w],
    })

with open(out, "w", encoding="utf-8") as f:
    json.dump({"fichier": bpy.data.filepath, "objets": objets}, f, ensure_ascii=False, indent=1)
print("naval: %d objets relevés dans %s" % (len(objets), out))
