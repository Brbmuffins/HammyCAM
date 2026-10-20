/**
 * Avocado half: a chunky pear-shaped half standing on its rounded bottom, the cut side to the camera. Pebbly dark
 * green skin round the back with a raised bumpy rim of it round the front edge, soft domed flesh going from butter
 * yellow in the middle to pale then bright green at the skin, and a big glossy brown pit set into a socket in the
 * lower bulb. The pit is its own material ("pit"): the app hides it and throws a real one, leaving the socket (its
 * own slightly darker, shinier material, "socket") showing.
 */
export default function build(kit) {
  const { Mesh, Canvas, sphere, fbm3, noise3, smooth, clamp, lerp, TAU, rng } = kit;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];

  // ---------- the outline (front view): a smooth union of a big bulb and a smaller top, bottom a bit flattened ----------
  const C1 = 0.34, R1 = 0.33, C2 = 0.745, R2 = 0.228, K = 0.24;
  const YF = 0.085; // below this the outline is squashed (a flatter bottom so it stands)
  const smin = (a, b, k) => {
    const h = Math.max(k - Math.abs(a - b), 0) / k;
    return Math.min(a, b) - h * h * k * 0.25;
  };
  const sdf = (x, y) => {
    const ye = y < YF ? YF - (YF - y) * 1.7 : y;
    return smin(Math.hypot(x, ye - C1) - R1, Math.hypot(x, ye - C2) - R2, K);
  };
  const grad = (x, y) => {
    const e = 1e-4;
    const gx = sdf(x + e, y) - sdf(x - e, y), gy = sdf(x, y + e) - sdf(x, y - e);
    const l = Math.hypot(gx, gy) || 1;
    return [gx / l, gy / l];
  };
  /** Distance along a ray from (ox, oy) in direction (dx, dy) to where sdf = level (first crossing). */
  const march = (ox, oy, dx, dy, level = 0) => {
    let t0 = 0, t1 = 0;
    while (sdf(ox + dx * t1, oy + dy * t1) < level) {
      t0 = t1;
      t1 += 0.004;
    }
    for (let k = 0; k < 30; k++) {
      const m = (t0 + t1) / 2;
      if (sdf(ox + dx * m, oy + dy * m) < level) t0 = m;
      else t1 = m;
    }
    return (t0 + t1) / 2;
  };

  // The right half of the outline, bottom pole to top pole, resampled evenly by arc length.
  const O = 0.43;
  const dense = [];
  for (let k = 0; k <= 1600; k++) {
    const g = (Math.PI * k) / 1600;
    const dx = Math.sin(g), dy = -Math.cos(g);
    const t = march(0, O, dx, dy);
    dense.push([Math.max(0, dx * t), O + dy * t]);
  }
  dense[0][0] = 0;
  dense[1600][0] = 0;
  const cum = [0];
  for (let k = 1; k < dense.length; k++) cum.push(cum[k - 1] + Math.hypot(dense[k][0] - dense[k - 1][0], dense[k][1] - dense[k - 1][1]));
  const ARC = cum[cum.length - 1];
  /** The outline at s (0 bottom pole .. 1 top pole): [w, y, nx, ny]. */
  const profileAt = (s) => {
    const target = clamp(s) * ARC;
    let lo = 0, hi = cum.length - 1;
    while (hi - lo > 1) {
      const m = (lo + hi) >> 1;
      if (cum[m] < target) lo = m;
      else hi = m;
    }
    const f = (target - cum[lo]) / (cum[hi] - cum[lo] || 1);
    const w = lerp(dense[lo][0], dense[hi][0], f), y = lerp(dense[lo][1], dense[hi][1], f);
    const n = s <= 0 ? [0, -1] : s >= 1 ? [0, 1] : grad(w, y);
    return [w, y, n[0], n[1]];
  };
  const YMIN = dense[0][1], YMAX = dense[1600][1];
  let WMAX = 0;
  for (const p of dense) WMAX = Math.max(WMAX, p[0]);

  // ---------- sizes ----------
  const KZ = 1.12; // the back's depth relative to the half-width (a solid of revolution, a bit deeper: chunky)
  const T = 0.03; // the skin rim's width at the front
  const YP = 0.335; // the pit's centre height
  const RP = 0.152; // pit radius
  const RH = RP + 0.008; // the socket's radius
  const pebble = (p) => {
    // the skin's knobbly lumps, big enough to show in the silhouette (0..1, 1 = top of a lump)
    const a = fbm3(p[0] * 17, p[1] * 17, p[2] * 17, { octaves: 2, seed: 3 });
    return clamp(smooth(0.3, 0.72, a));
  };
  /** Fine pebbles (painted): 1 on a pebble's crown .. 0 in the cracks between them. */
  const pebbleFine = (p) => {
    const [f1, f2] = worley(p[0] / 0.022, p[1] / 0.022, p[2] / 0.022, 7);
    return smooth(0.0, 0.45, f2 - f1) * (1 - 0.4 * smooth(0.2, 0.7, f1));
  };

  // ---------- the skin: a half shell round the back plus a raised rim round the front edge ----------
  // u: 0..1 across a row: [left rim lip | over the back | right rim lip]; LIP is how much of u each lip takes.
  const LIP = 0.05;
  const lipShape = [
    // [inward from the outline (in T), z (forward is -)]
    [1.28, 0.006],
    [1.0, -0.004],
    [0.62, -0.011],
    [0.25, -0.009],
    [0.0, -0.003],
  ];
  /** The skin point at (u, s), and the bump amount there. */
  const skinPoint = (u, s) => {
    const [w, y, nx, ny] = profileAt(s);
    let side, p, dir, lip = 0;
    if (u < LIP || u > 1 - LIP) {
      side = u < LIP ? 1 : -1; // u = 0 starts on the +X side
      const q = (u < LIP ? u / LIP : (1 - u) / LIP) * (lipShape.length - 1);
      const k = Math.min(lipShape.length - 2, Math.floor(q)), f = q - k;
      const inward = lerp(lipShape[k][0], lipShape[k + 1][0], f) * T, z = lerp(lipShape[k][1], lipShape[k + 1][1], f);
      p = [side * (w - inward * nx), y - inward * ny, z];
      dir = [side * nx, ny, -0.6];
      lip = 1 - Math.abs(q - 2) / 2;
    } else {
      const al = ((u - LIP) / (1 - 2 * LIP)) * Math.PI; // 0 = +X rim .. PI = -X rim, PI/2 the back
      p = [w * Math.cos(al), y, KZ * w * Math.sin(al)];
      dir = [nx * Math.cos(al), ny, (nx * Math.sin(al)) / KZ];
    }
    const l = Math.hypot(...dir) || 1;
    return { p, dir: dir.map((d) => d / l), lip };
  };
  const BUMP = 0.011;
  const displaced = (u, s) => {
    const { p, dir, lip } = skinPoint(u, s);
    const pole = smooth(0, 0.025, Math.min(s, 1 - s)); // calm at the poles
    const b = (pebble(p) - 0.45) * BUMP * pole * (1 - 0.35 * lip);
    return [p[0] + dir[0] * b, p[1] + dir[1] * b, p[2] + dir[2] * b];
  };

  const NR = 104; // rows, pole to pole
  const us = [];
  for (let k = 0; k <= 8; k++) us.push((LIP * k) / 8);
  const BACK = 84;
  for (let k = 1; k < BACK; k++) us.push(LIP + ((1 - 2 * LIP) * k) / BACK);
  for (let k = 8; k >= 0; k--) us.push(1 - (LIP * k) / 8);
  const skin = new Mesh();
  const rows = [];
  for (let j = 0; j <= NR; j++) {
    const s = j / NR;
    rows.push(us.map((u) => skin.vertex(displaced(u, s), [u, 1 - s])));
  }
  for (let j = 0; j < NR; j++)
    for (let i = 0; i + 1 < us.length; i++) {
      const a = rows[j][i], b = rows[j][i + 1], c = rows[j + 1][i + 1], d = rows[j + 1][i];
      skin.tri(a, b, c).tri(a, c, d);
    }
  orient(skin, (c, n) => n[0] * c[0] + n[2] * (c[2] - 0.1) + n[1] * (c[1] - 0.45) * 0.3);

  // Skin paint: deep green, lighter on the tops of the pebbles, darker in between; the rim's top a bit glossier.
  const skinTex = new Canvas(1024, 1024, [44, 88, 34]);
  skinTex.shade((u, v) => {
    const s = 1 - v;
    const { p, lip } = skinPoint(u, s);
    const b = pebble(p), f = pebbleFine(p);
    const m = fbm3(p[0] * 5, p[1] * 5, p[2] * 5, { octaves: 3, seed: 8 });
    let col = mix([22, 50, 18], [58, 104, 38], 0.15 + 0.85 * f);
    col = mul(col, 0.86 + 0.24 * b);
    col = mul(col, 0.95 + 0.1 * m);
    col = mix(col, [86, 132, 52], lip * 0.35);
    // the bottom: a little brown scar where it sat
    col = mix(col, [70, 66, 34], (1 - smooth(0.0, 0.035, s)) * 0.6);
    return mul(col, 0.94 + 0.12 * noise3(p[0] * 140, p[1] * 140, p[2] * 140, 9));
  });
  skinTex.speckle(1800, [0.6, 1.4], [120, 150, 70], 0.25, 12);
  const parts = [{ mesh: skin, material: { name: 'skin', texture: skinTex, roughness: 0.82 } }];

  // ---------- the flesh: a soft dome over the cut face, with a round socket for the pit ----------
  const tucked = T * 1.2; // the flesh's edge, tucked under the rim's inner lip
  const inside = (x, y) => -sdf(x, y) - T; // how far inside the rim's inner edge
  const fleshZ = (x, y) => {
    const t = inside(x, y);
    return 0.004 - 0.03 * smooth(-0.006, 0.075, t) - 0.012 * smooth(0.06, 0.26, t);
  };
  const NB = 144, NS = 24;
  const bound = [];
  for (let i = 0; i <= NB; i++) {
    const b = ringPhase(i, NB);
    bound.push(march(0, YP, Math.cos(b), Math.sin(b), -tucked) );
  }
  // rows: a rounded lip down into the socket, then out to the skin (denser at both ends)
  const holeLip = [
    [-0.006, 0.02],
    [-0.001, 0.011],
    [0.004, 0.004],
    [0.009, 0.001],
  ];
  const flesh = new Mesh();
  const frows = [];
  const zRim = []; // where the socket meets the lip, per angle
  for (let k = 0; k < holeLip.length + NS + 1; k++) {
    const row = [];
    for (let i = 0; i <= NB; i++) {
      const b = ringPhase(i, NB);
      const c = Math.cos(b), sn = Math.sin(b);
      let rr, z;
      if (k < holeLip.length) {
        rr = RH + holeLip[k][0] + 0.006;
        const x = c * (RH + 0.016), y = YP + sn * (RH + 0.016);
        z = fleshZ(x, y) + holeLip[k][1];
        if (k === 0) zRim[i] = z;
      } else {
        const q = (k - holeLip.length) / NS;
        const f = q < 0.5 ? 0.5 * (2 * q) ** 1.3 : 1 - 0.5 * (2 - 2 * q) ** 1.6;
        rr = lerp(RH + 0.016, bound[i], f);
        z = fleshZ(c * rr, YP + sn * rr);
      }
      const x = c * rr, y = YP + sn * rr;
      row.push(flesh.vertex([x, y, z]));
    }
    frows.push(row);
  }
  for (let k = 0; k + 1 < frows.length; k++)
    for (let i = 0; i < NB; i++) {
      const a = frows[k][i], b = frows[k][i + 1], c = frows[k + 1][i + 1], d = frows[k + 1][i];
      flesh.tri(a, b, c).tri(a, c, d);
    }
  orient(flesh, (c, n) => -n[2]);
  const fb = { min: [-WMAX, YMIN, 0], max: [WMAX, YMAX, 0] };
  flesh.planarUV(fb);
  const fleshTex = new Canvas(512, 768, [236, 232, 160]);
  fleshTex.shade((u, v) => {
    const x = WMAX - u * 2 * WMAX, y = YMAX - v * (YMAX - YMIN);
    const t = inside(x, y);
    const dp = Math.hypot(x, y - YP) - RH;
    let col = mix([124, 172, 54], [190, 220, 110], smooth(-0.01, 0.05, t));
    col = mix(col, [226, 234, 148], smooth(0.04, 0.16, t));
    col = mix(col, [244, 236, 158], smooth(0.1, 0.24, t) * 0.8);
    // a buttery glow round the pit
    col = mix(col, [248, 232, 140], (1 - smooth(0.0, 0.07, dp)) * 0.5);
    // a soft shadow where the flesh rolls down into the socket
    col = mul(col, 1 - 0.25 * (1 - smooth(0.004, 0.028, dp)));
    const fib = fbm3(x * 14, y * 3, 0.5, { octaves: 3, seed: 21 });
    col = mul(col, 0.97 + 0.06 * fib);
    return col;
  });
  parts.push({ mesh: flesh, material: { name: 'flesh', texture: fleshTex, roughness: 0.62 } });

  // ---------- the socket: a hollow a touch bigger than the pit, slightly darker and shinier ----------
  let zMean = 0;
  for (let i = 0; i < NB; i++) zMean += zRim[i] / NB;
  const NSK = 10;
  const socket = new Mesh();
  const srows = [];
  for (let k = 0; k <= NSK; k++) {
    const psi = (Math.PI / 2) * (1 - k / NSK); // rim .. bottom
    const row = [];
    for (let i = 0; i <= NB; i++) {
      const b = ringPhase(i, NB);
      const zr = lerp(zRim[i], zMean, 1 - Math.sin(psi));
      const rr = (RH - 0.006 + 0.006 * Math.sin(psi) ** 6) * Math.sin(psi);
      row.push(socket.vertex([Math.cos(b) * rr, YP + Math.sin(b) * rr, zr + RH * Math.cos(psi) * 0.98], [i / NB, k / NSK]));
    }
    srows.push(row);
  }
  for (let k = 0; k < NSK; k++)
    for (let i = 0; i < NB; i++) {
      const a = srows[k][i], b = srows[k][i + 1], c = srows[k + 1][i + 1], d = srows[k + 1][i];
      socket.tri(a, b, c).tri(a, c, d);
    }
  orient(socket, (c, n) => n[0] * -c[0] + n[1] * (YP - c[1]) + n[2] * (zMean - c[2]));
  const socketTex = new Canvas(256, 128, [214, 214, 128]);
  socketTex.shade((u, v) => {
    const n = fbm3(Math.cos(u * TAU) * 3, Math.sin(u * TAU) * 3, v * 4, { octaves: 3, seed: 31 });
    let col = mix([168, 170, 84], [104, 110, 46], smooth(0.0, 0.75, v));
    // faint radial fibres where the pit was attached
    col = mul(col, 0.95 + 0.08 * n + 0.012 * Math.sin(u * TAU * 22 + n * 4));
    return col;
  });
  parts.push({ mesh: socket, material: { name: 'socket', texture: socketTex, roughness: 0.42 } });

  // ---------- the pit: a big glossy brown ball, sat in the socket ----------
  const ZP = zMean + 0.012;
  const pit = sphere(RP, { segments: 48, rows: 28 }).transform({ translate: [0, YP, ZP] });
  const pitTex = new Canvas(512, 256, [118, 62, 30]);
  pitTex.shade((u, v) => {
    const a = u * TAU, th = v * Math.PI;
    const p = [Math.sin(th) * Math.cos(a), Math.cos(th), Math.sin(th) * Math.sin(a)];
    const m = fbm3(p[0] * 3, p[1] * 3, p[2] * 3, { octaves: 4, seed: 41 });
    let col = mix([100, 50, 24], [136, 72, 36], smooth(0.25, 0.8, m));
    // fine veins running round it, and a paler seed-coat blush
    const vein = Math.abs(Math.sin(a * 9 + m * 6 + th * 2));
    col = mul(col, 0.92 + 0.1 * smooth(0.0, 0.25, vein));
    col = mix(col, [168, 112, 70], smooth(0.62, 0.85, fbm3(p[0] * 6, p[1] * 6, p[2] * 6, { octaves: 2, seed: 43 })) * 0.35);
    return col;
  });
  parts.push({ mesh: pit, material: { name: 'pit', texture: pitTex, roughness: 0.34 } });

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const sc = 1.15 / (max[1] - min[1]);
  const FACE_Y = 0.66;
  return {
    parts,
    face: (FACE_Y - min[1]) / (max[1] - min[1]),
    faceSize: 0.78,
    anchors: { pit: [0, YP, ZP], pitFront: [0, YP, ZP - RP] },
    notes:
      `Pit radius ${RP.toFixed(3)} model units = ${(RP * sc).toFixed(3)} m in app space (the socket behind it is ` +
      `${(RH * sc).toFixed(3)} m). Materials: skin, flesh, socket (the hollow left when the pit is hidden), pit. ` +
      `About ${(((RP - (ZP - (zMean - 0.02))) / (2 * RP)) * 100).toFixed(0)}% of the pit's diameter stands proud of the flesh round it, ` +
      `so it can pop straight out toward the camera (-Z). The slab (flesh front to the back of the skin) is ` +
      `${((KZ * WMAX + 0.045) * sc).toFixed(3)} m deep; the pit adds ${((zMean - 0.02 - (ZP - RP)) * sc).toFixed(3)} m in front.`,
  };

  /** Angle of the i-th of n points round a ring (counter-clockwise in x/y, from the bottom). */
  function ringPhase(i, n) {
    return -Math.PI / 2 + (TAU * (i % n)) / n;
  }
}

