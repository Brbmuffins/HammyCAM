/**
 * sfx.mjs — sound effects for the app (splats, sizzles, bonks…) with ElevenLabs' text-to-sound-effect.
 *
 *   node src/sfx.mjs <jobs.json> [id …] [--force]
 *   jobs.json: [{ id, prompt, seconds?, loop?, influence? }]  →  raw/sfx/<id>.mp3, recorded in records/sfx.json
 *
 * Existing outputs are skipped unless --force (each generation spends ElevenLabs credits). The key
 * (ELEVENLABS_API_KEY in .ai-keys.local, gitignored) is read on every run and never printed.
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { ensureDir, loadKey, RAW, readJson, RECORDS, redact, REPO, sha256, sleep, writeJson } from './common.mjs';

const API = 'https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128';
let KEY = null;
function key() {
  if (KEY) return KEY;
  // ELEVENLABS_API_KEY (environment or .ai-keys.local), or the older ElevenLabsAPI.txt.
  const old = join(REPO, 'ElevenLabsAPI.txt');
  const match = existsSync(old) && readFileSync(old, 'utf8').match(/sk_[A-Za-z0-9]+/);
  return (KEY = match ? match[0] : loadKey('ELEVENLABS_API_KEY'));
}

async function generate(job) {
  const body = { text: job.prompt, prompt_influence: job.influence ?? 0.5 };
  if (job.seconds) body.duration_seconds = job.seconds;
  if (job.loop) body.loop = true;
  for (let attempt = 1; attempt <= 4; attempt++) {
    const res = await fetch(API, {
      method: 'POST',
      headers: { 'xi-api-key': key(), 'Content-Type': 'application/json', Accept: 'audio/mpeg' },
      body: JSON.stringify(body),
    });
    if (res.ok) return Buffer.from(await res.arrayBuffer());
    const text = redact(await res.text()).replace(/sk_[A-Za-z0-9]+/g, "[redacted]").slice(0, 300);
    // An older model may not know "loop": ask again without it.
    if ((res.status === 400 || res.status === 422) && body.loop && /loop/i.test(text)) {
      delete body.loop;
      continue;
    }
    if (res.status === 429 || res.status >= 500) {
      await sleep(2500 * attempt);
      continue;
    }
    throw new Error(`ElevenLabs ${res.status}: ${text}`);
  }
  throw new Error('ElevenLabs: gave up after retries');
}

const args = process.argv.slice(2);
const force = args.includes('--force');
const [jobsPath, ...only] = args.filter((a) => !a.startsWith('--'));
if (!jobsPath) {
  console.log('usage: node src/sfx.mjs <jobs.json> [id …] [--force]');
  process.exit(1);
}
const jobs = readJson(resolve(jobsPath), []).filter((j) => only.length === 0 || only.includes(j.id));
const recordsPath = join(RECORDS, 'sfx.json');
const records = readJson(recordsPath, {});
ensureDir(join(RAW, 'sfx'));
let made = 0;
for (const job of jobs) {
  const out = join(RAW, 'sfx', `${job.id}.mp3`);
  if (existsSync(out) && !force) {
    console.log(`skip ${job.id} (exists)`);
    continue;
  }
  process.stdout.write(`gen  ${job.id} … `);
  try {
    const audio = await generate(job);
    writeFileSync(out, audio);
    records[job.id] = { prompt: job.prompt, seconds: job.seconds ?? null, loop: !!job.loop, hash: `sha256:${sha256(audio)}`, generatedAt: new Date().toISOString() };
    writeJson(recordsPath, records);
    made++;
    console.log(`ok (${(audio.length / 1024).toFixed(0)}KB)`);
  } catch (err) {
    console.log(`FAILED: ${err.message}`);
  }
}
console.log(`\n${made} sound(s) made.`);
