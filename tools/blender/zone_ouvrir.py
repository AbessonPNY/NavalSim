# Run by tools/blender-zone.js inside Blender (--background): turns the game's zone
# export (world/blender/<port>.glb) into a ready-to-work .blend.
#   blender --background --factory-startup --python zone_ouvrir.py -- <in.glb> <out.blend>
#
# The terrain and the sea plane are locked (not selectable) so that a click always
# lands on a building; raw models sit in their own "Palette" collection, to be
# duplicated (Shift+D) and placed.
import sys
import bpy

glb, out = sys.argv[sys.argv.index("--") + 1:][:2]

# an empty scene, whatever the factory startup put in it
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

bpy.ops.import_scene.gltf(filepath=glb)

scene = bpy.context.scene
cols = {}
for name in ("Décor (verrouillé)", "Objets du jeu", "Palette (modèles bruts)"):
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    cols[name] = c

for o in list(scene.objects):
    if o.parent is not None:
        continue
    if o.name in ("terre", "mer"):
        target = cols["Décor (verrouillé)"]
        o.hide_select = True
    elif o.name.startswith("brut:"):
        target = cols["Palette (modèles bruts)"]
    else:
        target = cols["Objets du jeu"]
    for c in list(o.users_collection):
        c.objects.unlink(o)
    target.objects.link(o)

# a zone is a kilometre wide: the default 1 km far clip would cut it in half
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            for space in area.spaces:
                if space.type == 'VIEW_3D':
                    space.clip_start = 0.1
                    space.clip_end = 20000.0
                    space.shading.type = 'MATERIAL'

scene.unit_settings.system = 'METRIC'
scene["naval_zone"] = glb
bpy.ops.wm.save_as_mainfile(filepath=out)
print("naval: .blend écrit", out)
