/**
 * Builds the flour-sack avatar (Assets/Muffin/Avatars/Generated/flour_bag.glb) with no AI service and no
 * credits: a pillow-shaped paper sack with a flat crimped top and a printed label, written as a plain GLB.
 * No npm packages needed (Node's own zlib writes the texture's PNG).
 *
 *   node src/flourbag.mjs [out.glb] [--texture out.png]
 *
 * Like the SPAR3D avatars it faces the camera as it is (front = glTF +Z), so its spec in
 * MuffinPrototypeSetup.GeneratedAvatars uses Yaw = 0. The label is printed low on the sack, clear of the
 * middle, where the digital face goes.
 */
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const texFlag = args.indexOf('--texture');
const texOut = texFlag >= 0 ? args[texFlag + 1] : null;
const positional = args.filter((a, i) => !a.startsWith('--') && (texFlag < 0 || i !== texFlag + 1));
const OUT = positional[0] ?? join(HERE, '..', '..', 'MuffinVTuber', 'Assets', 'Muffin', 'Avatars', 'Generated', 'flour_bag.glb');

// ---- Shape ------------------------------------------------------------------------------------------------
// Half width A, half depth B (at the fattest), height H, in metres; the app rescales it to the avatar height.
const A = 0.26, B = 0.1, H = 0.78;
const RING = 72, ROWS = 64;

const smooth = (a, b, x) => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};
const lerp = (a, b, t) => a + (b - a) * t;

/** Half width at height fraction t: fullest low down, a little narrower at the crimped top. */
const halfWidth = (t) => A * (0.9 + 0.1 * Math.sin(Math.PI * Math.min(1, t / 0.8)) - 0.07 * smooth(0.8, 1, t));
/** Half depth: a flat bottom that fills out into a pillow, pinching to a flat seam at the top. */
const halfDepth = (t) => B * lerp(0.5, 1, smooth(0, 0.14, t)) * lerp(1, 0.07, smooth(0.6, 1, t)) * (1 + 0.06 * Math.sin(Math.PI * t));

const positions = [];
const uvs = [];
const SUPER = 3.2; // superellipse power: squarish with soft corners
const sgnPow = (v, p) => Math.sign(v) * Math.abs(v) ** p;

for (let j = 0; j <= ROWS; j++) {
  const t = j / ROWS;
  const y = t * H;
  const a = halfWidth(t), b = halfDepth(t);
  for (let i = 0; i < RING; i++) {
    const th = (i / RING) * Math.PI * 2;
    const x = a * sgnPow(Math.cos(th), 2 / SUPER);
    const z = b * sgnPow(Math.sin(th), 2 / SUPER);
    positions.push(x, y, z);
    // Printed straight on from the front. u runs against x: glTFast flips X on import, and with u along +x the
    // label read backwards in the app.
    uvs.push(0.5 - x / (2 * A), 1 - y / H);
  }
}
// Caps: a centre vertex on the floor and one in the top seam.
const bottomCentre = positions.length / 3;
positions.push(0, 0, 0);
uvs.push(0.5, 1);
const topCentre = positions.length / 3;
positions.push(0, H, 0);
uvs.push(0.5, 0);

const indices = [];
const at = (j, i) => j * RING + (i % RING);
for (let j = 0; j < ROWS; j++) {
  for (let i = 0; i < RING; i++) {
    const p = at(j, i), q = at(j, i + 1), r = at(j + 1, i + 1), s = at(j + 1, i);
    indices.push(p, q, r, p, r, s);
  }
}
for (let i = 0; i < RING; i++) {
  indices.push(bottomCentre, at(0, i + 1), at(0, i));
  indices.push(topCentre, at(ROWS, i), at(ROWS, i + 1));
}

