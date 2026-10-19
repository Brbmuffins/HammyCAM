/**
 * A small kit for building avatars in code (no AI service, no credits): shapes, a texture painter and a GLB
 * writer. Node only, no npm packages (Node's zlib does the PNG compression).
 *
 * Conventions (they match how the app imports models, see MuffinPrototypeSetup.BakeAvatar with Yaw = 0):
 * - Metres, +Y up, the model stands on y = 0 (the app rescales it to the avatar height anyway).
 * - The FRONT faces glTF -Z (that is the side the app's camera sees). Seen from the front, screen right is -X.
 * - Texture v = 0 is the top of the image. Painted textures read correctly from the front with either UV mode:
 *   'wrap' (round shapes: the front is the middle of the image, u = 0.5, the seam at the back) or 'front'
 *   (a flat print from the front, see planarUV).
 * - A material named "glass..." is swapped for the app's own see-through glass when the avatar is baked.
 * - Material colours (color) are LINEAR, as glTF says: 0.2 shows as about 0.48 in the app (and the preview). For
 *   a saturated colour, either use high/low values or paint it into a texture (textures are sRGB, shown as painted).
 */
import { deflateSync, inflateSync } from 'node:zlib';

// ---------- maths ----------------------------------------------------------------------------------------

export const TAU = Math.PI * 2;
export const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
export const lerp = (a, b, t) => a + (b - a) * t;
export const smooth = (a, b, x) => {
  const t = clamp((x - a) / (b - a));
  return t * t * (3 - 2 * t);
};
export const deg = (d) => (d * Math.PI) / 180;

/** A seeded random number generator: rng(seed)() gives 0..1. */
export function rng(seed = 1) {
  let s = seed >>> 0 || 1;
  return () => ((s = (Math.imul(s, 1664525) + 1013904223) >>> 0) / 4294967296);
}

function hash3(x, y, z, seed) {
  let h = Math.imul(x, 374761393) ^ Math.imul(y, 668265263) ^ Math.imul(z, 1274126177) ^ Math.imul(seed + 1, 1442695041);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** Smooth value noise, 0..1. */
export function noise3(x, y, z, seed = 0) {
  const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z);
  const fx = x - xi, fy = y - yi, fz = z - zi;
  const u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy), w = fz * fz * (3 - 2 * fz);
  const c = (dx, dy, dz) => hash3(xi + dx, yi + dy, zi + dz, seed);
  const x00 = lerp(c(0, 0, 0), c(1, 0, 0), u), x10 = lerp(c(0, 1, 0), c(1, 1, 0), u);
  const x01 = lerp(c(0, 0, 1), c(1, 0, 1), u), x11 = lerp(c(0, 1, 1), c(1, 1, 1), u);
  return lerp(lerp(x00, x10, v), lerp(x01, x11, v), w);
}
export const noise2 = (x, y, seed = 0) => noise3(x, y, 0.37, seed);

/** Fractal noise (several octaves), about 0..1. */
export function fbm3(x, y, z, { octaves = 4, seed = 0, gain = 0.5, lacunarity = 2 } = {}) {
  let sum = 0, amp = 1, norm = 0, f = 1;
  for (let o = 0; o < octaves; o++) {
    sum += noise3(x * f, y * f, z * f, seed + o * 17) * amp;
    norm += amp;
    amp *= gain;
    f *= lacunarity;
  }
  return sum / norm;
}
export const fbm2 = (x, y, opts) => fbm3(x, y, 0.37, opts);

const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const norm = (a) => {
  const l = Math.hypot(a[0], a[1], a[2]) || 1;
  return [a[0] / l, a[1] / l, a[2] / l];
};
export const vec = { sub, cross, dot, norm };

/** Rotation matrix (3x3, row-major) from Euler degrees, applied X then Y then Z. */
function rotation([rx = 0, ry = 0, rz = 0]) {
  const [cx, sx, cy, sy, cz, sz] = [Math.cos(deg(rx)), Math.sin(deg(rx)), Math.cos(deg(ry)), Math.sin(deg(ry)), Math.cos(deg(rz)), Math.sin(deg(rz))];
  const X = [1, 0, 0, 0, cx, -sx, 0, sx, cx], Y = [cy, 0, sy, 0, 1, 0, -sy, 0, cy], Z = [cz, -sz, 0, sz, cz, 0, 0, 0, 1];
  const mul = (a, b) => {
    const r = new Array(9).fill(0);
    for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) for (let k = 0; k < 3; k++) r[i * 3 + j] += a[i * 3 + k] * b[k * 3 + j];
    return r;
  };
  return mul(Z, mul(Y, X));
}
const apply = (m, p) => [m[0] * p[0] + m[1] * p[1] + m[2] * p[2], m[3] * p[0] + m[4] * p[1] + m[5] * p[2], m[6] * p[0] + m[7] * p[1] + m[8] * p[2]];

// ---------- meshes ---------------------------------------------------------------------------------------

