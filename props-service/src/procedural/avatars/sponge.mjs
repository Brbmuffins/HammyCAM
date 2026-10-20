/**
 * Kitchen sponge mascot standing on its end: a soft slab of light lemon-yellow foam (dense little foam cells all
 * over, a few slightly bigger holes, soft rounded edges and a gentle pinched grip waist on its left and right), a
 * thin dark-green scouring pad glued flush on top (scratchy non-woven fibres), and a lumpy crown of white soap suds
 * on the pad with a couple of drips of foam running down. The foam is a little wet (glossier). The face goes on the
 * big front, where the pores are calmer.
 */
export default function build(kit) {
  const { Mesh, Canvas, clamp, lerp, smooth, TAU, fbm2, fbm3, noise2, rng, vec, sphere, tube } = kit;

  // ---------- sizes ----------
  const W = 0.74, D = 0.34; // the slab's width and depth
  const HF = 0.82; // the foam's height: the scouring pad starts here
  const HS = 0.115; // the pad's thickness
  const RF = 0.075; // rounding of the slab's edges
  const RTOP = 0.03; // the pad's top edge is a flatter curve: RF across, this much down
  const HB = HF + HS + RF - RTOP; // the rounded box's height before its top edge is flattened
  const TOPV = HF + HS; // the pad's top
  const FACE_Y = 0.575;
  const faceMask = (x, y, z) => (z < 0 ? 1 : 0) * (1 - smooth(0.72, 1.15, Math.hypot(x / 0.27, (y - FACE_Y) / 0.24)));
  /** No big holes across the middle of the front (they read as a belly button or a mole there). */
  const holeMask = (x, y, z) => Math.max(faceMask(x, y, z), (z < 0 ? 1 : 0) * (1 - smooth(0.8, 1.05, Math.hypot(x / 0.27, (y - FACE_Y + 0.12) / 0.4))));
  const mix = (a, b, t) => a.map((c, k) => lerp(c, b[k], t));

  // ---------- a hash and cellular noise (for pores) ----------
  const hash = (...k) => {
    let h = 2166136261;
    for (const v of k) {
      h = Math.imul(h ^ (v | 0), 16777619);
      h = Math.imul(h ^ (h >>> 13), 1274126177);
    }
    const out = [];
    for (let i = 0; i < 4; i++) {
      h = Math.imul(h ^ (h >>> 15), 2246822519);
      h = Math.imul(h ^ (h >>> 13), 3266489917);
      out.push(((h ^ (h >>> 16)) >>> 0) / 4294967296);
    }
    return out;
  };
  /**
   * 2D pores: the nearest pore's distance as a fraction of its radius (< 1 inside) and the offset from its middle
   * (in radii). The lookup wobbles and each pore is stretched its own way, so they come out blobby, not round.
   */
  const pores2 = (x, y, size, seed, density, rmin, rmax) => {
    const k = 1.6 / size;
    const wx = x + (noise2(x * k, y * k, seed + 3) - 0.5) * size * 0.75;
    const wy = y + (noise2(x * k + 17.3, y * k - 9.1, seed + 4) - 0.5) * size * 0.75;
    const cx = Math.floor(wx / size), cy = Math.floor(wy / size);
    let best = 9, bdx = 0, bdy = 0;
    for (let j = -1; j <= 1; j++)
      for (let i = -1; i <= 1; i++) {
        const h = hash(cx + i, cy + j, seed);
        if (h[0] > density) continue;
        const g = hash(cx + i, cy + j, seed + 101);
        const r = lerp(rmin, rmax, h[3] * h[3]) * size;
        const dx = wx - (cx + i + h[1]) * size, dy = wy - (cy + j + h[2]) * size;
        const c = Math.cos(g[0] * Math.PI), s = Math.sin(g[0] * Math.PI), asp = lerp(0.7, 1.4, g[1]);
        const d = Math.hypot((dx * c + dy * s) / asp, (-dx * s + dy * c) * asp) / r;
        if (d < best) {
          best = d;
          bdx = dx / r;
          bdy = dy / r;
        }
      }
    return { d: best, dx: bdx, dy: bdy };
  };
  /** The few bigger holes, in 3D (so the painted ones and the dents in the mesh agree). */
  const HOLE = 0.09;
  const holes3 = (p) => {
    const c = p.map((v) => Math.floor(v / HOLE));
    let best = 9;
    for (let k = -1; k <= 1; k++)
      for (let j = -1; j <= 1; j++)
        for (let i = -1; i <= 1; i++) {
          const h = hash(c[0] + i, c[1] + j, c[2] + k, 99);
          if (h[0] > 0.3) continue;
          const q = [(c[0] + i + h[1]) * HOLE, (c[1] + j + h[2]) * HOLE, (c[2] + k + h[3]) * HOLE];
          const r = HOLE * lerp(0.14, 0.22, hash(c[0] + i, c[1] + j, c[2] + k, 7)[0]);
          best = Math.min(best, Math.hypot(p[0] - q[0], p[1] - q[1], p[2] - q[2]) / r);
        }
    return best;
  };

  // ---------- one rounded box for the whole slab, cut at the glue line into foam and pad ----------
  /** Positions from -h to h: `dense` steps over each rounded band, about `step` apart across the flat middle. */
  const samples = (h, round, step, dense = 5) => {
    const inner = h - round, out = [];
    for (let k = 0; k < dense; k++) out.push(-h + (round * k) / dense);
    const n = Math.max(1, Math.round((2 * inner) / step));
    for (let k = 0; k <= n; k++) out.push(-inner + (2 * inner * k) / n);
    for (let k = dense - 1; k >= 0; k--) out.push(h - (round * k) / dense);
    return out;
  };
  const withRows = (s, extra) => [...s.filter((v) => extra.every((e) => Math.abs(v - e) > 0.0025)), ...extra].sort((a, b) => a - b);
  // [fixed axis, sign, axis along u, axis along v (downward), name]
  const FACES = [[2, -1, 0, 1, 'front'], [2, 1, 0, 1, 'back'], [0, 1, 2, 1, 'px'], [0, -1, 2, 1, 'nx'], [1, 1, 0, 2, 'top'], [1, -1, 0, 2, 'bottom']];
  const faceOf = (name) => FACES.find((f) => f[4] === name);
  const half = [W / 2, HB / 2, D / 2];
  const inner = half.map((h) => h - RF);
  const SPLIT = HF - HB / 2; // the glue line in the box's own coordinates
  const S = [samples(half[0], RF, 0.015), withRows(samples(half[1], RF, 0.015), [SPLIT - 0.01, SPLIT - 0.005, SPLIT, SPLIT + 0.005, SPLIT + 0.01]), samples(half[2], RF, 0.015)];
  // Each region: a face (or the part of a side face between lo and hi, along its v axis) and its texture rectangle.
  const lo = -half[1], hi = half[1];
  const regions = [
    { face: 'front', tex: 'A', uv: [0, 0, 1, 1], lo, hi: SPLIT },
    { face: 'px', tex: 'B', uv: [0, 0, 0.3, 1], lo, hi: SPLIT },
    { face: 'nx', tex: 'B', uv: [0.3, 0, 0.6, 1], lo, hi: SPLIT },
    { face: 'back', tex: 'B', uv: [0.6, 0, 1, 0.82], lo, hi: SPLIT }, // the back and bottom are hardly seen: less room
    { face: 'bottom', tex: 'B', uv: [0.6, 0.82, 1, 1], lo: -half[2], hi: half[2] },
    { face: 'front', tex: 'C', uv: [0, 0, 1, 1 / 3], lo: SPLIT, hi },
    { face: 'back', tex: 'C', uv: [0, 1 / 3, 1, 0.6], lo: SPLIT, hi },
    { face: 'px', tex: 'C', uv: [0, 0.6, 0.25, 0.9], lo: SPLIT, hi },
    { face: 'nx', tex: 'C', uv: [0.25, 0.6, 0.5, 0.9], lo: SPLIT, hi },
    { face: 'top', tex: 'C', uv: [0.5, 0.6, 1, 1], lo: -half[2], hi: half[2] }, // mostly under the suds
  ];
  /** A point on the rounded box and its outward direction. */
  const push = (p) => {
    const c = p.map((v, k) => clamp(v, -inner[k], inner[k]));
    const d = vec.norm(vec.sub(p, c));
    return { p: [c[0] + d[0] * RF, c[1] + d[1] * RF, c[2] + d[2] * RF], n: d };
  };
  const toWorld = (p) => [p[0], p[1] + HB / 2, p[2]];
  const body = {};
  for (const r of regions) {
    const [fixed, sign, a1, a2] = faceOf(r.face);
    body[r.tex] ??= { mesh: new Mesh(), normals: [] };
    const { mesh, normals } = body[r.tex];
    const A = S[a1], B = S[a2].filter((b) => b >= r.lo - 1e-9 && b <= r.hi + 1e-9);
    const [u0, v0, u1, v1] = r.uv;
    const idx = [];
    for (let j = 0; j < B.length; j++)
      for (let i = 0; i < A.length; i++) {
        const p = [0, 0, 0];
        p[fixed] = sign * half[fixed];
        p[a1] = A[i];
        p[a2] = B[j];
        const fu = (A[i] + half[a1]) / (2 * half[a1]), fv = (r.hi - B[j]) / (r.hi - r.lo);
        const q = push(p);
        idx.push(mesh.vertex(q.p, [lerp(u0, u1, fu), lerp(v0, v1, fv)]));
        normals.push(q.n);
      }
    for (let j = 0; j + 1 < B.length; j++)
      for (let i = 0; i + 1 < A.length; i++) mesh.quad(idx[j * A.length + i], idx[j * A.length + i + 1], idx[(j + 1) * A.length + i + 1], idx[(j + 1) * A.length + i]);
  }
  for (const t of Object.values(body)) kit.outward(t.mesh, [0, 0, 0]);

  // ---------- the shape: flatter top edge, soft lumps, the grip waist, pillowed front and back ----------
  const yr = HB - RF; // where the top edge's rounding starts
  const flatTop = (y) => (y > yr ? yr + (y - yr) * (RTOP / RF) : y);
  /** The grip waist and the pillowing (x and z only, so the glue line stays level). */
  const shape = ([x, y, z]) => {
    const side = smooth(-0.15, 0.15, x); // screen left (+X) pinched a touch deeper and lower: soft foam, not machined
    const pinch = lerp(0.095, 0.11, side) * Math.exp(-(((y - lerp(0.44, 0.41, side)) / 0.21) ** 2));
    const ripple = 0.01 * Math.sin((TAU * y) / 0.34 + 0.9) * smooth(0.04, 0.16, y) * (1 - smooth(HF - 0.2, HF - 0.05, y));
    const sx = 1 - pinch + ripple;
    const sz = 1 + 0.05 * Math.sin(Math.PI * clamp(y / HF)) ** 0.8;
    return [x * sx, y, z * sz];
  };
  /** A rest point of the box (world, standing on y = 0) and its outward direction -> the finished surface. */
  const bodyPoint = (w, n) => {
    const p = [w[0], flatTop(w[1]), w[2]];
    const y = p[1];
    const fm = faceMask(p[0], y, p[2]);
    let o;
    if (y <= HF) {
      const keep = smooth(0, 0.07, y) * (1 - smooth(HF - 0.09, HF - 0.01, y)); // calm by the desk and the glue line
      const lump = (fbm3(p[0] * 3.2, y * 3.2, p[2] * 3.2, { octaves: 3, seed: 5 }) - 0.5) * 0.026 * (1 - 0.75 * fm) * keep;
      const h = holes3(w);
      const dent = h < 1 ? -0.01 * (1 - h * h) * (1 - holeMask(p[0], y, p[2])) * keep : 0;
      o = lump + dent;
    } else {
      // the pad: fibrous fuzz, set a hair inside the foam so it reads as a glued-on layer, not a lid
      o = (fbm3(p[0] * 45, y * 45, p[2] * 45, { octaves: 2, seed: 8 }) - 0.5) * 0.008 + (fbm3(p[0] * 6, y * 6, p[2] * 6, { octaves: 2, seed: 9 }) - 0.5) * 0.006 - 0.004;
    }
    o -= 0.0055 * Math.exp(-(((y - HF) / 0.0055) ** 2)); // the glue line, a fine crease
    return shape([p[0] + n[0] * o, y + n[1] * o, p[2] + n[2] * o]);
  };
  for (const { mesh, normals } of Object.values(body)) mesh.map((p, i) => bodyPoint(toWorld(p), normals[i]));
  /** A point on the finished surface from a point near the rest box (local), plus its outward direction. */
  const surfaceAt = (local) => {
    const q = push(local);
    return { p: bodyPoint(toWorld(q.p), q.n), n: q.n };
  };

  /** Paints every region of one texture: fn(rest point (world), normal, metres along u, metres down v, face). */
  const paintRegions = (canvas, tex, fn) => {
    for (const r of regions) {
      if (r.tex !== tex) continue;
      const [fixed, sign, a1, a2] = faceOf(r.face);
      const [u0, v0, u1, v1] = r.uv;
      const x0 = Math.floor(u0 * canvas.width), x1 = Math.ceil(u1 * canvas.width), y0 = Math.floor(v0 * canvas.height), y1 = Math.ceil(v1 * canvas.height);
      for (let py = y0; py < y1; py++)
        for (let px = x0; px < x1; px++) {
          const fu = clamp(((px + 0.5) / canvas.width - u0) / (u1 - u0)), fv = clamp(((py + 0.5) / canvas.height - v0) / (v1 - v0));
          const p = [0, 0, 0];
          p[fixed] = sign * half[fixed];
          p[a1] = lerp(-half[a1], half[a1], fu);
          p[a2] = lerp(r.hi, r.lo, fv);
          const q = push(p);
          const col = fn(toWorld(q.p), q.n, p[a1], -p[a2], r.face);
          const o = (py * canvas.width + px) * 4;
          canvas.px[o] = clamp(Math.round(col[0]), 0, 255);
          canvas.px[o + 1] = clamp(Math.round(col[1]), 0, 255);
          canvas.px[o + 2] = clamp(Math.round(col[2]), 0, 255);
          canvas.px[o + 3] = 255;
        }
    }
  };

  // ---------- the foam's paint: light lemon yellow, packed with little foam cells ----------
  const YEL = [255, 230, 86], PORE = [240, 186, 52], DEEP = [214, 146, 34], HOLE_IN = [200, 128, 28];
  const foamColour = (w, n, a, b, face) => {
    const fm = faceMask(w[0], w[1], w[2]);
    const seed = face.length * 13 + face.charCodeAt(0);
    let col = YEL.map((c) => c * (0.955 + 0.07 * fbm2(a * 7 + seed, b * 7, { octaves: 3, seed: 3 })));
    // [cell size, how many cells hold a pore, radius range (of the cell), strength]
    const layers = [
      [0.0075, 0.92, 0.26, 0.5, 0.6 * (1 - 0.5 * fm)],
      [0.015, 0.6, 0.24, 0.46, 0.8 * (1 - 0.8 * fm)],
      [0.028, 0.3, 0.22, 0.4, 0.85 * (1 - fm)],
    ];
    for (const [size, dens, r0, r1, strength] of layers) {
      if (strength <= 0.01) continue;
      const q = pores2(a, b, size, seed * 7 + Math.round(size * 10000), dens, r0, r1);
      if (q.d < 1) {
        // the inner wall under the top rim is in shadow; the lower wall catches the light
        const inside = smooth(1, 0.68, q.d);
        col = mix(col, mix(PORE, DEEP, smooth(0.3, -0.65, q.dy)), inside * strength);
        col = col.map((c) => c * (1 + 0.1 * strength * smooth(0.5, 0.92, q.d) * smooth(0.15, 0.8, q.dy)));
      } else if (q.d < 1.35) col = col.map((c) => c * (1 + 0.05 * strength * smooth(1.35, 1, q.d) * smooth(0.1, 0.7, q.dy)));
    }
    const h = holes3(w), hm = holeMask(w[0], w[1], w[2]);
    if (h < 1.25 && hm < 0.4) {
      const k = 1 - hm / 0.4;
      if (h < 1) col = mix(col, HOLE_IN, smooth(1, 0.62, h) * k);
      else col = col.map((c) => c * (1 - 0.1 * smooth(1.25, 1, h) * k));
    }
    return col;
  };
  const texA = new Canvas(1024, 1024, YEL), texB = new Canvas(1024, 768, YEL);
  paintRegions(texA, 'A', foamColour);
  paintRegions(texB, 'B', foamColour);
  const parts = [
    { mesh: body.A.mesh, material: { name: 'foam', texture: texA, roughness: 0.6 } },
    { mesh: body.B.mesh, material: { name: 'foam_side', texture: texB, roughness: 0.6 } },
  ];

  // ---------- the scouring pad's paint: dark green non-woven fibres ----------
  const GREEN = [30, 118, 54], GAP = [8, 48, 22];
  const texC = new Canvas(1024, 384, GREEN);
  paintRegions(texC, 'C', (w, n, a, b, face) => {
    const g1 = fbm2(a * 70 + 3, b * 70, { octaves: 2, seed: 12 }), g2 = fbm2(a * 9, b * 9, { octaves: 2, seed: 13 });
    let col = GREEN.map((c) => c * (0.74 + 0.46 * g1) * (0.9 + 0.2 * g2));
    // the open weave: tiny dark gaps between the fibres
    const q = pores2(a, b, 0.0055, 77 + face.length, 0.75, 0.22, 0.42);
    if (q.d < 1) col = mix(col, GAP, smooth(1, 0.45, q.d) * 0.75);
    return col;
  });
  // the fibres: lots of short, curly strokes, light flecks and dark, the same size on every face
  {
    const R0 = rng(31);
    for (const r of regions) {
      if (r.tex !== 'C') continue;
      const [, , a1] = faceOf(r.face);
      const [u0, v0, u1, v1] = r.uv;
      const mw = 2 * half[a1], mh = r.hi - r.lo;
      const sx = ((u1 - u0) * texC.width) / mw, sy = ((v1 - v0) * texC.height) / mh; // pixels per metre
      const count = Math.round(mw * mh * 130000);
      for (let k = 0; k < count; k++) {
        const x = (u0 + R0() * (u1 - u0)) * texC.width, y = (v0 + R0() * (v1 - v0)) * texC.height;
        const ang = R0() * TAU, len = lerp(0.0025, 0.0075, R0()), bend = (R0() - 0.5) * 1.6;
        const pts = [[x, y]];
        for (let s = 1; s <= 3; s++) {
          const t = ang + (bend * s) / 3, [px, py] = pts[s - 1];
          pts.push([px + Math.cos(t) * (len / 3) * sx, py + Math.sin(t) * (len / 3) * sy]);
        }
        const kind = R0();
        const colour = kind < 0.55 ? [112, 200, 108] : kind < 0.63 ? [168, 222, 124] : [10, 58, 26];
        texC.stroke(pts, lerp(0.0007, 0.0012, R0()) * (sx + sy) * 0.5, colour, lerp(0.35, 0.8, R0()));
      }
    }
  }
  parts.push({ mesh: body.C.mesh, material: { name: 'scrubber', texture: texC, roughness: 0.92 } });

  // ---------- soap suds: a lumpy crown of bubbles on the pad, and two drips of foam ----------
  const texD = new Canvas(512, 256, [248, 251, 255]);
  texD.shade((u, v) => {
    const blue = smooth(0.25, 1, v) * (0.65 + 0.6 * fbm2(u * 20, v * 10, { octaves: 2, seed: 41 }));
    return mix([250, 252, 255], [204, 223, 247], clamp(blue));
  });
  {
    // tiny bubbles in the foam: faint blue rings with a glint
    const R1 = rng(55);
    for (let k = 0; k < 230; k++) {
      const x = R1() * 512, y = R1() * 256, r = lerp(2.2, 7, R1() ** 1.6);
      texD.disc(x, y, r, [184, 206, 236], 0.55, 0.35);
      texD.disc(x, y, r * 0.7, [244, 248, 255], 0.9, 0.45);
      texD.disc(x - r * 0.3, y - r * 0.32, Math.max(0.9, r * 0.24), [255, 255, 255], 1, 0.3);
    }
  }
  const sudsMat = { name: 'suds', texture: texD, roughness: 0.22 };
  const blobs = [];
  /** The suds' top at (x, z) so far. */
  const surf = (x, z) => {
    let top = TOPV;
    for (const s of blobs) {
      const d2 = ((x - s.c[0]) / s.r) ** 2 + ((z - s.c[2]) / s.r) ** 2;
      if (d2 < 1) top = Math.max(top, s.c[1] + s.r * s.sy * Math.sqrt(1 - d2));
    }
    return top;
  };
  /** A bubble resting on the suds so far, sunk into them by `sink` of its height. */
  const addBlob = (x, z, r, sink, sy = 0.92) => blobs.push({ c: [x, surf(x, z) - r * sy * sink, z], r, sy });
  // the crown: big soft lumps sitting up on the pad, a cloud-like row of them across the front and a row behind,
  // peaking a little left of middle ...
  for (const [x, z, r, up] of [
    [-0.215, -0.04, 0.115, 0.035], [-0.035, -0.035, 0.13, 0.055], [0.15, -0.045, 0.115, 0.03], [0.29, -0.03, 0.085, 0.005], [-0.315, -0.02, 0.075, 0],
    [-0.13, 0.07, 0.11, 0.045], [0.07, 0.075, 0.115, 0.04], [0.25, 0.07, 0.085, 0.01], [-0.29, 0.08, 0.07, 0],
  ])
    blobs.push({ c: [x, TOPV + up, z], r, sy: 0.85 });
  // ... lumps spilling over the pad's front and side edges ...
  for (const [x, z, r] of [[-0.25, -0.145, 0.05], [0.05, -0.15, 0.046], [-0.34, -0.07, 0.045], [0.34, -0.1, 0.042], [0.205, -0.15, 0.038]])
    blobs.push({ c: [x, TOPV - 0.006, z], r, sy: 0.9 });
  // ... a few bubbles sunk into them, and little ones all over, most round the bottom of the crown
  const RS = rng(23);
  for (let k = 0; k < 11; k++) {
    const t = RS() * TAU, rr = Math.sqrt(RS());
    addBlob(Math.cos(t) * rr * 0.26, Math.sin(t) * rr * 0.1, lerp(0.035, 0.06, RS()), lerp(0.55, 0.75, RS()));
  }
  for (let k = 0; k < 42; k++) {
    const t = RS() * TAU, rr = lerp(0.55, 1.08, Math.sqrt(RS()));
    addBlob(Math.cos(t) * rr * 0.35, Math.sin(t) * rr * 0.16, lerp(0.012, 0.03, RS() ** 1.3), lerp(0.3, 0.55, RS()), 1);
  }
  const suds = new Mesh();
  for (const s of blobs) {
    const seg = s.r > 0.06 ? 26 : s.r > 0.03 ? 16 : 10;
    suds.append(sphere(s.r, { segments: seg, rows: Math.round(seg * 0.6), scale: [1, s.sy, 1] }).transform({ rotate: [0, RS() * 360, 0], translate: s.c }));
  }

  /**
   * A drip of foam running down a face of the slab: from and to are points on the rest box (its own coordinates, to
   * lower), n that face's outward direction, r its radius. A lumpy run flattened onto the surface, a drop at its end,
   * and a lump of suds over the pad's edge where it starts.
   */
  const drip = (from, to, n, r) => {
    const path = [], radii = [];
    const N = 16;
    for (let k = 0; k <= N; k++) {
      const t = k / N; // 0 at the bottom end
      const s = surfaceAt([lerp(to[0], from[0], t), lerp(to[1], from[1], t), lerp(to[2], from[2], t)]);
      const rr = r * (0.8 + 0.6 * t ** 1.4 + 0.15 * Math.sin(t * 15 + 1));
      const side = 0.005 * Math.sin(t * 6.5); // wanders a little
      path.push([s.p[0] + n[0] * rr * 0.15 + n[2] * side, s.p[1], s.p[2] + n[2] * rr * 0.15 + n[0] * side]);
      radii.push(rr);
    }
    const top = path[N];
    path.push([top[0] - n[0] * 0.03, TOPV + 0.01, top[2] - n[2] * 0.03]);
    radii.push(r * 1.4);
    /** The run's middle at a height (the path climbs). */
    const mid = (y) => {
      for (let k = 0; k + 1 < path.length; k++)
        if (y <= path[k + 1][1] || k + 2 === path.length) {
          const t = clamp((y - path[k][1]) / (path[k + 1][1] - path[k][1] || 1));
          return path[k].map((v, d) => lerp(v, path[k + 1][d], t));
        }
      return path[0];
    };
    const flatten = (m, keep) =>
      m.map((v) => {
        const c = mid(v[1]);
        const d = (v[0] - c[0]) * n[0] + (v[2] - c[2]) * n[2];
        return [v[0] - n[0] * d * (1 - keep), v[1], v[2] - n[2] * d * (1 - keep)];
      });
    suds.append(flatten(tube(path, r, { segments: 16, radii }), 0.6));
    const end = path[0];
    suds.append(flatten(sphere(r * 1.4, { segments: 20, rows: 14, scale: [1, 1.12, 1] }).transform({ translate: [end[0], end[1] - r * 0.7, end[2]] }), 0.7));
    suds.append(sphere(r * 2.4, { segments: 24, rows: 14, scale: [1, 0.82, 1] }).transform({ translate: [top[0] - n[0] * 0.012, TOPV - 0.004, top[2] - n[2] * 0.012] }));
    // little bubbles caught on the run
    for (const t of [0.3, 0.55, 0.8]) {
      const k = Math.round(t * N), c = path[k], rb = radii[k] * 0.55;
      const off = (t * 37) % 1 < 0.5 ? -1 : 1;
      suds.append(sphere(rb, { segments: 12, rows: 8 }).transform({ translate: [c[0] + n[0] * radii[k] * 0.35 + n[2] * off * radii[k] * 0.7, c[1], c[2] + n[2] * radii[k] * 0.35 + n[0] * off * radii[k] * 0.7] }));
    }
  };
  const Y = (y) => y - HB / 2; // a height on the slab -> the box's own y (below the flattened top edge)
  drip([0.283, Y(TOPV - RTOP), -half[2]], [0.287, Y(0.735), -half[2]], [0, 0, -1], 0.027); // down the front, screen left of the face
  drip([half[0], Y(TOPV - RTOP), -0.05], [half[0], Y(0.76), -0.06], [1, 0, 0], 0.025); // down the screen-left side
  parts.push({ mesh: suds, material: sudsMat });

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const s = 1.15 / (max[1] - min[1]);
  let frontZ = Infinity;
  for (const p of parts.slice(0, 2))
    for (let i = 0; i < p.mesh.vertexCount; i++) {
      const [x, y, z] = p.mesh.point(i);
      if (Math.abs(x) < 0.03 && Math.abs(y - FACE_Y) < 0.03) frontZ = Math.min(frontZ, z);
    }
  const sudsTop = surf(0, 0);
  return {
    parts,
    face: (FACE_Y - min[1]) / (max[1] - min[1]),
    faceSize: 0.84,
    anchors: { top: [0, sudsTop, 0], front: [0, FACE_Y, frontZ] },
    notes:
      `Foam ${(W * s).toFixed(3)} m wide x ${(D * s).toFixed(3)} m deep, ${(HF * s).toFixed(3)} m tall, with a gentle grip waist; ` +
      `the green scouring pad is ${(HS * s).toFixed(3)} m thick, flush on top, and a crown of suds rises ${((sudsTop - TOPV) * s).toFixed(3)} m above it ` +
      `(drips of foam down the front's screen-left edge and the screen-left side). Pores are painted (three sizes, calmer behind the face), ` +
      `the few bigger holes also dented into the mesh.`,
  };
}
