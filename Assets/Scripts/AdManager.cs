using System;

#if UNITY_ADS
using UnityEngine.Advertisements;
#endif

namespace NightTale
{
    /// <summary>
    /// Rewarded-ad wrapper. If a Unity Ads game id is configured it uses the native
    /// Unity Ads rewarded placement; otherwise it reports "not rewarded" and the
    /// caller falls back to the backend house-ad flow (which is what actually grants
    /// the +5 turns server-side via /api/ad-complete).
    /// </summary>
    public static class AdManager
    {
        private static string _gameId;
        private static string _placementId;
        private static bool _adsReady;
        private static Action<bool> _pendingReward;

        public static void Init(string gameId, string placementId)
        {
            _gameId = gameId;
            _placementId = string.IsNullOrEmpty(placementId) ? "rewardedVideo" : placementId;
#if UNITY_ADS
            if (!string.IsNullOrEmpty(_gameId) && Advertisement.isSupported)
            {
                Advertisement.Initialize(_gameId, true);
            }
#endif
        }

        /// <summary>Attempt to show a rewarded ad. Callback fires with true only after a full watch.</summary>
        public static void ShowRewarded(Action<bool> onComplete)
        {
#if UNITY_ADS
            if (!string.IsNullOrEmpty(_gameId) && Advertisement.IsReady(_placementId))
            {
                _pendingReward = onComplete;
                var options = new ShowOptions
                {
                    resultCallback = result =>
                    {
                        var rewarded = result == ShowResult.Finished;
                        var cb = _pendingReward;
                        _pendingReward = null;
                        cb?.Invoke(rewarded);
                    }
                };
                Advertisement.Show(_placementId, options);
                return;
            }
#endif
            // No native ads configured — signal "not rewarded" so the caller uses
            // the backend house-ad path.
            onComplete?.Invoke(false);
        }
    }
}
