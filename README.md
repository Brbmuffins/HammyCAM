# HammyCAM — Muffin Dynamic VTuber

A desktop VTuber app (Unity, Windows) where the avatar changes live during the stream.

## Getting set up (collaborators)

No keys are in this repo. The app itself needs none; they're only for making new assets.

1. **The app:** install Unity **6000.4.10f1** from Unity Hub, then Hub → Add → the `MuffinVTuber` folder and open
   `Assets/Muffin/Scenes/MotionPrototype.unity`. Details, controls and the build command: [MuffinVTuber/README.md](MuffinVTuber/README.md).
2. **Face tracker:** the MediaPipe helper is built from `tracker/muffin_facetracker.py` into
   `third-party/mediapipe/` (the steps are under "MediaPipe" in [MuffinVTuber/README.md](MuffinVTuber/README.md); the model is
   Google's `face_landmarker.task`). Without it the app falls back to OpenSeeFace v1.20.5 (download it into
   `third-party/OpenSeeFace-v1.20.5/`), or the mouse stands in for your head.
3. **Asset workflows** (new avatars, rooms, sounds): copy [.ai-keys.example](.ai-keys.example) to `.ai-keys.local` and fill
   in your own keys (gitignored), then `npm install` in `props-service/`. Each tool only needs its own key:
   Gemini for pictures, Tripo for paid models, ElevenLabs for sounds, a Hugging Face token for free local models
   with SPAR3D (setup in [third-party/spar3d/README.md](third-party/spar3d/README.md)). The batch files in
   `props-service/batches/` are the jobs already run; copy one to start a new batch.

Downloaded tools, Python environments, model weights, Unity's Library and builds are not committed (see
`.gitignore`); they're set up again from the READMEs.

**Requirement #1: smooth, fluid, professional motion.** Web-based setups jitter and drop frames; models feel
rigid. Everything else comes after this. Requirements from the 2026-10-14 voice note: [docs/voice-note-2026-10-14.md](docs/voice-note-2026-10-14.md).

- **The avatar:** any goofy object or mascot that copies the streamer's movements through the webcam (a potted
  sunflower whose stem bends and stretches, a steak that scrunches when you lean down, the Death Muffin mascot).
  The streamer is never on camera unless they choose the "my face" option. Objects get an automatic **bend rig**
  (a chain of bones from a rooted base up to the face, with spring physics), so any model works — Tripo, STL, GLB —
  without hand-rigging. Humanoid mascots use Tripo's biped rig instead.
