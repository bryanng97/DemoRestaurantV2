using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Private prototype tool: renders the reference avatar model into sprite frames. Never shipped.
public static class Bake
{
    const string ModelPath = "Assets/Model/avatar.dae";
    // The baker project lives in Builds/AvatarBaker, two levels under the game project.
    static readonly string Export = Path.GetFullPath(Path.Combine("..", "..", "Builds", "Exports", "GameAssets-0.9.143a", "extracted", "avatars"));
    static readonly StringBuilder Log = new StringBuilder();

    static string OutDir { get { var d = Path.GetFullPath("Out"); Directory.CreateDirectory(d); return d; } }

    public static void Setup()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        imp.animationType = ModelImporterAnimationType.Legacy;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        imp.importAnimation = true;
        imp.globalScale = 1f;
        imp.SaveAndReimport();
    }

    public static void Probe()
    {
        Log.Clear();
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            Log.AppendLine("ASSET " + a.GetType().Name + " " + a.name + (a is AnimationClip c ? " len=" + c.length + " fr=" + c.frameRate : ""));
        var go = Spawn(Look.Default);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            Log.AppendLine("R " + r.name + " active=" + r.gameObject.activeSelf + " " + r.GetType().Name + " b=" + r.bounds + " uv0=" + UvRange(r));
        Log.AppendLine("ROOT rot=" + go.transform.rotation.eulerAngles + " scale=" + go.transform.localScale);
        Sheet(go, "s1.png", 225f, 0f, 4.9f, 0.1f);
        Sheet(go, "s2.png", 225f, 5.0f, 9.9f, 0.1f);
        Sheet(go, "s3.png", 225f, 10.0f, 14.5f, 0.1f);
        var props = new Look { Props = new[] { "tray", "bucket_PIVOT", "brush", "repair" } };
        Object.DestroyImmediate(go); go = Spawn(props);
        Sheet(go, "s1p.png", 225f, 0f, 4.9f, 0.1f);
        Sheet(go, "s3p.png", 225f, 10.0f, 14.5f, 0.1f);
        File.WriteAllText(Path.Combine(OutDir, "probe.txt"), Log.ToString());
    }

    public static void Dump()
    {
        Log.Clear();
        var go = Spawn(Look.Default);
        var clip = Clip(go);
        var t = go.GetComponentsInChildren<Transform>();
        Transform F(string n) => t.First(x => x.name == n);
        var pelvis = F("Bip01"); var lt = F("Bip01_L_Thigh"); var rt = F("Bip01_R_Thigh"); var la = F("Bip01_L_UpperArm"); var ra = F("Bip01_R_UpperArm"); var lf = F("Bip01_L_Foot"); var rf = F("Bip01_R_Foot"); var lh = F("Bip01_L_Hand"); var rh = F("Bip01_R_Hand");
        for (int i = 0; i <= 145; i++)
        {
            clip.SampleAnimation(go, i / 10f);
            Vector3 P(Transform x) => go.transform.InverseTransformPoint(x.position);
            Log.AppendLine($"{i/10f:F1} pelvis={P(pelvis):F0} lfoot={P(lf):F0} rfoot={P(rf):F0} lhand={P(lh):F0} rhand={P(rh):F0}");
        }
        File.WriteAllText(Path.Combine(OutDir, "dump.txt"), Log.ToString());
    }

    static string UvRange(Renderer r)
    {
        Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        if (m == null || m.uv.Length == 0) return "-";
        var uv = m.uv;
        return $"[{uv.Min(v => v.x):F2},{uv.Min(v => v.y):F2}]-[{uv.Max(v => v.x):F2},{uv.Max(v => v.y):F2}]";
    }

    // ---------- look ----------

    public sealed class Look
    {
        public Color Skin = Hex("FFDBC0"), Hair = Hex("4f3e21");
        public string HairMesh = "hair05", HatMesh = null, HatTex = null;
        public string Shirt = "Shirt1", Pants = "PantsClassic", Eyes = "Eyes", Mouth = "Mouth", Brows = "EyeBrow01", Extra = null, Beard = null;
        public string[] Props = new string[0];
        public bool Dress, LongSleeves;
        public static Look Default => new Look();
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    static readonly string[] Optional = { "tray", "bucket", "brush", "repair", "dress", "armleft2", "armright2" };

    public static GameObject SpawnPublic(Look look) => Spawn(look);
    public static AnimationClip ClipPublic(GameObject go) => Clip(go);

    static GameObject Spawn(Look look)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var go = Object.Instantiate(model);
        go.name = "Avatar";
        var tex = Composite(look);
        var texMat = new Material(Shader.Find("Bake/Unlit")) { mainTexture = tex };
        var hairMat = new Material(Shader.Find("Bake/Unlit")) { color = look.Hair };
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.name;
            bool show = n == "shirt01" && !look.LongSleeves || n == "pants01" || n == "face0" ||
                        (n == "armleft" || n == "armright") && !look.LongSleeves ||
                        (n == "armleft2" || n == "armright2" || n == "shirt01") && look.LongSleeves ||
                        n == look.HairMesh || n == look.HatMesh || n == "dress" && look.Dress || look.Props.Contains(n);
            r.gameObject.SetActive(show);
            r.sharedMaterial = n.StartsWith("hair") ? hairMat : n == "tray" ? PropMat("Tray") : n == "bucket_PIVOT" || n == "brush" ? PropMat("Cleaner") : n == "repair" ? PropMat("Repair") : texMat;
        }
        return go;
    }

    static Material PropMat(string sprite)
    {
        var dir = Directory.GetDirectories(Path.Combine(Export, "sprites")).First(d => d.EndsWith("_" + sprite));
        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Path.Combine(dir, "1.png")));
        return new Material(Shader.Find("Bake/Unlit")) { mainTexture = t };
    }

    // The reference game paints every texture layer onto one 512 x 512 sheet; each layer's place comes from its shape.
    static Texture2D Composite(Look look)
    {
        const int S = 512;
        var px = Enumerable.Repeat((Color)look.Skin, S * S).ToArray();
        foreach (var layer in new[] { look.Pants, look.Shirt, look.HatTex, look.Brows, look.Eyes, look.Mouth, look.Extra, look.Beard })
            if (!string.IsNullOrEmpty(layer)) Draw(px, S, layer, false);
        Draw(px, S, "TextureShading", true);
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static void Draw(Color[] px, int S, string name, bool multiply)
    {
        var dir = Directory.GetDirectories(Path.Combine(Export, "sprites")).FirstOrDefault(d => Regex.IsMatch(Path.GetFileName(d), "^DefineSprite_\\d+_" + Regex.Escape(name) + "$"));
        if (dir == null) { Log.AppendLine("missing layer " + name); return; }
        int id = int.Parse(Regex.Match(Path.GetFileName(dir), "\\d+").Value);
        var off = ShapeOffset(id - 1);
        var img = new Texture2D(2, 2);
        img.LoadImage(File.ReadAllBytes(Path.Combine(dir, "1.png")));
        var src = img.GetPixels();
        int w = img.width, h = img.height;
        int ox = Mathf.RoundToInt(off.x), oy = Mathf.RoundToInt(off.y);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int tx = ox + x, ty = oy + (h - 1 - y); // ty measured from the top
            if (tx < 0 || ty < 0 || tx >= S || ty >= S) continue;
            int di = (S - 1 - ty) * S + tx;
            var c = src[y * w + x];
            if (multiply)
            {
                var k = Color.Lerp(Color.white, c, c.a);
                px[di] = new Color(px[di].r * k.r, px[di].g * k.g, px[di].b * k.b, 1f);
            }
            else px[di] = Color.Lerp(px[di], new Color(c.r, c.g, c.b, 1f), c.a);
        }
    }

    static Vector2 ShapeOffset(int shapeId)
    {
        var f = Path.Combine(Export, "shapes", shapeId + ".svg");
        if (!File.Exists(f)) { Log.AppendLine("no shape " + shapeId); return Vector2.zero; }
        var m = Regex.Match(File.ReadAllText(f), "matrix\\(([^)]*)\\)");
        var v = m.Groups[1].Value.Split(',').Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
        return new Vector2(-v[4], -v[5]);
    }

    // ---------- rendering ----------

    static Camera MakeCamera(int size)
    {
        var camGo = new GameObject("BakeCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.targetTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        return cam;
    }

    static Texture2D Shot(Camera cam, GameObject go, float yaw, float time, AnimationClip clip, float halfHeight)
    {
        if (clip != null) clip.SampleAnimation(go, time);
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        var rot = Quaternion.Euler(30f, yaw, 0f);
        var center = go.transform.position + Vector3.up * halfHeight * 0.8f;
        cam.transform.rotation = rot;
        cam.transform.position = center - rot * Vector3.forward * (b.size.magnitude * 3f + 10f);
        cam.orthographicSize = halfHeight;
        cam.nearClipPlane = 1f; cam.farClipPlane = b.size.magnitude * 8f + 100f;
        cam.Render();
        var rt = cam.targetTexture;
        RenderTexture.active = rt;
        var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        t.Apply();
        RenderTexture.active = null;
        return t;
    }

    static AnimationClip Clip(GameObject go)
    {
        var anim = go.GetComponent<Animation>();
        if (anim != null && anim.clip != null) return anim.clip;
        return AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
    }

    static float ModelHeight(GameObject go)
    {
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        return b.size.y;
    }

    static void Sheet(GameObject go, string file, float yaw, float from, float to, float step)
    {
        var clip = Clip(go);
        int tile = 150;
        var cam = MakeCamera(tile);
        float half = 4400f;
        var times = new List<float>();
        for (float t = from; t <= to + 1e-3f; t += step) times.Add(t);
        int cols = 10, rows = (times.Count + cols - 1) / cols;
        var sheet = new Texture2D(cols * tile, rows * tile, TextureFormat.RGBA32, false);
        sheet.SetPixels(Enumerable.Repeat(new Color(0.85f, 0.85f, 0.85f, 1), cols * tile * rows * tile).ToArray());
        for (int i = 0; i < times.Count; i++)
        {
            var shot = Shot(cam, go, yaw, times[i], clip, half);
            int cx = i % cols, cy = rows - 1 - i / cols;
            var p = shot.GetPixels();
            for (int k = 0; k < p.Length; k++)
                if (p[k].a > 0.01f)
                {
                    int x = k % tile, y = k / tile;
                    sheet.SetPixel(cx * tile + x, cy * tile + y, Color.Lerp(sheet.GetPixel(cx * tile + x, cy * tile + y), p[k], p[k].a));
                }
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, file), sheet.EncodeToPNG());
        Log.AppendLine($"sheet {file}: {times.Count} frames, clip={(clip ? clip.name + " " + clip.length : "none")}");
    }
}
