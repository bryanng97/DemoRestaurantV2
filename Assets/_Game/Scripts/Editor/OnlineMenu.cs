using System.IO;
using DemoRestaurant.Game.Online;
using UnityEditor;
using UnityEngine;

namespace DemoRestaurant.Editor
{
    public static class OnlineMenu
    {
        private const string Folder = "Assets/_Game/Resources/Online";

        /// <summary>Creates (or selects) Resources/Online/OnlineConfig. The asset is git-ignored: each developer
        /// pastes the project's public key locally.</summary>
        [MenuItem("DemoRestaurant/Online/Create Online Config")]
        public static void CreateConfig()
        {
            string path = Folder + "/OnlineConfig.asset";
            var existing = AssetDatabase.LoadAssetAtPath<OnlineConfig>(path);
            if (existing == null)
            {
                Directory.CreateDirectory(Folder);
                existing = ScriptableObject.CreateInstance<OnlineConfig>();
                // Placeholder so the sign-in screen shows straight away; replace it with the project's public key.
                existing.publicKey = "placeholder-replace-with-public-key";
                AssetDatabase.CreateAsset(existing, path);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
        }

        /// <summary>Copy the project's public key, then run this: it is written into OnlineConfig and saved.</summary>
        [MenuItem("DemoRestaurant/Online/Paste Public Key From Clipboard")]
        public static void PastePublicKey()
        {
            string key = (GUIUtility.systemCopyBuffer ?? "").Trim();
            if (key.StartsWith("sb_secret_") || key.Contains("service_role"))
            {
                EditorUtility.DisplayDialog("Online Config", "That is a secret/service key. Only the public (anon / sb_publishable_) key belongs in the app.", "OK");
                return;
            }
            if (key.Length < 20 || key.Contains(" ") || key.Contains("\n"))
            {
                EditorUtility.DisplayDialog("Online Config", "The clipboard doesn't look like a key. Copy the public key first.", "OK");
                return;
            }
            CreateConfig();
            var config = (OnlineConfig)Selection.activeObject;
            config.publicKey = key;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("Online Config: public key saved (" + key.Substring(0, Mathf.Min(16, key.Length)) + "...).");
        }

        [MenuItem("DemoRestaurant/Online/Clear Saved Session")]
        public static void ClearSession()
        {
            PlayerPrefs.DeleteKey("dr_auth_session");
            PlayerPrefs.Save();
            Debug.Log("Online session cleared.");
        }
    }
}