function hash(x, y, z, seed) {
  let h = Math.imul(x, 374761393) ^ Math.imul(y, 668265263) ^ Math.imul(z, 1274126177) ^ Math.imul(seed + 7, 1442695041);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}
/** Cellular noise: the distances to the nearest and second-nearest feature points (cell size 1). */
function worley(x, y, z, seed = 0) {
  const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z);
  let f1 = 9, f2 = 9;
  for (let dz = -1; dz <= 1; dz++)
    for (let dy = -1; dy <= 1; dy++)
      for (let dx = -1; dx <= 1; dx++) {
        const cx = xi + dx, cy = yi + dy, cz = zi + dz;
        const px = cx + hash(cx, cy, cz, seed), py = cy + hash(cx, cy, cz, seed + 1), pz = cz + hash(cx, cy, cz, seed + 2);
        const d = Math.hypot(px - x, py - y, pz - z);
        if (d < f1) {
          f2 = f1;
          f1 = d;
        } else if (d < f2) f2 = d;
      }
  return [f1, f2];
}

/**
 * Makes a mesh face the right way: test(centroid, normal) should be positive for an outward face; if most faces
 * say otherwise, every triangle is flipped.
 */
function orient(mesh, test) {
  let score = 0;
  for (let k = 0; k < mesh.indices.length; k += 3) {
    const [a, b, c] = [mesh.indices[k], mesh.indices[k + 1], mesh.indices[k + 2]].map((i) => mesh.point(i));
    const e1 = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], e2 = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
    const n = [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]];
    const l = Math.hypot(...n);
    if (l < 1e-12) continue;
    const cen = [(a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3, (a[2] + b[2] + c[2]) / 3];
    score += Math.sign(test(cen, [n[0] / l, n[1] / l, n[2] / l]));
  }
  if (score < 0) mesh.flip();
  return mesh;
}