/** Triangles with positions and uvs (normals are worked out when the GLB is written). Front faces wind CCW. */
export class Mesh {
  constructor() {
    this.positions = [];
    this.uvs = [];
    this.indices = [];
  }
  get vertexCount() {
    return this.positions.length / 3;
  }
  vertex(p, uv = [0, 0]) {
    this.positions.push(p[0], p[1], p[2]);
    this.uvs.push(uv[0], uv[1]);
    return this.vertexCount - 1;
  }
  tri(a, b, c) {
    this.indices.push(a, b, c);
    return this;
  }
  quad(a, b, c, d) {
    return this.tri(a, b, c).tri(a, c, d);
  }
  point(i) {
    return [this.positions[i * 3], this.positions[i * 3 + 1], this.positions[i * 3 + 2]];
  }
  setPoint(i, p) {
    this.positions[i * 3] = p[0];
    this.positions[i * 3 + 1] = p[1];
    this.positions[i * 3 + 2] = p[2];
  }
  /** Adds another mesh's triangles to this one. */
  append(other) {
    const base = this.vertexCount;
    this.positions.push(...other.positions);
    this.uvs.push(...other.uvs);
    for (const i of other.indices) this.indices.push(i + base);
    return this;
  }
  clone() {
    const m = new Mesh();
    m.positions = [...this.positions];
    m.uvs = [...this.uvs];
    m.indices = [...this.indices];
    return m;
  }
  /** Moves every point: fn([x, y, z], index) returns the new point. */
  map(fn) {
    for (let i = 0; i < this.vertexCount; i++) this.setPoint(i, fn(this.point(i), i));
    return this;
  }
  /** Scale, then rotate (Euler degrees), then move. */
  transform({ scale = [1, 1, 1], rotate = [0, 0, 0], translate = [0, 0, 0] } = {}) {
    const s = typeof scale === 'number' ? [scale, scale, scale] : scale;
    const r = rotation(rotate);
    const flips = s[0] * s[1] * s[2] < 0;
    this.map((p) => {
      const q = apply(r, [p[0] * s[0], p[1] * s[1], p[2] * s[2]]);
      return [q[0] + translate[0], q[1] + translate[1], q[2] + translate[2]];
    });
    if (flips) this.flip();
    return this;
  }
  /** Turns every triangle inside out. */
  flip() {
    for (let k = 0; k < this.indices.length; k += 3) [this.indices[k + 1], this.indices[k + 2]] = [this.indices[k + 2], this.indices[k + 1]];
    return this;
  }
  bounds() {
    const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < this.positions.length; i += 3)
      for (let d = 0; d < 3; d++) {
        min[d] = Math.min(min[d], this.positions[i + d]);
        max[d] = Math.max(max[d], this.positions[i + d]);
      }
    return { min, max };
  }
  /**
   * A flat print from the front: u runs from screen left to right (that is +X to -X), v from the top down, over
   * the given box (default: the mesh's own bounds).
   */
  planarUV(box = this.bounds()) {
    const { min, max } = box;
    for (let i = 0; i < this.vertexCount; i++) {
      const [x, y] = this.point(i);
      this.uvs[i * 2] = (max[0] - x) / (max[0] - min[0] || 1);
      this.uvs[i * 2 + 1] = (max[1] - y) / (max[1] - min[1] || 1);
    }
    return this;
  }
}

/** Where a ring's i-th of n points sits: it starts at the back (+Z) so the wrap seam is hidden there. */
export const ringAngle = (i, n) => Math.PI / 2 + (TAU * i) / n;

/**
 * Skins rings of points together. rings[j] is an array of n + 1 points [x, y, z] going round (the last repeats
 * the first, for the uv seam), made with ringAngle so the faces point outward; rows go upward. Each ring may also
 * carry a v (0 top .. 1 bottom of the texture) as rings[j].v. u follows 'wrap' (front in the middle).
 */
export function loft(rings, { capBottom = false, capTop = false } = {}) {
  const mesh = new Mesh();
  const n = rings[0].length - 1;
  const rows = rings.map((ring, j) => {
    const v = ring.v ?? 1 - j / (rings.length - 1);
    return ring.map((p, i) => mesh.vertex(p, [1 - i / n, v]));
  });
  for (let j = 0; j + 1 < rows.length; j++)
    for (let i = 0; i < n; i++) {
      const p = rows[j][i], q = rows[j][i + 1], r = rows[j + 1][i + 1], s = rows[j + 1][i];
      mesh.tri(p, r, q).tri(p, s, r);
    }
  const cap = (ring, up) => {
    const c = ring.slice(0, n).reduce((a, p) => [a[0] + p[0] / n, a[1] + p[1] / n, a[2] + p[2] / n], [0, 0, 0]);
    const centre = mesh.vertex(c, [0.5, up ? 0 : 1]);
    const edge = ring.map((p, i) => mesh.vertex(p, [1 - i / n, up ? 0 : 1]));
    for (let i = 0; i < n; i++) up ? mesh.tri(centre, edge[i + 1], edge[i]) : mesh.tri(centre, edge[i], edge[i + 1]);
  };
  if (capBottom) cap(rings[0], false);
  if (capTop) cap(rings[rings.length - 1], true);
  return mesh;
}

/**
 * A shape turned on a lathe. profile: [[radius, y], ...] from the bottom up (radius 0 at an end closes it).
 * A point given as [r, y, true] is a sharp edge (the row is doubled so the shading creases there).
 * section(t, angle, r, y) can bend the round cross-section into something else (returns [x, z]); t is 0..1 up the
 * profile. segments: points round.
 */
