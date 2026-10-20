/**
 * A quick software render of a GLB (no Unity needed): the front, a three-quarter view and the side, lit roughly
 * like the app, side by side in one PNG. Good enough to check shapes, proportions, textures and that every face
 * points outward (inside-out faces are culled, so they show up as holes).
 *
 *   node src/procedural/preview.mjs model.glb out.png [--face 0.5 --face-size 0.8]
 *
 * The model is scaled to the app's avatar height (1.15 m) and stood on the floor, like MuffinPrototypeSetup.BakeAvatar.
 * With --face, a dashed ring shows where the digital face would sit on the front.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { readGlb, decodePng, encodePng, clamp } from './kit.mjs';

const HEIGHT = 1.15;
const BG = [26, 18, 41];

/**
 * An accessor's values as plain numbers: floats, or integers (normalised to -1..1 / 0..1 when the accessor says so,
 * as gltf-transform's quantised meshes do), interleaved or not. Indices come back as integers.
 */
function accessorData(gltf, bin, index) {
  const acc = gltf.accessors[index];
  const bv = gltf.bufferViews[acc.bufferView];
  const comps = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 }[acc.type];
  const info = {
    5126: [4, (d, o) => d.getFloat32(o, true), 1],
    5125: [4, (d, o) => d.getUint32(o, true), 4294967295],
    5123: [2, (d, o) => d.getUint16(o, true), 65535],
    5122: [2, (d, o) => d.getInt16(o, true), 32767],
    5121: [1, (d, o) => d.getUint8(o), 255],
    5120: [1, (d, o) => d.getInt8(o), 127],
  }[acc.componentType];
  const [size, read, max] = info;
  const stride = bv.byteStride ?? size * comps;
  const view = new DataView(bin.buffer, bin.byteOffset + (bv.byteOffset ?? 0) + (acc.byteOffset ?? 0));
  const out = acc.componentType === 5126 || acc.normalized ? new Float32Array(acc.count * comps) : new Uint32Array(acc.count * comps);
  for (let i = 0; i < acc.count; i++)
    for (let c = 0; c < comps; c++) {
      let v = read(view, i * stride + c * size);
      if (acc.normalized && acc.componentType !== 5126) v = Math.max(-1, v / max);
      out[i * comps + c] = v;
    }
  return out;
}

/** 4x4 column-major helpers for node transforms. */
const mat4 = {
  identity: () => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
  mul(a, b) {
    const r = new Array(16).fill(0);
    for (let c = 0; c < 4; c++) for (let rI = 0; rI < 4; rI++) for (let k = 0; k < 4; k++) r[c * 4 + rI] += a[k * 4 + rI] * b[c * 4 + k];
    return r;
  },
  trs(t = [0, 0, 0], q = [0, 0, 0, 1], s = [1, 1, 1]) {
    const [x, y, z, w] = q;
    return [
      (1 - 2 * (y * y + z * z)) * s[0], 2 * (x * y + z * w) * s[0], 2 * (x * z - y * w) * s[0], 0,
      2 * (x * y - z * w) * s[1], (1 - 2 * (x * x + z * z)) * s[1], 2 * (y * z + x * w) * s[1], 0,
      2 * (x * z + y * w) * s[2], 2 * (y * z - x * w) * s[2], (1 - 2 * (x * x + y * y)) * s[2], 0,
      t[0], t[1], t[2], 1,
    ];
  },
};

/** Every mesh in the scene with the world matrix of the node that holds it. */
function meshNodes(gltf) {
  const out = [];
  const walk = (i, parent) => {
    const n = gltf.nodes[i];
    const local = n.matrix ? n.matrix : mat4.trs(n.translation, n.rotation, n.scale);
    const world = mat4.mul(parent, local);
    if (n.mesh !== undefined) out.push({ mesh: gltf.meshes[n.mesh], world });
    for (const c of n.children ?? []) walk(c, world);
  };
  const roots = gltf.scenes?.[gltf.scene ?? 0]?.nodes ?? gltf.nodes.map((_, i) => i);
  for (const r of roots) walk(r, mat4.identity());
  return out;
}

