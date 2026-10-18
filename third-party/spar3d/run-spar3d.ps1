# Runs SPAR3D on CPU for the given concept ids; outputs land in props-service/raw/spar3d/<id>/.
param([string[]]$Ids = @('cherry_cupcake', 'pizza_slice'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$vt = (Resolve-Path "$root\..\..").Path   # the project folder
$models = "$root\models"
New-Item -ItemType Directory -Force "$models\hf", "$models\alpha_clip" | Out-Null

# Hugging Face token (SPAR3D is gated): HF_TOKEN from the environment, .ai-keys.local, or the older HuggingFaceToken.txt.
if (-not $env:HF_TOKEN) {
  $line = if (Test-Path "$vt\.ai-keys.local") { Get-Content "$vt\.ai-keys.local" | Where-Object { $_ -match '^\s*HF_TOKEN\s*=\s*\S' } | Select-Object -First 1 }
  if ($line) { $env:HF_TOKEN = ($line -split '=', 2)[1].Trim() }
  elseif (Test-Path "$vt\HuggingFaceToken.txt") { $env:HF_TOKEN = (Get-Content "$vt\HuggingFaceToken.txt" -Raw).Trim() }
  else { throw 'no Hugging Face token: add HF_TOKEN=... to .ai-keys.local' }
}
$env:HF_HOME = "$models\hf"
$env:ALPHA_CLIP_PATH = "$models\alpha_clip"
$env:TRANSPARENT_BACKGROUND_FILE_PATH = $models
$env:SPAR3D_USE_CPU = '1'
$env:NO_ALBUMENTATIONS_UPDATE = '1'
$env:PYTHONWARNINGS = 'ignore'

$runDir = "$vt\props-service\raw\spar3d\_run"
if (Test-Path $runDir) { Remove-Item -Recurse -Force $runDir }
$inputs = @($Ids | ForEach-Object { "$vt\props-service\raw\concepts\$_.png" })   # @(): one id must stay a list

$sw = [Diagnostics.Stopwatch]::StartNew()
# cmd does the redirect: PowerShell 5.1 would turn Python's stderr warnings into terminating errors.
$log = "$root\last-run.log"
$quoted = ($inputs | ForEach-Object { "`"$_`"" }) -join ' '
cmd /c "`"$root\venv\Scripts\python.exe`" -u `"$root\repo\run.py`" $quoted --device cpu --output-dir `"$runDir`" --texture-resolution 1024 > `"$log`" 2>&1"
if ($LASTEXITCODE -ne 0) { Get-Content $log -Tail 30; throw "run.py exited $LASTEXITCODE" }
"total seconds: $([math]::Round($sw.Elapsed.TotalSeconds))"

$commit = (git -C "$root\repo" rev-parse --short HEAD)
New-Item -ItemType Directory -Force "$vt\props-service\records\spar3d" | Out-Null
for ($i = 0; $i -lt $Ids.Count; $i++) {
  $dest = "$vt\props-service\raw\spar3d\$($Ids[$i])"
  New-Item -ItemType Directory -Force $dest | Out-Null
  Copy-Item "$runDir\$i\*" $dest -Force
  Get-ChildItem $dest | Select-Object Name, Length | Format-Table -AutoSize | Out-String
  # Provenance, like records/tripo/<id>.json: what made the model, from which picture, under which licence.
  [ordered]@{
    id = $Ids[$i]
    model = 'stabilityai/stable-point-aware-3d'
    code = "github.com/Stability-AI/stable-point-aware-3d@$commit (+ local CPU patch)"
    settings = @{ device = 'cpu'; textureResolution = 1024; remesh = 'none' }
    input = "raw/concepts/$($Ids[$i]).png"
    inputSha256 = (Get-FileHash $inputs[$i] -Algorithm SHA256).Hash.ToLower()
    meshSha256 = (Get-FileHash "$dest\mesh.glb" -Algorithm SHA256).Hash.ToLower()
    license = 'Stability AI Community License: free under US $1M annual revenue; outputs owned by us; register at stability.ai/community-license before commercial use'
    generatedAt = (Get-Date).ToString('o')
  } | ConvertTo-Json | Set-Content -Encoding utf8 "$vt\props-service\records\spar3d\$($Ids[$i]).json"
}