export function lathe(profile, { segments = 48, section, v } = {}) {
  const rings = [];
  const total = profile.length - 1;
  profile.forEach(([r, y, sharp], j) => {
    const t = j / total;
    const ring = [];
    for (let i = 0; i <= segments; i++) {
      const a = ringAngle(i % segments, segments);
      const [x, z] = section ? section(t, a, r, y) : [r * Math.cos(a), r * Math.sin(a)];
      ring.push([x, y, z]);
    }
    ring.v = v ? v(t, y) : undefined;
    rings.push(ring);
    if (sharp && j > 0 && j < total) {
      const twin = ring.map((p) => [...p]);
      twin.v = ring.v;
      rings.push(twin);
    }
  });
  return loft(rings);
}

/** A superellipse cross-section for lathe's `section`: squarish (power 2 = round, higher = boxier). */
export function squircle(halfX, halfZ, power = 3) {
  const sp = (c, p) => Math.sign(c) * Math.abs(c) ** p;
  return (t, a, r) => [halfX(t, r) * sp(Math.cos(a), 2 / power), halfZ(t, r) * sp(Math.sin(a), 2 / power)];
}

/** A ball (or an egg/ellipsoid with scale), centre at the origin. */
export function sphere(radius = 0.5, { segments = 40, rows = 24, scale = [1, 1, 1] } = {}) {
  const profile = [];
  for (let j = 0; j <= rows; j++) {
    const a = -Math.PI / 2 + (Math.PI * j) / rows;
    profile.push([radius * Math.cos(a), radius * Math.sin(a)]);
  }
  profile[0][0] = 0;
  profile[rows][0] = 0;
  return lathe(profile, { segments }).transform({ scale });
}

/** A closed cylinder standing on y = 0 (sharp rims). */
export function cylinder(radius, height, { segments = 40, radiusTop = radius } = {}) {
  return lathe([[0, 0], [radius, 0, true], [radiusTop, height, true], [0, height]], { segments });
}

/** A box centred on the origin with rounded edges (round = corner radius; 0 = sharp). */
export function box(w, h, d, { round = 0, steps = 4 } = {}) {
  const mesh = new Mesh();
  const half = [w / 2, h / 2, d / 2];
  const inner = half.map((x) => Math.max(0, x - round));
  // Each face is a grid; its points are pushed out to the rounded shape.
  const n = Math.max(1, round > 0 ? steps * 2 : 1);
  const faces = [
    [[0, 0, -1], [-1, 0, 0], [0, 1, 0]], // front (-Z): u toward screen right (-X)
    [[0, 0, 1], [1, 0, 0], [0, 1, 0]],
    [[-1, 0, 0], [0, 0, 1], [0, 1, 0]],
    [[1, 0, 0], [0, 0, -1], [0, 1, 0]],
    [[0, 1, 0], [-1, 0, 0], [0, 0, 1]],
    [[0, -1, 0], [-1, 0, 0], [0, 0, -1]],
  ];
  for (const [normal, right, up] of faces) {
    const idx = [];
    for (let j = 0; j <= n; j++)
      for (let i = 0; i <= n; i++) {
        const s = (i / n) * 2 - 1, t = (j / n) * 2 - 1;
        let p = [0, 1, 2].map((k) => normal[k] * half[k] + right[k] * s * half[k] + up[k] * t * half[k]);
        if (round > 0) {
          const c = p.map((x, k) => clamp(x, -inner[k], inner[k]));
          const dir = norm(sub(p, c));
          p = c.map((x, k) => x + dir[k] * round);
        }
        idx.push(mesh.vertex(p, [i / n, 1 - j / n]));
      }
    for (let j = 0; j < n; j++)
      for (let i = 0; i < n; i++) {
        const a = idx[j * (n + 1) + i], b = idx[j * (n + 1) + i + 1], c = idx[(j + 1) * (n + 1) + i + 1], d = idx[(j + 1) * (n + 1) + i];
        mesh.tri(a, c, b).tri(a, d, c);
      }
  }
  // Make sure the faces point outward (the grid's handedness differs per face).
  return outward(mesh);
}

