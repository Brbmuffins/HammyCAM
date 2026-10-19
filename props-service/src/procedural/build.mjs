/**
 * Builds code-made avatars: each avatars/<id>.mjs default-exports build(kit) returning
 * { parts: [{ mesh, material }], face: 0..1 (height of the face), faceSize, notes? }.
 * Writes MuffinVTuber/Assets/Muffin/Avatars/Generated/<id>.glb and a preview, raw/procedural/<id>.png.
 *
 *   node src/procedural/build.mjs pillow volcano      (or: all)
 */
import { readdirSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import * as kit from './kit.mjs';
import { previewPng } from './preview.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const GENERATED = join(HERE, '..', '..', '..', 'MuffinVTuber', 'Assets', 'Muffin', 'Avatars', 'Generated');
const PREVIEWS = join(HERE, '..', '..', 'raw', 'procedural');

let ids = process.argv.slice(2);
if (ids.length === 0 || ids[0] === 'all') ids = readdirSync(join(HERE, 'avatars')).filter((f) => f.endsWith('.mjs')).map((f) => f.slice(0, -4));
mkdirSync(PREVIEWS, { recursive: true });

for (const id of ids) {
  const module = await import(pathToFileURL(join(HERE, 'avatars', `${id}.mjs`)).href);
  const model = module.default(kit);
  const glb = kit.writeGlb(model.parts, { name: id, crease: model.crease ?? 50 });
  writeFileSync(join(GENERATED, `${id}.glb`), glb);
  writeFileSync(join(PREVIEWS, `${id}.png`), previewPng(glb, { face: model.face, faceSize: model.faceSize ?? 1 }));
  const verts = model.parts.reduce((s, p) => s + p.mesh.vertexCount, 0), tris = model.parts.reduce((s, p) => s + p.mesh.indices.length / 3, 0);
  const all = new kit.Mesh();
  for (const p of model.parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  // Key points (crater, nozzle, chute...) in the app's avatar space: x flipped (glTFast), centred, standing on
  // y = 0, scaled to 1.15 m. The front is -Z there too.
  const s = 1.15 / (max[1] - min[1]), cx = (min[0] + max[0]) / 2, cz = (min[2] + max[2]) / 2;
  const anchors = Object.fromEntries(Object.entries(model.anchors ?? {}).map(([k, [x, y, z]]) =>
    [k, [-(x - cx) * s, (y - min[1]) * s, (z - cz) * s].map((v) => +v.toFixed(3))]));
  const size = [(max[0] - min[0]) * s, 1.15, (max[2] - min[2]) * s].map((v) => +v.toFixed(3));
  writeFileSync(join(PREVIEWS, `${id}.json`), JSON.stringify({ face: model.face, faceSize: model.faceSize, size, anchors, notes: model.notes }, null, 2));
  if (Object.keys(anchors).length) console.log(`  anchors (app space, metres): ${JSON.stringify(anchors)}`);
  console.log(`${id}: ${verts} vertices, ${tris} triangles, ${(max[0] - min[0]).toFixed(2)} x ${(max[1] - min[1]).toFixed(2)} x ${(max[2] - min[2]).toFixed(2)} m, ` +
    `${(glb.length / 1024).toFixed(0)} KB -> Generated/${id}.glb, raw/procedural/${id}.png`);
}
