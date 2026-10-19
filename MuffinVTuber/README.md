# MuffinVTuber — Unity app

Unity **6000.4.10f1**, URP, new Input System (install that editor version from Unity Hub).

## Motion prototype (step 1 of the build order)

The smoothness test: a potted sunflower made from code, with the automatic bend rig. The pot stays rooted,
the stem bends, squashes and stretches, and the flower (with a digital face) rides your head.

**Run it:** double-click **`Vtube/Muffin VTuber (latest).lnk`**. It runs `Launch Muffin VTuber.cmd`, which first
installs any update waiting in `Builds/Win64-next` (builds made while the app was open go there), then starts
`Builds/Win64`. If the app is already open it does nothing (two copies would fight over the camera). The readout
shows the build time; copies elsewhere (Downloads, old zips) are out of date.
**Open it in Unity:** Unity Hub → Add → this `MuffinVTuber` folder → open `Assets/Muffin/Scenes/MotionPrototype.unity` → Play.
To regenerate the scene: menu **Muffin → Create Motion Prototype Scene**.

| Control | Does |
|---|---|
| ↓ / ↑ | Next / previous avatar: sunflower, T-bone steak, potted cactus, soda can, pizza slice (floppy point, pepperoni), egg (rigid: rocks like a weeble; frying pans fall on it, it cracks and leaks), trash can (solid lid; garbage), juice cup (sloshes, drips down its front), vacuum (a long simulated hose whose nozzle follows your mouse over the desk and sucks up sprinkles, bits and puddles; click the hose or use the menu to switch that off; dust clouds), meatball sub and spaghetti (rigid bowl, the noodles sway side to side; meatballs roll with sauce trails, sauce flicks off and runs down the front), cherry cupcake (sprinkles keep falling), hot dog (sausage and bun top flop over; mustard, ketchup, relish), popcorn (a solid bucket that rocks, the heap stays put; kernels keep popping out and pile up on the desk; popping fits), jello (wobbles as one whole jelly, squashing and stretching all over, no bend in the middle; shivers on its own, flings jelly chunks and cream), taco (filling falls out as it moves, salsa drips), mouldy bread (spore clouds, stale crumbs), toaster (close your eyes hard and the toast pops out onto the desk), ketchup bottle (open your mouth and it squirts, squashing as it squeezes), coffee mug (steams, sloshes). Popcorn, jello and those five were modelled with SPAR3D (free, local: `third-party/spar3d`), the rest with Tripo |
| F1 / F2 / F3 / F4 | Emotion: happy / sad / angry / surprised (press again for neutral). Kept small, like a stop-motion or South Park face: the eyes stay the same, the brows tilt and the mouth curves a little, and a sign says the rest (blush, a running tear, puffs of steam off the head, a sweat drop) |
| V | Face: cartoon stickers ↔ your real eyes and mouth from the camera |
| E | Real face, how much of you shows: just eyeballs and lips (default, cut along their outlines) → eyes and mouth (ovals) → eyes with brows |
| Z / O | Zoom into the eyes (Z) or the mouth (O), each on its own: normal → big → huge → giant (both faces; real ones also crop tighter). The menu's Face tab has a slider for each, any size from ×0.6 to ×3.2 |
| S | Real-face edges: soft → sharp → sharp with a cartoon outline |
| (menu) | Place the face on this avatar (Face tab): move it left/right and up/down, resize it, raise or spread the eyes, raise the mouth; each avatar remembers its own |
| (menu) | Real eyes and mouth look: brightness, contrast and colour sliders in the menu's Face tab (the cut-outs only, not the rest of the picture); "As the camera sees it" resets them |
| Hold X | Dramatic zoom: the camera swings in on the face and the features blow up; springs back on release |
| P | Camera view (top right) with the tracker's face points (green: the person the avatar follows, grey: others) |
| T | More than one person in view: follow the next person (kept until they leave the picture) |
| [ / ] | Feel: smoother ↔ snappier, 5 levels (default 3, balanced; remembered) |
| - / + | Lean sensitivity (default 100%, remembered; looking around isn't scaled) |
| D | Desk scene on/off: the avatar's mini desk, keyboard, mouse and floating hands |
| **M** | **Menu**: every feature below with the mouse (Avatar, Face, Tracking, Desk & mess, Chaos, Stream tabs). Mirror is a button in the Tracking tab. F6 opens it too |
| F7 | Chaos set on/off: whether viewers' chat commands do anything |
| R | Face slam: the avatar slaps its face into the desk and springs back up (the pizza picks its pepperoni back up) |
| K | Clean up: a bucket pops onto the desk and a soapy sponge scrubs it clean (the panel also has "Clean instantly") |
| G | Background: preview → green screen → transparent → the rooms (vacuum sales floor, airport gate, construction site, laundromat, dentist's waiting room, bowling alley, castle throne room, DMV waiting room; pictures in `Assets/Muffin/Backdrops`, wide ones from props-service `batches/backdrops-01.json` and `03-dmv`, tall ones for vertical windows from `backdrops-02-tall.json`) |
| H | Hide the readout (it only shows the avatar, the tracker and warnings; everything else is in the menu, M) |
| F9 | Save diagnostics (screenshot, camera picture with face points and cut-out windows, crops, settings) to `Diagnostics/` next to the app |
| W | Switch between webcam tracking and the mouse |
| F5 | Face tracker: MediaPipe (default) / OpenSeeFace |
| L | Control: head turns (default: turning, nodding and tilting drive the avatar; where your head is in the picture doesn't) / body lean (moving your head around leans it too) |
| C | Re-centre the webcam (sit normally, look at the screen, press C) |
| N | Next camera |
| Mouse left/right, up/down | Mouse mode: lean; stretch / scrunch |
| Space / B | Mouse mode: talk / blink (with the webcam your mouth and eyes drive these) |
| F | Smoothing on/off: compare raw tracker input with filtered |
| J | Simulated webcam on/off for the mouse (30 updates a second plus jitter, like a real tracker) |

Hotkeys only work while the app's window has focus (a stream would need global hotkeys; not built yet).

**Framing:** `StageCamera` keeps a box around the avatar (room for its biggest stretch and lean) and the desk in
view for any window shape, so a square or narrow OBS capture never cuts the avatar off.

### Desk scene

`Scripts/Desk/`: `DeskInput` reads the real keyboard and mouse globally (works while a game has focus) by
polling, not hooks: it only counts key presses, never which key. `DeskScene` turns that into floating cartoon
hands tapping a mini keyboard and steering a mini mouse (the right hand stays on the mouse while you use it).
Limits: games that lock the cursor (shooters) don't move it, so mouse motion isn't seen there (clicks are); games
running as administrator hide their input from a normal app.

### The chaos set (viewers make it messier)

`Scripts/Chaos/`: viewers type commands in Twitch chat, which the app reads anonymously (no login, read-only:
`TwitchChat`), and `ChaosDirector` plays the event if the rules allow: a cooldown per event, a gap between any two,
a cooldown per viewer, at most three at once, and the master switch (F7). A caption names who did it. Set it up in
the menu (M → Chaos): type the channel, Connect, tune or disable each event, or fire them yourself (keys 1-8
while the panel is open; the streamer skips the cooldowns).

| Command | Event |
|---|---|
| !sprinkler !rain | Sprinklers: rain over the desk, puddles, the avatar goes shiny-wet, puts out a fire |
| !fire | Crumpled paper catches fire on the desk: flames, sparks, smoke, flickering light; it chars and leaves a scorch mark and burnt paper |
| !sneeze !achoo !slime | A sneeze: ah-CHOO, booger-green snot splats on the camera glass and drips down |
| !pie | A pie to the face: SPLAT, it knocks the avatar back, cream bursts all over the desk, the face is buried, drips run down, the pie slides off |
| !fruit !tomato | Tomatoes burst on the avatar and the desk: juice sprays, pulp and seeds stick and drip, chunks roll leaving juicy trails |
| !cream !whip | A can sprays piped swirls of whipped cream on the avatar's top and in a line across the desk |
| !water !spill | A pitcher tips over; water runs to the edge, carrying things along |
| !shake | The avatar's own mess (pepperoni, sprinkles, frying pan + egg yolk, garbage, juice, meatballs, vacuum dust) |
| !slam !faceplant | Face slam |

`Mess` keeps what's left (splats, puddles, sauce trails, scorch, cream) and fades it slowly; K scrubs it all away. It stays
alive: splats land with a wobble and spatter, puddles spread, the hands smear wet stains and squash blobs and cream
lying on the desk, blobs stuck on the avatar fly off when it moves hard, and drips run down its front to the desk.
Every avatar's own mess comes off when it turns, not only when it moves about (`MotionMeter`: the speed of its rim).
Sound effects (realistic and gross, not cartoon: splats, slops, drips, squelches as the hands smear things, pie, sneeze, fire
crackle and steam hiss, sprinklers, water spill, juice slosh, pan clonk, egg crack, soggy garbage,
meat slaps, face-slam thud, bucket, sponge scrubbing, bubbles, ketchup splurt, hot dog flop, farts, a wet pop when a viewer triggers
something) are made with ElevenLabs: props-service `node src/sfx.mjs batches/sfx-02-gross.json` (the bubbles from `sfx-01.json`; key from `ElevenLabsAPI.txt`,
never printed) → `Assets/Muffin/Sounds`; `SoundBank` plays them. The menu's Desk & mess tab has the sounds switch and volume.

### Things falling on the desk

`Scripts/Desk/DeskDebris.cs`: whatever falls off an avatar lands on its desk with simple physics of its own (gravity,
bounces, sliding to a stop on the desk, keyboard or mouse pad, and off the edge). The floating hands and the mini mouse
shove what's in their way and typing taps scatter it, so over a stream the sugar piles up and gets pushed around.
- **Sprinkle muffin** (`SprinkleShedder`): the harder its dome is shaken, the more sprinkles come off (plus a stray
  one now and then); up to 1,500 lie around, drawn instanced in six colours.
- **Pizza slice** (`Toppings`): five pepperoni discs ride the slice (the ones on the floppy point flop with it). One
  slides off every 25-70 s, and a hard shake can knock one loose; switching avatars puts them back.
- **R** folds the avatar onto the desk (`BendRig.Slam`), shakes sugar loose and flies the pepperoni back on. **K**
  sweeps the desk.

### Look-around physics, the mouth and floppy tips

Looking over your shoulder twists the whole body (lagging the head), looking down hunches it over the desk; if the
tracker loses your face it holds the last pose for 1.5 s. Glancing at another screen turns the body partway with you and leans it a little toward where you look (BendRig: bodyTwist, glanceLean): a shoulder glance, not a full turn. The cartoon mouth is deliberately exaggerated: small real
openings are boosted (after a small dead zone, so a closed mouth doesn't flap), the mouth spring overshoots, the
head stretches as you talk, and a one-sided smile shows as a smirk. Tuning lives on **BendRig** (Look-around
physics) and **DigitalFace** (talkBounce).

**Floppy tip:** an avatar can have a zone above its face that flops on its own (a pizza slice's point): its end is
a weight on a springy rod that lags when you move, swings, and sags further under gravity once tipped over. Set
`Tip` (fraction of the height, above the face) in `GeneratedAvatars`; tuning is on **BendRig** (Floppy tip).

### Webcam face tracking

**MediaPipe (default).** Google's MediaPipe Face Landmarker (Apache-2.0), ported from the Face Object Stream handoff
(`Downloads/FACE_TRACKING_HANDOFF.md`): 478 points, 52 expression scores and the head's rotation matrix per frame.
`tracker/muffin_facetracker.py` reads the app's camera frames on stdin and sends the results back over UDP (port
11574); it's packaged with PyInstaller into `third-party/mediapipe/dist/muffin_facetracker/` and shipped as
`TrackerMP/`. What came across from the handoff:
- turn and nod from the transformation matrix (YXZ), tilt from the eye line; angles against a neutral pose with
  wraparound (the neutral is averaged as directions);
- head-turn control: the avatar follows how your head turns and tilts, not where it is in the picture (tilt leans it);
- adaptive smoothing (`PoseFilter.Adaptive`): follow at 30-72 a second, faster for bigger moves (Feel scales it);
- expressions from blendshapes: jaw open, each blink, smile minus frown, each smile corner (smirk), brows, stretch vs pucker;
- the eyeball and lip cut-outs follow its 16-point eye and 20-point lip outlines (1.08 / 1.06 room), counter-rotated
  against your tilt, with their proportions kept within 0.48-1.12 of natural as you turn.

To rebuild the helper (everything stays in `third-party/mediapipe`): `uv` 0.13.1 with Python 3.12.15 in
`python/`, a venv with `mediapipe==1.1.1` and `pyinstaller==6.22.3`, the model in `models/`, then
`venv/Scripts/python -m PyInstaller --onedir --name muffin_facetracker --collect-all mediapipe tracker/muffin_facetracker.py`
(dist/build/spec under `third-party/mediapipe`). It costs ~11 ms a frame on this PC's CPU. MediaPipe may send
anonymous usage statistics (not images) to Google.

**OpenSeeFace** (F5):

Uses **OpenSeeFace** v1.20.5 (BSD-2; the tracker inside VSeeFace). The app starts `Tracker/Binary/facetracker.exe`
itself (in the editor: `../third-party/OpenSeeFace-v1.20.5`, downloaded, not committed) and reads its 68 face
points. Lean, turn and tilt come from the face centre, eye line and nose (the tip averaged with the points under
it, much steadier than the tip alone), measured in eye-widths, so they don't depend on how far you sit from the camera. The first frame it sees is your neutral pose; press **C** to
re-centre. Ranges and gains are on the **OpenSeeFaceHeadPoseSource** component.

**More than one person:** the tracker watches up to 3 faces. The avatar follows the main face (the biggest, most
centred one, normally whoever sits at the computer); someone leaning in has to be clearly the main face for 2.5 s
before it switches. T picks the next person by hand. Real-face mode shows the cartoon face whenever the followed
face is cut off by the edge of the picture (a half-visible face makes a broken crop).

**Feel:** the webcam pose is smoothed once per tracker update with the real time between updates (`PoseFilter`):
a One Euro filter that holds very still when you do and opens wide the moment you really move. The avatar then only
evens out the tracker's 20-30 Hz steps and follows with a turn spring that has no step "kick". Each Feel level
(`Feel.cs`) is the quickest setup for its shake, found by simulating the whole chain; the self-test measures it
again through the real avatar (`feel.txt`). Balanced covers 90% of a sudden turn in ~0.17 s with less shake than
the earlier heavy setup (~0.25 s).

**Cut-outs in step with the tracker:** the tracker's face points arrive a frame or two after the picture they were
measured on. The app keeps the last 8 camera frames on the GPU, numbered, and the tracker stamps each update with
the moment it read its frame, so every cut-out is taken from the very frame its points came from (`-muffin-nosync`
shows the old way: cut from the live picture, the windows trail your eyes and show cheek when you move).
`-muffin-testface-move` swings the test face side to side to check this.

**Tracker rate:** 30 updates a second (the camera's rate). Two things held it at ~22: the tracker padded each frame
to 1/30 s with a sleep that Windows overshoots (now `-F 0`; the pipe already waits for frames), and only one frame
could be on its way back from the GPU at a time, so camera frames were skipped (now three).

**Steadiness:** the camera is asked for 1080p and the tracker gets 1280×720 frames (a streamer sitting back from a
phone camera is a small face; more pixels, steadier points). Head angles go through a calmer filter than leaning
(BendRig: rotation min cutoff / beta). The real-face windows hang off one steadied face frame (from the four eye
corners, which don't move when you blink), so the eyes don't shake inside their windows. The readout shows the
tracker's update rate.
**Less plastic:** the generated models' shininess maps are made rougher (food mostly not metal) with props-service
`node src/matte.mjs out/<id>.glb --rough-min 0.5 --metal 0.1` after a `batch.mjs build` (metal things: `--rough-min 0.3
--metal 1`), the sky reflected in everything is turned well down and the lights are softer.
**Face parts follow the avatar's front:** at start each avatar maps its front around the face from its triangles
(`FrontSurface`); every frame each corner of every face part (cartoon stickers and real-face cut-outs, which are now
subdivided grids) sits `surfaceGap` (6 mm) in front of the surface under it. Big eyes wrap round a soda can or an egg,
ride over the pizza's bumps, and never cut into the avatar or flicker behind it.
**Head-turn control (default, L for body lean):** head turns, nods and tilts drive it; half of your sideways sway
still leans it (`headTurnSway`), and starting or stopping a turn flings floppy tips (`BendRig.turnWhip`), so the
pizza's point flops about as much as in the first version (self-test: typical 35° against 42°).

**Checking the camera:** `MuffinMotionPrototype.exe -muffin-diagnose <folder>` turns the camera on, waits for a
face, measures shake for 8 s (`jitter.txt`: old vs new filtering on the same live input), saves the F9 diagnostics into
the folder and quits. `-muffin-track 640x360` and `-muffin-faces N` try other tracker settings.

No webcam? The app says so and uses the mouse. A phone works as a webcam with a virtual-camera app.

### Vertical, for TikTok

Menu (M) → Stream → **Vertical 9:16 (TikTok)** makes the window 9:16, as tall as the screen allows (any tall window
works: drag it narrow). In a tall window the camera frames the avatar tightly so it fills the picture, the desk is
laid out compact (smaller keyboard, mouse pad beside it) so both stay in shot, viewer captions sit high up clear of
TikTok's comment feed along the bottom, and the rooms switch to versions drawn for a tall picture. In TikTok LIVE
Studio use a portrait scene and capture this window (green screen or transparent works as in OBS below).

### Into OBS

- **Transparent (best):** pick *Transparent* in the menu (M → Stream) (or press G), press H. In OBS add **Game Capture** →
  "Capture specific window" → the app → tick **Allow Transparency**.
- **Green screen (works everywhere):** pick *Green screen* (or press G), press H. In OBS add **Window Capture** →
  the app → Filters → **Chroma Key** (green).
- Coming: **Spout2** output (direct GPU hand-off, works while minimised).

### Avatars and faces

- Real eyes and mouth are the default (also applied once to existing installations; later face-mode choices
  are remembered). Real-mouth movement is shown without the extra cartoon mouth stretch or mouth-driven
  head squash/stretch. Manual eye/mouth sizing and dramatic zoom still work.
- **Generated avatars** (Gemini concept → Tripo model, `props-service/batches/demo-avatars-01/02/03.json`) live in
  `Assets/Muffin/Avatars/Generated/*.glb` (imported by glTFast 6.20.0). The scene generator bakes each one once:
  merges its meshes, turns it to face the camera (Tripo P1 models face glTF +X, so −90°), scales it to 1.15 m and
  plants it on the ground. Per-avatar settings (root/head/face heights, face size, bend limit) are in
  `GeneratedAvatars` in `Editor/MuffinPrototypeSetup.cs`. `MeshAvatar` hands the mesh to the bend rig and finds the
  front surface for the face.
- **Digital face** (`DigitalFace.cs`): flat sticker eyes, brows, mouth, teeth and blush, reshaped every frame.
  Driven by the tracker (blinks, mouth open/wide, smile, brows) mixed with the hotkey emotion. If smiling shows as
  a frown on the "smile" number (M → Tracking, live numbers), flip `invertSmile` on the webcam component.

## How the smoothness works

| Piece | File | What it does |
|---|---|---|
| Jitter filter | `Scripts/Motion/OneEuro.cs` | One Euro filter: steady when you hold still, no lag when you move fast |
| Springs | `Scripts/Motion/Springs.cs` | Second-order follow-through: overshoot and settle instead of snapping |
| Bend rig | `Scripts/Motion/BendRig.cs` | Bones generated from any mesh's height: rooted base, bending middle, rigid head; squash and stretch keep volume |
| Frame pacing | `Scripts/Prototype/PerfHud.cs` | Vsync to the monitor refresh; shows fps and the worst frame |

Every tuning value (bend amount, spring speeds, damping, filter strength) is in the **BendRig** inspector and can
be changed while playing.

## Automated self-test

`Builds/Win64/MuffinMotionPrototype.exe -muffin-selftest <folder>` plays a scripted movement, saves screenshots at
rest and mid-motion, writes `stats.json` (fps, frame-time percentiles, hitches, errors) and quits.
Batch build: `Unity.exe -batchmode -projectPath MuffinVTuber -executeMethod Muffin.EditorTools.MuffinPrototypeSetup.BuildWindowsBatch`.

## Next

- Global hotkeys (emotions, zoom) that work while a game has focus.
- Spout output to OBS (no window capture).
