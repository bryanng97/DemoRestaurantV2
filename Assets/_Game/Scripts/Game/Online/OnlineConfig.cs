using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    /// <summary>
    /// Backend connection settings. Create the asset with DemoRestaurant > Online > Create Online Config; it lives
    /// at Resources/Online/OnlineConfig. With no asset (or an empty key) the game runs offline only.
    /// Only the project's public (anon/publishable) key belongs here, never a service-role key.
    /// </summary>
    [CreateAssetMenu(menuName = "DemoRestaurant/Online Config", fileName = "OnlineConfig")]
    public sealed class OnlineConfig : ScriptableObject
    {
        public const string ResourcePath = "Online/OnlineConfig";

        [Tooltip("Supabase project URL, e.g. https://xxxx.supabase.co")]
        public string projectUrl = "https://gagwduknmwxdqogpnjhr.supabase.co";

        [Tooltip("Project public key (sent as the apikey header).")]
        public string publicKey = "";

        [Tooltip("Edge Function that serves the game routes (the backend renamed game-api to player-api).")]
        public string functionName = "player-api";

        [Tooltip("Seconds before a request times out.")]
        public int timeoutSeconds = 15;

        public string AuthBase => projectUrl.TrimEnd('/') + "/auth/v1";
        public string GameBase => projectUrl.TrimEnd('/') + "/functions/v1/" +
                                  (string.IsNullOrWhiteSpace(functionName) ? "player-api" : functionName.Trim()) + "/v1";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(projectUrl) && !string.IsNullOrWhiteSpace(publicKey);

        public static OnlineConfig Load() => Resources.Load<OnlineConfig>(ResourcePath);
    }
}
