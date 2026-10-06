"""퍼즐 칸 소품 모델 (Blender 5.x): 스위치, 문, 계단.

실행:
  blender -b -P build_props.py

산출물 (이 스크립트와 같은 폴더). 모두 1×1 칸 크기, 바닥이 높이 0, 앞(Unity +Z)은 Blender -Y.
  switch.fbx — 돌 받침 + 분홍 버튼. 재질 Stone / SwitchButton
  door.fbx   — 돌기둥 사이 얼음 결정 판 (좌우로 가로막음). 재질 Stone / DoorCrystal
  stairs.fbx — 앞(-Y)으로 올라가는 돌계단 3단. 재질 Stone
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color):
    m = bpy.data.materials.new(name)
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


M_STONE = material("Stone", (0.6, 0.62, 0.68))
M_BUTTON = material("SwitchButton", (0.95, 0.45, 0.62))
M_CRYSTAL = material("DoorCrystal", (0.62, 0.6, 0.95))


def box(name, size, loc, mat, bevel=0.02):
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.location = loc
    bpy.ops.object.transform_apply(location=True, scale=True)
    if bevel > 0:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = 3
        bpy.ops.object.modifier_apply(modifier="Bevel")
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return o


def cylinder(name, radius, z0, z1, mat, bevel=0.015):
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=radius, depth=z1 - z0, location=(0, 0, (z0 + z1) / 2))
    o = bpy.context.active_object
    o.name = name
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = bevel
    m.segments = 3
    m.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier="Bevel")
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return o


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    return o


def export(o, filename):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, filename), use_selection=True,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL")
    o.hide_set(True)


# ---------------------------------------------------------------- 스위치
base = cylinder("SwitchBase", 0.3, 0.0, 0.05, M_STONE)
button = cylinder("SwitchButton", 0.22, 0.05, 0.12, M_BUTTON, bevel=0.03)
export(join([base, button], "Switch"), "switch.fbx")

# ---------------------------------------------------------------- 문 (좌우 돌기둥 + 얼음 결정 판)
posts = [box(f"Post{s}", (0.14, 0.26, 0.86), (0.39 * s, 0, 0.43), M_STONE, 0.03) for s in (-1, 1)]
caps = [box(f"Cap{s}", (0.18, 0.3, 0.07), (0.39 * s, 0, 0.88), M_STONE, 0.02) for s in (-1, 1)]
panel = box("Panel", (0.64, 0.14, 0.72), (0, 0, 0.38), M_CRYSTAL, 0.025)
# 위쪽에 뾰족한 결정 몇 개
spikes = []
for i, (x, h) in enumerate(((-0.2, 0.18), (0, 0.26), (0.2, 0.16))):
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=0.09, radius2=0.0, depth=h, location=(x, 0, 0.74 + h / 2))
    s = bpy.context.active_object
    s.data.materials.append(M_CRYSTAL)
    spikes.append(s)
export(join(posts + caps + [panel] + spikes, "Door"), "door.fbx")

# ---------------------------------------------------------------- 계단 (앞 = -Y 로 올라감)
steps = []
for i in range(3):
    h = 0.1 * (i + 1)
    y = 0.27 - 0.27 * i          # 뒤(+Y)가 낮고 앞(-Y)이 높다
    steps.append(box(f"Step{i}", (0.78, 0.27, h), (0, y, h / 2), M_STONE, 0.02))
export(join(steps, "Stairs"), "stairs.fbx")

print("DONE")
