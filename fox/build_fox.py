"""하얀 여우 제작 스크립트 v2 (Blender 5.x) — ref/fox_turnaround.png 기반.

실행:
  blender -b -P build_fox.py

산출물 (이 스크립트와 같은 폴더):
  white_fox.blend / white_fox.fbx / white_fox.glb

여우는 Blender에서 -Y 방향을 바라본다 (glTF/Unity에서는 +Z 정면).
털 색은 정점 색(Color Attribute "Col")으로 칠한다: 흰 몸, 하늘빛 발끝, 얼음빛 꼬리 끝, 분홍 귀 안쪽.
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Vector

OUT = os.path.dirname(os.path.abspath(__file__))
FPS = 24

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS

# ---------------------------------------------------------------- 색 / 재질
WHITE = (0.97, 0.98, 1.0)
SHADE = (0.86, 0.9, 0.97)       # 배 쪽 은은한 푸른 그늘
PAW = (0.66, 0.75, 0.9)         # 발끝
ICE = (0.62, 0.75, 0.95)        # 꼬리 끝
PINK = (0.98, 0.62, 0.68)       # 귀 안쪽


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def material(name, color=None, rough=0.75, vcol=False, emit=0.0):
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    if vcol:
        a = nt.nodes.new("ShaderNodeVertexColor")
        a.layer_name = "Col"
        nt.links.new(a.outputs["Color"], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (*color, 1)
    if emit:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m


M_FUR = material("Fur", vcol=True, rough=0.85)
M_IRIS = material("EyeIris", (0.05, 0.1, 0.28), rough=0.12)
M_PUPIL = material("EyePupil", (0.005, 0.006, 0.012), rough=0.08)
M_SHINE = material("EyeShine", (1, 1, 1), rough=0.1, emit=2.0)
M_NOSE = material("Nose", (0.02, 0.02, 0.03), rough=0.25)

# ---------------------------------------------------------------- 메시 도우미
parts = []
BONES = ["root", "body", "head", "ear_L", "ear_R", "tail1", "tail2", "tail3",
         "leg_FL", "leg_FR", "leg_BL", "leg_BR"]


def finalize(o, mat, color_fn, weight_fn):
    """모디파이어 적용 → 재질, 정점 색, 본 웨이트 지정."""
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target="MESH")
    me = o.data
    me.materials.clear()
    me.materials.append(mat)
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        p.use_smooth = True
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    groups = {b: o.vertex_groups.new(name=b) for b in BONES}
    mw = o.matrix_world
    for v in me.vertices:
        co = mw @ v.co
        col.data[v.index].color = (*color_fn(co), 1.0)
        for b, w in weight_fn(co).items():
            if w > 0.001:
                groups[b].add([v.index], w, "REPLACE")
    parts.append(o)
    return o


def subsurf(o, levels=2):
    m = o.modifiers.new("Sub", "SUBSURF")
    m.levels = m.render_levels = levels


def primitive(kind, loc, scale, rot=(0, 0, 0), **kw):
    if kind == "sphere":
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=14, radius=1, **kw)
    elif kind == "cone":
        bpy.ops.mesh.primitive_cone_add(vertices=16, **kw)
    o = bpy.context.active_object
    o.location = loc
    o.rotation_euler = [math.radians(a) for a in rot]
    o.scale = scale
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o


def skin(name, points, edges, radii):
    me = bpy.data.meshes.new(name)
    me.from_pydata(points, edges, [])
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    sk = o.modifiers.new("Skin", "SKIN")
    sk.branch_smoothing = 0.6
    sk.use_smooth_shade = True
    for i, r in enumerate(radii):
        d = me.skin_vertices[0].data[i]
        d.radius = r if isinstance(r, tuple) else (r, r)
        d.use_root = i == 0
    subsurf(o, 2)
    return o


def seg_dist(p, a, b):
    a, b = Vector(a), Vector(b)
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def soft_weights(p, segs, power=6):
    """각 본의 대표 선분까지 거리 기반 부드러운 웨이트."""
    raw = {b: 1.0 / max(seg_dist(p, *s), 1e-4) ** power for b, s in segs.items()}
    tot = sum(raw.values())
    return {b: w / tot for b, w in raw.items()}


def rigid(bone):
    return lambda co: {bone: 1.0}


def white(co):
    return WHITE


# ---------------------------------------------------------------- 몸통 + 다리 (Skin)
LX = 0.08
FY, BY = -0.15, 0.15
SPINE_Z = 0.34
pts = [
    (0, -0.25, 0.57),         # 0 목 위
    (0, -0.21, 0.46),         # 1 목
    (0, -0.14, SPINE_Z + 0.01),  # 2 가슴
    (0, 0.0, SPINE_Z),        # 3 허리
    (0, 0.14, SPINE_Z + 0.01),   # 4 엉덩이
]
rad = [0.1, 0.125, 0.15, 0.128, 0.142]
edges = [(0, 1), (1, 2), (2, 3), (3, 4)]
LEG_SEGS = {}
for k, sx, front in (("FL", 1, True), ("FR", -1, True), ("BL", 1, False), ("BR", -1, False)):
    x = LX * sx
    if front:
        chain = [(x, FY, 0.28), (x, FY - 0.01, 0.16), (x, FY - 0.012, 0.062), (x, FY - 0.04, 0.028)]
        cr = [0.068, 0.048, 0.04, (0.048, 0.042)]
    else:
        chain = [(x, BY + 0.01, 0.29), (x, BY - 0.03, 0.18), (x, BY + 0.03, 0.09), (x, BY, 0.028)]
        cr = [0.088, 0.058, 0.04, (0.048, 0.042)]
    base = len(pts)
    pts += chain
    rad += cr
    edges.append((2 if front else 4, base))
    edges += [(base + i, base + i + 1) for i in range(3)]
    LEG_SEGS[f"leg_{k}"] = (chain[0], chain[-1])

body = skin("BodySkin", pts, edges, rad)

BODY_SEGS = {"body": ((0, -0.14, SPINE_Z), (0, 0.14, SPINE_Z)),
             "head": ((0, -0.21, 0.49), (0, -0.25, 0.6))}
BODY_SEGS.update({b: ((a[0], a[1], a[2] - 0.04), c) for b, (a, c) in LEG_SEGS.items()})


def body_color(co):
    c = mix(WHITE, SHADE, smoothstep(0.31, 0.24, co.z) * 0.6)
    return mix(c, PAW, smoothstep(0.12, 0.05, co.z))


finalize(body, M_FUR, body_color, lambda co: soft_weights(co, BODY_SEGS))

# ---------------------------------------------------------------- 꼬리 (Skin)
# 엉덩이에서 위로 솟았다가 뒤로 말려 끝이 아래를 향함
tail_pts = [(0, 0.11, 0.37), (0, 0.2, 0.45), (0, 0.28, 0.57), (0, 0.38, 0.66),
            (0, 0.5, 0.66), (0, 0.58, 0.57), (0, 0.6, 0.45), (0, 0.57, 0.34)]
tail_r = [0.115, 0.125, 0.14, 0.15, 0.146, 0.123, 0.085, 0.022]
tail = skin("TailSkin", tail_pts, [(i, i + 1) for i in range(len(tail_pts) - 1)], tail_r)
TAIL_SEGS = {"tail1": (tail_pts[0], tail_pts[2]), "tail2": (tail_pts[2], tail_pts[4]),
             "tail3": (tail_pts[4], tail_pts[7])}


def tail_param(co):
    """꼬리 시작(0) → 끝(1) 위치."""
    best, bi = 9, 0
    for i in range(len(tail_pts) - 1):
        d = seg_dist(co, tail_pts[i], tail_pts[i + 1])
        if d < best:
            best, bi = d, i
    return bi / (len(tail_pts) - 2)


finalize(tail, M_FUR, lambda co: mix(WHITE, ICE, smoothstep(0.45, 0.95, tail_param(co))),
         lambda co: soft_weights(co, TAIL_SEGS, 4))


# ---------------------------------------------------------------- 털뭉치 도우미
def tuft(at, direction, length, radius, bone, color=white, flat=0.55):
    """at 에서 direction 으로 뻗는 물방울 모양의 부드러운 털뭉치."""
    d = Vector(direction).normalized()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=1)
    o = bpy.context.active_object
    for v in o.data.vertices:
        t = (v.co.z + 1) / 2                 # 뿌리 0 → 끝 1
        k = 1 - 0.88 * t ** 1.6              # 끝으로 갈수록 가늘게
        v.co.x *= radius * k
        v.co.y *= radius * flat * k
        v.co.z *= length / 2
        v.co.y += 0.3 * length * t * t       # 끝을 살짝 휘게
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d)
    o.location = Vector(at) + d * length * 0.4
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    subsurf(o, 1)
    finalize(o, M_FUR, color, rigid(bone) if isinstance(bone, str) else bone)


# ---------------------------------------------------------------- 가슴 털
chest_w = lambda co: soft_weights(co, {"body": BODY_SEGS["body"], "head": BODY_SEGS["head"]})
o = primitive("sphere", (0, -0.23, 0.46), (0.13, 0.1, 0.14))
subsurf(o, 1)
finalize(o, M_FUR, white, chest_w)
for (x, y, z, dx, dz, ln, r) in (
        (0, -0.29, 0.41, 0, -1, 0.1, 0.075),
        (0.06, -0.28, 0.42, 0.35, -1, 0.09, 0.068), (-0.06, -0.28, 0.42, -0.35, -1, 0.09, 0.068)):
    tuft((x, y, z), (dx, -0.5, dz), ln, r, chest_w)

# ---------------------------------------------------------------- 머리 (HC 기준 상대 좌표)
HC = Vector((0, -0.31, 0.72))   # 머리 중심


def H(x, y, z):
    return HC + Vector((x, y, z))


head = primitive("sphere", HC, (0.2, 0.185, 0.175))
subsurf(head, 1)
finalize(head, M_FUR, white, rigid("head"))

# 볼 (얼굴 아래쪽 옆으로 통통하게)
for sx in (1, -1):
    o = primitive("sphere", H(0.105 * sx, -0.05, -0.045), (0.1, 0.09, 0.075))
    subsurf(o, 1)
    finalize(o, M_FUR, white, rigid("head"))

# 주둥이: 짧고 앞으로 갈수록 가늘어짐
MZ = H(0, -0.14, -0.03)
muzzle = primitive("sphere", MZ, (0.078, 0.11, 0.062))
for v in muzzle.data.vertices:
    t = smoothstep(MZ.y + 0.05, MZ.y - 0.11, v.co.y)     # 앞쪽일수록 1
    v.co.x *= 1 - 0.4 * t
    v.co.z = MZ.z + (v.co.z - MZ.z) * (1 - 0.3 * t) + 0.01 * t
subsurf(muzzle, 1)
finalize(muzzle, M_FUR, white, rigid("head"))


def on_face(x, z, push=0.0):
    """정면(-Y)에서 주둥이/머리 표면으로 광선을 쏴 표면 위 점을 구한다."""
    best = None
    for obj in (muzzle, head):
        ok, loc, nrm, _ = obj.ray_cast(Vector((x, -2, z)), Vector((0, 1, 0)))
        if ok and (best is None or loc.y < best[0].y):
            best = (loc, nrm)
    if best is None:
        return None
    return best[0] + best[1] * push


# 코: 주둥이 끝 위쪽 표면에
nose_at = on_face(0, MZ.z + 0.012, -0.008)
o = primitive("sphere", nose_at, (0.03, 0.022, 0.022))
subsurf(o, 1)
finalize(o, M_NOSE, white, rigid("head"))

# 입 (ω 모양 미소): 표면 위에 곡선
curve = bpy.data.curves.new("Mouth", "CURVE")
curve.dimensions = "3D"
curve.bevel_depth = 0.004
curve.bevel_resolution = 2
mz = MZ.z
mouth = [p for p in (on_face(x, mz + dz, 0.002) for x, dz in (
    (-0.04, -0.006), (-0.026, -0.018), (-0.012, -0.02), (0, -0.012),
    (0.012, -0.02), (0.026, -0.018), (0.04, -0.006))) if p is not None]
sp = curve.splines.new("POLY")
sp.points.add(len(mouth) - 1)
for p, c in zip(sp.points, mouth):
    p.co = (*c, 1)
sp.use_smooth = True
o = bpy.data.objects.new("Mouth", curve)
bpy.context.collection.objects.link(o)
finalize(o, M_NOSE, white, rigid("head"))
# 코와 입 사이 세로선
curve = bpy.data.curves.new("Philtrum", "CURVE")
curve.dimensions = "3D"
curve.bevel_depth = 0.0035
sp = curve.splines.new("POLY")
a, b = on_face(0, MZ.z + 0.0, 0.002), on_face(0, mz - 0.012, 0.002)
sp.points.add(1)
sp.points[0].co, sp.points[1].co = (*a, 1), (*b, 1)
o = bpy.data.objects.new("Philtrum", curve)
bpy.context.collection.objects.link(o)
finalize(o, M_NOSE, white, rigid("head"))

# 눈: 애니메이션풍 2D 눈 그림을 붙이는 판 (머리 표면을 따라 휘어짐, UV 있음).
# 그림(눈 뜸/웃음/감음)은 Unity 에서 FoxExpression 이 바꿔 끼운다. 그림은 "왼쪽에 있는 눈, 눈꼬리가 왼쪽"으로
# 그려져 있어서, 보는 사람 기준 왼쪽 눈(-X)은 그대로, 오른쪽 눈(+X)은 UV 를 좌우 반전한다.
M_EYE = material("EyeDecal", (1, 1, 1))
EYE_SIZE = 0.2
EYE_GRID = 8


def surface_hit(origin, direction):
    """지금까지 만든 부품들 중 광선이 처음 닿는 곳 (위치, 노멀)."""
    best = None
    for obj in parts:
        if obj.type != "MESH":
            continue
        ok, loc, nrm, _ = obj.ray_cast(origin, direction)
        if ok and (best is None or (loc - origin).length < (best[0] - origin).length):
            best = (loc, nrm)
    return best


for sx in (1, -1):
    center, normal = surface_hit(Vector((0.1 * sx, -2, HC.z + 0.045)), Vector((0, 1, 0)))
    n = normal.normalized()
    right = (Vector((1, 0, 0)) - n * n.x).normalized()
    up = n.cross(right)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    grid = {}
    for b in range(EYE_GRID + 1):
        for a in range(EYE_GRID + 1):
            s_, t_ = a / EYE_GRID - 0.5, b / EYE_GRID - 0.5
            p0 = center + right * s_ * EYE_SIZE + up * t_ * EYE_SIZE
            hit = surface_hit(p0 + n * 0.2, -n)
            p = hit[0] + hit[1].normalized() * 0.004 if hit else p0
            grid[a, b] = bm.verts.new(p)
    for b in range(EYE_GRID):
        for a in range(EYE_GRID):
            f = bm.faces.new((grid[a, b], grid[a + 1, b], grid[a + 1, b + 1], grid[a, b + 1]))
            for loop, (aa, bb) in zip(f.loops, ((a, b), (a + 1, b), (a + 1, b + 1), (a, b + 1))):
                u = aa / EYE_GRID
                loop[uv].uv = (u if sx < 0 else 1 - u, bb / EYE_GRID)
    me = bpy.data.meshes.new("Eye")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("Eye", me)
    bpy.context.collection.objects.link(o)
    finalize(o, M_EYE, white, rigid("head"))

# 귀: 크고 곧게 선 납작한 삼각형 + 분홍 안쪽
EAR_BASE = {}
for sx, side_name in ((1, "L"), (-1, "R")):
    rot = (6, 14 * sx, -10 * sx)
    base = H(0.11 * sx, 0.0, 0.13)
    EAR_BASE[side_name] = base
    up_dir = Vector((0, 0, 1))
    up_dir.rotate(Euler([math.radians(a) for a in rot]))
    o = primitive("cone", base + up_dir * 0.17, (1, 0.38, 1), rot,
                  radius1=0.175, radius2=0.012, depth=0.38)
    subsurf(o, 2)
    finalize(o, M_FUR, white, rigid(f"ear_{side_name}"))
    o = primitive("cone", base + up_dir * 0.17 + Vector((0, -0.04, 0)), (1, 0.24, 1), rot,
                  radius1=0.125, radius2=0.008, depth=0.3)
    subsurf(o, 2)
    finalize(o, M_FUR, lambda co: PINK, rigid(f"ear_{side_name}"))

# 볼 털: 얼굴 옆으로 퍼지는 부드러운 털
for sx in (1, -1):
    for (y, z, dx, dz, ln, r) in ((0.0, 0.01, 1, 0.3, 0.11, 0.055), (-0.03, -0.05, 1, -0.15, 0.13, 0.062),
                                  (-0.05, -0.1, 0.7, -0.7, 0.11, 0.055)):
        tuft(H(0.17 * sx, y, z), (dx * sx, 0.2, dz), ln, r, "head")


# ---------------------------------------------------------------- 하나의 메시로 합치기
bpy.ops.object.select_all(action="DESELECT")
for p in parts:
    p.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
fox = bpy.context.active_object
fox.name = "WhiteFox"
fox.data.name = "WhiteFox"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ---------------------------------------------------------------- 리깅
bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
arm_obj = bpy.context.active_object
arm_obj.name = "FoxArmature"
arm = arm_obj.data
arm.name = "FoxArmature"
arm.edit_bones.remove(arm.edit_bones[0])


def bone(name, head, tail, parent=None):
    b = arm.edit_bones.new(name)
    b.head, b.tail = head, tail
    if parent:
        b.parent = arm.edit_bones[parent]


bone("root", (0, 0, 0), (0, 0.15, 0))
bone("body", (0, 0, SPINE_Z), (0, 0.15, SPINE_Z), "root")
bone("head", (0, -0.21, 0.47), (0, -0.06, 0.47), "body")
bone("ear_L", EAR_BASE["L"], EAR_BASE["L"] + Vector((0, 0, 0.15)), "head")
bone("ear_R", EAR_BASE["R"], EAR_BASE["R"] + Vector((0, 0, 0.15)), "head")
bone("tail1", tail_pts[0], (0, tail_pts[0][1] + 0.15, tail_pts[0][2]), "body")
bone("tail2", tail_pts[2], (0, tail_pts[2][1] + 0.15, tail_pts[2][2]), "tail1")
bone("tail3", tail_pts[4], (0, tail_pts[4][1] + 0.15, tail_pts[4][2]), "tail2")
for k, (top, _) in LEG_SEGS.items():
    bone(k, top, (top[0], top[1], 0.0), "root")

bpy.ops.object.mode_set(mode="OBJECT")
fox.parent = arm_obj
mod = fox.modifiers.new("Armature", "ARMATURE")
mod.object = arm_obj

# ---------------------------------------------------------------- 애니메이션
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="POSE")
pb = arm_obj.pose.bones
for b in pb:
    b.rotation_mode = "XYZ"
arm_obj.animation_data_create()


def new_action(name, length):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm_obj.animation_data.action = act
    act.use_frame_range = True
    act.frame_start, act.frame_end = 1, length
    for b in pb:
        b.location = (0, 0, 0)
        b.rotation_euler = (0, 0, 0)
        b.scale = (1, 1, 1)
    return act


def key(frame, bone_name, rot=None, loc=None, scale=None):
    b = pb[bone_name]
    if rot is not None:
        b.rotation_euler = [math.radians(a) for a in rot]
        b.keyframe_insert("rotation_euler", frame=frame)
    if loc is not None:
        b.location = loc
        b.keyframe_insert("location", frame=frame)
    if scale is not None:
        b.scale = scale
        b.keyframe_insert("scale", frame=frame)


LEG_NAMES = ["leg_FL", "leg_FR", "leg_BL", "leg_BR"]

# --- Idle (48프레임, 반복): 숨쉬기 + 고개 갸웃 + 꼬리 살랑 + 귀 쫑긋
N = 48
act_idle = new_action("Idle", N)
for f in range(1, N + 2, 4):
    t = (f - 1) / N * 2 * math.pi
    key(f, "root", loc=(0, 0, 0))
    key(f, "body", scale=(1 + 0.015 * math.sin(t), 1, 1 + 0.02 * math.sin(t)))
    key(f, "head", rot=(3 * math.sin(t - 0.6), 0, 5 * math.sin(t)), loc=(0, 0, 0.006 * math.sin(t)))
    key(f, "tail1", rot=(2 * math.sin(t), 0, 10 * math.sin(t)))
    key(f, "tail2", rot=(0, 0, 12 * math.sin(t - 0.7)))
    key(f, "tail3", rot=(0, 0, 14 * math.sin(t - 1.4)))
    tw = max(0.0, math.sin(2 * t - 1)) ** 6   # 가끔 귀를 쫑긋
    key(f, "ear_L", rot=(0, 0, 4 * math.sin(t) + 10 * tw))
    key(f, "ear_R", rot=(0, 0, -4 * math.sin(t + 0.5)))
    for k in LEG_NAMES:
        key(f, k, rot=(0, 0, 0))

# --- Move (24프레임, 반복): 대각선 다리 교차 + 통통 튀는 걸음
N = 24
act_move = new_action("Move", N)
SW = 30
for f in range(1, N + 2, 2):
    t = (f - 1) / N * 2 * math.pi
    key(f, "root", loc=(0, 0, 0.03 * abs(math.sin(t))))
    key(f, "body", rot=(2 * math.sin(2 * t), 0, 3 * math.sin(t)))
    key(f, "head", rot=(-4 * math.sin(2 * t), 0, -3 * math.sin(t)))
    key(f, "tail1", rot=(-6, 0, 12 * math.sin(t)))
    key(f, "tail2", rot=(0, 0, 14 * math.sin(t - 0.9)))
    key(f, "tail3", rot=(0, 0, 16 * math.sin(t - 1.8)))
    key(f, "ear_L", rot=(0, 0, 6 * math.sin(2 * t)))
    key(f, "ear_R", rot=(0, 0, -6 * math.sin(2 * t)))
    key(f, "leg_FL", rot=(SW * math.sin(t), 0, 0))
    key(f, "leg_BR", rot=(SW * math.sin(t), 0, 0))
    key(f, "leg_FR", rot=(-SW * math.sin(t), 0, 0))
    key(f, "leg_BL", rot=(-SW * math.sin(t), 0, 0))

# --- Jump (30프레임, 1회): 웅크림 → 도약 → 공중 → 착지 → 복귀
act_jump = new_action("Jump", 30)
J = {
    #  frame: (root z, body scale z, body pitch, head pitch, front leg, back leg, tail pitch)
    1: (0.0, 1.0, 0, 0, 0, 0, 0),
    5: (-0.05, 0.9, -5, 8, 10, -10, 10),
    9: (0.25, 1.06, 8, -12, -45, 35, -10),
    14: (0.45, 1.0, 4, -6, -35, 30, -22),
    19: (0.28, 1.0, -2, 4, 15, -20, -8),
    23: (-0.04, 0.92, -4, 6, 8, -8, 10),
    27: (0.01, 1.02, 0, 0, 0, 0, 0),
    30: (0.0, 1.0, 0, 0, 0, 0, 0),
}
for f, (z, sz, bp, hp, fl, bl, tl) in J.items():
    key(f, "root", loc=(0, 0, z))
    key(f, "body", rot=(bp, 0, 0), scale=(1, 1, sz))
    key(f, "head", rot=(hp, 0, 0))
    key(f, "tail1", rot=(tl, 0, 0))
    key(f, "tail2", rot=(tl * 0.8, 0, 0))
    key(f, "tail3", rot=(tl * 0.6, 0, 0))
    key(f, "ear_L", rot=(-z * 30, 0, -z * 30))
    key(f, "ear_R", rot=(-z * 30, 0, z * 30))
    for k in ("leg_FL", "leg_FR"):
        key(f, k, rot=(fl, 0, 0))
    for k in ("leg_BL", "leg_BR"):
        key(f, k, rot=(bl, 0, 0))

bpy.ops.object.mode_set(mode="OBJECT")
arm_obj.animation_data.action = act_idle
scene.frame_start, scene.frame_end = 1, 48

# ---------------------------------------------------------------- 저장 / 내보내기
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "white_fox.blend"))

bpy.ops.object.select_all(action="DESELECT")
fox.select_set(True)
arm_obj.select_set(True)

bpy.ops.export_scene.gltf(
    filepath=os.path.join(OUT, "white_fox.glb"),
    export_format="GLB",
    use_selection=True,
    export_animation_mode="ACTIONS",
    export_vertex_color="ACTIVE",
)

bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "white_fox.fbx"),
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0.0,
    apply_scale_options="FBX_SCALE_ALL",
    colors_type="LINEAR",
)
print("DONE", len(fox.data.vertices), "verts")