/** A tube along a path of points (a hose, a wick, a string), radius r. */
export function tube(path, r, { segments = 12, radii } = {}) {
  const rings = [];
  // The ring's frame is carried along the path (each step keeps as much of the last one as it can), so it
  // never flips round where the path turns through vertical.
  let a = null;
  for (let j = 0; j < path.length; j++) {
    const p = path[j];
    const ahead = path[Math.min(path.length - 1, j + 1)], behind = path[Math.max(0, j - 1)];
    const t = norm(sub(ahead, behind));
    if (!a) a = norm(cross(Math.abs(t[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0], t));
    else {
      const d = dot(a, t);
      const kept = [a[0] - t[0] * d, a[1] - t[1] * d, a[2] - t[2] * d];
      if (Math.hypot(...kept) > 1e-6) a = norm(kept);
    }
    const b = cross(t, a);
    const rr = radii ? radii[j] : r;
    const ring = [];
    for (let i = 0; i <= segments; i++) {
      const ang = (TAU * (i % segments)) / segments;
      ring.push([0, 1, 2].map((k) => p[k] + (a[k] * Math.cos(ang) + b[k] * Math.sin(ang)) * rr));
    }
    rings.push(ring);
  }
  // (a, b, path) is a right-handed frame, the lathe's (x, z, y) a left-handed one: the same skinning comes out
  // inside out here, so it is flipped (a curved hose can't be fixed by looking from the middle of its bounds).
  return loft(rings, { capBottom: true, capTop: true }).flip();
}

/** A flat shape (outline in x, y, counter-clockwise or not) pushed out to a thickness along z, centred on z = 0. */
export function extrude(outline, depth) {
  const mesh = new Mesh();
  const n = outline.length;
  const xs = outline.map((p) => p[0]), ys = outline.map((p) => p[1]);
  const bx = [Math.min(...xs), Math.max(...xs)], by = [Math.min(...ys), Math.max(...ys)];
  const uv = ([x, y]) => [(bx[1] - x) / (bx[1] - bx[0] || 1), (by[1] - y) / (by[1] - by[0] || 1)];
  const front = outline.map((p) => mesh.vertex([p[0], p[1], -depth / 2], uv(p)));
  const back = outline.map((p) => mesh.vertex([p[0], p[1], depth / 2], uv(p)));
  // Fan from the middle (fine for star-like and convex outlines).
  const c = [xs.reduce((a, b) => a + b) / n, ys.reduce((a, b) => a + b) / n];
  const fc = mesh.vertex([c[0], c[1], -depth / 2], uv(c)), bc = mesh.vertex([c[0], c[1], depth / 2], uv(c));
  for (let i = 0; i < n; i++) {
    const j = (i + 1) % n;
    mesh.tri(fc, front[i], front[j]);
    mesh.tri(bc, back[j], back[i]);
    const a = mesh.vertex([outline[i][0], outline[i][1], -depth / 2], [i / n, 0]), b = mesh.vertex([outline[j][0], outline[j][1], -depth / 2], [(i + 1) / n, 0]);
    const d = mesh.vertex([outline[i][0], outline[i][1], depth / 2], [i / n, 1]), e = mesh.vertex([outline[j][0], outline[j][1], depth / 2], [(i + 1) / n, 1]);
    mesh.tri(a, b, e).tri(a, e, d);
  }
  return outward(mesh);
}

/**
 * Flips any triangle whose face points toward the mesh's middle. Only for shapes that are roughly convex around
 * their centre (boxes, tubes, extrusions); lathe and loft already wind outward.
 */
export function outward(mesh, centre) {
  const { min, max } = mesh.bounds();
  const c = centre ?? [(min[0] + max[0]) / 2, (min[1] + max[1]) / 2, (min[2] + max[2]) / 2];
  for (let k = 0; k < mesh.indices.length; k += 3) {
    const [a, b, d] = [mesh.indices[k], mesh.indices[k + 1], mesh.indices[k + 2]];
    const pa = mesh.point(a), pb = mesh.point(b), pd = mesh.point(d);
    const n = cross(sub(pb, pa), sub(pd, pa));
    const mid = [(pa[0] + pb[0] + pd[0]) / 3 - c[0], (pa[1] + pb[1] + pd[1]) / 3 - c[1], (pa[2] + pb[2] + pd[2]) / 3 - c[2]];
    if (dot(n, mid) < 0) [mesh.indices[k + 1], mesh.indices[k + 2]] = [d, b];
  }
  return mesh;
}

/** Pushes points along their outward direction from an axis or centre by fn(p) metres (lumps, dents). */
export function bulge(mesh, fn, { axis = 'y', centre = [0, 0, 0] } = {}) {
  return mesh.map((p) => {
    let d = axis === 'y' ? [p[0] - centre[0], 0, p[2] - centre[2]] : sub(p, centre);
    const l = Math.hypot(d[0], d[1], d[2]);
    if (l < 1e-6) return p;
    d = [d[0] / l, d[1] / l, d[2] / l];
    const o = fn(p);
    return [p[0] + d[0] * o, p[1] + d[1] * o, p[2] + d[2] * o];
  });
}

// ---------- normals --------------------------------------------------------------------------------------

/**
 * Smooth normals, area-weighted. Points that sit in the same place (a uv seam) share their normal when it bends
 * less than creaseDegrees, so seams don't show but sharp rims stay sharp.
 */
export function computeNormals(mesh, creaseDegrees = 50) {
  const n = mesh.vertexCount;
  const acc = new Float64Array(n * 3);
  for (let k = 0; k < mesh.indices.length; k += 3) {
    const [a, b, c] = [mesh.indices[k], mesh.indices[k + 1], mesh.indices[k + 2]];
    const f = cross(sub(mesh.point(b), mesh.point(a)), sub(mesh.point(c), mesh.point(a)));
    for (const v of [a, b, c]) for (let d = 0; d < 3; d++) acc[v * 3 + d] += f[d];
  }
  const own = [];
  for (let v = 0; v < n; v++) own.push(norm([acc[v * 3], acc[v * 3 + 1], acc[v * 3 + 2]]));
  const groups = new Map();
  const key = (p) => p.map((x) => Math.round(x * 1e5)).join(',');
  for (let v = 0; v < n; v++) {
    const k = key(mesh.point(v));
    if (!groups.has(k)) groups.set(k, []);
    groups.get(k).push(v);
  }
  const limit = Math.cos(deg(creaseDegrees));
  const out = new Float32Array(n * 3);
  for (const members of groups.values())
    for (const v of members) {
      let s = [0, 0, 0];
      for (const w of members) if (dot(own[v], own[w]) >= limit) s = [s[0] + acc[w * 3], s[1] + acc[w * 3 + 1], s[2] + acc[w * 3 + 2]];
      const m = norm(s);
      out[v * 3] = m[0];
      out[v * 3 + 1] = m[1];
      out[v * 3 + 2] = m[2];
    }
  return out;
}

// ---------- textures -------------------------------------------------------------------------------------

/** An RGBA picture to paint on (colours are [r, g, b] 0..255; a is 0..1). */
export class Canvas {
  constructor(width = 512, height = 512, fill = [255, 255, 255]) {
    this.width = width;
    this.height = height;
    this.px = new Uint8ClampedArray(width * height * 4);
    this.rect(0, 0, width, height, fill);
  }
  blend(x, y, [r, g, b], a = 1) {
    x = Math.floor(x);
    y = Math.floor(y);
    if (x < 0 || y < 0 || x >= this.width || y >= this.height || a <= 0) return;
    const o = (y * this.width + x) * 4;
    a = Math.min(1, a);
    this.px[o] = this.px[o] * (1 - a) + r * a;
    this.px[o + 1] = this.px[o + 1] * (1 - a) + g * a;
    this.px[o + 2] = this.px[o + 2] * (1 - a) + b * a;
    this.px[o + 3] = 255;
  }
  get(x, y) {
    x = clamp(Math.floor(x), 0, this.width - 1);
    y = clamp(Math.floor(y), 0, this.height - 1);
    const o = (y * this.width + x) * 4;
    return [this.px[o], this.px[o + 1], this.px[o + 2]];
  }
  rect(x0, y0, x1, y1, colour, a = 1) {
    for (let y = Math.max(0, Math.floor(y0)); y < Math.min(this.height, Math.ceil(y1)); y++)
      for (let x = Math.max(0, Math.floor(x0)); x < Math.min(this.width, Math.ceil(x1)); x++) this.blend(x, y, colour, a);
    return this;
  }
  /** An ellipse; soft (0..1) fades its edge (1 = a soft blob all the way in). */
  ellipse(cx, cy, rx, ry, colour, a = 1, soft = 0) {
    for (let y = Math.floor(cy - ry - 1); y <= Math.ceil(cy + ry + 1); y++)
      for (let x = Math.floor(cx - rx - 1); x <= Math.ceil(cx + rx + 1); x++) {
        const d = Math.hypot((x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry);
        const edge = soft > 0 ? 1 - smooth(1 - soft, 1, d) : clamp((1 - d) * Math.min(rx, ry) + 0.5);
        if (edge > 0) this.blend(x, y, colour, a * edge);
      }
    return this;
  }
  disc(cx, cy, r, colour, a = 1, soft = 0) {
    return this.ellipse(cx, cy, r, r, colour, a, soft);
  }
  /** A filled polygon ([[x, y], ...]), anti-aliased by 4x4 supersampling on its edge. */
  poly(points, colour, a = 1) {
    const xs = points.map((p) => p[0]), ys = points.map((p) => p[1]);
    const inside = (x, y) => {
      let c = false;
      for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
        const [xi, yi] = points[i], [xj, yj] = points[j];
        if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) c = !c;
      }
      return c;
    };
    for (let y = Math.floor(Math.min(...ys)); y <= Math.ceil(Math.max(...ys)); y++)
      for (let x = Math.floor(Math.min(...xs)); x <= Math.ceil(Math.max(...xs)); x++) {
        let hit = 0;
        for (let sy = 0; sy < 4; sy++) for (let sx = 0; sx < 4; sx++) if (inside(x + (sx + 0.5) / 4, y + (sy + 0.5) / 4)) hit++;
        if (hit) this.blend(x, y, colour, (a * hit) / 16);
      }
    return this;
  }
  /** A thick line through points, round ends. */
  stroke(points, width, colour, a = 1) {
    const half = width / 2;
    const xs = points.map((p) => p[0]), ys = points.map((p) => p[1]);
    for (let y = Math.floor(Math.min(...ys) - half - 1); y <= Math.ceil(Math.max(...ys) + half + 1); y++)
      for (let x = Math.floor(Math.min(...xs) - half - 1); x <= Math.ceil(Math.max(...xs) + half + 1); x++) {
        let d = Infinity;
        for (let s = 0; s < points.length - 1; s++) {
          const [ax, ay] = points[s], [bx, by] = points[s + 1];
          const dx = bx - ax, dy = by - ay;
          const t = clamp(((x + 0.5 - ax) * dx + (y + 0.5 - ay) * dy) / (dx * dx + dy * dy || 1));
          d = Math.min(d, Math.hypot(x + 0.5 - (ax + t * dx), y + 0.5 - (ay + t * dy)));
        }
        const cover = clamp(half + 0.5 - d);
        if (cover > 0) this.blend(x, y, colour, a * cover);
      }
    return this;
  }
  /** Block capitals (A-Z, 0-9, a few signs), centred on cx at baseline-top y, letters `height` tall. */
  text(str, cx, y, height, colour, { weight = 0.16, spacing = 0.22, a = 1, align = 'center' } = {}) {
    const glyphs = [...str.toUpperCase()].map((c) => FONT[c] ?? FONT['?']);
    const gap = spacing * height;
    const width = glyphs.reduce((s, g) => s + g.w * height, 0) + gap * (glyphs.length - 1);
    let x = align === 'center' ? cx - width / 2 : align === 'right' ? cx - width : cx;
    for (const g of glyphs) {
      for (const line of g.lines) this.stroke(line.map(([gx, gy]) => [x + gx * height, y + gy * height]), weight * height, colour, a);
      x += g.w * height + gap;
    }
    return this;
  }
  /** Paint every pixel: fn(u, v, [r, g, b]) returns [r, g, b] or [r, g, b, a] (u, v 0..1, v down). */
  shade(fn) {
    for (let y = 0; y < this.height; y++)
      for (let x = 0; x < this.width; x++) {
        const c = this.get(x, y);
        const out = fn((x + 0.5) / this.width, (y + 0.5) / this.height, c);
        if (out) this.blend(x, y, out, out.length > 3 ? out[3] : 1);
      }
    return this;
  }
  /** Speckles: count dots of size range, colour, opacity (paper grain, glitter, seeds). */
  speckle(count, [r0, r1], colour, a = 0.3, seed = 7) {
    const R = rng(seed);
    for (let i = 0; i < count; i++) this.disc(R() * this.width, R() * this.height, lerp(r0, r1, R()), colour, a, 0.6);
    return this;
  }
  png() {
    return encodePng(this.width, this.height, this.px);
  }
}

