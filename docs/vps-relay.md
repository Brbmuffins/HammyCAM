# HammyCAM relay: build brief for the VPS

You are building the **server side** of a relay for **HammyCAM**, a desktop VTuber app running on the streamer's
Windows PC. It does two things:

1. Brings **TikTok LIVE** viewer activity (comments, gifts, likes, follows) into the app. Viewers already make a mess
   of the streamer's desk through Twitch chat commands (`!pie`, `!fire`, `!whack`...); this does the same for
   TikTok, which has no chat API the app can use directly.
2. Serves a **phone-friendly web page** so the streamer can run the show from any phone browser, anywhere: status,
   recent events, buttons that fire the chaos and switch avatars, and the gift map.

The app side (a WebSocket client inside the Unity app) is being built separately against the protocol below, so
**stick to the protocol exactly**.

```
TikTok LIVE (the streamer's own room)
      │  tiktok-live-connector (npm), running on this VPS
      ▼
hammycam-relay (Node, this VPS) ── gifts/likes/follows → commands, comments forwarded
      │  wss://<domain>/app   (TLS, token; the app connects OUT, so nothing is opened at home)
      ▼                        ▲
HammyCAM app (streamer's PC)   │  https://<domain>/   phone dashboard (password)
  applies its own cooldowns    └── buttons become `remote` messages to the app
  and rules, makes the mess
```

## What the app already does (context, don't rebuild it)

- `ChaosDirector` handles viewer commands with a cooldown per event, a cooldown per viewer, a gap between any two
  events, at most three at once, and a master switch. **The app enforces all rules**; the relay only forwards.
- Commands the app knows: `!sprinkler !sprinklers !rain`, `!fire`, `!sneeze !achoo !slime`, `!pie`,
  `!fruit !tomato !tomatoes`, `!cream !whip !whippedcream`, `!water !spill`, `!shake`, `!slam !faceplant`,
  `!whack !bonk !hit`. A chat line counts if it starts with one of these.
- Twitch chat is read by the app itself (anonymous IRC); the relay doesn't need Twitch for now.
- On the home Wi-Fi the app already serves its own little phone remote; the dashboard here is the same idea over the
  internet, using the same action names (see `remote` below).

## Build this

A Node 20+ service, `hammycam-relay`, in its own folder (or repo) on the VPS.

1. **TikTok in.** Connect to the LIVE room of the configured username with `tiktok-live-connector` (pin the
   version). When the streamer isn't live, check every 30–60 s and connect when they go live; reconnect with
   backoff after drops. Read its README first: recent versions may need a signing-service API key; if so, it goes
   in the config, never in code or git.
2. **Events.**
   - Comment → a `chat` message (below), text as typed.
   - Gift → look its name up in `giftMap`; if mapped, send a `gift`. Gifts that "streak" (combo repeats) fire
     **once per individual gift**, including every repeat in a streak. Send a `gift` message with `command` and `count` (new gifts only). Track streak count deltas and deduplicate replayed events; never double-count the final notification. Unmapped gifts are ignored (but listed on the
     dashboard so they can be mapped).
   - Likes → every `likeEvery` likes in total (room-wide), send `likeCommand` (optional).
   - Follow / share → `followCommand` / `shareCommand` (optional, may be empty).
   - Light pre-filter only: filter excessive comments only; never drop paid gifts. The real rules are the
     app's job.
3. **App connection.** A WebSocket server behind TLS at `wss://<domain>/app`. Up to 3 app connections at once (the
   streamer might run two PCs). Ping each connection every 20 s; drop ones that don't answer within 10 s.
4. **Auth.** `appToken` (random, at least 32 bytes, base64url) from the config. The app's first message must be a
   `hello` with the right token within 5 s, or the connection is closed with code 4001. Compare in constant time.
   Never log tokens, passwords or keys.
