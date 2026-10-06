"""장치 모델 (Blender 5.x): 레버, 누름판, 미는 블록(나무 상자). 1칸 = 1 단위, 바닥 높이 0, 앞(Unity +Z) = Blender -Y.

실행:
  blender -b -P build_mechanisms.py

산출물 (이 스크립트와 같은 폴더):
  lever.fbx      — LeverBase(돌 받침) + LeverHandle(나무 손잡이 + 둥근 손잡이 머리, 원점 = 경첩). 재질 Stone / Wood / Metal / Knob
  plate.fbx      — PlateBase(팔각 돌 테두리) + PlateTop(눌리는 판). 재질 Stone / PlateTop
  pushblock.fbx  — 눈 덮인 나무 상자 (0.72 × 0.72 × 0.6). 재질 Wood / WoodDark / Metal / Snow
Knob·PlateTop 은 게임에서 채널 색으로 칠한다 (흰색으로 둔다).
"""
import math
import os

import bpy
from mathutils import Vector, noise

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color):
    m = bpy.data.materials.new(name)
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


M_STONE = material("Stone", (0.6, 0.62, 0.68))
M_WOOD = material("Wood", (0.72, 0.5, 0.3))
M_WOOD_DARK = material("WoodDark", (0.45, 0.3, 0.18))
M_METAL = material("Metal", (0.5, 0.52, 0.6))
M_KNOB = material("Knob", (1, 1, 1))
M_PLATE = material("PlateTop", (1, 1, 1))
M_SNOW = material("Snow", (0.95, 0.97, 1.0))


def finish(o, mat, smooth=True, bevel=0.0, segments=2):
    if bevel > 0:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = segments
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier="Bevel")
    o.data.materials.append(mat)
    if smooth:
        bpy.ops.object.shade_smooth()
    return o


def box(name, loc, size, mat, bevel=0.02, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.rotation_euler = [math.radians(a) for a in rot]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(o, mat, smooth=False, bevel=bevel)


def cyl(name, loc, r, depth, mat, verts=16, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.rotation_euler = [math.radians(a) for a in rot]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(o, mat, bevel=bevel)


def sphere(name, loc, r, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=r, location=loc)
    o = bpy.context.active_object
    o.name = name
    return finish(o, mat)


def join(objs, name, origin=(0, 0, 0)):
    """objs 를 하나로 합치고 원점을 origin 으로 둔다 (Unity 가 물체 위치를 지키도록 메시에 굽지 않고 둔다)."""
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    bpy.context.scene.cursor.location = origin
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = (0, 0, 0)
    return o


def export(objs, filename):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, filename), use_selection=True,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL")
    for o in objs:
        o.hide_set(True)


# ---------------------------------------------------------------- 레버 (손잡이는 X 축을 따라 좌우로 기운다)
HINGE = (0, 0, 0.16)
base = join([
    box("Base", (0, 0, 0.07), (0.42, 0.26, 0.14), M_STONE, bevel=0.035),
    box("Slot", (0, 0, 0.142), (0.3, 0.07, 0.02), M_WOOD_DARK, bevel=0.005),
    cyl("Axle", HINGE, 0.03, 0.2, M_METAL, verts=12, rot=(90, 0, 0)),
], "LeverBase")
handle = join([
    cyl("Stick", (0, 0, HINGE[2] + 0.2), 0.026, 0.4, M_WOOD, verts=10),
    sphere("Knob", (0, 0, HINGE[2] + 0.42), 0.075, M_KNOB),
    cyl("Ring", (0, 0, HINGE[2] + 0.33), 0.034, 0.03, M_METAL, verts=10),
], "LeverHandle", origin=HINGE)
export([base, handle], "lever.fbx")

# ---------------------------------------------------------------- 누름판 (팔각 돌 테두리 + 눌리는 판)
ring = cyl("PlateBase", (0, 0, 0.025), 0.42, 0.05, M_STONE, verts=8, rot=(0, 0, 22.5), bevel=0.015)
ring = join([ring], "PlateBase")
top = cyl("PlateTop", (0, 0, 0.065), 0.32, 0.05, M_PLATE, verts=24, bevel=0.012)
top = join([top], "PlateTop", origin=(0, 0, 0.065))
export([ring, top], "plate.fbx")

# ---------------------------------------------------------------- 미는 블록: 눈 덮인 나무 상자
W, H = 0.72, 0.6
parts = [box("Crate", (0, 0, H / 2), (W, W, H), M_WOOD, bevel=0.03)]
# 판자 줄무늬 (네 옆면에 가로 띠 두 줄)
for z in (H * 0.33, H * 0.66):
    for sx, sy, size in ((0, -W / 2, (W * 0.94, 0.012, 0.022)), (0, W / 2, (W * 0.94, 0.012, 0.022)),
                         (-W / 2, 0, (0.012, W * 0.94, 0.022)), (W / 2, 0, (0.012, W * 0.94, 0.022))):
        parts.append(box("Plank", (sx, sy, z), size, M_WOOD_DARK, bevel=0.003))
# 모서리 쇠 장식
for sx in (-1, 1):
    for sy in (-1, 1):
        parts.append(box("Corner", (sx * W / 2, sy * W / 2, H / 2), (0.06, 0.06, H * 1.01), M_METAL, bevel=0.01))
# 눈 덮개: 울퉁불퉁한 납작한 덩어리
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, H + 0.035))
snow = bpy.context.active_object
snow.scale = (W * 0.96, W * 0.96, 0.08)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
sub = snow.modifiers.new("Sub", "SUBSURF")
sub.levels = 2
bpy.ops.object.modifier_apply(modifier="Sub")
for v in snow.data.vertices:
    w = snow.matrix_world @ v.co
    if v.co.z > 0:
        v.co.z += noise.noise(w * 6) * 0.02
finish(snow, M_SNOW)
parts.append(snow)
crate = join(parts, "PushBlock")
export([crate], "pushblock.fbx")
print("DONE")
