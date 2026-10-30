using System.Collections.Generic;
using UnityEngine;

// Builds a big 3 octave piano (C3 to B5) with walls around it, plus the marble.
public class PianoRoll : MonoBehaviour
{
    public static readonly Dictionary<int, PianoKey> Keys = new Dictionary<int, PianoKey>();
    public int octaves = 3, firstMidi = 48;
    public float keyWidth = 3.2f, keyLength = 14f;
    Transform root;
    Vector3 off;
    float totalWidth;

    void Start()
    {
        Keys.Clear();
        if (GetComponent<PillarMusic>() == null) gameObject.AddComponent<PillarMusic>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        int whites = octaves * 7;
        float totalW = whites * keyWidth;
        totalWidth = totalW;
        off = new Vector3(totalW / 2, 0, keyLength / 2);
        var rg = new GameObject("Piano"); // turned 180 degrees so low notes are on the left as seen from the camera
        rg.transform.SetParent(transform, false);
        rg.transform.localPosition = off; rg.transform.localRotation = Quaternion.Euler(0, 180, 0);
        root = rg.transform;
        int[] wOff = { 0, 2, 4, 5, 7, 9, 11 };
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        for (int i = 0; i < whites; i++)
        {
            int step = i % 7, midi = firstMidi + (i / 7) * 12 + wOff[step];
            MakeKey(midi, new Vector3((i + 0.5f) * keyWidth, 0, 0), keyWidth * 0.97f, keyLength, 2f, new Color(0.88f, 0.86f, 0.8f), shader, cube);
            if (i < whites - 1 && (step == 0 || step == 1 || step == 3 || step == 4 || step == 5))
                MakeKey(midi + 1, new Vector3((i + 1) * keyWidth, 0.9f, 0), keyWidth * 0.55f, keyLength * 0.62f, 2f, new Color(0.04f, 0.04f, 0.05f), shader, cube);
        }

        var wood = new Material(shader); wood.SetColor("_BaseColor", new Color(0.17f, 0.18f, 0.2f)); wood.SetFloat("_Smoothness", 0.2f);
        Box("Base", new Vector3(totalW / 2, -3.2f, keyLength / 2), new Vector3(totalW + 3f, 0.4f, keyLength + 3f), wood, cube);
        // visible wooden casing around the keys (low rim) plus tall invisible walls so the marble can't leave
        float t = 1.5f, rimY = -0.5f, rimH = 5f, cx = totalW / 2, cz = keyLength / 2;
        Box("CaseFront", new Vector3(cx, rimY, keyLength + t / 2), new Vector3(totalW + 2 * t, rimH, t), wood, cube);
        Box("CaseBack", new Vector3(cx, rimY, -t / 2), new Vector3(totalW + 2 * t, rimH, t), wood, cube);
        Box("CaseLeft", new Vector3(-t / 2, rimY, cz), new Vector3(t, rimH, keyLength + 2 * t), wood, cube);
        Box("CaseRight", new Vector3(totalW + t / 2, rimY, cz), new Vector3(t, rimH, keyLength + 2 * t), wood, cube);
        float wh = 16f;
        Box("WallFront", new Vector3(cx, 6, keyLength + t), new Vector3(totalW + 2 * t, wh, 1f), null, cube);
        Box("WallBack", new Vector3(cx, 6, -t), new Vector3(totalW + 2 * t, wh, 1f), null, cube);
        Box("WallLeft", new Vector3(-t, 6, cz), new Vector3(1f, wh, keyLength + 2 * t), null, cube);
        Box("WallRight", new Vector3(totalW + t, 6, cz), new Vector3(1f, wh, keyLength + 2 * t), null, cube);

        var ball = HexCauseway.MakeMarble(root.TransformPoint(new Vector3(cx, 3f, keyLength * 0.7f) - off), shader);
        var cam = Camera.main;
        var iso = cam.GetComponent<IsoCamera>() ?? cam.gameObject.AddComponent<IsoCamera>();
        iso.target = ball.transform; iso.size = 11f;
        cam.orthographicSize = 11f; cam.nearClipPlane = 1f; cam.farClipPlane = 150f;
    }

    void MakeKey(int midi, Vector3 hinge, float w, float len, float h, Color col, Shader shader, Mesh cube)
    {
        var pivot = new GameObject("Key" + midi);
        pivot.transform.SetParent(root, false);
        hinge.x = totalWidth - hinge.x; // mirrored so pitch rises to the right as seen from the camera
        pivot.transform.localPosition = hinge - off;
        var rb = pivot.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
        var vis = new GameObject("Body");
        vis.transform.SetParent(pivot.transform, false);
        vis.transform.localPosition = new Vector3(0, -h / 2, len / 2);
        vis.transform.localScale = new Vector3(w, h, len);
        vis.AddComponent<MeshFilter>().sharedMesh = cube;
        vis.AddComponent<BoxCollider>().material = new PhysicsMaterial { bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum, dynamicFriction = 0.6f, staticFriction = 0.6f };
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", col); mat.SetFloat("_Smoothness", 0.5f);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeIndirectEmission;
        mat.SetColor("_EmissionColor", Color.black);
        vis.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var k = pivot.AddComponent<PianoKey>();
        k.midi = midi; k.mat = mat; k.length = len; k.width = w; k.maxAngle = 1.2f; k.maxDepth = 0.4f;
        Keys[midi] = k;
    }

    void Box(string name, Vector3 pos, Vector3 size, Material mat, Mesh cube)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos - off; go.transform.localScale = size;
        go.AddComponent<BoxCollider>();
        if (mat == null) return; // invisible wall
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
