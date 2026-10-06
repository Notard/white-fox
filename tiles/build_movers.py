"""움직이는 것 모델 (Blender 5.x): 눈덩이, 부엉이, 발판. 1칸 = 1 단위, 바닥 높이 0, 앞(Unity +Z) = Blender -Y.

실행:
  blender -b -P build_movers.py

산출물 (이 스크립트와 같은 폴더):
  snowball.fbx  — 울퉁불퉁한 눈 공 + 화난 눈썹·눈. 재질 Snow / Ink
  owl.fbx       — 동그란 부엉이: 몸, 배, 큰 눈, 부리, 귀깃, 날개. 재질 OwlBody / OwlBelly / OwlEye / Ink / Beak
  platform.fbx  — 잔디 덮인 떠 있는 섬 조각 (윗면 높이 0). 재질 DecorGrass / Stone
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


M_SNOW = material("Snow", (0.95, 0.97, 1.0))
M_INK = material("Ink", (0.08, 0.07, 0.15))
M_OWL = material("OwlBody", (0.55, 0.42, 0.32))
M_BELLY = material("OwlBelly", (0.93, 0.86, 0.72))
M_EYE = material("OwlEye", (1.0, 0.85, 0.3))
M_BEAK = material("Beak", (0.95, 0.62, 0.25))
M_GRASS = material("DecorGrass", (0.36, 0.64, 0.22))
M_STONE = material("Stone", (0.6, 0.62, 0.68))


def sphere(name, loc, scale, mat, seg=24, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    o.rotation_euler = [math.radians(a) for a in rot]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return o


def cone(name, loc, r, depth, mat, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=r, radius2=0, depth=depth, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.rotation_euler = [math.radians(a) for a in rot]
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return o


def join_export(objs, name, filename):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    # 원점을 바닥(0,0,0)으로: Unity 는 FBX 루트 물체의 위치를 버리므로 메시에 구워 둔다
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, filename), use_selection=True,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL")
    o.hide_set(True)


# ---------------------------------------------------------------- 눈덩이 (반지름 0.28, 바닥 0)
R = 0.28
ball = sphere("Ball", (0, 0, R), (R, R, R), M_SNOW, 32)
# 메시 좌표는 물체 원점(공 가운데) 기준이다 (위치는 적용하지 않았음)
for v in ball.data.vertices:
    d = v.co.normalized()
    v.co = d * R * (1 + noise.noise(d * 3.2) * 0.07)
parts = [ball]
CENTER = Vector((0, 0, R))
for sx in (1, -1):
    # 눈: 앞(-Y) 위쪽 표면에 작은 검은 타원
    eye_dir = Vector((0.3 * sx, -0.9, 0.3)).normalized()
    parts.append(sphere(f"Eye{sx}", tuple(CENTER + eye_dir * R * 0.97), (0.032, 0.02, 0.048), M_INK, 12))
    # 화난 눈썹: 눈 위, 안쪽이 내려간 기울어진 막대
    brow_dir = Vector((0.3 * sx, -0.78, 0.55)).normalized()
    bpy.ops.mesh.primitive_cube_add(size=1, location=tuple(CENTER + brow_dir * R * 1.0))
    brow = bpy.context.active_object
    brow.scale = (0.09, 0.022, 0.024)
    brow.rotation_euler = (math.radians(-35), math.radians(-22 * sx), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    brow.data.materials.append(M_INK)
    parts.append(brow)
join_export(parts, "Snowball", "snowball.fbx")

# ---------------------------------------------------------------- 부엉이 (날아다님: 바닥에서 띄워 그림, 높이 약 0.55)
H = 0.22   # 몸 중심 높이 (날아 있는 느낌은 게임에서 위아래로 흔듦)
parts = [
    sphere("Body", (0, 0, H + 0.2), (0.21, 0.19, 0.24), M_OWL),
    sphere("Belly", (0, -0.1, H + 0.15), (0.15, 0.1, 0.17), M_BELLY),
]
for sx in (1, -1):
    parts.append(sphere(f"EyeWhite{sx}", (0.075 * sx, -0.15, H + 0.3), (0.07, 0.04, 0.07), M_EYE))
    parts.append(sphere(f"Pupil{sx}", (0.075 * sx, -0.185, H + 0.3), (0.035, 0.02, 0.04), M_INK, 12))
    parts.append(cone(f"Tuft{sx}", (0.14 * sx, 0, H + 0.44), 0.06, 0.14, M_OWL, rot=(0, 25 * sx, 0)))
    parts.append(sphere(f"Wing{sx}", (0.2 * sx, 0.02, H + 0.17), (0.06, 0.14, 0.16), M_OWL, rot=(0, 15 * sx, 0)))
    parts.append(sphere(f"Foot{sx}", (0.06 * sx, -0.06, H - 0.04), (0.04, 0.05, 0.025), M_BEAK, 12))
parts.append(cone("Beak", (0, -0.19, H + 0.22), 0.03, 0.07, M_BEAK, rot=(-100, 0, 0)))
join_export(parts, "Owl", "owl.fbx")

# ---------------------------------------------------------------- 발판 (작은 떠 있는 섬: 잔디 윗면 0, 아래로 바위)
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.47, depth=0.1, location=(0, 0, -0.05))
top = bpy.context.active_object
top.rotation_euler = (0, 0, math.radians(22.5))
bpy.ops.object.transform_apply(rotation=True)
m = top.modifiers.new("Bevel", "BEVEL")
m.width = 0.04
m.segments = 3
bpy.ops.object.modifier_apply(modifier="Bevel")
top.data.materials.append(M_GRASS)
bpy.ops.object.shade_smooth()
bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.44, radius2=0.08, depth=0.5, location=(0, 0, -0.33))
rock = bpy.context.active_object
rock.rotation_euler = (math.radians(180), 0, math.radians(22.5))
bpy.ops.object.transform_apply(rotation=True)
for v in rock.data.vertices:
    v.co += Vector((noise.noise(v.co * 4) * 0.04, noise.noise(v.co * 4 + Vector((5, 5, 5))) * 0.04, 0))
rock.data.materials.append(M_STONE)
bpy.ops.object.shade_smooth()
join_export([top, rock], "Platform", "platform.fbx")
print("DONE")
