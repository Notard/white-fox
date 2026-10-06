using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 움직이는 것(눈덩이·부엉이·발판) 프리팹과 연출 자원: 길 점, 눈 발자국, 착지 먼지, 보석 주변 반짝임.
/// 텍스처(점, 발바닥)는 여기서 그려서 Assets/Textures/FX 에 저장한다.
/// </summary>
public static partial class ProjectSetup
{
    const string FxFolder = "Assets/Textures/FX";

    public class MoverAssets
    {
        public GameObject snowball, owl, platform;
        public Material pathDot, footprint;
        public ParticleSystem dust;
    }

    static MoverAssets CreateMoverAssets()
    {
        var ink = Toon("Mover_Ink", new Color(0.1f, 0.09f, 0.17f), new Color(0.1f, 0.09f, 0.17f), 0f);
        var snow = Toon("Mover_Snow", Color.white, new Color(0.68f, 0.72f, 0.97f), 0.0028f, 0.45f, new Color(0.25f, 0.27f, 0.45f));
        var owlBody = Toon("Owl_Body", new Color(0.62f, 0.45f, 0.33f), new Color(0.5f, 0.38f, 0.5f), 0.0028f, 0.5f, new Color(0.25f, 0.15f, 0.12f));
        var owlBelly = Toon("Owl_Belly", new Color(0.97f, 0.9f, 0.76f), new Color(0.8f, 0.72f, 0.82f), 0.0018f, 0.5f, new Color(0.35f, 0.25f, 0.2f));
        var owlEye = Toon("Owl_Eye", new Color(1f, 0.86f, 0.3f), new Color(0.95f, 0.7f, 0.4f), 0.0015f, 0.5f,
            new Color(0.3f, 0.2f, 0.05f), null, new Color(0.25f, 0.18f, 0.02f));
        var beak = Toon("Owl_Beak", new Color(0.98f, 0.64f, 0.26f), new Color(0.85f, 0.5f, 0.4f), 0.0015f, 0.5f, new Color(0.35f, 0.2f, 0.08f));
        var grass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Decor_Grass.mat");
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rock_Stone.mat");
        // 발판 잔디는 외곽선이 있어야 공중에 뜬 모습이 또렷하다
        var platformGrass = Toon("Platform_Grass", new Color(0.42f, 0.72f, 0.26f), new Color(0.55f, 0.7f, 0.85f), 0.0024f, 0.5f, new Color(0.15f, 0.28f, 0.12f));

        Remap("Assets/Models/snowball.fbx", ("Snow", snow), ("Ink", ink));
        Remap("Assets/Models/owl.fbx", ("OwlBody", owlBody), ("OwlBelly", owlBelly), ("OwlEye", owlEye), ("Ink", ink), ("Beak", beak));
        Remap("Assets/Models/platform.fbx", ("DecorGrass", platformGrass != null ? platformGrass : grass), ("Stone", stone));

        Directory.CreateDirectory(FxFolder);
        var dotTex = SaveTexture("dot", DrawDot(64));
        var pawTex = SaveTexture("paw", DrawPaw(64));

        return new MoverAssets
        {
            snowball = ModelPrefab("Assets/Models/snowball.fbx", "Assets/Prefabs/Snowball.prefab", true),
            owl = ModelPrefab("Assets/Models/owl.fbx", "Assets/Prefabs/Owl.prefab", true),
            platform = ModelPrefab("Assets/Models/platform.fbx", "Assets/Prefabs/Platform.prefab", true),
            pathDot = FadeMat("PathDot", dotTex),
            footprint = FadeMat("Footprint", pawTex),
            dust = CreateDustPrefab(dotTex),
        };
    }

    // ------------------------------------------------------------ 텍스처 그리기
    static Texture2D DrawDot(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
            t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - d) * 6f)));
        }
        return t;
    }

    /// <summary>발바닥: 큰 패드 하나 + 앞쪽 발가락 네 개 (위 = 앞). 풀이 눌린 듯한 짙은 초록.</summary>
    static Texture2D DrawPaw(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var blobs = new (Vector2 c, Vector2 r)[]
        {
            (new Vector2(0.5f, 0.36f), new Vector2(0.22f, 0.18f)),
            (new Vector2(0.24f, 0.62f), new Vector2(0.08f, 0.1f)),
            (new Vector2(0.4f, 0.75f), new Vector2(0.08f, 0.1f)),
            (new Vector2(0.6f, 0.75f), new Vector2(0.08f, 0.1f)),
            (new Vector2(0.76f, 0.62f), new Vector2(0.08f, 0.1f)),
        };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
            float a = 0;
            foreach (var (c, r) in blobs)
            {
                var q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
                a = Mathf.Max(a, Mathf.Clamp01((1 - q.magnitude) * 5f));
            }
            t.SetPixel(x, y, new Color(0.12f, 0.24f, 0.08f, a));
        }
        return t;
    }

    static Texture2D SaveTexture(string name, Texture2D tex)
    {
        string path = $"{FxFolder}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>URP Unlit 투명 (알파 혼합). 색·알파는 _BaseColor (프로퍼티 블록으로 바꿈).</summary>
    static Material FadeMat(string name, Texture tex)
    {
        var m = Mat(name, Shader.Find("Universal Render Pipeline/Unlit"), Color.white, 0);
        m.shader = Shader.Find("Universal Render Pipeline/Unlit");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------ 착지 먼지
    static ParticleSystem CreateDustPrefab(Texture dot)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Dust.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, "Assets/Materials/Dust.mat");
        }
        mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 0);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);

        var go = new GameObject("Dust");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.42f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.96f, 0.95f, 0.9f, 0.85f), new Color(0.85f, 0.88f, 0.98f, 0.75f));
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0, 20) });
        // 발밑에서 둥글게 옆으로 퍼진다
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.12f;
        shape.rotation = new Vector3(90, 0, 0);
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.25f;
        limit.limit = 0.2f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.4f));
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0, 1) });
        color.color = g;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Dust.prefab");
        Object.DestroyImmediate(go);
        return prefab.GetComponent<ParticleSystem>();
    }

    // ------------------------------------------------------------ 보석 주변 반짝임 (계속)
    /// <summary>보석 둘레에 작은 별빛이 천천히 반짝인다. 보석의 자식이라 같이 떠다니고, 먹으면 같이 사라진다.</summary>
    static void AddGemGlint(GameObject gemRoot)
    {
        var sparkleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sparkle.mat");
        var go = new GameObject("Glint");
        go.transform.SetParent(gemRoot.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.92f, 1f), Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission;
        emission.rateOverTime = 5f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.32f;
        shape.radiusThickness = 0.3f;
        // 커졌다 작아지며 반짝
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.3f, 1), new Keyframe(1, 0)));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = sparkleMat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
}
