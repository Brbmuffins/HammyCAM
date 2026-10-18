/**
 * batch.mjs — run a batch of props through concept → model → build, one stage
 * at a time, so you can look at the concept pictures before paying for models.
 * These are the same stages the live props service will run per chat redemption.
 *
 *   node src/batch.mjs plan     <batch.json> [id …]          what would run + estimated credits (spends nothing)
 *   node src/batch.mjs concepts <batch.json> [id …] --yes    Gemini concept images → raw/concepts/<id>.png
 *   node src/batch.mjs models   <batch.json> [id …] --yes    Tripo image → model, + rig for avatars (resumable, never pays twice);
 *                                                            props marked "model": "spar3d" are skipped (third-party/spar3d/run-spar3d.ps1)
 *   node src/batch.mjs build    <batch.json> [id …]          optimize → out/<id>.glb + out/manifest.json (free)
 *   node src/batch.mjs balance                               Tripo credit balance (free)
 *
 * Add --force to `concepts` to redraw an existing picture.
 */
import { existsSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { buildProp } from './build.mjs';
import { ensureDir, OUT, RAW, readJson, RECORDS, redact, sha256, tripoLicense, writeJson } from './common.mjs';
import { GEMINI_MODEL, generateImage } from './gemini.mjs';
import { conceptAspect, conceptPrompt, SLOTS } from './prompts.mjs';
import { AVATAR_RIG, balance, generate, MODEL_CREDITS, modelPath, RIG_CREDITS, rigPath } from './tripo.mjs';

const conceptPath = (id) => join(RAW, 'concepts', `${id}.png`);
const isAvatar = (p) => p.slot === 'avatar';
/** `"model": "spar3d"` props are modelled for free by third-party/spar3d/run-spar3d.ps1, never by Tripo. */
const isSpar3d = (p) => p.model === 'spar3d';
/** The raw file a prop or avatar ships from: the avatar needs its rigged version. */
const finalModel = (p) => (isSpar3d(p) ? join(RAW, 'spar3d', p.id, 'mesh.glb') : isAvatar(p) ? rigPath(p.id) : modelPath(p.id));
const kb = (n) => `${(n / 1024).toFixed(0)}KB`;

async function plan(batch, props) {
  const quality = batch.generation?.texture_quality ?? 'standard';
  const each = MODEL_CREDITS[quality] ?? MODEL_CREDITS.standard;
  let concepts = 0;
  let models = 0;
  let credits = 0;
  for (const p of props) {
    const c = existsSync(conceptPath(p.id));
    const m = existsSync(finalModel(p));
    const b = existsSync(join(OUT, `${p.id}.glb`));
    if (!c) concepts++;
    if (!m && !isSpar3d(p)) {
      models++;
      credits += each + (isAvatar(p) ? RIG_CREDITS : 0);
    }
    const tag = p.mood ? ` (${p.mood})` : '';
    console.log(`${(p.id + tag).padEnd(26)} ${p.slot.padEnd(9)} concept ${c ? 'yes' : ' — '}  model ${m ? 'yes' : ' — '}  built ${b ? 'yes' : ' — '}`);
  }
  console.log(
    `\nStill to make: ${concepts} concept picture(s) with Gemini (${GEMINI_MODEL}), ` +
      `${models} model(s) ≈ ${credits} Tripo credits (${quality} texture, ${each} each; avatars +${RIG_CREDITS} for the rig).\nNothing spent.`,
  );
  try {
    const lic = tripoLicense();
    console.log(`Tripo plan on record: ${lic.plan}${lic.tier ? ` (${lic.tier})` : ''}, declared ${lic.declaredAt}; stamped on every task.`);
  } catch (err) {
    console.log(`Tripo models are blocked: ${err.message}`);
  }
}

async function concepts(batch, props, force) {
  const recordsPath = join(RECORDS, 'concepts.json');
  const records = readJson(recordsPath, {});
  for (const p of props) {
    const out = conceptPath(p.id);
    if (existsSync(out) && !force) {
      console.log(`skip ${p.id} (exists)`);
      continue;
    }
    const prompt = conceptPrompt(p, batch.style);
    process.stdout.write(`gen  ${p.id} … `);
    try {
      const { png, model } = await generateImage(prompt, { aspect: conceptAspect(p) });
      ensureDir(dirname(out));
      writeFileSync(out, png);
      records[p.id] = { batch: batch.id, slot: p.slot, mood: p.mood ?? null, model, prompt, hash: sha256(png), generatedAt: new Date().toISOString() };
      writeJson(recordsPath, records);
      console.log(`ok (${kb(png.length)})`);
    } catch (err) {
      console.log(`FAILED: ${redact(err.message)}`);
    }
  }
  console.log('\nLook at raw/concepts/ before running models: redrawing a bad picture costs nothing from Tripo.');
}

async function models(batch, props) {
  tripoLicense(); // no paid plan on record → stop before anything is uploaded or spent
  const before = await balance();
  for (const p of props) {
    if (isSpar3d(p)) {
      console.log(`skip ${p.id} (SPAR3D model: run third-party/spar3d/run-spar3d.ps1 -Ids ${p.id})`);
      continue;
    }
    if (!existsSync(conceptPath(p.id))) {
      console.log(`skip ${p.id} (no concept picture yet)`);
      continue;
    }
    console.log(`\n== ${p.id}`);
    try {
      const rig = isAvatar(p) ? { rig: batch.rig ?? AVATAR_RIG } : {};
      const r = await generate({ id: p.id, input: conceptPath(p.id), generation: batch.generation, ...rig });
      console.log(`  ok, ${r.credits} credits for ${p.id} in total`);
    } catch (err) {
      console.log(`  FAILED: ${redact(err.message)}`);
    }
  }
  const after = await balance();
  console.log(`\ncredits: ${before.balance} → ${after.balance} (spent ${(before.balance - after.balance).toFixed(2)}, frozen ${after.frozen})`);
}

async function build(props) {
  const manifestPath = join(OUT, 'manifest.json');
  const manifest = readJson(manifestPath, {});
  for (const p of props) {
    const raw = finalModel(p);
    if (!existsSync(raw)) {
      console.log(`skip ${p.id} (no model yet)`);
      continue;
    }
    try {
      const r = await buildProp(raw, join(OUT, `${p.id}.glb`));
      manifest[p.id] = { slot: p.slot, mood: p.mood ?? null, file: `${p.id}.glb`, tris: r.tris, bytes: r.bytes };
      console.log(`${p.id.padEnd(16)} ${kb(statSync(raw).size)} → ${kb(r.bytes)}  tris ${r.tris}`);
    } catch (err) {
      console.log(`${p.id.padEnd(16)} FAILED: ${err.message}`);
    }
  }
  writeJson(manifestPath, manifest);
}

async function main() {
  const args = process.argv.slice(2);
  const flags = new Set(args.filter((a) => a.startsWith('--')));
  const [cmd, file, ...ids] = args.filter((a) => !a.startsWith('--'));
  if (cmd === 'balance') {
    console.log(await balance());
    return;
  }
  if (!file || !['plan', 'concepts', 'models', 'build'].includes(cmd)) {
    console.error('usage: node src/batch.mjs plan|concepts|models|build <batch.json> [id …] [--yes] [--force]  |  balance');
    process.exit(1);
  }
  const batch = readJson(resolve(file));
  if (!batch) throw new Error(`batch not found: ${file}`);
  for (const p of batch.props) {
    if (!SLOTS[p.slot]) throw new Error(`${p.id}: unknown slot "${p.slot}" (${Object.keys(SLOTS).join(', ')})`);
  }
  const props = ids.length ? batch.props.filter((p) => ids.includes(p.id)) : batch.props;

  if (cmd === 'plan') return plan(batch, props);
  if (cmd === 'build') return build(props);
  if (!flags.has('--yes')) {
    await plan(batch, props);
    console.log(`\n\`${cmd}\` spends money (${cmd === 'concepts' ? 'Gemini' : 'Tripo credits'}). Re-run with --yes.`);
    return;
  }
  if (cmd === 'concepts') return concepts(batch, props, flags.has('--force'));
  return models(batch, props);
}

main().catch((e) => {
  console.error('\n' + redact(e.message ?? e));
  process.exit(1);
});
