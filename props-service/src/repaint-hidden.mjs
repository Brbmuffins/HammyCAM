/**
 * Repaints the parts of a Tripo model's body that its separate arm pieces hide at rest. Tripo paints the arm's
 * colour and shading onto the body underneath; once the app's stretchy arms (BendRig.AddStretchArm) reach away,
 * those smudges show. The hidden area (body surface within --near metres of an arm piece, or behind one seen from
 * the front, measured at the app's 1.15 m height) is filled on the surface from the fur round it (colour; the normal
 * map is flattened there) and the GLB is rewritten. 4 cm suits the hamster: more pulls shoulder fur into blobs.
 *
 *   node src/repaint-hidden.mjs <model.glb> [--near 0.04] [--front +x] [--debug out.png] [--dry]
 *
 * Arms = the pieces (other than the body, the largest) whose bounds sit in the arm zone: middle height, toward
 * the front. Prints what it found; --debug writes the colour texture with the hidden area in red.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { readGlb, decodePng as decodeRaw, encodePng } from './procedural/kit.mjs';

const decodePng = (bytes) => { const d = decodeRaw(bytes); return { width: d.width, height: d.height, data: d.px }; };

const args = process.argv.slice(2);
const file = args.find((a) => !a.startsWith('--') && !/^\d/.test(a));
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : fallback;
};
const NEAR = Number(opt('--near', 0.04));
const debug = opt('--debug', null);
const dry = args.includes('--dry');
if (!file) throw new Error('usage: node src/repaint-hidden.mjs <model.glb> [--near m] [--debug out.png] [--dry]');

const buf = readFileSync(file);
const { gltf, bin } = readGlb(buf);

// ---------- read the one mesh (positions, uvs, triangles), in world units scaled to 1.15 m ----------
const SIZES = { 5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4 };
const COUNT = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };
function read(index) {
  const acc = gltf.accessors[index], bv = gltf.bufferViews[acc.bufferView];
  const n = COUNT[acc.type], size = SIZES[acc.componentType];
  const stride = bv.byteStride ?? n * size;
  const base = (bv.byteOffset ?? 0) + (acc.byteOffset ?? 0);
  const out = new Float64Array(acc.count * n);
  const view = new DataView(bin.buffer, bin.byteOffset, bin.byteLength);
  const max = { 5120: 127, 5121: 255, 5122: 32767, 5123: 65535 }[acc.componentType];
  for (let i = 0; i < acc.count; i++)
    for (let k = 0; k < n; k++) {
      const o = base + i * stride + k * size;
      let v;
      switch (acc.componentType) {
        case 5126: v = view.getFloat32(o, true); break;
        case 5125: v = view.getUint32(o, true); break;
        case 5123: v = view.getUint16(o, true); break;
        case 5122: v = view.getInt16(o, true); break;
        case 5121: v = view.getUint8(o); break;
        default: v = view.getInt8(o);
      }
      if (acc.normalized && max) v = Math.max(v / max, -1);
      out[i * n + k] = v;
    }
  return out;
}
if (gltf.meshes.length !== 1 || gltf.meshes[0].primitives.length !== 1) throw new Error('expected one mesh with one primitive (a Tripo model)');
const prim = gltf.meshes[0].primitives[0];
const pos = read(prim.attributes.POSITION), uv = read(prim.attributes.TEXCOORD_0), idx = read(prim.indices);
const nv = pos.length / 3;
let lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
for (let i = 0; i < nv; i++) for (let d = 0; d < 3; d++) { lo[d] = Math.min(lo[d], pos[i * 3 + d]); hi[d] = Math.max(hi[d], pos[i * 3 + d]); }
const scale = 1.15 / (hi[1] - lo[1]);
const P = (i) => [(pos[i * 3] - lo[0]) * scale, (pos[i * 3 + 1] - lo[1]) * scale, (pos[i * 3 + 2] - lo[2]) * scale];

// ---------- pieces (welded by position) ----------
const parent = Array.from({ length: nv }, (_, i) => i);
const find = (x) => { while (parent[x] !== x) x = parent[x] = parent[parent[x]]; return x; };
const join = (a, b) => { a = find(a); b = find(b); if (a !== b) parent[b] = a; };
const seen = new Map();
for (let i = 0; i < nv; i++) {
  const k = P(i).map((x) => Math.round(x * 2000)).join(',');
  if (seen.has(k)) join(seen.get(k), i); else seen.set(k, i);
}
for (let k = 0; k < idx.length; k += 3) { join(idx[k], idx[k + 1]); join(idx[k], idx[k + 2]); }
const pieces = new Map();
for (let i = 0; i < nv; i++) { const r = find(i); if (!pieces.has(r)) pieces.set(r, []); pieces.get(r).push(i); }
const list = [...pieces.entries()].map(([root, verts]) => {
  const a = [Infinity, Infinity, Infinity], b = [-Infinity, -Infinity, -Infinity];
  for (const i of verts) { const p = P(i); for (let d = 0; d < 3; d++) { a[d] = Math.min(a[d], p[d]); b[d] = Math.max(b[d], p[d]); } }
  return { root, verts, lo: a, hi: b };
}).sort((x, y) => y.verts.length - x.verts.length);
const body = list[0];
// Arms: middle height (their middle between 30% and 65% up), not thin, not tiny.
const H = 1.15;
const arms = list.slice(1).filter((p) => {
  const midY = (p.lo[1] + p.hi[1]) / 2, e = p.hi.map((h, d) => h - p.lo[d]).sort((m, n) => m - n);
  return midY > 0.3 * H && midY < 0.65 * H && e[2] < 12 * e[0] && p.verts.length > 100;
});
console.log(`pieces ${list.length}: body ${body.verts.length} verts; arms: ${arms.map((a) => `${a.verts.length} verts y ${a.lo[1].toFixed(2)}..${a.hi[1].toFixed(2)}`).join(', ') || 'none'}`);
if (!arms.length) throw new Error('no arm pieces found');

const pieceOf = new Int32Array(nv);
for (let i = 0; i < nv; i++) pieceOf[i] = find(i);
// The arms' points, in a grid, for "how far is this bit of body from an arm".
const grid = new Map();
const key = (p) => p.map((x) => Math.floor(x / NEAR)).join(',');
for (const a of arms) for (const i of a.verts) { const k = key(P(i)); if (!grid.has(k)) grid.set(k, []); grid.get(k).push(P(i)); }
function armDistance(p) {
  const c = p.map((x) => Math.floor(x / NEAR));
  let best = Infinity;
  for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) for (let dz = -1; dz <= 1; dz++) {
    const g = grid.get(`${c[0] + dx},${c[1] + dy},${c[2] + dz}`);
    if (g) for (const q of g) best = Math.min(best, Math.hypot(p[0] - q[0], p[1] - q[1], p[2] - q[2]));
  }
  return best;
}

// ---------- the mask in texture space: rasterise the hidden body triangles' uvs ----------
const tex = gltf.materials[prim.material].pbrMetallicRoughness.baseColorTexture.index;
const colourImage = gltf.textures[tex].source;
const normalTex = gltf.materials[prim.material].normalTexture?.index;
const normalImage = normalTex !== undefined ? gltf.textures[normalTex].source : -1;
const imageBytes = (i) => { const bv = gltf.bufferViews[gltf.images[i].bufferView]; return Buffer.from(bin.subarray(bv.byteOffset ?? 0, (bv.byteOffset ?? 0) + bv.byteLength)); };
const colour = decodePng(imageBytes(colourImage));
const W = colour.width, Hh = colour.height;
const mask = new Float32Array(W * Hh); // how strongly to repaint each texel
function raster(a, b, c, f, target) {
  const pa = [uv[a * 2] * W, uv[a * 2 + 1] * Hh], pb = [uv[b * 2] * W, uv[b * 2 + 1] * Hh], pc = [uv[c * 2] * W, uv[c * 2 + 1] * Hh];
  const x0 = Math.max(0, Math.floor(Math.min(pa[0], pb[0], pc[0])) - 1), x1 = Math.min(W - 1, Math.ceil(Math.max(pa[0], pb[0], pc[0])) + 1);
  const y0 = Math.max(0, Math.floor(Math.min(pa[1], pb[1], pc[1])) - 1), y1 = Math.min(Hh - 1, Math.ceil(Math.max(pa[1], pb[1], pc[1])) + 1);
  const d = (pb[1] - pc[1]) * (pa[0] - pc[0]) + (pc[0] - pb[0]) * (pa[1] - pc[1]);
  if (Math.abs(d) < 1e-9) return;
  for (let y = y0; y <= y1; y++)
    for (let x = x0; x <= x1; x++) {
      const px = x + 0.5, py = y + 0.5;
      const wa = ((pb[1] - pc[1]) * (px - pc[0]) + (pc[0] - pb[0]) * (py - pc[1])) / d;
      const wb = ((pc[1] - pa[1]) * (px - pc[0]) + (pa[0] - pc[0]) * (py - pc[1])) / d;
      const wc = 1 - wa - wb;
      if (wa < -0.02 || wb < -0.02 || wc < -0.02) continue;
      const v = f(wa, wb, wc);
      target(y * W + x, v);
    }
}
// Every body texel's point on the model (the atlas islands are scattered, so this is how texels find their
// neighbours), and how hidden it is: right against an arm 1, fading out to NEAR away.
const where = new Float32Array(W * Hh * 3);
const onBody = new Uint8Array(W * Hh);
for (let k = 0; k < idx.length; k += 3) {
  const [a, b, c] = [idx[k], idx[k + 1], idx[k + 2]];
  if (pieceOf[a] !== body.root) continue;
  const pa = P(a), pb = P(b), pc = P(c);
  raster(a, b, c, (wa, wb, wc) => [0, 1, 2].map((d) => wa * pa[d] + wb * pb[d] + wc * pc[d]), (t, p) => {
    where.set(p, t * 3);
    onBody[t] = 1;
  });
}
// Seen from the front (Tripo paints from front-on pictures, so what an arm hides from the front got smeared):
// an orthographic depth picture of the arms only. Tripo P1 models face glTF +X.
const FRONT = opt('--front', '+x');
const axis = { '+x': [1, 0, 0], '-x': [-1, 0, 0], '+z': [0, 0, 1], '-z': [0, 0, -1] }[FRONT];
const across = Math.abs(axis[0]) > 0 ? [0, 0, 1] : [1, 0, 0];
const PIX = 0.003, FEATHER = 4; // picture pixel (m) and the soft edge round an arm (pixels)
const span = [0, 1, 2].map((d) => (hi[d] - lo[d]) * scale);
const PW = Math.ceil((Math.abs(across[0]) ? span[0] : span[2]) / PIX) + 2, PH = Math.ceil(span[1] / PIX) + 2;
const armDepth = new Float32Array(PW * PH).fill(Infinity);
const project = (p) => [(p[0] * across[0] + p[2] * across[2]) / PIX, p[1] / PIX, -(p[0] * axis[0] + p[1] * axis[1] + p[2] * axis[2])];
for (let k = 0; k < idx.length; k += 3) {
  const [a, b, c] = [idx[k], idx[k + 1], idx[k + 2]];
  if (!arms.some((arm) => arm.root === pieceOf[a])) continue;
  const A = project(P(a)), B = project(P(b)), C = project(P(c));
  const x0 = Math.max(0, Math.floor(Math.min(A[0], B[0], C[0]))), x1 = Math.min(PW - 1, Math.ceil(Math.max(A[0], B[0], C[0])));
  const y0 = Math.max(0, Math.floor(Math.min(A[1], B[1], C[1]))), y1 = Math.min(PH - 1, Math.ceil(Math.max(A[1], B[1], C[1])));
  const d = (B[1] - C[1]) * (A[0] - C[0]) + (C[0] - B[0]) * (A[1] - C[1]);
  if (Math.abs(d) < 1e-9) continue;
  for (let y = y0; y <= y1; y++)
    for (let x = x0; x <= x1; x++) {
      const wa = ((B[1] - C[1]) * (x + 0.5 - C[0]) + (C[0] - B[0]) * (y + 0.5 - C[1])) / d;
      const wb = ((C[1] - A[1]) * (x + 0.5 - C[0]) + (A[0] - C[0]) * (y + 0.5 - C[1])) / d;
      const wc = 1 - wa - wb;
      if (wa < -0.05 || wb < -0.05 || wc < -0.05) continue;
      const z = wa * A[2] + wb * B[2] + wc * C[2];
      if (z < armDepth[y * PW + x]) armDepth[y * PW + x] = z;
    }
}
let behind = 0;
for (let t = 0; t < W * Hh; t++) {
  if (!onBody[t]) continue;
  const p = [where[t * 3], where[t * 3 + 1], where[t * 3 + 2]];
  const d = armDistance(p);
  if (d < NEAR) mask[t] = 1 - d / NEAR;
  // Behind an arm seen from the front (and facing the front: the back of the body isn't the arm's doing).
  // How much of the little patch of view round it an arm covers: a soft edge along the arm's outline.
  const q = project(p), x = Math.floor(q[0]), y = Math.floor(q[1]);
  let covered = 0, all = 0;
  for (let dy = -FEATHER; dy <= FEATHER; dy++)
    for (let dx = -FEATHER; dx <= FEATHER; dx++) {
      const xx = x + dx, yy = y + dy;
      if (xx < 0 || yy < 0 || xx >= PW || yy >= PH) continue;
      all++;
      const z = armDepth[yy * PW + xx];
      if (z < q[2] - 0.004 && q[2] - z < 0.2) covered++;
    }
  const f = all ? covered / all : 0;
  if (f > 0) {
    mask[t] = Math.max(mask[t], Math.min(1, f * 1.6) / 3); // (the soften step below triples it)
    if (f > 0.5) behind++;
  }
}
console.log(`behind an arm from the front: ${behind} texels`);
// Soften: anything closer than two thirds of NEAR is fully repainted, fading out beyond.
let masked = 0;
for (let t = 0; t < mask.length; t++) { mask[t] = Math.min(1, mask[t] * 3); if (mask[t] > 0.02) masked++; }
console.log(`texture ${W}x${Hh}: ${masked} texels to repaint (${((100 * masked) / (W * Hh)).toFixed(1)}%)`);

if (debug) {
  const out = new Uint8ClampedArray(colour.data.length);
  for (let t = 0; t < W * Hh; t++) {
    const m = mask[t];
    out[t * 4] = colour.data[t * 4] * (1 - m) + 255 * m;
    out[t * 4 + 1] = colour.data[t * 4 + 1] * (1 - m);
    out[t * 4 + 2] = colour.data[t * 4 + 2] * (1 - m);
    out[t * 4 + 3] = 255;
  }
  writeFileSync(debug, encodePng(W, Hh, out));
  console.log(`debug -> ${debug}`);
}
if (dry) process.exit(0);

// ---------- fill, on the surface: each hidden texel takes the colour of the nearest visible fur round it ----------
// The texture is an atlas of many small islands, so the texels next to a hole in the picture are often somewhere
// else on the model. Instead every body texel gets its 3D point (from its triangle), and a hidden one is filled
// from the visible body texels nearest it in 3D.
const SAMPLE = 0.012;
const visible = new Map();
for (let t = 0; t < W * Hh; t++) {
  if (!onBody[t] || mask[t] > 0.02) continue;
  const k = [0, 1, 2].map((d) => Math.floor(where[t * 3 + d] / SAMPLE)).join(',');
  if (!visible.has(k)) visible.set(k, []);
  visible.get(k).push(t);
}
// Each little cube of visible fur: its middle and mean colour (per image), for a smooth blend.
const cells = [...visible.values()].map((ts) => {
  const m = [0, 0, 0];
  for (const u of ts) for (let d = 0; d < 3; d++) m[d] += where[u * 3 + d] / ts.length;
  return { ts, m, key: m.map((x) => Math.floor(x / SAMPLE)).join(',') };
});
const cellAt = new Map(cells.map((c) => [c.key, c]));
const BLEND = 0.03, REACH = 6; // gaussian width (m) and how many cells out to look
/** The colour of the visible fur round 3D point p, a soft blend (so a belly edge fades rather than steps). */
function around(p, data) {
  const c = p.map((x) => Math.floor(x / SAMPLE));
  let s = [0, 0, 0], w = 0;
  for (let dx = -REACH; dx <= REACH; dx++) for (let dy = -REACH; dy <= REACH; dy++) for (let dz = -REACH; dz <= REACH; dz++) {
    const cell = cellAt.get(`${c[0] + dx},${c[1] + dy},${c[2] + dz}`);
    if (!cell) continue;
    if (!cell.mean) cell.mean = new Map();
    let mean = cell.mean.get(data);
    if (!mean) {
      mean = [0, 0, 0];
      for (const u of cell.ts) for (let ch = 0; ch < 3; ch++) mean[ch] += data[u * 4 + ch] / cell.ts.length;
      cell.mean.set(data, mean);
    }
    const d = Math.hypot(p[0] - cell.m[0], p[1] - cell.m[1], p[2] - cell.m[2]);
    const k = Math.exp(-((d / BLEND) ** 2)) * cell.ts.length;
    for (let ch = 0; ch < 3; ch++) s[ch] += mean[ch] * k;
    w += k;
  }
  return w > 1e-6 ? s.map((x) => x / w) : null;
}
function fill(image, flat) {
  const { width, height, data } = image;
  const out = Buffer.from(data);
  let filled = 0;
  for (let t = 0; t < width * height; t++) {
    const m = mask[t];
    if (m <= 0.02) continue;
    const want = flat ?? (onBody[t] ? around([where[t * 3], where[t * 3 + 1], where[t * 3 + 2]], data) : null);
    if (!want) continue;
    for (let ch = 0; ch < 3; ch++) out[t * 4 + ch] = Math.round(data[t * 4 + ch] * (1 - m) + want[ch] * m);
    filled++;
  }
  // The seams: texels just outside the atlas islands still hold the old colour and bleed in when the texture is
  // filtered, so the fill is grown a few texels out over them.
  let grow = new Uint8Array(width * height);
  for (let t = 0; t < width * height; t++) if (mask[t] > 0.02) grow[t] = 1;
  for (let pass = 0; pass < 3; pass++) {
    const next = grow.slice();
    for (let t = 0; t < width * height; t++) {
      if (grow[t] || onBody[t] && mask[t] <= 0.02) continue;
      const x = t % width, y = (t / width) | 0;
      let s = [0, 0, 0], k = 0;
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [-1, -1], [1, -1], [-1, 1]]) {
        const xx = x + dx, yy = y + dy;
        if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
        const u = yy * width + xx;
        if (!grow[u]) continue;
        for (let ch = 0; ch < 3; ch++) s[ch] += out[u * 4 + ch];
        k++;
      }
      if (!k) continue;
      for (let ch = 0; ch < 3; ch++) out[t * 4 + ch] = Math.round(s[ch] / k);
      next[t] = 1;
    }
    grow = next;
  }
  console.log(`  filled ${filled} texels`);
  return { width, height, data: out };
}

