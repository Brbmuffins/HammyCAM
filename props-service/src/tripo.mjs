/**
 * Tripo v3: image → model for props, plus rig-check → rig for the avatar.
 * Ported from Death Muffin's tools/ai/tripo.mjs (same retries, rate-limit
 * handling and create-or-resume steps), minus animation retargeting: the
 * avatar is driven live by face/body tracking, not by preset clips.
 *
 * Resume rule: each step's task id is saved to raw/tripo/<id>/state.json
 * before polling, so a rerun after a crash or timeout waits on the same task
 * and never pays twice. Failed tasks refund on Tripo's side (credits freeze,
 * then return).
 */
import { createWriteStream, existsSync, readFileSync } from 'node:fs';
import { basename, join, relative } from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { ensureDir, loadKey, RAW, readJson, RECORDS, redact, ROOT, sha256, sleep, tripoLicense, unsigned, writeJson } from './common.mjs';

const BASE = 'https://openapi.tripo3d.ai/v3';
export const TRIPO_MODEL = 'P1-20260311';
/** Image → model cost measured in Death Muffin (Sept 2026), by texture_quality. */
export const MODEL_CREDITS = { standard: 50, detailed: 60 };
/** Biped rig, also measured in Death Muffin (rig-check is free). Avatars need it; props don't. */
export const RIG_CREDITS = 25;
/**
 * Avatar rig: v1.0 is Tripo's biped rig. Mixamo bone names (Death Muffin used Tripo's own) are the
 * ones Unity's Humanoid importer maps automatically, so the tracking code can drive standard bones.
 */
export const AVATAR_RIG = { model: 'v1.0-20240301', rig_type: 'biped', spec: 'mixamo' };

let KEY = null;
const key = () => (KEY ??= loadKey('TRIPO_API_KEY'));

async function api(method, path, body, isForm = false) {
  for (let attempt = 1; attempt <= 5; attempt++) {
    const res = await fetch(`${BASE}${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${key()}`,
        ...(body && !isForm ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body ? (isForm ? body : JSON.stringify(body)) : undefined,
    });
    const json = await res.json().catch(() => ({}));
    if (res.status === 429) {
      const reset = Number(res.headers.get('X-RateLimit-Reset'));
      const wait = reset ? Math.max(2000, reset * 1000 - Date.now()) : 5000 * attempt;
      console.warn(`  rate limited, waiting ${(wait / 1000).toFixed(0)}s`);
      await sleep(wait);
      continue;
    }
    if (res.status >= 500 && attempt < 5) {
      await sleep(3000 * attempt);
      continue;
    }
    if (!res.ok || json.code !== 0) {
      throw new Error(`${method} ${path} → HTTP ${res.status} code ${json.code}: ${redact(json.message ?? JSON.stringify(json)).slice(0, 300)}`);
    }
    return json.data;
  }
  throw new Error(`${method} ${path}: gave up after retries`);
}

async function upload(path) {
  const buf = readFileSync(path);
  const form = new FormData();
  const type = path.endsWith('.jpg') || path.endsWith('.jpeg') ? 'image/jpeg' : 'image/png';
  form.append('file', new Blob([buf], { type }), basename(path));
  const data = await api('POST', '/files', form, true);
  return { fileToken: data.file_token, hash: sha256(buf) };
}

async function waitTask(taskId, label, timeoutMs = 20 * 60 * 1000) {
  const start = Date.now();
  let last = -1;
  while (Date.now() - start < timeoutMs) {
    const t = await api('GET', `/tasks/${taskId}`);
    if (t.progress !== last) {
      process.stdout.write(`\r  ${label}: ${t.status} ${t.progress ?? 0}%   `);
      last = t.progress;
    }
    if (t.status === 'success') {
      process.stdout.write('\n');
      return t;
    }
    if (['failed', 'cancelled', 'banned', 'expired', 'unknown'].includes(t.status)) {
      process.stdout.write('\n');
      throw new Error(`${label} task ${taskId} ended: ${t.status} ${t.error_msg ?? t.message ?? ''}`);
    }
    await sleep(3000);
  }
  throw new Error(`${label} task ${taskId} timed out (still running — rerun to resume)`);
}

async function download(url, dest) {
  const res = await fetch(url);
  if (!res.ok || !res.body) throw new Error(`download failed ${res.status}: ${dest}`);
  await pipeline(Readable.fromWeb(res.body), createWriteStream(dest));
  return dest;
}

export const balance = () => api('GET', '/account/balance');