// Make every face point outwards: count how many of the body's triangles point away from the middle line.
const faceNormal = (a, b, c) => {
  const u = [positions[b * 3] - positions[a * 3], positions[b * 3 + 1] - positions[a * 3 + 1], positions[b * 3 + 2] - positions[a * 3 + 2]];
  const v = [positions[c * 3] - positions[a * 3], positions[c * 3 + 1] - positions[a * 3 + 1], positions[c * 3 + 2] - positions[a * 3 + 2]];
  return [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
};
let outward = 0, inward = 0;
for (let k = 0; k < ROWS * RING * 6; k += 3) {
  const [a, b, c] = [indices[k], indices[k + 1], indices[k + 2]];
  const n = faceNormal(a, b, c);
  const mx = (positions[a * 3] + positions[b * 3] + positions[c * 3]) / 3;
  const mz = (positions[a * 3 + 2] + positions[b * 3 + 2] + positions[c * 3 + 2]) / 3;
  const d = n[0] * mx + n[2] * mz;
  if (Math.abs(d) < 1e-12) continue;
  if (d > 0) outward++; else inward++;
}
if (inward > outward) for (let k = 0; k < indices.length; k += 3) [indices[k + 1], indices[k + 2]] = [indices[k + 2], indices[k + 1]];

// Smooth normals: each face's (area-weighted) normal added to its corners.
const normals = new Array(positions.length).fill(0);
for (let k = 0; k < indices.length; k += 3) {
  const [a, b, c] = [indices[k], indices[k + 1], indices[k + 2]];
  const n = faceNormal(a, b, c);
  for (const v of [a, b, c]) for (let d = 0; d < 3; d++) normals[v * 3 + d] += n[d];
}
for (let v = 0; v < normals.length; v += 3) {
  const len = Math.hypot(normals[v], normals[v + 1], normals[v + 2]) || 1;
  normals[v] /= len;
  normals[v + 1] /= len;
  normals[v + 2] /= len;
}

// ---- Texture ----------------------------------------------------------------------------------------------
const TW = 512, TH = 1024;
const px = new Uint8ClampedArray(TW * TH * 4);
let seed = 12345;
const rand = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);

function blend(x, y, r, g, b, a) {
  if (x < 0 || y < 0 || x >= TW || y >= TH || a <= 0) return;
  const o = (y * TW + x) * 4;
  px[o] = px[o] * (1 - a) + r * a;
  px[o + 1] = px[o + 1] * (1 - a) + g * a;
  px[o + 2] = px[o + 2] * (1 - a) + b * a;
  px[o + 3] = 255;
}
function rect(x0, y0, x1, y1, [r, g, b], a = 1) {
  for (let y = Math.max(0, Math.floor(y0)); y < Math.min(TH, Math.ceil(y1)); y++)
    for (let x = Math.max(0, Math.floor(x0)); x < Math.min(TW, Math.ceil(x1)); x++) blend(x, y, r, g, b, a);
}
/** A thick line through points (pixels), anti-aliased, round ends. */
function stroke(points, width, [r, g, b], a = 1) {
  const half = width / 2;
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const [x, y] of points) {
    minX = Math.min(minX, x); minY = Math.min(minY, y); maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
  }
  for (let y = Math.floor(minY - half - 1); y <= Math.ceil(maxY + half + 1); y++) {
    for (let x = Math.floor(minX - half - 1); x <= Math.ceil(maxX + half + 1); x++) {
      let d = Infinity;
      for (let s = 0; s < points.length - 1; s++) {
        const [ax, ay] = points[s], [bx, by] = points[s + 1];
        const dx = bx - ax, dy = by - ay;
        const len2 = dx * dx + dy * dy || 1;
        const t = Math.min(1, Math.max(0, ((x - ax) * dx + (y - ay) * dy) / len2));
        d = Math.min(d, Math.hypot(x - (ax + t * dx), y - (ay + t * dy)));
      }
      const cover = Math.min(1, Math.max(0, half + 0.5 - d));
      if (cover > 0) blend(x, y, r, g, b, a * cover);
    }
  }
}
function disc(cx, cy, radius, [r, g, b], a) {
  for (let y = Math.floor(cy - radius); y <= Math.ceil(cy + radius); y++)
    for (let x = Math.floor(cx - radius); x <= Math.ceil(cx + radius); x++) {
      const d = Math.hypot(x - cx, y - cy) / radius;
      if (d < 1) blend(x, y, r, g, b, a * (1 - d * d));
    }
}

