using UnityEngine;
using UnityEngine.Rendering;

// Note name that rises from the marble; its letters drift apart randomly and fade out.
public class FloatingNote : MonoBehaviour
{
    const float Life = 1.5f, Speed = 1.2f, Spacing = 0.14f;
    TextMesh[] tms;
    Vector2[] drift;
    Color color;
    float age;
    static Material mat;

    public static void Spawn(Vector3 pos, string text, Color c)
    {
        var root = new GameObject("NoteLabel");
        root.transform.position = pos;
        var f = root.AddComponent<FloatingNote>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (mat == null) { mat = new Material(font.material); mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always); mat.renderQueue = 4000; }
        f.color = c;
        f.tms = new TextMesh[text.Length]; f.drift = new Vector2[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            var go = new GameObject("c" + i);
            go.transform.SetParent(root.transform, false);
            var t = go.AddComponent<TextMesh>();
            t.font = font; t.text = text[i].ToString(); t.fontSize = 64; t.characterSize = 0.06f;
            t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center; t.fontStyle = FontStyle.Bold;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            f.tms[i] = t;
            f.drift[i] = new Vector2(Random.Range(-0.7f, 0.7f), Random.Range(-0.2f, 0.7f));
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += Vector3.up * (Speed * Time.deltaTime);
        transform.rotation = Camera.main.transform.rotation;
        float alpha = Mathf.Clamp01(1f - age / Life), n = tms.Length;
        for (int i = 0; i < tms.Length; i++)
        {
            float x = (i - (n - 1) / 2f) * Spacing * (1f + age * 1.5f) + drift[i].x * age;
            tms[i].transform.localPosition = new Vector3(x, drift[i].y * age, 0);
            tms[i].color = new Color(color.r, color.g, color.b, alpha);
        }
        if (age >= Life) Destroy(gameObject);
    }
}