const arc = (cx, cy, rx, ry, from, to, steps = 14) =>
  Array.from({ length: steps + 1 }, (_, k) => {
    const t = lerp(from, to, k / steps);
    return [cx + rx * Math.cos(t), cy + ry * Math.sin(t)];
  });
const P = Math.PI;
/** Block letters: polylines in a box `w` wide and 1 tall, y down. */
const FONT = {
  A: { w: 0.66, lines: [[[0, 1], [0.33, 0], [0.66, 1]], [[0.12, 0.62], [0.54, 0.62]]] },
  B: { w: 0.6, lines: [[[0, 1], [0, 0], [0.38, 0], ...arc(0.38, 0.24, 0.2, 0.24, -P / 2, P / 2), [0, 0.48], [0.4, 0.48], ...arc(0.4, 0.74, 0.22, 0.26, -P / 2, P / 2), [0, 1]]] },
  C: { w: 0.62, lines: [arc(0.36, 0.5, 0.36, 0.5, -P * 0.28, -P * 1.72, 20)] },
  D: { w: 0.64, lines: [[[0, 0], [0, 1], [0.28, 1], ...arc(0.28, 0.5, 0.36, 0.5, P / 2, -P / 2), [0, 0]]] },
  E: { w: 0.56, lines: [[[0.56, 0], [0, 0], [0, 1], [0.56, 1]], [[0, 0.48], [0.46, 0.48]]] },
  F: { w: 0.56, lines: [[[0, 1], [0, 0], [0.56, 0]], [[0, 0.46], [0.46, 0.46]]] },
  G: { w: 0.66, lines: [[...arc(0.36, 0.5, 0.36, 0.5, -P * 0.25, -P * 1.85, 20), [0.7, 0.55], [0.4, 0.55]]] },
  H: { w: 0.62, lines: [[[0, 0], [0, 1]], [[0.62, 0], [0.62, 1]], [[0, 0.5], [0.62, 0.5]]] },
  I: { w: 0.08, lines: [[[0.04, 0], [0.04, 1]]] },
  J: { w: 0.5, lines: [[[0.5, 0], [0.5, 0.7], ...arc(0.25, 0.7, 0.25, 0.3, 0, P)]] },
  K: { w: 0.6, lines: [[[0, 0], [0, 1]], [[0.6, 0], [0, 0.58]], [[0.2, 0.42], [0.62, 1]]] },
  L: { w: 0.52, lines: [[[0, 0], [0, 1], [0.52, 1]]] },
  M: { w: 0.78, lines: [[[0, 1], [0, 0], [0.39, 0.62], [0.78, 0], [0.78, 1]]] },
  N: { w: 0.62, lines: [[[0, 1], [0, 0], [0.62, 1], [0.62, 0]]] },
  O: { w: 0.72, lines: [arc(0.36, 0.5, 0.36, 0.5, 0, P * 2, 32)] },
  P: { w: 0.58, lines: [[[0, 1], [0, 0], [0.34, 0], ...arc(0.34, 0.27, 0.24, 0.27, -P / 2, P / 2), [0, 0.54]]] },
  Q: { w: 0.72, lines: [arc(0.36, 0.5, 0.36, 0.5, 0, P * 2, 32), [[0.44, 0.7], [0.76, 1.04]]] },
  R: { w: 0.62, lines: [[[0, 1], [0, 0], [0.36, 0], ...arc(0.36, 0.26, 0.24, 0.26, -P / 2, P / 2), [0, 0.52]], [[0.3, 0.52], [0.64, 1]]] },
  S: { w: 0.58, lines: [[...arc(0.29, 0.25, 0.27, 0.25, -P * 0.1, -P * 1.5, 12), ...arc(0.29, 0.75, 0.29, 0.25, -P / 2, P * 0.85, 12)]] },
  T: { w: 0.62, lines: [[[0, 0], [0.62, 0]], [[0.31, 0], [0.31, 1]]] },
  U: { w: 0.62, lines: [[[0, 0], [0, 0.66], ...arc(0.31, 0.66, 0.31, 0.34, P, 0), [0.62, 0]]] },
  V: { w: 0.64, lines: [[[0, 0], [0.32, 1], [0.64, 0]]] },
  W: { w: 0.9, lines: [[[0, 0], [0.22, 1], [0.45, 0.35], [0.68, 1], [0.9, 0]]] },
  X: { w: 0.62, lines: [[[0, 0], [0.62, 1]], [[0.62, 0], [0, 1]]] },
  Y: { w: 0.62, lines: [[[0, 0], [0.31, 0.5], [0.62, 0]], [[0.31, 0.5], [0.31, 1]]] },
  Z: { w: 0.6, lines: [[[0, 0], [0.6, 0], [0, 1], [0.6, 1]]] },
  0: { w: 0.6, lines: [arc(0.3, 0.5, 0.3, 0.5, 0, P * 2, 28)] },
  1: { w: 0.3, lines: [[[0, 0.18], [0.22, 0], [0.22, 1]]] },
  2: { w: 0.58, lines: [[...arc(0.29, 0.28, 0.28, 0.28, -P, 0.15), [0, 1], [0.6, 1]]] },
  3: { w: 0.56, lines: [[...arc(0.27, 0.25, 0.26, 0.25, -P * 0.9, P / 2), ...arc(0.27, 0.74, 0.29, 0.26, -P / 2, P * 0.9)]] },
  4: { w: 0.6, lines: [[[0.46, 1], [0.46, 0], [0, 0.7], [0.62, 0.7]]] },
  5: { w: 0.56, lines: [[[0.54, 0], [0.06, 0], [0.02, 0.44], ...arc(0.26, 0.68, 0.3, 0.32, -P * 0.8, P * 0.85)]] },
  6: { w: 0.58, lines: [[...arc(0.3, 0.7, 0.28, 0.3, 0, P * 2, 24), [0.02, 0.6], ...arc(0.4, 0.4, 0.38, 0.4, P, P * 1.6)]] },
  7: { w: 0.56, lines: [[[0, 0], [0.56, 0], [0.18, 1]]] },
  8: { w: 0.58, lines: [arc(0.29, 0.25, 0.24, 0.25, 0, P * 2, 20), arc(0.29, 0.74, 0.29, 0.26, 0, P * 2, 22)] },
  9: { w: 0.58, lines: [[...arc(0.28, 0.3, 0.28, 0.3, 0, P * 2, 24), [0.56, 0.4], [0.4, 1]]] },
  '!': { w: 0.1, lines: [[[0.05, 0], [0.05, 0.68]], [[0.05, 0.96], [0.05, 1]]] },
  '?': { w: 0.5, lines: [[...arc(0.25, 0.25, 0.25, 0.25, -P, P * 0.4), [0.25, 0.68]], [[0.25, 0.96], [0.25, 1]]] },
  '.': { w: 0.1, lines: [[[0.05, 0.96], [0.05, 1]]] },
  '-': { w: 0.4, lines: [[[0, 0.52], [0.4, 0.52]]] },
  '¢': { w: 0.56, lines: [arc(0.3, 0.55, 0.26, 0.34, -P * 0.25, -P * 1.75, 16), [[0.3, 0.06], [0.3, 1.04]]] },
  '%': { w: 0.66, lines: [arc(0.14, 0.18, 0.13, 0.16, 0, P * 2, 12), arc(0.52, 0.82, 0.13, 0.16, 0, P * 2, 12), [[0.6, 0], [0.06, 1]]] },
  ' ': { w: 0.3, lines: [] },
};