const PAPER = [242, 235, 218], PAPER_DARK = [222, 211, 186], BLUE = [38, 86, 160], RED = [196, 52, 44], WHITE = [250, 250, 246];
// Paper with a little grain.
rect(0, 0, TW, TH, PAPER);
for (let n = 0; n < 9000; n++) disc(rand() * TW, rand() * TH, 1 + rand() * 2.5, rand() < 0.5 ? PAPER_DARK : WHITE, 0.25);
// The crimped, stitched top seam (top 7% of the sack).
rect(0, 0, TW, 72, PAPER_DARK, 0.7);
for (let y = 6; y < 66; y += 7) rect(0, y, TW, y + 2, [196, 183, 152], 0.55);
for (let x = 8; x < TW; x += 14) rect(x, 76, x + 7, 79, [170, 90, 70], 0.9);
// Label: a blue band (full width, so it wraps round the sides) with the name in white, red stripes either side.
const BAND_TOP = 660, BAND_BOTTOM = 800;
rect(0, BAND_TOP - 16, TW, BAND_TOP - 6, RED);
rect(0, BAND_BOTTOM + 6, TW, BAND_BOTTOM + 16, RED);
rect(0, BAND_TOP, TW, BAND_BOTTOM, BLUE);
rect(0, BAND_TOP + 8, TW, BAND_TOP + 11, WHITE, 0.85);
rect(0, BAND_BOTTOM - 11, TW, BAND_BOTTOM - 8, WHITE, 0.85);

// Block letters, each a polyline in a unit box (width given, height 1; y down).
const arc = (cx, cy, rx, ry, from, to, steps = 18) =>
  Array.from({ length: steps + 1 }, (_, k) => {
    const t = lerp(from, to, k / steps);
    return [cx + rx * Math.cos(t), cy + ry * Math.sin(t)];
  });
const LETTERS = {
  F: { w: 0.6, lines: [[[0, 1], [0, 0], [0.6, 0]], [[0, 0.45], [0.5, 0.45]]] },
  L: { w: 0.6, lines: [[[0, 0], [0, 1], [0.6, 1]]] },
  O: { w: 0.7, lines: [arc(0.35, 0.5, 0.35, 0.5, 0, Math.PI * 2, 40)] },
  U: { w: 0.7, lines: [[[0, 0], [0, 0.65], ...arc(0.35, 0.65, 0.35, 0.35, Math.PI, 0).map(([x, y]) => [x, y]), [0.7, 0]]] },
  R: { w: 0.7, lines: [[[0, 1], [0, 0], [0.4, 0], ...arc(0.4, 0.25, 0.28, 0.25, -Math.PI / 2, Math.PI / 2), [0, 0.5]], [[0.34, 0.5], [0.7, 1]]] },
};
const LETTER_H = 84, GAP = 20, STROKE = 15;
const word = 'FLOUR';
const wordWidth = [...word].reduce((s, c) => s + LETTERS[c].w * LETTER_H, 0) + GAP * (word.length - 1);
let cursor = (TW - wordWidth) / 2;
const top = (BAND_TOP + BAND_BOTTOM) / 2 - LETTER_H / 2;
for (const c of word) {
  const { w, lines } = LETTERS[c];
  for (const line of lines) stroke(line.map(([x, y]) => [cursor + x * LETTER_H, top + y * LETTER_H]), STROKE, WHITE);
  cursor += w * LETTER_H + GAP;
}
// A wheat sprig under the band.
const stemX = TW / 2, stemTop = 835, stemBottom = 930;
stroke([[stemX, stemBottom], [stemX, stemTop]], 5, [120, 98, 52]);
for (let k = 0; k < 5; k++) {
  const y = stemTop + 6 + k * 16;
  stroke([[stemX, y + 10], [stemX - 14, y - 6]], 5, [196, 150, 60]);
  stroke([[stemX, y + 10], [stemX + 14, y - 6]], 5, [196, 150, 60]);
}
stroke([[stemX, stemTop + 4], [stemX, stemTop - 14]], 7, [196, 150, 60]);
// Dusty flour smudges near the bottom and on the shoulders.
for (let n = 0; n < 60; n++) disc(rand() * TW, 900 + rand() * 124, 14 + rand() * 34, WHITE, 0.35);
for (let n = 0; n < 20; n++) disc(rand() * TW, 90 + rand() * 70, 10 + rand() * 24, WHITE, 0.3);

