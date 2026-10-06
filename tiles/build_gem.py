"""보석 모델 + UI 아이콘 생성 (Blender 5.x).

실행:
  blender -b -P build_gem.py

산출물 (이 스크립트와 같은 폴더):
  gem.fbx       — 브릴리언트 컷 보석. 중심이 원점, 높이 약 0.5, 재질 슬롯 Gem
  gem_icon.png  — UI용 보석 아이콘 (투명 배경 256×256)
"""
import math
import os

import bmesh
import bpy

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------------------------------------------------------------- 모델
N = 8                 # 둘레 면 수
R = 0.22              # 거들(가장 넓은 부분) 반지름
TABLE_R = 0.12        # 윗면 반지름
CROWN_H = 0.1         # 거들 → 윗면 높이
PAV_H = 0.26          # 거들 → 아래 꼭짓점 깊이

# 꼭짓점 링(윗면 / 크라운 중간 / 거들 / 정자 중간 / 아래 끝)을 만들고 볼록 껍질로 면을 만든다.
bm = bmesh.new()


def ring(n, r, z, offset=0.0):
    for i in range(n):
        a = 2 * math.pi * i / n + offset
        bm.verts.new((r * math.cos(a), r * math.sin(a), z))


ring(N, TABLE_R, CROWN_H, math.pi / N)                # 윗면
ring(N, R * 0.82, CROWN_H * 0.6)                      # 크라운 중간 (별 면)
ring(2 * N, R, 0)                                     # 거들
ring(N, R * 0.55, -PAV_H * 0.45, math.pi / N)         # 정자 중간
bm.verts.new((0, 0, -PAV_H))                          # 아래 끝
bmesh.ops.convex_hull(bm, input=bm.verts)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

me = bpy.data.meshes.new("Gem")
bm.to_mesh(me)
bm.free()
gem = bpy.data.objects.new("Gem", me)
scene.collection.objects.link(gem)
bpy.context.view_layer.objects.active = gem
gem.select_set(True)
gem.location.z = (PAV_H - CROWN_H) / 2     # 높이 중심을 원점으로
bpy.ops.object.transform_apply(location=True)

mat = bpy.data.materials.new("Gem")
b = mat.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.12, 0.55, 0.95, 1)
b.inputs["Metallic"].default_value = 0.3
b.inputs["Roughness"].default_value = 0.12
b.inputs["Emission Color"].default_value = (0.2, 0.6, 1.0, 1)
b.inputs["Emission Strength"].default_value = 0.08
me.materials.append(mat)

bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "gem.fbx"),
    use_selection=True,
    object_types={"MESH"},
    apply_scale_options="FBX_SCALE_ALL",
    mesh_smooth_type="FACE",
)

# ---------------------------------------------------------------- 아이콘 렌더
gem.rotation_euler = (math.radians(18), 0, math.radians(12))
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = 256
scene.render.film_transparent = True
scene.view_settings.view_transform = "Standard"

world = bpy.data.worlds.new("W")
nt = world.node_tree
nt.nodes.clear()
bg = nt.nodes.new("ShaderNodeBackground")
bg.inputs["Color"].default_value = (0.8, 0.9, 1.0, 1)
bg.inputs["Strength"].default_value = 0.25
nt.links.new(bg.outputs[0], nt.nodes.new("ShaderNodeOutputWorld").inputs["Surface"])
scene.world = world

for rot, energy in (((40, 0, -30), 4.0), ((60, 0, 140), 1.5), ((-30, 0, 60), 1.0)):
    l = bpy.data.objects.new("L", bpy.data.lights.new("L", "SUN"))
    l.data.energy = energy
    l.rotation_euler = [math.radians(a) for a in rot]
    scene.collection.objects.link(l)

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = 0.56
cam.location = (0, -2, 0.35)
cam.rotation_euler = (math.radians(80), 0, 0)
scene.collection.objects.link(cam)
scene.camera = cam
scene.render.filepath = os.path.join(OUT, "gem_icon.png")
bpy.ops.render.render(write_still=True)
print("DONE")
