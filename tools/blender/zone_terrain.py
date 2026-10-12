# Run by tools/blender-zone.js inside Blender (--background): swaps the terrain and the
# sea plane of a working .blend for fresh ones, leaving every placed object alone.
#   blender --background <file.blend> --python zone_terrain.py -- <port-terre.glb>
#
# Opened WITH the user's add-ons (no --factory-startup): this script saves the file, and
# data owned by an add-on that is not loaded (V-Ray materials) could be lost on save.
import sys
import bpy

glb = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene

# where the old decor lived, so the new one lands in the same (locked) collection
target = None
for name in ("terre", "mer"):
    o = bpy.data.objects.get(name)
    if o is None:
        continue
    if target is None and o.users_collection:
        target = o.users_collection[0]
    mesh = o.data if o.type == 'MESH' else None
    bpy.data.objects.remove(o, do_unlink=True)
    if mesh is not None and mesh.users == 0:
        bpy.data.meshes.remove(mesh)
if target is None:
    target = bpy.data.collections.get("Décor (verrouillé)") or scene.collection

before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=glb)
fresh = [o for o in bpy.data.objects if o not in before]
for o in fresh:
    for c in list(o.users_collection):
        c.objects.unlink(o)
    target.objects.link(o)
    if o.name in ("terre", "mer"):
        o.hide_select = True

names = sorted(o.name for o in fresh)
if "terre" not in names:
    print("naval: ERREUR — la terre neuve n'a pas pris son nom (%s) ; le .blend n'est pas enregistré" % names)
    sys.exit(1)
bpy.ops.wm.save_mainfile()
print("naval: terre et mer remplacées dans", bpy.data.filepath)
