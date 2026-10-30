using UnityEngine;
using UnityEngine.InputSystem;

// Arrow keys roll (relative to the camera), Space jumps.
[RequireComponent(typeof(Rigidbody))]
public class MarbleBall : MonoBehaviour
{
    public float moveForce = 22f, maxSpeed = 7f, jumpImpulse = 6.5f;
    Rigidbody rb;
    Vector3 start;
    Vector2 input;
    bool jump;
    public Material coreMat; // inner emissive core seen through the glass
    Material mat;
    Color glow;
    Material outlineMat;
    Transform shadow; SpriteRenderer shadowSr; // blob shadow: small on the ground, bigger the higher the marble is
    const float ShadowMin = 0.8f, ShadowMax = 2.6f, ShadowFullHeight = 5f;

    public void SetGlow(Color c) { glow = c; }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0.3f; rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        start = transform.position;
        mat = GetComponent<Renderer>().material;
        MakeOutline();
        MakeShadow();
    }

    // back-face shell behind the glass; its color is a darker shade of the current glow and it fades with the glow strength
    void MakeOutline()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "MarbleOutline";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false); go.transform.localScale = Vector3.one * 1.08f;
        outlineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        outlineMat.SetFloat("_Surface", 1f); outlineMat.SetFloat("_Blend", 0f);
        outlineMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        outlineMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        outlineMat.SetFloat("_ZWrite", 0f); outlineMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
        outlineMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        outlineMat.SetOverrideTag("RenderType", "Transparent");
        outlineMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 1; // drawn just before the glass
        outlineMat.SetColor("_BaseColor", new Color(0, 0, 0, 0));
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = outlineMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    }

    void UpdateOutline()
    {
        float m = Mathf.Max(glow.r, glow.g, glow.b);
        float strength = Mathf.Clamp01(m / 1.2f);
        Color hue = m > 0.001f ? glow / m : Color.white;
        var c = hue * 0.35f; c.a = strength;
        outlineMat.SetColor("_BaseColor", c);
    }

    void MakeShadow()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f - 0.5f, N / 2f - 0.5f)) / (N / 2f);
                tex.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp01(1f - d) * Mathf.Clamp01((1f - d) * 3f)));
            }
        tex.Apply();
        var go = new GameObject("MarbleShadow");
        shadow = go.transform;
        shadowSr = go.AddComponent<SpriteRenderer>();
        shadowSr.sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        shadowSr.sortingOrder = -1;
    }

    void LateUpdate()
    {
        if (shadow == null) return;
        // start just below the sphere so the marble's own collider is not hit
        if (Physics.Raycast(transform.position + Vector3.down * 0.55f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            float h = Mathf.Clamp01(hit.distance / ShadowFullHeight);
            float size = Mathf.Lerp(ShadowMin, ShadowMax, h);
            shadow.position = hit.point + Vector3.up * 0.03f;
            shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.localScale = new Vector3(size, size, 1f);
            shadowSr.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.7f, 0.45f, h));
            shadowSr.enabled = true;
        }
        else shadowSr.enabled = false;
    }

    void Update()
    {
        glow = Color.Lerp(glow, Color.black, 1.5f * Time.deltaTime);
        if (mat != null) mat.SetColor("_EmissionColor", glow * 0.7f);
        if (outlineMat != null) UpdateOutline();
        if (coreMat != null) coreMat.SetColor("_EmissionColor", glow * 1.5f + new Color(0.04f, 0.04f, 0.05f));
        var kb = Keyboard.current;
        if (kb == null) return;
        input = new Vector2((kb.rightArrowKey.isPressed ? 1 : 0) - (kb.leftArrowKey.isPressed ? 1 : 0),
                            (kb.upArrowKey.isPressed ? 1 : 0) - (kb.downArrowKey.isPressed ? 1 : 0));
        if (kb.spaceKey.wasPressedThisFrame) jump = true;
    }

    void FixedUpdate()
    {
        bool grounded = Physics.SphereCast(transform.position, 0.4f, Vector3.down, out _, 0.2f, ~0, QueryTriggerInteraction.Ignore);
        var cam = Camera.main.transform;
        Vector3 f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized, r = cam.right;
        rb.AddForce((f * input.y + r * input.x) * moveForce * (grounded ? 1f : 0.4f));
        if (jump && grounded) rb.AddForce(Vector3.up * jumpImpulse, ForceMode.VelocityChange);
        jump = false;

        var v = rb.linearVelocity;
        var h = new Vector3(v.x, 0, v.z);
        if (h.magnitude > maxSpeed) { h = h.normalized * maxSpeed; rb.linearVelocity = new Vector3(h.x, v.y, h.z); }

        if (transform.position.y < -8f)
        {
            transform.position = start;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
    }
}