/** Unrigged model (what props ship from). */
export const modelPath = (id) => join(RAW, 'tripo', id, 'model.glb');
/** Rigged model (what the avatar ships from). */
export const rigPath = (id) => join(RAW, 'tripo', id, 'rig.glb');

class Run {
  /** spec: { id, input (concept PNG path), generation, rig? } */
  constructor(spec) {
    this.spec = spec;
    this.dir = ensureDir(join(RAW, 'tripo', spec.id));
    this.statePath = join(this.dir, 'state.json');
    this.state = readJson(this.statePath, { id: spec.id, steps: {} });
  }

  credits() {
    return Object.values(this.state.steps).reduce((n, s) => n + (s.credits ?? 0), 0);
  }

  save() {
    writeJson(this.statePath, this.state);
    const { id, input, ...rest } = this.spec;
    writeJson(join(RECORDS, 'tripo', `${id}.json`), {
      id,
      ...rest,
      // The plan the model itself was generated under; each step also carries its own stamp.
      license: this.state.steps.generate?.license ?? null,
      steps: unsigned(this.state.steps), // records are committed: no signed download links in them
      totalCredits: this.credits(),
    });
  }

  /** Create-or-resume one task step. `create` returns the POST path + body. */
  async step(name, create, outFile) {
    const st = (this.state.steps[name] ??= {});
    if (st.status === 'success' && (!outFile || existsSync(join(this.dir, outFile)))) {
      console.log(`  ${name}: done (${st.taskId})`);
      return st;
    }
    if (!st.taskId || st.status === 'failed') {
      // Checked right before every paid task, so a plan change mid-batch is caught and recorded.
      const license = tripoLicense();
      const { path, body } = create();
      const data = await api('POST', path, body);
      Object.assign(st, { taskId: data.task_id, status: 'created', request: body, createdAt: new Date().toISOString(), license });
      this.save();
    }
    try {
      const t = await waitTask(st.taskId, `${this.spec.id} ${name}`);
      st.status = 'success';
      st.credits = t.credits_consumed ?? t.consumed_credit ?? null;
      st.output = t.output;
      st.finishedAt = new Date().toISOString();
      if (outFile) {
        if (!t.output?.model_url) throw new Error(`${name} finished without a model_url`);
        const f = join(this.dir, outFile);
        await download(t.output.model_url, f);
        st.fileHash = sha256(readFileSync(f));
      }
      if (t.output?.rendered_image_url && !existsSync(join(this.dir, `${name}-preview.png`))) {
        await download(t.output.rendered_image_url, join(this.dir, `${name}-preview.png`)).catch(() => {});
      }
    } catch (err) {
      if (/ended: failed|ended: banned/.test(err.message)) st.status = 'failed';
      st.error = err.message;
      this.save();
      throw err;
    }
    this.save();
    return st;
  }

  async go() {
    const s = this.spec;
    tripoLicense(); // fail before the upload, not halfway through a batch
    // 1. upload the concept picture
    const up = (this.state.steps.upload ??= {});
    if (!up.fileToken) {
      if (!existsSync(s.input)) throw new Error(`concept image missing: ${relative(ROOT, s.input)}`);
      Object.assign(up, await upload(s.input), { status: 'success', input: relative(ROOT, s.input).replace(/\\/g, '/') });
      this.save();
    }
    // 2. image → model
    const gen = await this.step(
      'generate',
      () => ({ path: '/generation/image-to-model', body: { input: up.fileToken, texture: true, pbr: true, model: TRIPO_MODEL, ...s.generation } }),
      'model.glb',
    );
    if (!s.rig) return;
    // 3. rig check (free), then rig
    const check = await this.step('rigCheck', () => ({ path: '/animations/rig-check', body: { input: gen.taskId } }));
    if (check.output?.riggable === false) {
      throw new Error(`${s.id}: not riggable (${JSON.stringify(check.output)}) — redraw the concept (clearer T-pose, separated limbs)`);
    }
    if (check.output?.rig_type && check.output.rig_type !== s.rig.rig_type) {
      console.warn(`  rig-check suggests ${check.output.rig_type}, spec asks for ${s.rig.rig_type}`);
    }
    await this.step('rig', () => ({ path: '/animations/rig', body: { input: gen.taskId, out_format: 'glb', ...s.rig } }), 'rig.glb');
  }
}

/** Build one prop (model only) or the avatar (model + rig). Returns the credits this id has used in total. */
export async function generate(spec) {
  const run = new Run(spec);
  await run.go();
  return { credits: run.credits() };
}
