using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 장치(레버·누름판·미는 블록) 프리팹과 채널 표시 고리 재질.
/// 레버 손잡이 머리(Mech_Knob)와 누름판 윗면(Mech_PlateTop)은 흰색으로 두고, 게임에서 채널 색으로 칠한다.
/// </summary>
public static partial class ProjectSetup
{
    public class MechanismAssets
    {
        public GameObject lever, plate, block;
        public Material rune;
    }

    static MechanismAssets CreateMechanismAssets()
    {
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rock_Stone.mat");
        var wood = Toon("Mech_Wood", new Color(0.86f, 0.6f, 0.36f), new Color(0.72f, 0.6f, 0.78f), 0.0026f, 0.5f, new Color(0.3f, 0.17f, 0.08f));
        var woodDark = Toon("Mech_WoodDark", new Color(0.5f, 0.32f, 0.18f), new Color(0.7f, 0.6f, 0.78f), 0f);
        var metal = Toon("Mech_Metal", new Color(0.62f, 0.65f, 0.75f), new Color(0.6f, 0.62f, 0.85f), 0.0018f, 0.5f, new Color(0.18f, 0.18f, 0.26f));
        var knob = Toon("Mech_Knob", Color.white, new Color(0.72f, 0.72f, 0.82f), 0.0022f, 0.45f, new Color(0.2f, 0.18f, 0.26f));
        var plateTop = Toon("Mech_PlateTop", Color.white, new Color(0.72f, 0.72f, 0.82f), 0.0022f, 0.45f, new Color(0.2f, 0.18f, 0.26f));
        var snow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mover_Snow.mat");
        knob.SetFloat("_SpecStrength", 0.6f);
        plateTop.SetFloat("_SpecStrength", 0.4f);

        Remap("Assets/Models/lever.fbx", ("Stone", stone), ("Wood", wood), ("WoodDark", woodDark), ("Metal", metal), ("Knob", knob));
        Remap("Assets/Models/plate.fbx", ("Stone", stone), ("PlateTop", plateTop));
        Remap("Assets/Models/pushblock.fbx", ("Wood", wood), ("WoodDark", woodDark), ("Metal", metal), ("Snow", snow));

        Directory.CreateDirectory(FxFolder);
        var ringTex = SaveTexture("rune_ring", DrawRing(128));

        return new MechanismAssets
        {
            lever = ModelPrefab("Assets/Models/lever.fbx", "Assets/Prefabs/Lever.prefab", true),
            plate = ModelPrefab("Assets/Models/plate.fbx", "Assets/Prefabs/Plate.prefab", true),
            block = ModelPrefab("Assets/Models/pushblock.fbx", "Assets/Prefabs/PushBlock.prefab", true),
            rune = FadeMat("Rune", ringTex),
        };
    }

    /// <summary>채널 고리: 두꺼운 바깥 고리 + 네 방향 작은 점. 흰색 (색은 _BaseColor 로).</summary>
    static Texture2D DrawRing(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var c = new Vector2(n / 2f, n / 2f);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = Vector2.Distance(p, c) / (n / 2f);
            float ring = Mathf.Clamp01(1 - Mathf.Abs(d - 0.8f) / 0.09f) * 1.6f;
            float dots = 0;
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2 + Mathf.PI / 4;
                var q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * n * 0.27f;
                dots = Mathf.Max(dots, Mathf.Clamp01((1 - Vector2.Distance(p, q) / (n * 0.06f)) * 3f));
            }
            t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(Mathf.Max(ring, dots))));
        }
        return t;
    }
}
