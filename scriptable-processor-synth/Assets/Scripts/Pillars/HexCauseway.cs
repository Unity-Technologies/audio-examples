using System.Collections.Generic;
using UnityEngine;

// Builds a Giant's Causeway style field of hexagonal pillars that step down from a start pillar, and the marble.
public class HexCauseway : MonoBehaviour
{
    public int radius = 9;
    [Header("Height variance (applied when Play starts)")]
    [Range(0, 10)] public float hills = 3f;        // size of the rolling hills
    [Range(0, 3)] public float jitter = 0.3f;      // per-pillar random +-
    [Range(0, 0.3f)] public float spikeChance = 0.04f; // fraction of pillars that are tall spikes
    public Vector2 spikeHeight = new Vector2(2f, 4f);
    [Header("Shape")]
    public float topHeight = 14f, stepDown = 0.5f, cell = 1f;

    void Start()
    {
        Random.InitState(7);
        if (GetComponent<PillarMusic>() == null) gameObject.AddComponent<PillarMusic>();
        var all = new List<HexPillar>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var sideMat = new Material(shader);
        sideMat.SetColor("_BaseColor", new Color(0.14f, 0.16f, 0.2f)); sideMat.SetFloat("_Smoothness", 0.15f);
        var mesh = HexMesh(cell * 0.97f);
        Vector3 startPos = Vector3.zero;

        for (int q = -radius; q <= radius; q++)
            for (int r = Mathf.Max(-radius, -q - radius); r <= Mathf.Min(radius, -q + radius); r++)
            {
                int dq = q + radius, dr = r - radius; // distance from the start corner (-radius, radius)
                int d = (Mathf.Abs(dq) + Mathf.Abs(dr) + Mathf.Abs(dq + dr)) / 2;
                float h = topHeight;
                if (d > 0)
                {
                    h = topHeight - d * stepDown + (Mathf.PerlinNoise(q * 0.3f + 50f, r * 0.3f + 50f) - 0.5f) * hills + Random.Range(-jitter, jitter);
                    if (Random.value < spikeChance) h += Random.Range(spikeHeight.x, spikeHeight.y); // tall spikes
                }
                h = Mathf.Max(0.5f, Mathf.Round(h * 10f) / 10f);

                var go = new GameObject("Pillar");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(cell * Mathf.Sqrt(3f) * (q + r / 2f), 0, cell * 1.5f * r);
                go.transform.localScale = new Vector3(1, h, 1);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;

                var topMat = new Material(shader);
                float g = Random.Range(0.28f, 0.38f);
                topMat.SetColor("_BaseColor", new Color(g, g + 0.02f, g + 0.05f)); topMat.SetFloat("_Smoothness", 0.2f);
                topMat.EnableKeyword("_EMISSION");
                topMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeIndirectEmission;
                topMat.SetColor("_EmissionColor", Color.black);
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { sideMat, topMat };

                var p = go.AddComponent<HexPillar>();
                p.topMat = topMat; p.topY = h; all.Add(p); p.jitter = Random.Range(-1, 2);
                if (d == 0) startPos = go.transform.position + Vector3.up * (h + 0.7f);
            }

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var pl in all) { lo = Mathf.Min(lo, pl.topY); hi = Mathf.Max(hi, pl.topY); }
        foreach (var pl in all) pl.t = Mathf.InverseLerp(lo, hi, pl.topY);

        var ball = MakeMarble(startPos, shader);

        var cam = Camera.main;
        var iso = cam.GetComponent<IsoCamera>() ?? cam.gameObject.AddComponent<IsoCamera>();
        iso.target = ball.transform;
    }

    public static GameObject MakeMarble(Vector3 start, Shader shader)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Marble";
        ball.transform.position = start;
        // glass shell
        var bm = ball.GetComponent<Renderer>().material = new Material(shader);
        bm.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f, 0.22f)); bm.SetFloat("_Metallic", 0f); bm.SetFloat("_Smoothness", 0.95f);
        bm.SetFloat("_Surface", 1f); bm.SetFloat("_Blend", 0f);
        bm.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        bm.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        bm.SetFloat("_ZWrite", 0f);
        bm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); bm.EnableKeyword("_EMISSION");
        bm.SetOverrideTag("RenderType", "Transparent");
        bm.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        bm.SetShaderPassEnabled("ShadowCaster", false);
        bm.SetColor("_EmissionColor", Color.black);
        ball.AddComponent<Rigidbody>();
        ball.AddComponent<MarbleScope>();
        ball.AddComponent<MarbleBall>();
        return ball;
    }

    // Unit-height hexagonal prism (pointy-top). Submesh 0 = sides, submesh 1 = top.
    static Mesh HexMesh(float rad)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var sides = new List<int>(); var top = new List<int>();
        Vector3 Corner(int i, float y) { float a = (30f + 60f * i) * Mathf.Deg2Rad; return new Vector3(rad * Mathf.Cos(a), y, rad * Mathf.Sin(a)); }
        void Tri(List<int> t, int a, int b, int c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0) { int s = b; b = c; c = s; }
            t.Add(a); t.Add(b); t.Add(c);
        }
        for (int i = 0; i < 6; i++)
        {
            float a = (60f + 60f * i) * Mathf.Deg2Rad;
            var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            int s = v.Count;
            v.Add(Corner(i, 0)); v.Add(Corner(i + 1, 0)); v.Add(Corner(i + 1, 1)); v.Add(Corner(i, 1));
            for (int k = 0; k < 4; k++) n.Add(o);
            Tri(sides, s, s + 1, s + 2, o); Tri(sides, s, s + 2, s + 3, o);
        }
        int c0 = v.Count;
        v.Add(new Vector3(0, 1, 0)); n.Add(Vector3.up);
        for (int i = 0; i < 6; i++) { v.Add(Corner(i, 1)); n.Add(Vector3.up); }
        for (int i = 0; i < 6; i++) Tri(top, c0, c0 + 1 + i, c0 + 1 + (i + 1) % 6, Vector3.up);

        var m = new Mesh { subMeshCount = 2 };
        m.SetVertices(v); m.SetNormals(n);
        m.SetTriangles(sides, 0); m.SetTriangles(top, 1);
        m.RecalculateBounds();
        return m;
    }
}
