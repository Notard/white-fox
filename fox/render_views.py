"""white_fox.blend 를 정면/측면/뒤/3·4 각도로 렌더해 renders/ 에 저장 (모델 확인용).

실행:
  blender -b white_fox.blend -P render_views.py [-- 액션이름 프레임]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

OUT = os.path.join(os.path.dirname(bpy.data.filepath), "renders")
os.makedirs(OUT, exist_ok=True)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
scene = bpy.context.scene

arm = bpy.data.objects["FoxArmature"]
if argv:
    arm.animation_data.action = bpy.data.actions[argv[0]]
    scene.frame_set(int(argv[1]))
else:
    arm.animation_data.action = None
    for b in arm.pose.bones:
        b.location = (0, 0, 0)
        b.rotation_euler = (0, 0, 0)
        b.scale = (1, 1, 1)

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = 640
scene.render.film_transparent = False
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("W")
nt = world.node_tree
nt.nodes.clear()
bg = nt.nodes.new("ShaderNodeBackground")
bg.inputs["Color"].default_value = (0.75, 0.77, 0.8, 1)
bg.inputs["Strength"].default_value = 0.35
nt.links.new(bg.outputs[0], nt.nodes.new("ShaderNodeOutputWorld").inputs["Surface"])
scene.world = world

sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.0
sun.rotation_euler = (math.radians(50), math.radians(-20), math.radians(-35))
scene.collection.objects.link(sun)
fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
fill.data.energy = 1.2
fill.rotation_euler = (math.radians(60), 0, math.radians(150))
scene.collection.objects.link(fill)

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = 1.6
scene.collection.objects.link(cam)
scene.camera = cam
target = Vector((0, 0.05, 0.55))

views = {"front": (0, -3, 0.15), "side": (3, 0, 0.15), "back": (0, 3, 0.15), "threequarter": (-2.1, -2.1, 1.0)}
tag = f"_{argv[0]}{argv[1]}" if argv else ""
for name, off in views.items():
    cam.location = target + Vector(off)
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, f"{name}{tag}.png")
    bpy.ops.render.render(write_still=True)