- **Face toggle:** *digital face* (cartoon eyes and mouth stickers that follow expressions and mouth shapes like
  "ooh"/"ah") or *my face* (the streamer's real face from the webcam on the object).
- **Library:** a scrollable set of ready-rigged avatars, plus "I want a steak" → generated, rigged and added.
- **Mood layer (free per use):** voice energy, chat mood and stream events shift the avatar's look —
  glow, colour, particles, and mood props (horns when angry, a rain cloud when sad).
- **Chat props (paid per generation):** a viewer redeems "give him a pirate hat", a 3D hat is generated in
  about a minute and snaps onto the avatar. Popular props are cached, so the second pirate hat is free and instant.

Other VTuber apps treat the avatar as a fixed puppet that copies your face. This one is built to change.

## How a chat prop works

```
Viewer redeems "Hat: pirate hat" (channel points or bits)
        │
        ▼
UNITY APP (streamer's PC)
  Twitch EventSub WebSocket receives the redemption and its text
  POST /props {prompt, slot}  ──────────────────────────┐
  Shows an "incoming…" card on stream                   │
                                                        ▼
PROPS SERVICE (our VPS — keys never ship in the app)
  1. Moderate the prompt ............. rejected → refund the viewer's points
  2. Cache lookup .................... hit → GLB instantly, 0 credits
  3. Gemini concept picture .......... sent to Unity as a 2D preview card
  4. Streamer approves (optional) .... rejected → refund, no Tripo spend
  5. Tripo image → model (~50 credits)  resumes, refunds itself on failure
  6. Optimize the GLB (PNG textures, quantized mesh)
  7. Cache it, charge the streamer's credit balance
        │
        ▼
UNITY APP
  glTFast loads the GLB → fit to slot → attach to avatar bone → summon effect
  Mark the redemption FULFILLED (or CANCELED → points refunded)
```

## What's here

| Path | What |
|---|---|
| `props-service/` | Prop generation, ported from Death Muffin's asset pipeline. Today a command-line tool for test batches; later the VPS service. |
| `props-service/batches/` | Lists of props to generate (`prop-test-01.json` is the first test). |
| `props-service/records/` | What was generated: prompts, Tripo task ids, credits, and the Tripo plan each task ran under. Keep these. |
| `props-service/licensing.json` | The Tripo plan in effect (paid). Copied onto every Tripo task when it's created; Tripo spending is blocked unless it says `paid`. |
| `props-service/raw/`, `out/` | Raw downloads and the optimized GLBs (not committed). `out/manifest.json` tells Unity each prop's slot. |
| `.ai-keys.local` | Your keys (from `.ai-keys.example`): Gemini, Tripo, ElevenLabs, Hugging Face — never commit, never ship in the app. |

## Running a test batch

From `props-service/` (run `npm install` once):

```bash
node src/batch.mjs plan batches/prop-test-01.json            # what would run + cost; spends nothing
node src/batch.mjs concepts batches/prop-test-01.json --yes  # Gemini pictures → raw/concepts/ (cheap)
node src/batch.mjs models batches/prop-test-01.json --yes    # Tripo models (≈50 credits each)
node src/batch.mjs build batches/prop-test-01.json           # Unity-ready GLBs → out/ (free)
node src/batch.mjs balance                                   # Tripo credits left (free)
```

Look at the pictures in `raw/concepts/` before running `models`. Redraw a bad one with
`concepts … <id> --yes --force`; only pay Tripo for pictures you like.

## Build order

1. [ ] **Motion prototype (the smoothness test)** — Unity, one bendy object (potted sunflower), webcam head
   tracking with jitter filtering, the bend rig with spring physics, rendering at 60 fps+ and Spout output to OBS.
   Judge it on a real stream recording before building anything else.
   **Built 2026-10-14** (mouse stands in for the head; see `MuffinVTuber/README.md`): self-test steady 60 fps,
   no dropped frames, no errors. Webcam face tracking (OpenSeeFace) and a transparent background for OBS
   Game Capture added the same day, and tracking confirmed on a live face using an iPhone through Iriun Webcam
   (the PC has no webcam). Still to do: feel-tuning from real use, Spout2 output, iPhone ARKit tracking.
2. [ ] **Digital face** — sticker eyes/mouth driven by expressions plus mouth shapes from the voice ("ooh", "ah").
3. [ ] **Avatar library** — a handful of objects (auto bend rig) plus the Death Muffin mascot. Death Muffin's
   concept and stream portraits are done; its Tripo model + Mixamo-named biped rig is ≈85 credits
   (`models batches/avatar-ideas-01.json death_muffin --yes`, then `build … death_muffin`).
4. [ ] **"My face" option** — the streamer's real face from the webcam mapped onto the object.
5. [ ] **Mood layer** — voice/chat mood → sticker expressions, glow, colour, particles, mood props.
6. [ ] **Prop test** — `prop-test-01` concepts are drawn (10 props); Tripo models (≈500 credits) wait until the
   avatar works, so the props get fitted on the real avatar.
7. [ ] **One prop end to end** — hard-coded prompt → props service → onto the head.
8. [ ] **Twitch** — channel-point redemptions drive it, refunds on failure.
9. [ ] **Preview card, approval queue, cache.**
10. [ ] **Accounts, credit balance, payments** (reuse the Death Muffin backend's Express + JWT + MySQL setup).

## Gotchas already learned

- **No WebP textures.** glTFast (Unity's runtime glTF loader) will never support `EXT_texture_webp`. The build
  step writes PNG and refuses to output WebP. KHR_mesh_quantization is fine.
- **Exactly one copy of `sharp`.** `@gltf-transform/functions` pulls in its own `sharp` via `ndarray-pixels`. If
  that version differs from ours, two copies of libvips load and every texture fails with
  `colourspace: parameter space not set`. Keep our `sharp` version equal to theirs (`npm ls sharp` shows one copy).
- **Avatar rigs use Mixamo bone names** (`spec: "mixamo"`, Tripo's Unity-compatible option) so Unity's Humanoid
  importer maps them automatically. Death Muffin used Tripo's own names, which suit three.js but not Humanoid.
- **Only paid-plan Tripo models can be sold.** On the free plan Tripo keeps the rights to outputs; paid plans
  grant full commercial rights. Every task record carries the plan stamp from `licensing.json`; keep the matching
  Tripo invoices in the private asset vault (not git). Update `licensing.json` if the plan ever changes.
- **Keys stay on the server.** A key inside a desktop app can be extracted and spent by anyone.
- **Twitch refunds:** only the app that created a channel-point reward can refund its redemptions, so the app
  creates its own rewards.
- **Check prop facing in Unity.** Death Muffin's Tripo characters face +X; props may need a fixed rotation per
  slot so a hat's front faces forward.
