using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DemoRestaurant.Game.Online
{
    /// <summary>Raw outcome of one HTTP call. Status 0 = no response (network failure or timeout).</summary>
    public readonly struct HttpResult
    {
        public readonly int Status;
        public readonly string Body;
        public readonly string RetryAfter;

        public HttpResult(int status, string body, string retryAfter)
        {
            Status = status;
            Body = body;
            RetryAfter = retryAfter;
        }

        public bool IsSuccess => Status >= 200 && Status < 300;
    }

    /// <summary>
    /// Thin UnityWebRequest wrapper. Adds the apikey header; never logs tokens or bodies.
    /// Continuations run on the main thread (Unity's synchronization context).
    /// </summary>
    public sealed class SupabaseHttp
    {
        private readonly OnlineConfig _config;

        public SupabaseHttp(OnlineConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public OnlineConfig Config => _config;

        public async Awaitable<HttpResult> Send(string method, string url, string jsonBody, string bearer,
            string idempotencyKey = null)
        {
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (jsonBody != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("apikey", _config.publicKey);
                if (!string.IsNullOrEmpty(bearer))
                    request.SetRequestHeader("Authorization", "Bearer " + bearer);
                if (!string.IsNullOrEmpty(idempotencyKey))
                    request.SetRequestHeader("Idempotency-Key", idempotencyKey);
                request.timeout = Mathf.Max(1, _config.timeoutSeconds);

                try
                {
                    await request.SendWebRequest();
                }
                catch (Exception)
                {
                    // UnityWebRequest reports failures through result/responseCode below.
                }

                bool noResponse = request.result == UnityWebRequest.Result.ConnectionError ||
                                  request.result == UnityWebRequest.Result.InProgress;
                int status = noResponse ? 0 : (int)request.responseCode;
                return new HttpResult(status, request.downloadHandler?.text, request.GetResponseHeader("Retry-After"));
            }
        }
    }
}