export function loadModel(buf) {
  const { gltf, bin } = readGlb(buf);
  const images = (gltf.images ?? []).map((img) => {
    const bv = gltf.bufferViews[img.bufferView];
    return decodePng(Buffer.from(bin.subarray(bv.byteOffset ?? 0, (bv.byteOffset ?? 0) + bv.byteLength)));
  });
  const prims = [];
  for (const { mesh, world } of meshNodes(gltf))
    for (const p of mesh.primitives) {
      const mat = gltf.materials?.[p.material] ?? {};
      const pbr = mat.pbrMetallicRoughness ?? {};
      const texIndex = pbr.baseColorTexture?.index;
      // Into world space with the node's transform (quantised meshes keep their scale and offset there).
      const pos = accessorData(gltf, bin, p.attributes.POSITION);
      for (let i = 0; i < pos.length; i += 3) {
        const [x, y, z] = [pos[i], pos[i + 1], pos[i + 2]];
        pos[i] = world[0] * x + world[4] * y + world[8] * z + world[12];
        pos[i + 1] = world[1] * x + world[5] * y + world[9] * z + world[13];
        pos[i + 2] = world[2] * x + world[6] * y + world[10] * z + world[14];
      }
      let nor = p.attributes.NORMAL !== undefined ? accessorData(gltf, bin, p.attributes.NORMAL) : null;
      if (nor)
        for (let i = 0; i < nor.length; i += 3) {
          const [x, y, z] = [nor[i], nor[i + 1], nor[i + 2]];
          const n = [world[0] * x + world[4] * y + world[8] * z, world[1] * x + world[5] * y + world[9] * z, world[2] * x + world[6] * y + world[10] * z];
          const l = Math.hypot(...n) || 1;
          nor[i] = n[0] / l;
          nor[i + 1] = n[1] / l;
          nor[i + 2] = n[2] / l;
        }
      prims.push({
        pos,
        nor,
        uv: p.attributes.TEXCOORD_0 !== undefined ? accessorData(gltf, bin, p.attributes.TEXCOORD_0) : null,
        idx: accessorData(gltf, bin, p.indices),
        color: pbr.baseColorFactor ?? [1, 1, 1, 1],
        image: texIndex !== undefined ? images[gltf.textures[texIndex].source] : null,
        blend: mat.alphaMode === 'BLEND' || /^glass/i.test(mat.name ?? ''),
        glass: /^glass/i.test(mat.name ?? ''),
        doubleSided: !!mat.doubleSided,
        emissive: mat.emissiveFactor ?? [0, 0, 0],
        name: mat.name,
      });
    }
  // Scale to the avatar height, stand it on y = 0, centred.
  const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
  for (const p of prims)
    for (let i = 0; i < p.pos.length; i += 3)
      for (let d = 0; d < 3; d++) {
        min[d] = Math.min(min[d], p.pos[i + d]);
        max[d] = Math.max(max[d], p.pos[i + d]);
      }
  const s = HEIGHT / (max[1] - min[1]);
  for (const p of prims)
    for (let i = 0; i < p.pos.length; i += 3) {
      p.pos[i] = (p.pos[i] - (min[0] + max[0]) / 2) * s;
      p.pos[i + 1] = (p.pos[i + 1] - min[1]) * s;
      p.pos[i + 2] = (p.pos[i + 2] - (min[2] + max[2]) / 2) * s;
    }
  return { prims, size: [(max[0] - min[0]) * s, HEIGHT, (max[2] - min[2]) * s] };
}

