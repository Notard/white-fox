using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 프로젝트 초기 구성 (2단계). 메뉴 FoxGame/Run Project Setup 또는
/// Unity.exe -batchmode -projectPath FoxGame -executeMethod ProjectSetup.Run -quit
/// 여러 번 실행해도 같은 결과가 나온다.
/// </summary>
public static partial class ProjectSetup
{
    const string FoxFbx = "Assets/Models/white_fox.fbx";
    const string ControllerPath = "Assets/Animations/Fox.controller";
    const string PrefabPath = "Assets/Prefabs/Fox.prefab";
    const string ScenePath = "Assets/Scenes/Main.unity";

    static MoverAssets movers;
    static MechanismAssets mechanisms;

    [MenuItem("FoxGame/Run Project Setup")]
    public static void Run()
    {
        SetupPlayer();
        var mats = CreateMaterials();
        SetupFoxImport(mats);
        var controller = CreateAnimator();
        var prefab = CreatePrefab(controller);
        var board = CreateBoardAssets();
        var gem = CreateGemAssets();
        movers = CreateMoverAssets();
        mechanisms = CreateMechanismAssets();
        CreateScene(prefab, board, gem);
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectSetup] DONE");
    }

    // ------------------------------------------------------------ 플레이어 설정 (Windows 전용)
    static void SetupPlayer()
    {
        PlayerSettings.companyName = "FoxGame";
        PlayerSettings.productName = "White Fox";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
    }

    // ------------------------------------------------------------ 재질
    static Dictionary<string, Material> CreateMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        return new Dictionary<string, Material>
        {
            // 애니메이션풍 셀 셰이딩 (FoxGame/FoxToon): 털은 외곽선, 눈·코는 얇거나 없는 외곽선
            // 털: 외곽선 얇게(0.0032), 그늘 경계 낮게(0.45) → 더 하얀 여우
            ["Fur"] = Toon("Fox_Fur", Color.white, new Color(0.7f, 0.71f, 0.96f), 0.0032f, 0.45f),
            ["EyeDecal"] = EyeMat(),
            ["Nose"] = Toon("Fox_Nose", new Color(0.06f, 0.05f, 0.09f), new Color(0.7f, 0.7f, 0.85f), 0f),
        };
    }

    /// <summary>셀 셰이딩 재질 (FoxGame/FoxToon). 여우와 소품이 같이 써서 화풍을 맞춘다.</summary>
    static Material Toon(string name, Color baseColor, Color shade, float outline, float threshold = 0.5f,
        Color? outlineColor = null, Texture texture = null, Color? emission = null)
    {
        var m = Mat(name, Shader.Find("FoxGame/FoxToon"), baseColor, 0);
        m.SetColor("_BaseColor", baseColor);
        m.SetColor("_ShadeColor", shade);
        m.SetFloat("_OutlineWidth", outline);
        m.SetColor("_OutlineColor", outlineColor ?? new Color(0.33f, 0.28f, 0.48f));
        m.SetFloat("_ShadeThreshold", threshold);
        m.SetTexture("_BaseMap", texture);
        m.SetColor("_EmissionColor", emission ?? Color.black);
        EditorUtility.SetDirty(m);
        return m;
    }

    const string EyeTexFolder = "Assets/Textures/Fox";

    static Texture2D EyeTex(string name)
    {
        string path = $"{EyeTexFolder}/{name}.png";
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return null;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>애니메이션풍 눈 그림 재질 (FoxGame/ToonDecal). 표정은 FoxExpression 이 바꾼다.</summary>
    static Material EyeMat()
    {
        var m = Mat("Fox_Eye", Shader.Find("FoxGame/ToonDecal"), Color.white, 0);
        m.SetTexture("_BaseMap", EyeTex("eye_open"));
        m.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Mat(string name, Shader shader, Color color, float smoothness)
    {
        string path = $"Assets/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        if (m.HasProperty("_BaseColor") && name != "Fox_Fur") m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------ 여우 FBX 임포트 설정
    static void SetupFoxImport(Dictionary<string, Material> mats)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(FoxFbx);
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.importAnimation = true;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            string n = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
            c.name = n;
            c.loopTime = n == "Idle" || n == "Move";
            Debug.Log($"[ProjectSetup] clip {c.takeName} -> {n} (loop {c.loopTime})");
        }
        imp.clipAnimations = clips;

        foreach (var kv in mats)
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
        imp.SaveAndReimport();
    }

    static AnimationClip Clip(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(FoxFbx).OfType<AnimationClip>()
            .First(c => c.name == name);

    // ------------------------------------------------------------ 애니메이터 (Idle / Move / Jump)
    static AnimatorController CreateAnimator()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Moving", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        idle.motion = Clip("Idle");
        var move = sm.AddState("Move");
        move.motion = Clip("Move");
        var jump = sm.AddState("Jump");
        jump.motion = Clip("Jump");
        sm.defaultState = idle;

        var t = idle.AddTransition(move);
        t.hasExitTime = false; t.duration = 0.1f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Moving");

        t = move.AddTransition(idle);
        t.hasExitTime = false; t.duration = 0.15f;
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Moving");

        t = sm.AddAnyStateTransition(jump);
        t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

        t = jump.AddTransition(idle);
        t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.1f;
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Moving");

        t = jump.AddTransition(move);
        t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.1f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Moving");

        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    // ------------------------------------------------------------ 프리팹
    static GameObject CreatePrefab(AnimatorController ctrl)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FoxFbx);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "Fox";
        var anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        var expression = go.GetComponent<FoxExpression>() ?? go.AddComponent<FoxExpression>();
        expression.eyeOpen = EyeTex("eye_open");
        expression.eyeHappy = EyeTex("eye_happy");
        expression.eyeBlink = EyeTex("eye_blink");
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = ShadowCastingMode.On;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ------------------------------------------------------------ 보드 (타일 / 장애물 / 맵)
    const string TileFbx = "Assets/Models/tile.fbx";
    const string RockFbx = "Assets/Models/rock.fbx";

    const string BlockFbx = "Assets/Models/block.fbx";
    const string SwitchFbx = "Assets/Models/switch.fbx";
    const string DoorFbx = "Assets/Models/door.fbx";
    const string StairsFbx = "Assets/Models/stairs.fbx";

    struct BoardAssets
    {
        public GameObject tile, iceTile, crumbleTile, block, rock, sw, door, stairs;
        public Material terrain;
        public GameObject[] decor;   // 풀숲, 꽃, 자갈 (자갈이 마지막)
    }

    static BoardAssets CreateBoardAssets()
    {
        foreach (var name in new[] { "grass_top", "dirt_side", "stone", "ice_top", "cracked_top", "cliff_side", "grass_lush" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath($"Assets/Textures/Tiles/{name}.png");
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = 1024;
            ti.anisoLevel = 4;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var grass = TexMat("Tile_Grass", lit, "grass_top", 0.08f);
        var dirt = TexMat("Tile_Dirt", lit, "dirt_side", 0.05f);
        var stone = Toon("Rock_Stone", Color.white, new Color(0.62f, 0.64f, 0.88f), 0.0026f, 0.5f,
            new Color(0.22f, 0.22f, 0.32f), AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Tiles/stone.png"));
        var ice = TexMat("Tile_Ice", lit, "ice_top", 0.85f);
        var cracked = TexMat("Tile_Cracked", lit, "cracked_top", 0.08f);
        var button = Toon("Switch_Button", new Color(0.97f, 0.5f, 0.66f), new Color(0.78f, 0.62f, 0.9f), 0.0026f, 0.5f,
            new Color(0.45f, 0.18f, 0.32f), null, new Color(0.12f, 0.03f, 0.06f));
        var crystal = Toon("Door_Crystal", new Color(0.66f, 0.64f, 0.98f), new Color(0.62f, 0.66f, 1f), 0.0026f, 0.5f,
            new Color(0.25f, 0.22f, 0.5f), null, new Color(0.08f, 0.08f, 0.2f));

        Remap(TileFbx, ("GrassTop", grass), ("DirtSide", dirt));
        Remap(RockFbx, ("Stone", stone));
        Remap(BlockFbx, ("DirtSide", dirt));
        Remap(SwitchFbx, ("Stone", stone), ("SwitchButton", button));
        Remap(DoorFbx, ("Stone", stone), ("DoorCrystal", crystal));
        Remap(StairsFbx, ("Stone", stone));

        // 섬 지형 재질 (FoxGame/Terrain 셰이더) + 장식
        var terrain = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Terrain.mat");
        if (terrain == null)
        {
            terrain = new Material(Shader.Find("FoxGame/Terrain"));
            AssetDatabase.CreateAsset(terrain, "Assets/Materials/Terrain.mat");
        }
        terrain.shader = Shader.Find("FoxGame/Terrain");
        Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Textures/Tiles/{n}.png");
        terrain.SetTexture("_GrassTex", Tex("grass_lush"));
        terrain.SetTexture("_IceTex", Tex("ice_top"));
        terrain.SetTexture("_CrackTex", Tex("cracked_top"));
        terrain.SetTexture("_CliffTex", Tex("cliff_side"));
        EditorUtility.SetDirty(terrain);

        // 풀잎은 외곽선 없이 (가는 잎에 선이 지저분해짐), 꽃은 아주 얇게
        var decorGrass = Toon("Decor_Grass", new Color(0.4f, 0.7f, 0.24f), new Color(0.6f, 0.75f, 0.85f), 0f);
        var petal = Toon("Decor_Petal", new Color(0.99f, 0.98f, 0.96f), new Color(0.75f, 0.76f, 0.95f), 0.0012f, 0.45f);
        var center = Toon("Decor_Center", new Color(1f, 0.82f, 0.25f), new Color(0.95f, 0.65f, 0.55f), 0f);
        Remap("Assets/Models/decor_grass.fbx", ("DecorGrass", decorGrass));
        Remap("Assets/Models/decor_flower.fbx", ("DecorGrass", decorGrass), ("DecorPetal", petal), ("DecorCenter", center));
        Remap("Assets/Models/decor_pebble.fbx", ("Stone", stone));

        var assets = new BoardAssets
        {
            tile = ModelPrefab(TileFbx, "Assets/Prefabs/Tile.prefab", true),
            // 얼음·금 간 타일은 같은 타일 모델에서 윗면 재질만 바꾼다
            iceTile = ModelPrefab(TileFbx, "Assets/Prefabs/Tile_Ice.prefab", true, (grass, ice)),
            crumbleTile = ModelPrefab(TileFbx, "Assets/Prefabs/Tile_Crumble.prefab", true, (grass, cracked)),
            block = ModelPrefab(BlockFbx, "Assets/Prefabs/Block.prefab", true),
            rock = ModelPrefab(RockFbx, "Assets/Prefabs/Obstacle.prefab", true),
            sw = ModelPrefab(SwitchFbx, "Assets/Prefabs/Switch.prefab", true),
            door = ModelPrefab(DoorFbx, "Assets/Prefabs/Door.prefab", true),
            stairs = ModelPrefab(StairsFbx, "Assets/Prefabs/Stairs.prefab", true),
            terrain = terrain,
            decor = new[]
            {
                ModelPrefab("Assets/Models/decor_grass.fbx", "Assets/Prefabs/Decor_Grass.prefab", false),
                ModelPrefab("Assets/Models/decor_flower.fbx", "Assets/Prefabs/Decor_Flower.prefab", false),
                ModelPrefab("Assets/Models/decor_pebble.fbx", "Assets/Prefabs/Decor_Pebble.prefab", true),
            },
        };
        return assets;
    }

    static Material TexMat(string name, Shader shader, string texture, float smoothness)
    {
        var m = Mat(name, shader, Color.white, smoothness);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Textures/Tiles/{texture}.png");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Remap(string fbx, params (string source, Material mat)[] maps)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
        imp.importAnimation = false;
        imp.animationType = ModelImporterAnimationType.None;
        imp.bakeAxisConversion = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        foreach (var (source, mat) in maps)
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source), mat);
        imp.SaveAndReimport();
    }

    static GameObject ModelPrefab(string fbx, string prefabPath, bool castShadows, (Material from, Material to)? swap = null)
    {
        // 빈 루트 아래에 모델을 두어, 보드가 루트를 회전/확대해도 모델의 축 보정(-90° X)은 유지되게 한다
        var go = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), go.transform);
        model.transform.localPosition = Vector3.zero;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
        if (swap is { } sw)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                    if (mats[k] == sw.from) mats[k] = sw.to;
                r.sharedMaterials = mats;
            }
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ------------------------------------------------------------ 보석 / 반짝이 / UI 아이콘
    const string GemFbx = "Assets/Models/gem.fbx";
    const string GemIconPath = "Assets/Textures/UI/gem_icon.png";

    static Gem CreateGemAssets()
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(GemIconPath);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.SaveAndReimport();

        // 보석: 밝은 하늘색 셀 셰이딩 + 은은한 발광 + 진한 파란 외곽선, 반사광 크게
        var gemMat = Toon("Gem", new Color(0.35f, 0.78f, 1f), new Color(0.45f, 0.6f, 1f), 0.0028f, 0.45f,
            new Color(0.1f, 0.25f, 0.55f), null, new Color(0.05f, 0.2f, 0.4f));
        gemMat.SetFloat("_SpecSize", 0.08f);
        gemMat.SetFloat("_SpecStrength", 0.8f);
        Remap(GemFbx, ("Gem", gemMat));

        var sparkle = CreateSparklePrefab();

        var root = new GameObject("Gem");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GemFbx), root.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale *= 1.6f;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = ShadowCastingMode.On;
        root.AddComponent<Gem>().sparklePrefab = sparkle;
        AddGemGlint(root);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Gem.prefab");
        Object.DestroyImmediate(root);
        return prefab.GetComponent<Gem>();
    }

    static ParticleSystem CreateSparklePrefab()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sparkle.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, "Assets/Materials/Sparkle.mat");
        }
        // 가산 혼합 투명 파티클
        mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 2);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);

        var go = new GameObject("Sparkle");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.9f, 1f), Color.white);
        main.gravityModifier = 0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0, 28) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Sparkle.prefab");
        Object.DestroyImmediate(go);
        return prefab.GetComponent<ParticleSystem>();
    }

    // ------------------------------------------------------------ 메인 씬
    const string MenuScenePath = "Assets/Scenes/Menu.unity";
    const string SkinPath = "Assets/Settings/UiSkin.asset";
    const string GenFolder = "Assets/Textures/UI/gen";

    static Sprite RoundedSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    static void CreateScene(GameObject foxPrefab, BoardAssets boardAssets, Gem gemPrefab)
    {
        var skin = CreateSkin();
        CreateGameScene(foxPrefab, boardAssets, gemPrefab, skin);
        CreateMenuScene(foxPrefab, boardAssets, skin);
        // 메뉴가 첫 화면, 게임 씬은 두 번째
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MenuScenePath, true),
            new EditorBuildSettingsScene(ScenePath, true),
        };
    }

    // ------------------------------------------------------------ 후처리 (톤매핑, 블룸, 비네트, 색감)
    static VolumeProfile PostProfile()
    {
        const string path = "Assets/Settings/FoxPost.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.45f);      // ACES 가 어둡게 만드는 만큼 보정
        color.contrast.Override(8f);
        color.saturation.Override(14f);
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.35f);
        bloom.threshold.Override(1.05f);
        bloom.scatter.Override(0.6f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.5f);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    // ------------------------------------------------------------ UI 그림 (Codex) → 스킨
    /// <summary>9-slice 테두리 (픽셀, 왼·아래·오른·위). 그림이 바뀌면 여기를 맞춘다.</summary>
    static readonly Dictionary<string, Vector4> SliceBorders = new()
    {
        // 여백을 잘라 낸 그림 기준 (panel 936×912, primary 962×252, secondary 975×299)
        ["ui_panel"] = new Vector4(165, 165, 165, 165),           // 모서리 눈꽃 장식까지
        ["ui_button_primary"] = new Vector4(140, 110, 140, 110),  // 알약 끝 반지름 + 위아래 광택
        ["ui_button_secondary"] = new Vector4(160, 130, 160, 130),
    };

    static UiSkin CreateSkin()
    {
        var skin = AssetDatabase.LoadAssetAtPath<UiSkin>(SkinPath);
        if (skin == null)
        {
            skin = ScriptableObject.CreateInstance<UiSkin>();
            AssetDatabase.CreateAsset(skin, SkinPath);
        }
        skin.panel = UiSprite("ui_panel");
        skin.buttonPrimary = UiSprite("ui_button_primary");
        skin.buttonSecondary = UiSprite("ui_button_secondary");
        skin.logo = UiSprite("title_logo");
        skin.iconLock = UiSprite("icon_lock");
        skin.iconClock = UiSprite("icon_clock");
        skin.iconStar = UiSprite("icon_star");
        skin.iconPause = UiSprite("icon_pause");
        skin.iconGem = AssetDatabase.LoadAssetAtPath<Sprite>(GemIconPath);

        string skyPath = $"{GenFolder}/bg_sky.png";
        if (AssetImporter.GetAtPath(skyPath) is TextureImporter sky)
        {
            sky.textureType = TextureImporterType.Default;
            sky.mipmapEnabled = false;
            sky.wrapMode = TextureWrapMode.Clamp;
            sky.maxTextureSize = 2048;
            sky.SaveAndReimport();
            skin.sky = AssetDatabase.LoadAssetAtPath<Texture2D>(skyPath);
        }
        EditorUtility.SetDirty(skin);
        return skin;
    }

    /// <summary>gen 폴더의 PNG 를 UI 스프라이트로 가져온다. 없으면 null (기본 둥근 사각형으로 대신).</summary>
    static Sprite UiSprite(string name)
    {
        string path = $"{GenFolder}/{name}.png";
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return null;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.maxTextureSize = 2048;
        ti.spriteBorder = SliceBorders.TryGetValue(name, out var b) ? b : Vector4.zero;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ------------------------------------------------------------ 공통 환경 (카메라, 빛, 보드)
    static (Camera cam, CameraFit fit, Board board) CreateEnvironment(BoardAssets boardAssets)
    {
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.69f, 0.84f, 0.95f);
        cam.fieldOfView = 27;
        cam.farClipPlane = 200;
        var fit = camGo.AddComponent<CameraFit>();   // 위치는 스테이지 크기에 맞춰 자동 결정
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var volume = new GameObject("Post Processing").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = PostProfile();

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.color = new Color(1f, 0.97f, 0.93f);
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.75f;
        lightGo.transform.rotation = Quaternion.Euler(50, -150, 0);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.75f, 0.82f, 0.92f);
        RenderSettings.ambientEquatorColor = new Color(0.6f, 0.65f, 0.7f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.4f, 0.35f);

        var board = new GameObject("Board").AddComponent<Board>();
        board.tilePrefab = boardAssets.tile;
        board.blockPrefab = boardAssets.block;
        board.iceTilePrefab = boardAssets.iceTile;
        board.crumbleTilePrefab = boardAssets.crumbleTile;
        board.switchPrefab = boardAssets.sw;
        board.doorPrefab = boardAssets.door;
        board.stairsPrefab = boardAssets.stairs;
        board.terrainMaterial = boardAssets.terrain;
        board.decorPrefabs = boardAssets.decor;
        board.obstaclePrefab = boardAssets.rock;
        if (mechanisms != null)
        {
            board.leverPrefab = mechanisms.lever;
            board.platePrefab = mechanisms.plate;
            board.pushBlockPrefab = mechanisms.block;
            board.runeMaterial = mechanisms.rune;
        }
        // 에디터에서 보이도록 스테이지 1을 미리 깔아 둔다 (실행하면 다시 만든다)
        StageLibrary.ClearCache();
        board.Build(StageLibrary.Load(0));
        fit.Fit(board);
        return (cam, fit, board);
    }

    static void CreateMenuScene(GameObject foxPrefab, BoardAssets boardAssets, UiSkin skin)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var (_, _, board) = CreateEnvironment(boardAssets);
        var fox = (GameObject)PrefabUtility.InstantiatePrefab(foxPrefab);   // 메뉴에서는 움직이지 않는 여우 (FoxController 없음)
        fox.transform.position = board.CellToWorld(board.Map.StartCell);
        fox.transform.rotation = Quaternion.Euler(0, 180, 0);

        var menu = new GameObject("Menu").AddComponent<MenuController>();
        menu.board = board;
        menu.fox = fox.GetComponent<Animator>();
        menu.roundedSprite = RoundedSprite;
        menu.gemIcon = AssetDatabase.LoadAssetAtPath<Sprite>(GemIconPath);
        menu.skin = skin;
        EditorSceneManager.SaveScene(scene, MenuScenePath);
    }

    static void CreateGameScene(GameObject foxPrefab, BoardAssets boardAssets, Gem gemPrefab, UiSkin skin)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var (cam, _, board) = CreateEnvironment(boardAssets);
        cam.gameObject.AddComponent<CameraZoom>();   // 마우스 휠 확대·축소 (게임 화면만)

        var fox = (GameObject)PrefabUtility.InstantiatePrefab(foxPrefab);
        fox.transform.position = board.CellToWorld(board.Map.StartCell);
        fox.transform.rotation = Quaternion.Euler(0, 180, 0);   // 시작할 때는 카메라 쪽을 바라봄
        var foxController = fox.AddComponent<FoxController>();
        foxController.board = board;
        var trail = fox.AddComponent<FoxTrail>();
        trail.footprintMaterial = movers?.footprint;
        trail.dustPrefab = movers?.dust;

        var gameGo = new GameObject("Game");
        var game = gameGo.AddComponent<GameManager>();
        game.board = board;
        game.fox = foxController;
        game.gemPrefab = gemPrefab;
        var turns = gameGo.AddComponent<TurnSystem>();
        turns.board = board;
        if (movers != null)
        {
            turns.snowballPrefab = movers.snowball;
            turns.owlPrefab = movers.owl;
            turns.platformPrefab = movers.platform;
            turns.pathDotMaterial = movers.pathDot;
        }
        var ui = gameGo.AddComponent<GameUI>();
        ui.game = game;
        ui.gemIcon = AssetDatabase.LoadAssetAtPath<Sprite>(GemIconPath);
        ui.roundedSprite = RoundedSprite;
        ui.skin = skin;

        EditorSceneManager.SaveScene(scene, ScenePath);

        // 확인용 스크린샷 (셰이더 컴파일이 끝나도록 한 번 먼저 렌더)
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);
        Clip("Idle").SampleAnimation(fox, 0f);
        Shot(cam, Path.Combine(dir, "board.png"));
        Shot(cam, Path.Combine(dir, "board.png"));

        // 여우 가까이 (카툰 렌더링 확인용): 정면 3/4, 옆
        var close = Object.Instantiate(cam.gameObject).GetComponent<Camera>();
        var foxPos = fox.transform.position + Vector3.up * 0.45f;
        foreach (var (file, offset) in new[] { ("fox_toon_front.png", new Vector3(1.0f, 0.45f, -1.7f)), ("fox_toon_side.png", new Vector3(2.0f, 0.35f, 0.1f)) })
        {
            close.transform.position = foxPos + offset;
            close.transform.LookAt(foxPos);
            close.fieldOfView = 30;
            Shot(close, Path.Combine(dir, file));
        }
        Clip("Move").SampleAnimation(fox, 0.25f);
        close.transform.position = foxPos + new Vector3(1.6f, 0.6f, -1.2f);
        close.transform.LookAt(foxPos);
        Shot(close, Path.Combine(dir, "fox_toon_move.png"));
        Clip("Idle").SampleAnimation(fox, 0f);
        Object.DestroyImmediate(close.gameObject);
    }

    static void Shot(Camera cam, string file)
    {
        const int w = 960, h = 540;
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
