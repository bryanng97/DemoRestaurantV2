using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Renders each character look x animation x view into frames for Tools/RefProto/import.ps1 ("sourceDir" entries).
public static class BakeAll
{
    static readonly string Project = Path.GetFullPath(Path.Combine("..", ".."));
    const int W = 256, H = 320, Ground = 24;
    const float UnitsPerPixel = 30f;

    struct Anim { public string Name; public float From, To, Step; public string[] Props; public Anim(string n, float f, float t, float s, params string[] p) { Name = n; From = f; To = t; Step = s; Props = p; } }

    static readonly Anim[] Anims =
    {
        new Anim("walk", 0.0f, 0.3f, 0.1f),
        new Anim("carry", 1.0f, 1.3f, 0.1f),
        new Anim("hold", 1.6f, 1.6f, 0.1f),
        new Anim("idle", 4.4f, 7.2f, 0.2f),
        new Anim("cook", 3.0f, 3.3f, 0.1f),
        new Anim("sit", 3.5f, 3.5f, 0.1f),
        new Anim("eat", 3.5f, 4.2f, 0.1f),
        new Anim("clean", 14.1f, 14.4f, 0.1f, "bucket_PIVOT", "brush"),
        new Anim("fix", 13.6f, 13.9f, 0.1f, "repair"),
    };

    static Dictionary<string, Bake.Look> Looks()
    {
        Color C(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        return new Dictionary<string, Bake.Look>
        {
            ["char_chef"] = new Bake.Look { Shirt = "Shirt3", Pants = "Pants15", HatMesh = "hat01", HatTex = "Hat101", HairMesh = "hair07", Hair = C("4f3e21"), Eyes = "Eyes6", Mouth = "Mouth7" },
            ["char_waiter"] = new Bake.Look { Shirt = "Shirt23", Pants = "Pants15", HairMesh = "hair08", Hair = C("844921"), Eyes = "Eyes6", Mouth = "Mouth7" },
            ["char_cleaner"] = new Bake.Look { Shirt = "Shirt39", Pants = "Pants7", HairMesh = "hair05", Hair = C("332e26"), Eyes = "Eyes1", Mouth = "Mouth" },
            ["char_owner"] = new Bake.Look { Shirt = "Shirt13", Pants = "Pants4", HairMesh = "hair01", Hair = C("a9773e"), Eyes = "Eyes5", Mouth = "Mouth7" },
            ["char_office_worker"] = new Bake.Look { Shirt = "Shirt5", Pants = "Pants15", HairMesh = "hair05", Hair = C("332e26"), Eyes = "Eyes", Mouth = "Mouth8" },
            ["char_student"] = new Bake.Look { Shirt = "Shirt9", Pants = "Pants5", Dress = true, HairMesh = "hair06", Hair = C("4f3e21"), Eyes = "Eyes19", Mouth = "Mouth7", Skin = C("F9C095") },
            ["char_auntie"] = new Bake.Look { Shirt = "Shirt15", Pants = "Pants19", Dress = true, HairMesh = "hair17", Hair = C("c7b8a7"), Eyes = "Eyes4", Mouth = "Mouth", Skin = C("DBA163") },
        };
    }

    public static void Run()
    {
        string outRoot = Path.Combine(Project, @"Builds\Exports\Characters");
        Directory.CreateDirectory(outRoot);
        var entries = new List<string>();
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.orthographicSize = H / 2f * UnitsPerPixel;
        cam.nearClipPlane = 1f; cam.farClipPlane = 100000f;
        cam.targetTexture = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };

        foreach (var pair in Looks())
        foreach (var anim in Anims)
        {
            var look = pair.Value; look.Props = anim.Props;
            var go = Bake.SpawnPublic(look);
            var clip = Bake.ClipPublic(go);
            var hand = go.GetComponentsInChildren<Transform>().First(t => t.name == "Bip01_R_Hand");
            foreach (var (view, yaw) in new[] { ("f", 225f), ("b", 315f) })
            {
                string key = $"{pair.Key}_{anim.Name}_{view}";
                string dir = Path.Combine(outRoot, key);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var rot = Quaternion.Euler(30f, yaw, 0f);
                cam.transform.rotation = rot;
                cam.transform.position = go.transform.position - rot * Vector3.forward * 30000f + cam.transform.up * ((H / 2f - Ground) * UnitsPerPixel);
                int n = 0; Vector2 handVp = default;
                for (float t = anim.From; t <= anim.To + 1e-3f; t += anim.Step)
                {
                    clip.SampleAnimation(go, t);
                    cam.Render();
                    RenderTexture.active = cam.targetTexture;
                    var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, (++n) + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    if (n == 1) handVp = cam.WorldToViewportPoint(hand.position);
                }
                var inv = CultureInfo.InvariantCulture;
                string extra = anim.Name == "carry" || anim.Name == "hold"
                    ? string.Format(inv, ", \"hand\": {{ \"x\": {0:F4}, \"y\": {1:F4} }}", handVp.x, handVp.y) : "";
                entries.Add(string.Format(inv,
                    "    {{ \"key\": \"{0}\", \"sourceDir\": \"Builds/Exports/Characters/{0}\", \"frames\": [1, {1}], \"crop\": [0, 0, {2}, {3}], \"anchor\": {{ \"x\": 0.5, \"y\": {4:F4} }}{5} }}",
                    key, n, W, H, Ground / (float)H, extra));
            }
            Object.DestroyImmediate(go);
        }
        // Portraits: a head-and-shoulders close-up, slightly turned, for the profile card and the staff list.
        const int P = 256;
        cam.targetTexture = new RenderTexture(P, P, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        foreach (var pair in Looks())
        {
            var look = pair.Value; look.Props = new string[0];
            var go = Bake.SpawnPublic(look);
            Bake.ClipPublic(go).SampleAnimation(go, 4.4f);
            var head = go.GetComponentsInChildren<Transform>().First(t => t.name == "Bip01_Head");
            var rot = Quaternion.Euler(8f, 200f, 0f);
            cam.transform.rotation = rot;
            var focus = head.position + Vector3.up * 1900f;
            cam.transform.position = focus - rot * Vector3.forward * 30000f;
            cam.orthographicSize = 3400f;
            cam.Render();
            RenderTexture.active = cam.targetTexture;
            var tex = new Texture2D(P, P, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, P, P), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string key = pair.Key + "_portrait";
            string dir = Path.Combine(outRoot, key);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "1.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            entries.Add(string.Format(CultureInfo.InvariantCulture,
                "    {{ \"key\": \"{0}\", \"sourceDir\": \"Builds/Exports/Characters/{0}\", \"frame\": 1, \"crop\": [0, 0, {1}, {1}] }}", key, P));
        }

        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"comment\": \"Generated by the avatar baker: reference model frames shared for prototype development, not release builds.\",");
        json.AppendLine("  \"sprites\": [");
        json.AppendLine(string.Join(",\n", entries));
        json.AppendLine("  ]");
        json.AppendLine("}");
        File.WriteAllText(Path.Combine(Project, @"Tools\RefProto\art_map_characters.json"), json.ToString());
    }
}