function sample(image, u, v) {
  const x = clamp(u, 0, 1) * (image.width - 1), y = clamp(v, 0, 1) * (image.height - 1);
  const x0 = Math.floor(x), y0 = Math.floor(y), x1 = Math.min(image.width - 1, x0 + 1), y1 = Math.min(image.height - 1, y0 + 1);
  const fx = x - x0, fy = y - y0;
  const out = [0, 0, 0, 0];
  for (let c = 0; c < 4; c++) {
    const a = image.px[(y0 * image.width + x0) * 4 + c], b = image.px[(y0 * image.width + x1) * 4 + c];
    const d = image.px[(y1 * image.width + x0) * 4 + c], e = image.px[(y1 * image.width + x1) * 4 + c];
    out[c] = ((a * (1 - fx) + b * fx) * (1 - fy) + (d * (1 - fx) + e * fx) * fy) / 255;
  }
  return out;
}

const normalize = (v) => {
  const l = Math.hypot(...v) || 1;
  return v.map((x) => x / l);
};
const KEY = normalize([0.45, 0.65, -0.62]); // from the upper left, in front (screen left is +X)
const RIM = normalize([-0.3, 0.35, 0.9]);

/** Renders one view: the model turned by yaw degrees (positive turns its front toward screen right, showing its +X side). */
export function render(model, { width = 420, height = 560, yaw = 0, face, faceSize = 1 } = {}) {
  const color = new Float32Array(width * height * 3);
  for (let i = 0; i < width * height; i++) color.set(BG.map((c) => c / 255), i * 3);
  const depth = new Float32Array(width * height).fill(Infinity);
  const fov = (26 * Math.PI) / 180;
  const focal = height / 2 / Math.tan(fov / 2);
  const target = [0, HEIGHT * 0.5, 0];
  const dist = (HEIGHT * 0.62) / Math.tan(fov / 2) + model.size[2] * 0.5;
  const cam = [0, target[1] + 0.05, -dist];
  const cy = Math.cos((yaw * Math.PI) / 180), sy = Math.sin((yaw * Math.PI) / 180);
  const turn = (x, y, z) => [x * cy + z * sy, y, -x * sy + z * cy];
  const project = (p) => {
    const q = [p[0] - cam[0], p[1] - cam[1], p[2] - cam[2]];
    return [width / 2 + (-q[0] / q[2]) * focal, height / 2 - (q[1] / q[2]) * focal, q[2]];
  };
  const blended = [];

  for (const prim of model.prims) {
    const n = prim.pos.length / 3;
    const P = [], N = [], S = [];
    for (let i = 0; i < n; i++) {
      P.push(turn(prim.pos[i * 3], prim.pos[i * 3 + 1], prim.pos[i * 3 + 2]));
      N.push(prim.nor ? turn(prim.nor[i * 3], prim.nor[i * 3 + 1], prim.nor[i * 3 + 2]) : [0, 0, -1]);
      S.push(project(P[i]));
    }
    for (let k = 0; k < prim.idx.length; k += 3) {
      const [a, b, c] = [prim.idx[k], prim.idx[k + 1], prim.idx[k + 2]];
      const e1 = P[b].map((x, d) => x - P[a][d]), e2 = P[c].map((x, d) => x - P[a][d]);
      const fn = [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]];
      const toCam = cam.map((x, d) => x - P[a][d]);
      const facing = fn[0] * toCam[0] + fn[1] * toCam[1] + fn[2] * toCam[2];
      if (facing <= 0 && !prim.doubleSided) continue;
      const tri = { prim, v: [a, b, c], P, N, S, flipNormal: facing <= 0 };
      if (prim.blend) blended.push({ tri, z: (S[a][2] + S[b][2] + S[c][2]) / 3 });
      else raster(tri, false);
    }
  }
  blended.sort((x, y) => y.z - x.z);
  for (const { tri } of blended) raster(tri, true);

  function shadeAt(prim, nrm, uv) {
    // glTF colour factors are linear (the app shows 0.2 as about 0.48): shown here the way the app shows them.
    let base = [Math.pow(prim.color[0], 1 / 2.2), Math.pow(prim.color[1], 1 / 2.2), Math.pow(prim.color[2], 1 / 2.2), prim.color[3]];
    if (prim.image && uv) {
      const t = sample(prim.image, uv[0], uv[1]);
      base = [base[0] * t[0], base[1] * t[1], base[2] * t[2], base[3] * t[3]];
    }
    const nn = normalize(nrm);
    const diff = Math.max(0, nn[0] * KEY[0] + nn[1] * KEY[1] + nn[2] * KEY[2]);
    const rim = Math.max(0, nn[0] * RIM[0] + nn[1] * RIM[1] + nn[2] * RIM[2]);
    const sky = 0.5 + 0.5 * nn[1];
    const light = 0.42 + 0.18 * sky + 0.75 * diff + 0.22 * rim;
    let out = [base[0] * light * 1.0, base[1] * light * 0.97, base[2] * light * 0.93].map((x, i) => x + prim.emissive[i]);
    if (prim.glass) {
      const view = Math.abs(nn[2]);
      out = out.map((x) => x + Math.pow(1 - view, 3) * 0.5 + Math.pow(diff, 40) * 0.8);
      base[3] = 0.18 + 0.5 * Math.pow(1 - view, 2);
    }
    return [out[0], out[1], out[2], base[3]];
  }

  function raster(tri, blend) {
    const { prim, v, N, S } = tri;
    const [A, B, C] = v.map((i) => S[i]);
    if (A[2] <= 0.05 || B[2] <= 0.05 || C[2] <= 0.05) return;
    const area = (B[0] - A[0]) * (C[1] - A[1]) - (B[1] - A[1]) * (C[0] - A[0]);
    if (Math.abs(area) < 1e-9) return;
    const minX = Math.max(0, Math.floor(Math.min(A[0], B[0], C[0]))), maxX = Math.min(width - 1, Math.ceil(Math.max(A[0], B[0], C[0])));
    const minY = Math.max(0, Math.floor(Math.min(A[1], B[1], C[1]))), maxY = Math.min(height - 1, Math.ceil(Math.max(A[1], B[1], C[1])));
    const uvs = prim.uv ? v.map((i) => [prim.uv[i * 2], prim.uv[i * 2 + 1]]) : null;
    const ns = v.map((i) => (tri.flipNormal ? N[i].map((x) => -x) : N[i]));
    for (let y = minY; y <= maxY; y++)
      for (let x = minX; x <= maxX; x++) {
        const px = x + 0.5, py = y + 0.5;
        let w0 = ((B[0] - px) * (C[1] - py) - (B[1] - py) * (C[0] - px)) / area;
        let w1 = ((C[0] - px) * (A[1] - py) - (C[1] - py) * (A[0] - px)) / area;
        let w2 = 1 - w0 - w1;
        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
        // Perspective-correct weights.
        const iz = w0 / A[2] + w1 / B[2] + w2 / C[2];
        const z = 1 / iz;
        const o = y * width + x;
        if (z >= depth[o]) continue;
        const p0 = (w0 / A[2]) * z, p1 = (w1 / B[2]) * z, p2 = (w2 / C[2]) * z;
        const nrm = [0, 1, 2].map((d) => ns[0][d] * p0 + ns[1][d] * p1 + ns[2][d] * p2);
        const uv = uvs ? [uvs[0][0] * p0 + uvs[1][0] * p1 + uvs[2][0] * p2, uvs[0][1] * p0 + uvs[1][1] * p1 + uvs[2][1] * p2] : null;
        const [r, g, b, a] = shadeAt(prim, nrm, uv);
        if (blend) {
          for (const [d, c] of [r, g, b].entries()) color[o * 3 + d] = color[o * 3 + d] * (1 - a) + c * a;
        } else {
          depth[o] = z;
          color[o * 3] = r;
          color[o * 3 + 1] = g;
          color[o * 3 + 2] = b;
        }
      }
  }

  // The face guide: where the app's cartoon face lands (MeshAvatar: the layout is fitted to faceSize x 80% of the
  // avatar's width at the face's height; the offsets below were measured off the flour bag in the app).
  if (face !== undefined && yaw === 0) {
    const faceY = HEIGHT * face;
    let lo = Infinity, hi = -Infinity, front = Infinity;
    for (const prim of model.prims)
      for (let i = 0; i < prim.pos.length; i += 3)
        if (Math.abs(prim.pos[i + 1] - faceY) < HEIGHT * 0.035) {
          lo = Math.min(lo, prim.pos[i]);
          hi = Math.max(hi, prim.pos[i]);
          if (Math.abs(prim.pos[i]) < (hi - lo) * 0.2 + 0.02) front = Math.min(front, prim.pos[i + 2]);
        }
    const scale = (faceSize * (hi - lo) * 0.8) / 0.26;
    const z = Number.isFinite(front) ? front - 0.01 : -model.size[2] / 2;
    const blob = (x, y, rx, ry) => {
      const c = project([x * scale, faceY + y * scale, z]);
      const sx = ((rx * scale) / c[2]) * focal, sy = ((ry * scale) / c[2]) * focal;
      for (let py = Math.floor(c[1] - sy); py <= c[1] + sy; py++)
        for (let px = Math.floor(c[0] - sx); px <= c[0] + sx; px++) {
          if (px < 0 || py < 0 || px >= width || py >= height) continue;
          if (((px - c[0]) / sx) ** 2 + ((py - c[1]) / sy) ** 2 > 1) continue;
          const o = (py * width + px) * 3;
          for (let d = 0; d < 3; d++) color[o + d] = color[o + d] * 0.35 + [0.06, 0.04, 0.08][d] * 0.65;
        }
    };
    blob(0.076, 0.023, 0.04, 0.055);
    blob(-0.076, 0.023, 0.04, 0.055);
    blob(0.076, 0.078, 0.042, 0.012);
    blob(-0.076, 0.078, 0.042, 0.012);
    blob(0, -0.084, 0.064, 0.012);
  }
  return { width, height, color };
}

