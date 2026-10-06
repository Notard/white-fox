"""타일과 장애물(바위) 모델 생성 (Blender 5.x).

실행:
  blender -b -P build_tiles.py

산출물 (이 스크립트와 같은 폴더):
  tile.fbx  — 1×1 크기 잔디 큐브 타일. 윗면 높이 0, 재질 슬롯 GrassTop / DirtSide
  rock.fbx  — 장애물 바위 무리. 바닥 높이 0, 재질 슬롯 Stone
  block.fbx — 높은 칸 아래를 채우는 흙 블록 (1×1, 높이 0.5, 윗면 0). 재질 슬롯 DirtSide
  tiles.blend
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Vector, noise

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color):
    m = bpy.data.materials.new(name)
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


M_GRASS = material("GrassTop", (0.35, 0.6, 0.3))
M_DIRT = material("DirtSide", (0.45, 0.3, 0.2))
M_STONE = material("Stone", (0.6, 0.62, 0.68))


def rounded_box(name, size, z0, z1, bevel, mat):
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.name = name
    o.scale = (size, size, z1 - z0)
    o.location = (0, 0, (z0 + z1) / 2)
    bpy.ops.object.transform_apply(location=True, scale=True)
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = bevel
    m.segments = 4
    m.limit_method = "NONE"
    bpy.ops.object.modifier_apply(modifier="Bevel")
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return o


def cube_uv(o, size):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=size, correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def export(objs, filename):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, filename),
        use_selection=True,
        object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE",
    )


# ---------------------------------------------------------------- 타일
dirt = rounded_box("Dirt", 0.95, -0.5, -0.04, 0.07, M_DIRT)
# 아래로 갈수록 살짝 좁아지는 흙 블록
for v in dirt.data.vertices:
    t = (-v.co.z - 0.04) / 0.46
    v.co.x *= 1 - 0.06 * t
    v.co.y *= 1 - 0.06 * t
cube_uv(dirt, 1.0)

grass = rounded_box("Grass", 1.0, -0.11, 0.0, 0.045, M_GRASS)
# 잔디 판 가장자리를 살짝 물결치게 (손으로 빚은 느낌)
for v in grass.data.vertices:
    if v.co.z < -0.06:
        n = noise.noise(Vector((v.co.x * 6, v.co.y * 6, 0.5)))
        v.co.z -= 0.025 * (n + 0.5)
cube_uv(grass, 1.0)

bpy.ops.object.select_all(action="DESELECT")
grass.select_set(True)
dirt.select_set(True)
bpy.context.view_layer.objects.active = grass
bpy.ops.object.join()
tile = bpy.context.active_object
tile.name = "Tile"
export([tile], "tile.fbx")

# ---------------------------------------------------------------- 흙 블록 (높은 칸의 아래층)
# 타일과 같은 1×1, 높이 0.5. 윗면 높이 0. 위에 타일이나 다른 블록이 얹힌다.
block = rounded_box("Block", 0.95, -0.5, 0.0, 0.05, M_DIRT)
for v in block.data.vertices:          # 아래로 갈수록 살짝 좁아지게 (타일과 같은 비율)
    t = -v.co.z / 0.5
    v.co.x *= 1 - 0.04 * t
    v.co.y *= 1 - 0.04 * t
cube_uv(block, 1.0)
export([block], "block.fbx")

# ---------------------------------------------------------------- 바위 무리
random.seed(7)


def boulder(name, loc, radius, squash, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=radius, location=(0, 0, 0))
    o = bpy.context.active_object
    o.name = name
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    for v in o.data.vertices:
        d = v.co.normalized()
        n = noise.noise(d * 1.6 + off) * 0.18 + noise.noise(d * 4 + off) * 0.05
        v.co = d * radius * (1 + n)
        v.co.z *= squash
    # 바닥을 평평하게 잘라 땅에 안착
    bm = bmesh.new()
    bm.from_mesh(o.data)
    lowest = -radius * squash * 0.55
    for v in bm.verts:
        if v.co.z < lowest:
            v.co.z = lowest
    bm.to_mesh(o.data)
    bm.free()
    o.location = Vector(loc) + Vector((0, 0, -lowest - 0.02))
    bpy.ops.object.transform_apply(location=True)
    o.data.materials.append(M_STONE)
    bpy.ops.object.shade_smooth()
    cube_uv(o, 0.8)
    return o


rocks = [
    boulder("RockBig", (0.02, 0.03, 0), 0.3, 0.85, 1),
    boulder("RockMid", (0.25, -0.2, 0), 0.16, 0.8, 2),
    boulder("RockSmall", (-0.24, -0.17, 0), 0.1, 0.75, 3),
]
bpy.ops.object.select_all(action="DESELECT")
for r in rocks:
    r.select_set(True)
bpy.context.view_layer.objects.active = rocks[0]
bpy.ops.object.join()
rock = bpy.context.active_object
rock.name = "Rock"
export([rock], "rock.fbx")

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "tiles.blend"))
print("DONE")
