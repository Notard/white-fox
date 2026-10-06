using System.Collections;
using UnityEngine;

/// <summary>
/// 여우 연출: 걸을 때 눈 발자국(천천히 사라짐), 점프 착지 때 흙먼지.
/// FoxController 의 Stepped / Landed 이벤트를 받는다.
/// </summary>
[RequireComponent(typeof(FoxController))]
public class FoxTrail : MonoBehaviour
{
    public Material footprintMaterial;
    public ParticleSystem dustPrefab;
    public float footprintLife = 4f;
    public float footprintSize = 0.26f;

    FoxController fox;
    bool left;

    void Awake()
    {
        fox = GetComponent<FoxController>();
        fox.Stepped += OnStepped;
        fox.Landed += OnLanded;
    }

    void OnDestroy()
    {
        if (fox == null) return;
        fox.Stepped -= OnStepped;
        fox.Landed -= OnLanded;
    }

    void OnStepped(Vector3 at, Vector2Int dir)
    {
        if (footprintMaterial == null) return;
        var forward = new Vector3(dir.x, 0, dir.y);
        var side = Vector3.Cross(Vector3.up, forward).normalized;
        float scale = fox.board != null ? fox.board.cellSize : 1.5f;
        left = !left;
        StartCoroutine(Footprint(at + side * (left ? 0.07f : -0.07f) * scale, forward, scale));
    }

    IEnumerator Footprint(Vector3 at, Vector3 forward, float scale)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "Footprint";
        q.transform.position = at + Vector3.up * 0.02f;
        q.transform.rotation = Quaternion.LookRotation(Vector3.down, forward);
        q.transform.localScale = Vector3.one * footprintSize * scale;
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = footprintMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        for (float t = 0; t < footprintLife; t += Time.deltaTime)
        {
            float a = t < footprintLife * 0.5f ? 0.85f : Mathf.Lerp(0.85f, 0, (t - footprintLife * 0.5f) / (footprintLife * 0.5f));
            block.SetColor("_BaseColor", new Color(1, 1, 1, a));
            r.SetPropertyBlock(block);
            yield return null;
        }
        Destroy(q);
    }

    /// <summary>먼지 한 번 (블록이 구멍에 떨어질 때 등).</summary>
    public void Puff(Vector3 at) => OnLanded(at);

    void OnLanded(Vector3 at)
    {
        if (dustPrefab == null) return;
        var fx = Instantiate(dustPrefab, at + Vector3.up * 0.05f, Quaternion.identity);
        Destroy(fx.gameObject, 2f);
    }
}
