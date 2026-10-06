using System.Collections;
using UnityEngine;

/// <summary>둥실둥실 떠서 회전하는 보석. Collect() 하면 튀어 오르며 반짝이고 사라진다.</summary>
public class Gem : MonoBehaviour
{
    public float hoverHeight = 0.55f;
    public float bobAmount = 0.08f;
    public float bobSpeed = 2.2f;
    public float spinSpeed = 70f;
    public ParticleSystem sparklePrefab;

    public Vector2Int Cell { get; set; }
    /// <summary>있으면 칸 높이(승강 땅)를 따라간다.</summary>
    public Board board;
    public bool Collected { get; private set; }

    Vector3 basePos;
    float phase;

    void Start()
    {
        basePos = transform.position;
        phase = (Cell.x * 1.7f + Cell.y * 2.9f);
    }

    void Update()
    {
        if (Collected) return;
        if (board != null)
            basePos.y = Mathf.MoveTowards(basePos.y, board.CellToWorld(Cell).y, board.LiftSpeed * Time.deltaTime);
        float t = Time.time * bobSpeed + phase;
        transform.position = basePos + Vector3.up * (hoverHeight + Mathf.Sin(t) * bobAmount);
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    public void Collect()
    {
        if (Collected) return;
        Collected = true;
        if (sparklePrefab != null)
        {
            var fx = Instantiate(sparklePrefab, transform.position, Quaternion.identity);
            Destroy(fx.gameObject, 2f);
        }
        StartCoroutine(Pop());
    }

    IEnumerator Pop()
    {
        Vector3 start = transform.position, scale = transform.localScale;
        const float d = 0.35f;
        for (float t = 0; t < d; t += Time.deltaTime)
        {
            float k = t / d;
            transform.position = start + Vector3.up * (0.6f * Mathf.Sin(k * Mathf.PI * 0.5f));
            transform.localScale = scale * (k < 0.3f ? 1 + k : 1.3f * (1 - (k - 0.3f) / 0.7f));
            transform.Rotate(Vector3.up, 720 * Time.deltaTime, Space.World);
            yield return null;
        }
        Destroy(gameObject);
    }
}