const images = gltf.images.map((_, i) => imageBytes(i));
const fixedColour = fill(colour, null);
images[colourImage] = Buffer.from(encodePng(fixedColour.width, fixedColour.height, fixedColour.data));
if (normalImage >= 0) {
  const normal = decodePng(imageBytes(normalImage));
  if (normal.width === W && normal.height === Hh) {
    const f = fill(normal, [128, 128, 255]);
    images[normalImage] = Buffer.from(encodePng(f.width, f.height, f.data));
  } else console.log('normal map is a different size: left as it is');
}

// ---------- rewrite the GLB: same JSON and binary, the images' bytes replaced ----------
const chunks = [];
let offset = 0;
const views = gltf.bufferViews.map((bv, i) => {
  const imageIndex = gltf.images.findIndex((im) => im.bufferView === i);
  const bytes = imageIndex >= 0 ? images[imageIndex] : Buffer.from(bin.subarray(bv.byteOffset ?? 0, (bv.byteOffset ?? 0) + bv.byteLength));
  const pad = (4 - (offset % 4)) % 4;
  if (pad) { chunks.push(Buffer.alloc(pad)); offset += pad; }
  const view = { ...bv, byteOffset: offset, byteLength: bytes.length };
  chunks.push(bytes);
  offset += bytes.length;
  return view;
});
const tail = (4 - (offset % 4)) % 4;
if (tail) chunks.push(Buffer.alloc(tail));
const newBin = Buffer.concat(chunks);
const json = { ...gltf, bufferViews: views, buffers: [{ ...gltf.buffers[0], byteLength: newBin.length }] };
let jsonBytes = Buffer.from(JSON.stringify(json));
const jpad = (4 - (jsonBytes.length % 4)) % 4;
jsonBytes = Buffer.concat([jsonBytes, Buffer.alloc(jpad, 0x20)]);
const header = Buffer.alloc(12);
header.writeUInt32LE(0x46546c67, 0);
header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + jsonBytes.length + 8 + newBin.length, 8);
const jh = Buffer.alloc(8); jh.writeUInt32LE(jsonBytes.length, 0); jh.writeUInt32LE(0x4e4f534a, 4);
const bh = Buffer.alloc(8); bh.writeUInt32LE(newBin.length, 0); bh.writeUInt32LE(0x004e4942, 4);
writeFileSync(file, Buffer.concat([header, jh, jsonBytes, bh, newBin]));
console.log(`repainted -> ${file} (${(buf.length / 1024).toFixed(0)} KB -> ${((12 + 16 + jsonBytes.length + newBin.length) / 1024).toFixed(0)} KB)`);