// ---------- PNG ------------------------------------------------------------------------------------------

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
/** RGBA pixels → PNG bytes. */
export function encodePng(width, height, px) {
  const stride = width * 4 + 1;
  const raw = Buffer.alloc(stride * height);
  for (let y = 0; y < height; y++) {
    raw[y * stride] = 0;
    Buffer.from(px.buffer, px.byteOffset + y * width * 4, width * 4).copy(raw, y * stride + 1);
  }
  const head = Buffer.alloc(13);
  head.writeUInt32BE(width, 0);
  head.writeUInt32BE(height, 4);
  head[8] = 8;
  head[9] = 6;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', head), chunk('IDAT', deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

/** PNG bytes (8-bit RGB or RGBA, not interlaced) → { width, height, px (RGBA) }. */
export function decodePng(buf) {
  let o = 8, width = 0, height = 0, type = 6;
  const idat = [];
  while (o < buf.length) {
    const len = buf.readUInt32BE(o), kind = buf.toString('latin1', o + 4, o + 8);
    const data = buf.subarray(o + 8, o + 8 + len);
    if (kind === 'IHDR') {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      type = data[9];
    } else if (kind === 'IDAT') idat.push(data);
    o += 12 + len;
  }
  const bpp = type === 6 ? 4 : 3;
  const raw = inflateSync(Buffer.concat(idat));
  const stride = width * bpp;
  const out = new Uint8ClampedArray(width * height * 4);
  const prev = new Uint8Array(stride), cur = new Uint8Array(stride);
  for (let y = 0; y < height; y++) {
    const f = raw[y * (stride + 1)];
    for (let x = 0; x < stride; x++) {
      const v = raw[y * (stride + 1) + 1 + x];
      const a = x >= bpp ? cur[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
      let p;
      if (f === 0) p = v;
      else if (f === 1) p = v + a;
      else if (f === 2) p = v + b;
      else if (f === 3) p = v + ((a + b) >> 1);
      else {
        const pa = Math.abs(b - c), pb = Math.abs(a - c), pc = Math.abs(a + b - 2 * c);
        p = v + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
      }
      cur[x] = p & 255;
    }
    for (let x = 0; x < width; x++) {
      out[(y * width + x) * 4] = cur[x * bpp];
      out[(y * width + x) * 4 + 1] = cur[x * bpp + 1];
      out[(y * width + x) * 4 + 2] = cur[x * bpp + 2];
      out[(y * width + x) * 4 + 3] = bpp === 4 ? cur[x * bpp + 3] : 255;
    }
    prev.set(cur);
  }
  return { width, height, px: out };
}

// ---------- GLB ------------------------------------------------------------------------------------------

/**
 * Writes a model as GLB bytes. parts: [{ mesh, material }] where material is
 * { name, color: [r, g, b, a?] (0..1), texture?: Canvas, roughness = 0.85, metallic = 0, blend?: bool,
 *   doubleSided?: bool, emissive?: [r, g, b] }. Parts sharing a material (same object or same name) are merged.
 */
export function writeGlb(parts, { name = 'avatar', crease = 50 } = {}) {
  const byMaterial = new Map();
  for (const { mesh, material } of parts) {
    const key = material.name;
    if (!byMaterial.has(key)) byMaterial.set(key, { material, mesh: new Mesh() });
    byMaterial.get(key).mesh.append(mesh);
  }
  const chunks = [];
  let offset = 0;
  const bufferViews = [], accessors = [], images = [], textures = [], materials = [], primitives = [];
  const view = (buf, target) => {
    const padded = buf.length % 4 ? Buffer.concat([buf, Buffer.alloc(4 - (buf.length % 4))]) : buf;
    bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: buf.length, ...(target ? { target } : {}) });
    chunks.push(padded);
    offset += padded.length;
    return bufferViews.length - 1;
  };
  for (const { material, mesh } of byMaterial.values()) {
    const normals = computeNormals(mesh, crease);
    const { min, max } = mesh.bounds();
    const pos = view(Buffer.from(new Float32Array(mesh.positions).buffer), 34962);
    const nor = view(Buffer.from(normals.buffer), 34962);
    const uv = view(Buffer.from(new Float32Array(mesh.uvs).buffer), 34962);
    const idx = view(Buffer.from(new Uint32Array(mesh.indices).buffer), 34963);
    accessors.push({ bufferView: pos, componentType: 5126, count: mesh.vertexCount, type: 'VEC3', min, max });
    accessors.push({ bufferView: nor, componentType: 5126, count: mesh.vertexCount, type: 'VEC3' });
    accessors.push({ bufferView: uv, componentType: 5126, count: mesh.vertexCount, type: 'VEC2' });
    accessors.push({ bufferView: idx, componentType: 5125, count: mesh.indices.length, type: 'SCALAR' });
    const a = accessors.length;
    const [r, g, b, alpha = 1] = material.color ?? [1, 1, 1, 1];
    const pbr = { baseColorFactor: [r, g, b, alpha], metallicFactor: material.metallic ?? 0, roughnessFactor: material.roughness ?? 0.85 };
    if (material.texture) {
      images.push({ bufferView: view(material.texture.png()), mimeType: 'image/png' });
      textures.push({ source: images.length - 1, sampler: 0 });
      pbr.baseColorTexture = { index: textures.length - 1 };
    }
    materials.push({
      name: material.name,
      pbrMetallicRoughness: pbr,
      ...(material.blend ? { alphaMode: 'BLEND' } : {}),
      ...(material.doubleSided ? { doubleSided: true } : {}),
      ...(material.emissive ? { emissiveFactor: material.emissive } : {}),
    });
    primitives.push({ attributes: { POSITION: a - 4, NORMAL: a - 3, TEXCOORD_0: a - 2 }, indices: a - 1, material: materials.length - 1 });
  }
  const gltf = {
    asset: { version: '2.0', generator: 'props-service/src/procedural/kit.mjs' },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes: [{ name, mesh: 0 }],
    meshes: [{ name, primitives }],
    materials,
    ...(textures.length ? { textures, images, samplers: [{ magFilter: 9729, minFilter: 9987, wrapS: 33071, wrapT: 33071 }] } : {}),
    accessors,
    bufferViews,
    buffers: [{ byteLength: offset }],
  };
  const jsonText = Buffer.from(JSON.stringify(gltf), 'utf8');
  const json = Buffer.concat([jsonText, Buffer.alloc((4 - (jsonText.length % 4)) % 4, 0x20)]);
  const bin = Buffer.concat(chunks);
  const header = Buffer.alloc(12);
  header.write('glTF', 0, 'latin1');
  header.writeUInt32LE(2, 4);
  header.writeUInt32LE(12 + 8 + json.length + 8 + bin.length, 8);
  const jh = Buffer.alloc(8);
  jh.writeUInt32LE(json.length, 0);
  jh.write('JSON', 4, 'latin1');
  const bh = Buffer.alloc(8);
  bh.writeUInt32LE(bin.length, 0);
  bh.write('BIN\0', 4, 'latin1');
  return Buffer.concat([header, jh, json, bh, bin]);
}

/** GLB bytes → { gltf, bin } (for the preview). */
export function readGlb(buf) {
  const jsonLen = buf.readUInt32LE(12);
  const gltf = JSON.parse(buf.toString('utf8', 20, 20 + jsonLen));
  const binStart = 20 + jsonLen + 8;
  return { gltf, bin: buf.subarray(binStart, binStart + buf.readUInt32LE(20 + jsonLen)) };
}
