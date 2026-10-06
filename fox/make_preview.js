// white_fox.glb 를 base64로 내장한 preview.html 을 생성한다.  실행: node make_preview.js
const fs = require("fs");
const glb = fs.readFileSync(__dirname + "/white_fox.glb").toString("base64");

const html = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>하얀 여우 미리보기</title>
<style>
  :root { --bg1:#cfe3f3; --bg2:#eef5fb; --ui:#ffffffd9; --fg:#27323f; --accent:#4a7fb5; }
  @media (prefers-color-scheme: dark) { :root { --bg1:#1d2733; --bg2:#2b3a4b; --ui:#101820cc; --fg:#e6edf5; --accent:#7fb2e5; } }
  html,body { margin:0; height:100%; overflow:hidden; font-family:"Malgun Gothic",system-ui,sans-serif; color:var(--fg);
    background:linear-gradient(var(--bg1),var(--bg2)); }
  canvas { display:block; width:100%; height:100%; }
  .bar { position:fixed; left:50%; bottom:22px; transform:translateX(-50%); display:flex; gap:8px; padding:10px;
    background:var(--ui); border-radius:14px; box-shadow:0 4px 18px #0003; backdrop-filter:blur(6px); }
  button { font:inherit; font-size:15px; padding:9px 18px; border:2px solid transparent; border-radius:10px; cursor:pointer;
    background:transparent; color:var(--fg); }
  button:hover { background:#8884; }
  button.on { border-color:var(--accent); color:var(--accent); font-weight:700; }
  .title { position:fixed; top:16px; left:20px; font-size:18px; font-weight:700; }
  .hint { position:fixed; top:44px; left:20px; font-size:13px; opacity:.7; }
  #msg { position:fixed; inset:0; display:grid; place-items:center; font-size:16px; pointer-events:none; }
</style>
</head>
<body>
<div class="title">🦊 하얀 여우</div>
<div class="hint">드래그: 회전 · 휠: 확대/축소 · 키: 1 대기 / 2 이동 / 3 점프(Space)</div>
<div id="msg">불러오는 중…</div>
<div class="bar">
  <button data-a="Idle" class="on">대기 (1)</button>
  <button data-a="Move">이동 (2)</button>
  <button data-a="Jump">점프 (3)</button>
</div>
<script type="importmap">
{ "imports": {
  "three": "https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.module.js",
  "three/addons/": "https://cdn.jsdelivr.net/npm/three@0.160.0/examples/jsm/" } }
</script>
<script type="module">
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { RoomEnvironment } from "three/addons/environments/RoomEnvironment.js";

const GLB = "${glb}";
const bytes = Uint8Array.from(atob(GLB), c => c.charCodeAt(0));

const renderer = new THREE.WebGLRenderer({ antialias:true, alpha:true });
renderer.setPixelRatio(devicePixelRatio);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.0;
document.body.prepend(renderer.domElement);

const scene = new THREE.Scene();
scene.environment = new THREE.PMREMGenerator(renderer).fromScene(new RoomEnvironment(), 0.04).texture;
const camera = new THREE.PerspectiveCamera(35, 1, 0.1, 50);
camera.position.set(1.7, 1.15, 2.3);    // 여우는 +Z 를 바라봄 → 앞쪽 사선에서 본다
const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0.5, 0);
controls.enableDamping = true;
controls.maxPolarAngle = Math.PI * 0.52;

scene.add(new THREE.HemisphereLight(0xffffff, 0x9aaabb, 0.6));
const sun = new THREE.DirectionalLight(0xfff6ee, 1.6);
sun.position.set(2, 4, 3);
sun.castShadow = true;
sun.shadow.mapSize.set(1024, 1024);
Object.assign(sun.shadow.camera, { left:-2, right:2, top:2, bottom:-2 });
scene.add(sun);

// 바닥 타일 (게임의 타일 느낌)
const tile = new THREE.Mesh(new THREE.BoxGeometry(2, 0.12, 2),
  new THREE.MeshStandardMaterial({ color:0x6f9a62, roughness:.9 }));
tile.position.y = -0.06; tile.receiveShadow = true;
scene.add(tile);

function resize() {
  renderer.setSize(innerWidth, innerHeight);
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
}
addEventListener("resize", resize); resize();

let mixer, actions = {}, current, fox;
const clock = new THREE.Clock();

new GLTFLoader().parse(bytes.buffer, "", gltf => {
  fox = gltf.scene;
  fox.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; o.frustumCulled = false; } });
  scene.add(fox);
  mixer = new THREE.AnimationMixer(fox);
  for (const clip of gltf.animations) actions[clip.name] = mixer.clipAction(clip);
  actions.Jump.setLoop(THREE.LoopOnce); actions.Jump.clampWhenFinished = true;
  document.getElementById("msg").remove();
  play("Idle");
}, err => { document.getElementById("msg").textContent = "모델 로드 실패: " + err.message; });

function play(name) {
  const next = actions[name]; if (!next) return;
  if (current && current !== next) current.fadeOut(0.2);
  next.reset().fadeIn(0.2).play();
  current = next;
  document.querySelectorAll("button").forEach(b => b.classList.toggle("on", b.dataset.a === name));
}
// 점프가 끝나면 직전 반복 동작으로 복귀
let back = "Idle";
function onKey(name) { if (name !== "Jump") back = name; play(name); }
document.querySelectorAll("button").forEach(b => b.onclick = () => onKey(b.dataset.a));
addEventListener("keydown", e => {
  if (e.key === "1") onKey("Idle");
  if (e.key === "2") onKey("Move");
  if (e.key === "3" || e.code === "Space") { e.preventDefault(); play("Jump"); }
});
addEventListener("keydown", () => {});
setInterval(() => { if (current === actions.Jump && !actions.Jump.isRunning()) play(back); }, 100);

renderer.setAnimationLoop(() => {
  const dt = clock.getDelta();
  if (mixer) mixer.update(dt);
  controls.update();
  renderer.render(scene, camera);
});
</script>
</body>
</html>`;
fs.writeFileSync(__dirname + "/preview.html", html);
console.log("preview.html", (html.length / 1024).toFixed(0) + "KB");