5. **Phone dashboard** at `https://<domain>/`, described below.
6. **Fake mode.** `node relay.mjs --fake` (or `FAKE=1`): no TikTok; send a made-up comment or gift every 3–8 s
   (`!pie`, `!whack`, a Rose...), so the app side and the dashboard can be tested without going live.
7. **Test client.** `node test-client.mjs wss://<domain>/app <token>` connects, says hello, prints every message
   (including the `remote` messages the dashboard's buttons send) and answers pings.
8. **Running it.** A systemd unit (restart always, unprivileged user), TLS through Caddy (simplest) or nginx +
   certbot, only 443 (and 80 for the certificate) open to the world. `GET /health` returns status (below).
   `config.json` lives next to the service and is in `.gitignore`; ship `config.example.json`.
9. **README** with the setup steps, how to rotate the token and the dashboard password, and how to read the logs.

## The phone dashboard

A single mobile-first page that works in any phone browser (and is fine on a desktop): big thumb-sized buttons, a
dark theme, no app to install, plain HTML/CSS/JS served by the relay (no build step; keep it light and fast on a
phone connection).

- **Login:** a dashboard password from the config (separate from the app's token). On success, a session cookie
  (`HttpOnly; Secure; SameSite=Strict`, 30 days). Five wrong tries from one IP lock it out for a minute. Every
  dashboard API call checks the session.
- **Shows:** whether TikTok is connected and live (with the viewer count), whether the app is connected, the app's
  current avatar, and the last ~50 events (comments that were commands, gifts and what they became, likes,
  follows), newest first, updating live (the page can open its own WebSocket at `/dash`, or poll every 1–2 s).
- **Controls**, each sending a `remote` message to the app: fire any chaos event; shake, slam, whack; scrub and
  clean the desk; switch avatar (the list comes from the app's `state`); emotion; cartoon or real face; background
  and room; wide or vertical window; sound on/off and volume; show/hide the readout and the desk; the chaos master
  switch; and a big "talking makes mess" on/off toggle (`faceMess`).
- **Gift map:** the gifts that have arrived recently, each with a drop-down of commands (or "nothing"); saving writes
  `giftMap` to `config.json` and applies at once. The same for `likeEvery`, `likeCommand`, `followCommand` and
  `shareCommand`.
- When the app isn't connected, the controls are greyed out with a note ("HammyCAM isn't connected").

## The protocol (JSON, one object per WebSocket text message)

### Paid gift rules (supersede chat limits above)

- Rose (requested 1-coin gift): `!tomato`, exactly one tomato per Rose.
- Ice Lolly (10 coins): `!pie`, one pie to the face per gift.
- Friendship Necklace (10 coins): `!clean`, clean the desk.
- Doughnut (requested 30-coin gift): `!fire`.

Map specific gift identities, not every gift with the same price. Confirm the chosen gifts against the live room's catalog.
The app accepts `{ "type": "gift", "user": "Viewer", "command": "!tomato", "count": 10 }` for ten newly received Roses.
No paid gift is rejected for chat cooldowns, viewer limits, disabled chat effects, or simultaneous-event limits.
The app starts one purchased effect per frame and retains pending gifts while the chaos master switch is off.
The pending queue is in memory for the running app session; durable delivery across restarts requires relay persistence and acknowledgments.
Legacy `command` messages with a `reason` starting `gift:` also use this paid path and may include `count`.
The relay must implement this revised delivery protocol before live gifts can be verified.

App → relay:

```json
{ "type": "hello", "token": "<appToken>", "app": "HammyCAM", "version": "2026-10-19" }
{ "type": "pong" }
{ "type": "state", "state": { "avatar": 3, "avatars": ["Sunflower", "T-bone steak"], "chaosOn": true } }
```

`state` is the app's own remote-control snapshot (the same JSON its home-Wi-Fi phone remote reads), sent on connect
and whenever it changes (at most every 2 s). Keep the latest one and hand it to the dashboard as-is; don't depend on
fields beyond `avatar` (an index) and `avatars` (names): the rest may change.

Relay → app:

```json
{ "type": "welcome", "relay": "hammycam-relay", "version": "1" }
{ "type": "status", "platform": "tiktok", "connected": true, "live": true, "room": "@name", "viewers": 123 }
{ "type": "chat", "platform": "tiktok", "user": "Display Name", "userId": "1234", "text": "!pie lol", "ts": 1760900000000 }
{ "type": "command", "platform": "tiktok", "user": "Display Name", "userId": "1234", "command": "!tomato", "reason": "gift: Rose", "ts": 1760900000000 }
{ "type": "remote", "action": "chaos", "value": "Pie" }
{ "type": "ping" }
```

- Send `status` right after `welcome`, then whenever it changes (went live, ended, reconnecting), and at most every
  30 s otherwise (viewer count).
- `command` is always one of the app's commands listed above. `reason` is free text for the on-stream caption
  ("gift: Rose x5", "500 likes", "followed").
- `user` is the viewer's display name (nickname), trimmed to 40 characters, control characters removed.
- `remote` is a dashboard button. `value` is always a string. Actions (the app's own phone-remote actions):
  `avatar` (index), `emotion` (Neutral, Happy, Sad, Angry, Surprised), `face` (real, cartoon), `chaos` (Sprinklers,
  Fire, Slime, Pie, Fruit, WhippedCream, WaterSpill, Shake, Slam, Whack), `chaosOn` (1/0), `shake`, `slam`, `whack`,
  `scrub`, `clean`, `background` (Preview, GreenScreen, Transparent), `room` (index), `window` (wide, vertical),
  `sound` (1/0), `volume` (0..1), `readout` (1/0), `desk` (1/0), `recentre`, `faceMess` (1/0: whether the
  streamer's mouth and face set off the avatar's mess; a big toggle on the dashboard, for when they're chatting a
  lot). The app ignores actions it doesn't know.
- Unknown message types must be ignored by both sides (so either side can add things later).

## Config (`config.json`, example)

```json
{
  "tiktokUser": "streamer_username_without_at",
  "appToken": "<generate: node -e \"console.log(require('crypto').randomBytes(32).toString('base64url'))\">",
  "dashboardPassword": "<a long passphrase>",
  "signApiKey": "",
  "port": 8787,
  "giftMap": { "Rose": "!tomato", "Ice Lolly": "!pie", "Friendship Necklace": "!clean", "Doughnut": "!fire" },
  "likeEvery": 500,
  "likeCommand": "!shake",
  "followCommand": "!whack",
  "shareCommand": ""
}
```

The gift names are examples; the dashboard lists the ones that actually arrive so they can be mapped.

## Done when

- `GET /health` returns `{ "ok": true, "tiktok": { "connected": …, "live": … }, "apps": <n> }`.
- With `--fake`, the test client gets `welcome`, `status`, then a stream of `chat` / `command` messages.
- With the streamer live, a TikTok comment `!pie` reaches the test client within about a second; a mapped gift
  arrives as one `command` per streak; the relay survives the live ending and starting again.
- A wrong or missing token is refused (close code 4001); tokens, passwords and keys never appear in logs.
- On a phone browser the dashboard logs in, shows the status and the events, and its buttons reach the app (with
  `--fake` and the test client connected, the client prints the `remote` messages the buttons send).

## Then tell the streamer (they'll pass it on to the app side)

- The dashboard address (`https://<domain>/`) and the relay URL (`wss://<domain>/app`), and that the token and the
  dashboard password are in `config.json` on the VPS. The streamer types the token into the app themselves
  (menu → Chaos → Relay); don't paste secrets anywhere else.
- The gift names that showed up, so the gift map can be tuned.

## Later (not now)

The **props service** from the main README (viewers redeem a prop; Gemini concept → Tripo model on the VPS so API keys
never ship in the app) can live in the same service behind `POST /props`.
