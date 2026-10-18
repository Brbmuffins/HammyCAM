/**
 * Gemini image generation: concept pictures for Tripo, plus previews such as
 * stream portraits. Ported from Death Muffin's tools/ai/gemini.mjs, trimmed to
 * prompt + optional reference images in, one PNG out.
 *
 * A refusal throws `no image: <reason>`. That makes Gemini's own safety filter
 * a free first moderation pass before any Tripo credits are spent.
 *
 * CLI (previews and other one-off pictures; every output is recorded in records/images.json):
 *   node src/gemini.mjs <jobs.json> [jobId …] [--force]
 *   jobs.json: [{ id, prompt, out, refs?: [path], aspect?: "1:1" }]  (paths relative to props-service/)
 */
import sharp from 'sharp';
import { existsSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ensureDir, loadKey, readJson, RECORDS, redact, ROOT, sha256, sleep, writeJson } from './common.mjs';

export const GEMINI_MODEL = 'gemini-3.1-flash-image';
const API = 'https://generativelanguage.googleapis.com/v1beta/models';

let KEY = null;
const key = () => (KEY ??= loadKey('GEMINI_API_KEY'));

/** A reference image (e.g. the avatar's T-pose drawing) keeps a redraw on-model. */
async function refPart(path) {
  const buf = await sharp(path).png().toBuffer();
  return { inline_data: { mime_type: 'image/png', data: buf.toString('base64') } };
}

export async function generateImage(prompt, { aspect = '1:1', model = GEMINI_MODEL, refs = [] } = {}) {
  const body = {
    contents: [{ role: 'user', parts: [{ text: prompt }, ...(await Promise.all(refs.map(refPart)))] }],
    generationConfig: { responseModalities: ['IMAGE'], imageConfig: { aspectRatio: aspect } },
  };
  for (let attempt = 1; attempt <= 4; attempt++) {
    const res = await fetch(`${API}/${model}:generateContent`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'x-goog-api-key': key() },
      body: JSON.stringify(body),
    });
    const json = await res.json().catch(() => ({}));
    if (res.status === 429 || res.status >= 500) {
      console.warn(`  HTTP ${res.status}, retry ${attempt}`);
      await sleep(4000 * attempt);
      continue;
    }
    if (!res.ok) throw new Error(`HTTP ${res.status}: ${redact(JSON.stringify(json.error ?? json)).slice(0, 400)}`);
    const out = json.candidates?.[0]?.content?.parts?.find((p) => p.inlineData || p.inline_data);
    const data = out?.inlineData?.data ?? out?.inline_data?.data;
    if (!data) {
      const reason = json.candidates?.[0]?.finishReason ?? json.promptFeedback?.blockReason ?? 'no image returned';
      throw new Error(`no image: ${reason}`);
    }
    // Tripo's upload picks the MIME type from the extension, so always hand it a real PNG.
    return { png: await sharp(Buffer.from(data, 'base64')).png().toBuffer(), model };
  }
  throw new Error('gave up after retries');
}

async function runJobs(jobsFile, ids, force) {
  const jobs = readJson(resolve(jobsFile));
  if (!jobs) throw new Error(`jobs not found: ${jobsFile}`);
  const recordsPath = join(RECORDS, 'images.json');
  const records = readJson(recordsPath, {});
  for (const job of jobs) {
    if (ids.length && !ids.includes(job.id)) continue;
    const out = join(ROOT, job.out);
    if (existsSync(out) && !force) {
      console.log(`skip ${job.id} (exists)`);
      continue;
    }
    process.stdout.write(`gen  ${job.id} … `);
    try {
      const refs = (job.refs ?? []).map((r) => join(ROOT, r));
      const { png, model } = await generateImage(job.prompt, { aspect: job.aspect, refs });
      ensureDir(dirname(out));
      writeFileSync(out, png);
      records[job.id] = { model, prompt: job.prompt, refs: job.refs ?? [], aspect: job.aspect ?? '1:1', out: job.out, hash: sha256(png), generatedAt: new Date().toISOString() };
      writeJson(recordsPath, records);
      console.log(`ok (${(png.length / 1024).toFixed(0)}KB)`);
    } catch (err) {
      console.log(`FAILED: ${redact(err.message)}`);
    }
  }
}

const isCli = process.argv[1] && resolve(process.argv[1]).toLowerCase() === fileURLToPath(import.meta.url).toLowerCase();
if (isCli) {
  const args = process.argv.slice(2);
  const [jobsFile, ...ids] = args.filter((a) => !a.startsWith('--'));
  if (!jobsFile) {
    console.error('usage: node src/gemini.mjs <jobs.json> [jobId …] [--force]');
    process.exit(1);
  }
  runJobs(jobsFile, ids, args.includes('--force')).catch((e) => {
    console.error(redact(e.message ?? e));
    process.exit(1);
  });
}
