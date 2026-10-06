"""지형 장식 모델 (Blender 5.x): 풀숲, 꽃 무리, 자갈. 1칸 = 1 단위, 바닥 높이 0.

실행:
  blender -b -P build_decor.py

산출물 (이 스크립트와 같은 폴더):
  decor_grass.fbx   — 휘어진 풀잎 다발. 재질 DecorGrass
  decor_flower.fbx  — 흰 꽃 3송이 + 잎. 재질 DecorGrass / DecorPetal / DecorCenter
  decor_pebble.fbx  — 납작한 자갈 2~3개. 재질 Stone
"""
import math
import os
import random

import bpy
from mathutils import Vector, noise

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(5)


def material(name, color):
    m = bpy.data.materials.new(name)
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


M_GRASS = material("DecorGrass", (0.35, 0.62, 0.2))
M_PETAL = material("DecorPetal", (0.98, 0.97, 0.95))
M_CENTER = material("DecorCenter", (1.0, 0.82, 0.25))
M_STONE = material("Stone", (0.6, 0.62, 0.68))


def blade(root, height, width, lean, yaw):
    """끝으로 갈수록 가늘어지며 휘는 풀잎 (양면 납작한 띠)."""
    me = bpy.data.meshes.new("Blade")
    verts, faces = [], []
    seg = 4
    for s in range(seg + 1):
        t = s / seg
        w = width * (1 - t) ** 0.8
        x = lean * t * t
        z = height * t
        verts += [(x - w / 2, 0, z), (x + w / 2, 0, z)]
    for s in range(seg):
        a = s * 2
        faces.append((a, a + 1, a + 3, a + 2))
    me.from_pydata(verts, [], faces)
    o = bpy.data.objects.new("Blade", me)
    bpy.context.collection.objects.link(o)
    o.rotation_euler = (0, 0, yaw)
    o.location = root
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True)
    o.data.materials.append(M_GRASS)
    # 양면이 보이게 두께
    mod = o.modifiers.new("Solid", "SOLIDIFY")
    mod.thickness = 0.004
    bpy.ops.object.modifier_apply(modifier="Solid")
    bpy.ops.object.shade_smooth()
    o.select_set(False)
    return o


def tuft(n, radius, height):
    parts = []
    for i in range(n):
        a = random.random() * math.tau
        r = random.random() * radius
        parts.append(blade((r * math.cos(a), r * math.sin(a), 0),
                           height * (0.7 + 0.5 * random.random()), 0.035,
                           0.05 + 0.06 * random.random(), a + math.pi / 2))
    return parts


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


# ---------------------------------------------------------------- 풀숲
export(join(tuft(11, 0.06, 0.17), "DecorGrass"), "decor_grass.fbx")

# ---------------------------------------------------------------- 꽃 무리
parts = tuft(6, 0.05, 0.1)
for i, (x, y, h) in enumerate(((0.0, 0.0, 0.15), (0.06, 0.04, 0.12), (-0.05, 0.05, 0.11))):
    # 줄기
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.006, depth=h, location=(x, y, h / 2))
    stem = bpy.context.active_object
    stem.data.materials.append(M_GRASS)
    parts.append(stem)
    # 꽃잎 5장
    for k in range(5):
        a = k / 5 * math.tau
        bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=1,
                                             location=(x + 0.022 * math.cos(a), y + 0.022 * math.sin(a), h))
        petal = bpy.context.active_object
        petal.scale = (0.02, 0.012, 0.006)
        petal.rotation_euler = (0, 0, a)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        petal.data.materials.append(M_PETAL)
        bpy.ops.object.shade_smooth()
        parts.append(petal)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=0.011, location=(x, y, h + 0.004))
    center = bpy.context.active_object
    center.data.materials.append(M_CENTER)
    bpy.ops.object.shade_smooth()
    parts.append(center)
export(join(parts, "DecorFlower"), "decor_flower.fbx")

# ---------------------------------------------------------------- 자갈
stones = []
for i, (x, y, r) in enumerate(((0, 0, 0.055), (0.07, 0.03, 0.035), (-0.05, 0.05, 0.03))):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=r, location=(0, 0, 0))
    s = bpy.context.active_object
    off = Vector((i * 3.3, i * 1.9, 0.7))
    for v in s.data.vertices:
        d = v.co.normalized()
        v.co = d * r * (1 + noise.noise(d * 2 + off) * 0.25)
        v.co.z *= 0.55
    s.location = (x, y, r * 0.2)
    bpy.ops.object.transform_apply(location=True)
    s.data.materials.append(M_STONE)
    bpy.ops.object.shade_smooth()
    stones.append(s)
export(join(stones, "DecorPebble"), "decor_pebble.fbx")
print("DONE")
