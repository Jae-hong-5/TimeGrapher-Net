# Blender 4.x Python script: strapless round watch model
# Run from Blender's Scripting workspace, or:
#   blender --background --python create_round_watch_model_blender.py

import bpy
import math
from pathlib import Path

OUT_DIR = Path(bpy.path.abspath("//"))
GLB_PATH = OUT_DIR / "watch_model_round_blender.glb"
BLEND_PATH = OUT_DIR / "watch_model_round.blend"

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)


def mat(name, color, metallic=0.0, roughness=0.5):
    m = bpy.data.materials.new(name)
    m.diffuse_color = color
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = color
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = roughness
    return m

M = {
    'case': mat('Brushed Steel', (0.57, 0.60, 0.63, 1), .90, .28),
    'bezel': mat('Dark Bezel', (0.03, 0.04, 0.05, 1), .78, .24),
    'dial': mat('Ivory Dial', (0.81, 0.78, 0.70, 1), .05, .72),
    'marker': mat('Markers', (0.02, 0.025, 0.03, 1), .20, .38),
    'accent': mat('12 Accent', (0.42, 0.03, 0.025, 1), .10, .46),
}


def finish(obj, material, bevel=0.0003):
    obj.data.materials.append(material)
    if bevel > 0:
        mod = obj.modifiers.new('Small bevel', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.shade_smooth_by_angle()
    obj.select_set(False)
    return obj


def cyl(name, radius, depth, z, material, vertices=96):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=(0,0,z))
    o = bpy.context.object
    o.name = name
    return finish(o, material, 0.00016)


def cube(name, scale_xyz, loc, material, rot_z=0):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=(0,0,rot_z))
    o = bpy.context.object
    o.name = name
    o.dimensions = scale_xyz
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, material, 0.00018)

# Rotate this root node in the app.
bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0,0,0))
root = bpy.context.object
root.name = 'WatchRoot'

case = cyl('Case', .020, .0058, 0, M['case'])
back = cyl('CaseBack', .0188, .00075, -.00328, M['case'])
back_ring = cyl('CaseBackRing', .0202, .00055, -.00355, M['bezel'])
bezel = cyl('Bezel', .0206, .00125, .00325, M['bezel'])
dial = cyl('Dial', .01765, .00055, .00392, M['dial'])
for obj in (case, back, back_ring, bezel, dial):
    obj.parent = root

for hour in range(12):
    theta = math.radians(hour * 30)
    if hour == 0:
        length, width, material = .0046, .00155, M['accent']
    elif hour in (3,6,9):
        length, width, material = .0037, .00125, M['marker']
    else:
        length, width, material = .00235, .00072, M['marker']
    r = .0142
    x, y = r * math.sin(theta), r * math.cos(theta)
    marker = cube(f'Marker_{hour}', (width, length, .00048), (x,y,.00428), material, -theta)
    marker.parent = root

for name, length, width, angle, material, z in [
    ('HourHand', .0094, .00115, 305, M['marker'], .00455),
    ('MinuteHand', .0131, .00078, 60, M['marker'], .00472),
    ('SecondHand', .0142, .00028, 180, M['accent'], .00490),
]:
    a = math.radians(angle)
    r = length * .45
    o = cube(name, (width, length, .00034), (r*math.sin(a), r*math.cos(a), z), material, -a)
    o.parent = root

pin = cyl('CenterPin', .00115, .00072, .0050, M['accent'], 40)
pin.parent = root

# Crown at +X: retained as a useful 3 o'clock orientation cue.
for name, radius, depth, x in [
    ('CrownStem', .00115, .0033, .0208),
    ('Crown', .00275, .0037, .0234),
]:
    bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=radius, depth=depth,
                                        location=(x,0,0), rotation=(0,math.pi/2,0))
    o = bpy.context.object
    o.name = name
    finish(o, M['case'], .00012)
    o.parent = root

# Deliberately no straps, lugs, buckle, or strap holes.
bpy.context.view_layer.objects.active = root
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
bpy.ops.export_scene.gltf(filepath=str(GLB_PATH), export_format='GLB', export_apply=True)
print(f'Wrote {BLEND_PATH}')
print(f'Wrote {GLB_PATH}')
