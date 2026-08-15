using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace NightTale
{
    /// <summary>
    /// Thin HTTP client for the NightTale Flask backend (play.nighttalegames.com).
    /// The "brain" — story generation, auth, turns, ads — stays server-side; this
    /// client just POSTs/GETs JSON.
    ///
    /// Identity is carried as explicit tokens (NOT cookies). Browsers forbid manual
    /// Cookie/Set-Cookie headers, so WebGL builds can't use the cookie approach —
    /// tokens work identically on WebGL, native, Android and iOS. The backend returns
    /// `session_token` (login/register) and `guest_token` (guest-start) in the JSON
    /// body; we echo them back as X-Session-Token / X-Guest-Token request headers.
    /// </summary>
    public static class NightTaleApi
    {
        public static string BaseUrl = "https://play.nighttalegames.com";

        public static string SessionToken;  // authenticated account
        public static string GuestToken;    // anonymous device identity

        public static void ClearSession() { SessionToken = null; GuestToken = null; }

        private static UnityWebRequest Request(string method, string path, object body)
        {
            var url = BaseUrl.TrimEnd('/') + path;
            var req = new UnityWebRequest(url, method);
            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(SessionToken))
                req.SetRequestHeader("X-Session-Token", SessionToken);
            if (!string.IsNullOrEmpty(GuestToken))
                req.SetRequestHeader("X-Guest-Token", GuestToken);
            return req;
        }

        private static void CaptureTokens(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return;
            try
            {
                var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawJson);
                if (obj == null) return;
                if (obj.TryGetValue("session_token", out var s) && s != null) SessionToken = s.ToString();
                if (obj.TryGetValue("guest_token", out var g) && g != null) GuestToken = g.ToString();
            }
            catch { /* ignore malformed / non-token payloads */ }
        }

        public static IEnumerator GetGames(Action<List<GameInfo>, string> onDone)
        {
            var req = Request("GET", "/api/games", null);
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            if (req.result == UnityWebRequest.Result.Success)
                onDone(JsonConvert.DeserializeObject<List<GameInfo>>(req.downloadHandler.text), null);
            else
                onDone(null, req.downloadHandler.text);
            req.Dispose();
        }

        public static IEnumerator Start(string playerName, string gameType, Action<StoryResponse> onDone)
        {
            var req = Request("POST", "/api/start",
                new { name = playerName, game_type = gameType, language = "en" });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<StoryResponse>(req));
            req.Dispose();
        }

        public static IEnumerator Action(string sessionId, string actionText, Action<StoryResponse> onDone)
        {
            var req = Request("POST", "/api/action",
                new { session_id = sessionId, action = actionText });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<StoryResponse>(req));
            req.Dispose();
        }

        public static IEnumerator Roll(string sessionId, Action<StoryResponse> onDone)
        {
            var req = Request("POST", "/api/roll", new { session_id = sessionId });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<StoryResponse>(req));
            req.Dispose();
        }

        public static IEnumerator Load(string sessionId, Action<StoryResponse> onDone)
        {
            var req = Request("GET", "/api/load/" + Uri.EscapeDataString(sessionId), null);
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<StoryResponse>(req));
            req.Dispose();
        }

        public static IEnumerator Account(Action<UserState, string> onDone)
        {
            var req = Request("GET", "/api/account", null);
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            if (req.result == UnityWebRequest.Result.Success)
                onDone(JsonConvert.DeserializeObject<UserState>(req.downloadHandler.text), null);
            else
                onDone(null, req.downloadHandler.text);
            req.Dispose();
        }

        public static IEnumerator Me(Action<UserState, string> onDone)
        {
            // Detect an existing session: /api/me returns user_state when a valid
            // session token is present, otherwise 401. No session == guest.
            var req = Request("GET", "/api/me", null);
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            if (req.result == UnityWebRequest.Result.Success)
                onDone(Safe<UserState>(req), null);
            else
                onDone(null, ErrorMessage(req));
            req.Dispose();
        }

        public static IEnumerator Login(string identifier, string password, Action<UserState, string> onDone)
        {
            // identifier may be a username or email; backend matches either.
            var req = Request("POST", "/api/auth/login",
                new { username = identifier, password = password });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            if (req.result == UnityWebRequest.Result.Success)
                onDone(Safe<UserState>(req), null);
            else
                onDone(null, ErrorMessage(req));
            req.Dispose();
        }

        public static IEnumerator Register(string username, string email, string name,
            string password, Action<UserState, string> onDone)
        {
            var req = Request("POST", "/api/auth/register",
                new { username = username, email = email, name = name, password = password, tos_accept = true });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            if (req.result == UnityWebRequest.Result.Success)
                onDone(Safe<UserState>(req), null);
            else
                onDone(null, ErrorMessage(req));
            req.Dispose();
        }

        public static IEnumerator Logout(Action onDone)
        {
            var req = Request("POST", "/api/auth/logout", new { });
            yield return req.SendWebRequest();
            ClearSession();
            if (onDone != null) onDone();
            req.Dispose();
        }

        private static string ErrorMessage(UnityWebRequest req)
        {
            if (string.IsNullOrEmpty(req.downloadHandler.text)) return req.error ?? "Request failed";
            try
            {
                var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(req.downloadHandler.text);
                if (obj != null)
                {
                    if (obj.TryGetValue("error", out var e) && e != null) return e.ToString();
                    if (obj.TryGetValue("message", out var m) && m != null) return m.ToString();
                }
                return req.downloadHandler.text;
            }
            catch { return req.error ?? "Request failed"; }
        }

        public static IEnumerator AdSlot(Action<AdSlotResponse> onDone)
        {
            var req = Request("POST", "/api/ad-slot", new { });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<AdSlotResponse>(req));
            req.Dispose();
        }

        public static IEnumerator AdComplete(string slotId, Action<UserState> onDone)
        {
            var req = Request("POST", "/api/ad-complete", new { slot_id = slotId });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<UserState>(req));
            req.Dispose();
        }

        public static IEnumerator GuestStart(string playerName, string gameType, Action<StoryResponse> onDone)
        {
            var req = Request("POST", "/api/guest-start",
                new { name = playerName, game_type = gameType, language = "en" });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<StoryResponse>(req));
            req.Dispose();
        }

        public static IEnumerator GuestAction(string sessionId, string actionText, Action<StoryResponse> onDone)
        {
            var req = Request("POST", "/api/guest-action",
                new { session_id = sessionId, action = actionText });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<StoryResponse>(req));
            req.Dispose();
        }

        public static IEnumerator GuestRoll(string sessionId, Action<StoryResponse> onDone)
        {
            var req = Request("POST", "/api/guest-roll", new { session_id = sessionId });
            yield return req.SendWebRequest();
            CaptureTokens(req.downloadHandler.text);
            onDone(Safe<StoryResponse>(req));
            req.Dispose();
        }

        private static T Safe<T>(UnityWebRequest req)
        {
            try { return JsonConvert.DeserializeObject<T>(req.downloadHandler.text); }
            catch { return default; }
        }
    }
}
