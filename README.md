# NightTale — Unity Client

Native Unity client for **NightTale**, the choose-your-own-adventure game. The
"brain" (AI story generation, character state, auth, turns, ads, payments) stays
on the existing Flask backend at `play.nighttalegames.com` — this client is a thin
UI layer that POSTs/GETs JSON and carries the Flask session cookie.

Building this natively (instead of the Capacitor WebView wrapper) solves two
problems: App Store / Google Play accept a real native binary, and we get native
rewarded-ad SDKs (Unity Ads / ironSource) with no Google H5 approval gauntlet.

## Setup

1. Open this folder in **Unity 2022.3 LTS** (any 2022.3.x). First open imports
   the packages in `Packages/manifest.json` (TextMeshPro, Newtonsoft JSON, uGUI, Unity Ads).
2. Create an **empty scene** (`File → New Scene → Empty`).
3. Add an **empty GameObject** and attach the `NightTaleBootstrap` component
   (`Assets/Scripts/NightTaleBootstrap.cs`).
4. Optionally set `Unity Ads Game Id` + a rewarded placement id — leave blank to
   use the backend house-ad flow.
5. Press **Play**. The entire UI (canvas, event system, game picker, story view,
   paywall) is built at runtime — no scene wiring needed.

## Architecture

```
Assets/Scripts/
├── Models.cs                # [Serializable] DTOs matching the Flask JSON (GameInfo,
│                            #   StoryResponse, ButtonInfo, UserState, AdSlotResponse…)
├── NightTaleApi.cs          # UnityWebRequest client; carries the session cookie,
│                            #   guest + authed + ad endpoints
├── NightTaleBootstrap.cs    # entry MonoBehaviour — builds all UI programmatically
│                            #   (picker → story loop → paywall) and drives the flow
├── AdManager.cs             # rewarded-ad wrapper (Unity Ads when configured,
│                            #   else signals fallback to the backend house ads)
└── RectTransformExtensions.cs
```

## Flow

1. `GET /api/games` → game picker (skips `coming_soon`).
2. `POST /api/guest-start {name, game_type}` → session + opening story + portrait +
   `buttons[]`. Anonymous play gets **12 free turns**.
3. `POST /api/guest-action {session_id, action}` → next story beat (text, image,
   new `buttons[]`). `POST /api/guest-roll` resolves dice when `roll_required`.
4. Out of turns → paywall modal. **"Watch Ad for +5 Turns"** → rewarded ad →
   `POST /api/ad-slot` → `POST /api/ad-complete {slot_id}` → server grants +5 turns
   (the backend is the source of truth; reward only lands after a full watch).

## API contract (server-side, already live)

| Endpoint | Method | Body → Returns |
|---|---|---|
| `/api/games` | GET | — → `[{slug,title,subtitle,cover,mode,coming_soon}]` |
| `/api/guest-start` | POST | `{name,game_type,language}` → `StoryResponse` (session_id, story, image, portrait, buttons, health/gold/inventory/location/stats, state) |
| `/api/guest-action` | POST | `{session_id,action}` → `StoryResponse` |
| `/api/guest-roll` | POST | `{session_id}` → `StoryResponse` |
| `/api/start` / `/api/action` / `/api/roll` | POST | same, but `@login_required` (account mode) |
| `/api/load/<session_id>` | GET | resume a saved session |
| `/api/ad-slot` | POST | `{slot_id, ad_url, ad_type, reward_turns}` |
| `/api/ad-complete` | POST | `{slot_id}` → `UserState` (+5 turns) |
| `/api/auth/login` / `/api/auth/register` | POST | sets session cookie |
| `/api/account` | GET | `UserState` |

`ButtonInfo = {label, action, description}` — the `action` string is sent back to
the action endpoint.

## Next steps (not yet built)

- **Auth UI** — login/register screen; swap `GuestStart` → `NightTaleApi.Start`
  after auth, keep the session cookie.
- **Native rewarded ads** — drop in a Unity Ads game id + placement; `AdManager`
  is already wired to call `ShowRewarded` first and fall back to house ads.
- **TTS** — `/api/me/voice` + `/api/tts` exist; stream the audio under the story text.
- **Session resume** — `/api/load/<session_id>` to continue a story on relaunch.

## Build

`File → Build Settings → iOS / Android / WebGL` — same codebase. For the store,
enable `Internet Access: Require` in Player Settings (the client talks to the live
backend over HTTPS).
