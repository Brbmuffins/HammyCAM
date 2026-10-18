# SPAR3D (local, CPU)

Stability AI's image-to-3D model, run on this PC's CPU for free. Stability AI Community License: free under
US $1M annual revenue; register at stability.ai/community-license before any commercial use.

Only this README, `run-spar3d.ps1` and the patch are committed; the rest is set up again as below.

- `repo/` — github.com/Stability-AI/stable-point-aware-3d @ fdc311b, with one local patch
  (`git -C repo apply ../cpu-half-precision.patch`): the AlphaCLIP call no longer casts to half precision on CPU
  (it crashed with "expected scalar type Half").
- `venv/` — Python 3.12 (uv), torch 2.5.1 CPU, texture_baker + uv_unwrapper compiled with VS 2022 Build Tools
  (no OpenMP under MSVC, so baking is single-threaded), gpytoolbox 0.3.8 wheel (run.py crashes without a remesher),
  flet 0.24.1 (newer flet breaks transparent-background).
- `models/` — weights: SPAR3D (gated: accept its terms on Hugging Face; token as `HF_TOKEN` in `.ai-keys.local`),
  DINOv2, AlphaCLIP/CLIP, InSPyReNet. Downloaded on the first run.

Run: `powershell -File run-spar3d.ps1 -Ids cherry_cupcake,pizza_slice` → `props-service/raw/spar3d/<id>/mesh.glb`
(input `props-service/raw/concepts/<id>.png`). About 6 min per model. Models face glTF −Z (Tripo P1 faces +X).
Compare with Tripo: serve `props-service/raw` (launch config "props-raw") and open `compare-spar3d.html`.