// PNG (RGBA, no filter) from the pixels.
const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
const crc32 = (buf) => {
  let c = 0xffffffff;
  for (const byte of buf) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
};
function chunk(type, data) {
  const out = Buffer.alloc(12 + data.length);
  out.writeUInt32BE(data.length, 0);
  out.write(type, 4, 'latin1');
  data.copy(out, 8);
  out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
  return out;
}
function encodePng() {
  const raw = Buffer.alloc((TW * 4 + 1) * TH);
  for (let y = 0; y < TH; y++) {
    raw[y * (TW * 4 + 1)] = 0;
    for (let x = 0; x < TW * 4; x++) raw[y * (TW * 4 + 1) + 1 + x] = px[y * TW * 4 + x];
  }
  const head = Buffer.alloc(13);
  head.writeUInt32BE(TW, 0);
  head.writeUInt32BE(TH, 4);
  head[8] = 8; // bit depth
  head[9] = 6; // RGBA
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', head), chunk('IDAT', deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const png = encodePng();
if (texOut) writeFileSync(texOut, png);

// ---- GLB --------------------------------------------------------------------------------------------------
const pos = Buffer.from(new Float32Array(positions).buffer);
const nor = Buffer.from(new Float32Array(normals).buffer);
const tex = Buffer.from(new Float32Array(uvs).buffer);
const idx = Buffer.from(new Uint32Array(indices).buffer);
const pad4 = (b) => (b.length % 4 === 0 ? b : Buffer.concat([b, Buffer.alloc(4 - (b.length % 4))]));
const parts = [pos, nor, tex, idx, png].map(pad4);
const offsets = [];
let total = 0;
for (const p of parts) { offsets.push(total); total += p.length; }

const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
for (let v = 0; v < positions.length; v += 3) for (let d = 0; d < 3; d++) { min[d] = Math.min(min[d], positions[v + d]); max[d] = Math.max(max[d], positions[v + d]); }

const gltf = {
  asset: { version: '2.0', generator: 'props-service/src/flourbag.mjs' },
  scene: 0,
  scenes: [{ nodes: [0] }],
  nodes: [{ name: 'flour_bag', mesh: 0 }],
  meshes: [{ name: 'flour_bag', primitives: [{ attributes: { POSITION: 0, NORMAL: 1, TEXCOORD_0: 2 }, indices: 3, material: 0 }] }],
  materials: [{ name: 'flour_paper', pbrMetallicRoughness: { baseColorTexture: { index: 0 }, metallicFactor: 0, roughnessFactor: 0.95 } }],
  textures: [{ source: 0, sampler: 0 }],
  samplers: [{ magFilter: 9729, minFilter: 9987, wrapS: 33071, wrapT: 33071 }],
  images: [{ bufferView: 4, mimeType: 'image/png' }],
  accessors: [
    { bufferView: 0, componentType: 5126, count: positions.length / 3, type: 'VEC3', min, max },
    { bufferView: 1, componentType: 5126, count: normals.length / 3, type: 'VEC3' },
    { bufferView: 2, componentType: 5126, count: uvs.length / 2, type: 'VEC2' },
    { bufferView: 3, componentType: 5125, count: indices.length, type: 'SCALAR' },
  ],
  bufferViews: [
    { buffer: 0, byteOffset: offsets[0], byteLength: pos.length, target: 34962 },
    { buffer: 0, byteOffset: offsets[1], byteLength: nor.length, target: 34962 },
    { buffer: 0, byteOffset: offsets[2], byteLength: tex.length, target: 34962 },
    { buffer: 0, byteOffset: offsets[3], byteLength: idx.length, target: 34963 },
    { buffer: 0, byteOffset: offsets[4], byteLength: png.length },
  ],
  buffers: [{ byteLength: total }],
};
const json = pad4(Buffer.from(JSON.stringify(gltf), 'utf8')).fill(0x20, Buffer.byteLength(JSON.stringify(gltf), 'utf8'));
const bin = Buffer.concat(parts);
const header = Buffer.alloc(12);
header.write('glTF', 0, 'latin1');
header.writeUInt32LE(2, 4);
header.writeUInt32LE(12 + 8 + json.length + 8 + bin.length, 8);
const jsonHead = Buffer.alloc(8);
jsonHead.writeUInt32LE(json.length, 0);
jsonHead.write('JSON', 4, 'latin1');
const binHead = Buffer.alloc(8);
binHead.writeUInt32LE(bin.length, 0);
binHead.write('BIN\0', 4, 'latin1');
writeFileSync(OUT, Buffer.concat([header, jsonHead, json, binHead, bin]));

console.log(`flour_bag: ${positions.length / 3} vertices, ${indices.length / 3} triangles, ` +
  `${(max[0] - min[0]).toFixed(2)} x ${(max[1] - min[1]).toFixed(2)} x ${(max[2] - min[2]).toFixed(2)} m, ` +
  `${outward >= inward ? 'wound outward' : 'flipped to outward'} -> ${OUT}`);