/** Several views side by side → PNG bytes. */
export function previewPng(buf, { face, faceSize } = {}) {
  const model = loadModel(buf);
  const views = [0, 38, 90].map((yaw) => render(model, { yaw, face, faceSize }));
  const W = views.reduce((s, v) => s + v.width, 0), H = views[0].height;
  const px = new Uint8ClampedArray(W * H * 4);
  let x0 = 0;
  for (const v of views) {
    for (let y = 0; y < v.height; y++)
      for (let x = 0; x < v.width; x++) {
        const o = ((y * W) + x0 + x) * 4, i = (y * v.width + x) * 3;
        // A little tone curve so it reads like the app (gamma-ish).
        for (let c = 0; c < 3; c++) px[o + c] = Math.round(255 * Math.pow(clamp(v.color[i + c]), 1 / 1.25));
        px[o + 3] = 255;
      }
    x0 += v.width;
  }
  return encodePng(W, H, px);
}

if (import.meta.url === `file://${process.argv[1].replace(/\\/g, '/').replace(/^([A-Za-z]):/, '/$1:')}` || process.argv[1]?.endsWith('preview.mjs')) {
  const args = process.argv.slice(2);
  const [input, output] = args.filter((a, i) => !a.startsWith('--') && !(i > 0 && args[i - 1].startsWith('--')));
  const arg = (name) => {
    const i = process.argv.indexOf(name);
    return i >= 0 ? Number(process.argv[i + 1]) : undefined;
  };
  if (input && output) {
    writeFileSync(output, previewPng(readFileSync(input), { face: arg('--face'), faceSize: arg('--face-size') ?? 1 }));
    console.log(`preview -> ${output}`);
  }
}
