/**
 * Shared helpers for the props tools. Keys come from the environment or from
 * Vtube/.ai-keys.local (gitignored), are read per run, and are never printed,
 * logged or written into records. Ported from Death Muffin's tools/ai/common.mjs.
 */
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** props-service/ */
export const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
/** Vtube/, where .ai-keys.local lives. */
export const REPO = join(ROOT, '..');
/** Raw generator outputs: concept PNGs, Tripo GLBs, resume state (gitignored). */
export const RAW = join(ROOT, 'raw');
/** Optimized, Unity-ready GLBs plus manifest.json (gitignored). */
export const OUT = join(ROOT, 'out');
/** Generation records: prompts, task ids, credits, hashes (committed). */
export const RECORDS = join(ROOT, 'records');

/** A key from the environment or .ai-keys.local (copy .ai-keys.example to start one). */
export function loadKey(name) {
  if (process.env[name]) return process.env[name];
  const file = join(REPO, '.ai-keys.local');
  if (!existsSync(file)) throw new Error(`.ai-keys.local not found in the project folder — copy .ai-keys.example to .ai-keys.local and fill in ${name}`);
  for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
    const i = line.indexOf('=');
    if (i > 0 && line.slice(0, i).trim() === name) {
      const v = line.slice(i + 1).trim();
      if (v) return v;
    }
  }
  throw new Error(`${name} missing from .ai-keys.local`);
}

export function ensureDir(p) {
  mkdirSync(p, { recursive: true });
  return p;
}

export function sha256(buf) {
  return 'sha256:' + createHash('sha256').update(buf).digest('hex');
}

export function readJson(path, fallback = null) {
  if (!existsSync(path)) return fallback;
  return JSON.parse(readFileSync(path, 'utf8'));
}

export function writeJson(path, data) {
  ensureDir(dirname(path));
  writeFileSync(path, JSON.stringify(data, null, 2) + '\n');
}

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * Proof of commercial rights: the Tripo plan declared in licensing.json, stamped onto every Tripo task
 * record when the task is created. Free-plan outputs belong to Tripo and can't go into a product we sell,
 * so anything other than a paid plan stops the spend.
 */
export function checkTripoLicense(licensing) {
  const t = licensing?.tripo;
  if (t?.plan !== 'paid') {
    throw new Error('licensing.json must declare tripo.plan "paid" before spending Tripo credits (free-plan models cannot be sold)');
  }
  return { plan: t.plan, tier: t.tier ?? null, declaredAt: t.declaredAt ?? null, stampedAt: new Date().toISOString() };
}

export const tripoLicense = () => checkTripoLicense(readJson(join(ROOT, 'licensing.json')));

/** Redact anything key-shaped from error text before it reaches the console. */
export function redact(text) {
  return String(text).replace(/(key=|Bearer\s+)[A-Za-z0-9_\-.]+/gi, '$1[redacted]');
}

/** A copy with the signatures cut off every link (Tripo's download links are signed): for committed records. */
export function unsigned(value) {
  if (typeof value === 'string') return /^https?:\/\/[^?]*\?/.test(value) ? value.slice(0, value.indexOf('?')) : value;
  if (Array.isArray(value)) return value.map(unsigned);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, unsigned(v)]));
  return value;
}
