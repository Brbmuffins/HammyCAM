/**
 * Water balloon: a fat, glossy bubblegum-pink latex balloon full of water, sagging into a teardrop that is fullest
 * low down and sits on a flattened patch on the desk. The latex is paler where it is stretched thinnest (round the
 * belly), deeper pink toward the neck where faint stretch lines run in to it, and the water inside shows low down
 * as a deeper, sloshing band with a bright wavy waterline. On top: the pinched neck, a lumpy tied knot and the
 * leftover neck flicking out to one side, ending in its rolled rim.
 * Modelled at about app scale (1.15 m tall), so model units are roughly the app's metres.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, tube, sphere, fbm2, fbm3, noise3, smooth, lerp, clamp, TAU } = kit;

  // ---- the body's profile: a smooth curve through control points (Catmull-Rom), bottom to neck ------------
  const CTRL = [
    [0.0, 0.0], [0.15, 0.0], [0.264, 0.016], [0.344, 0.06], [0.4, 0.13], [0.43, 0.218], [0.438, 0.312], [0.43, 0.405],
    [0.408, 0.5], [0.372, 0.592], [0.322, 0.676], [0.258, 0.75], [0.184, 0.81], [0.112, 0.854], [0.06, 0.882], [0.036, 0.9], [0.028, 0.918],
  ];
  const catmull = (p0, p1, p2, p3, t) => {
    const t2 = t * t, t3 = t2 * t;
    return [0, 1].map((d) => 0.5 * (2 * p1[d] + (-p0[d] + p2[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t2 + (-p0[d] + 3 * p1[d] - 3 * p2[d] + p3[d]) * t3));
  };
  const curve = [];
  for (let i = 1; i < CTRL.length; i++) {
    const p0 = CTRL[Math.max(0, i - 2)], p1 = CTRL[i - 1], p2 = CTRL[i], p3 = CTRL[Math.min(CTRL.length - 1, i + 1)];
    const steps = i === 1 ? 4 : 8; // about 1.2 cm apart
    for (let k = i === 1 ? 0 : 1; k <= steps; k++) curve.push(catmull(p0, p1, p2, p3, k / steps));
  }
  const NECK = CTRL[CTRL.length - 1]; // where the body pinches into the neck
  const HB = NECK[1];
  const FACE_Y = 0.47;

  // ---- the latex texture ----------------------------------------------------------------------------------
  // u wraps round (front in the middle, screen left is u < 0.5); v 0..0.03 is a plain strip for the knot and the
  // neck's tail, v 0.03..1 runs from the neck (top) down to the bottom of the balloon.
  const TW = 1024, TH = 1024;
  const KNOT_V = 0.015;
  const vOf = (y) => 0.03 + 0.97 * clamp(1 - y / HB);
  const yOf = (v) => (1 - (v - 0.03) / 0.97) * HB;
  // radius at a height (for "how stretched is it here")
  const radiusAt = (y) => {
    for (let i = 1; i < curve.length; i++) if (curve[i][1] >= y) return lerp(curve[i - 1][0], curve[i][0], (y - curve[i - 1][1]) / (curve[i][1] - curve[i - 1][1] || 1));
    return 0;
  };
  const DEEP = [226, 28, 128], PINK = [255, 72, 164], PALE = [255, 116, 190];
  const WATER = (u) => 0.19 + 0.022 * Math.sin(u * TAU * 2 + 0.6) + 0.012 * Math.sin(u * TAU * 3 + 2.1); // slosh line height
  const tex = new Canvas(TW, TH, PINK);
  tex.shade((u, v) => {
    const cu = Math.cos(u * TAU), su = Math.sin(u * TAU);
    if (v < 0.03) {
      const n = fbm3(cu * 4, su * 4, v * 40, { octaves: 2, seed: 2 }) - 0.5;
      return DEEP.map((x) => x * (0.92 + 0.12 * n));
    }
    const y = yOf(v), r = radiusAt(y);
    // Stretch: paler where the latex is stretched thinnest (the widest part), deeper toward the pinched neck.
    const stretch = clamp(r / 0.436);
    let c = stretch < 0.5 ? DEEP.map((x, i) => lerp(x, PINK[i], smooth(0.04, 0.5, stretch))) : PINK.map((x, i) => lerp(x, PALE[i], smooth(0.6, 1, stretch) * 0.6));
    // Faint stretch lines running in to the neck (strongest near it, gone by the belly).
    const wob = fbm3(cu * 2, su * 2, y * 3, { octaves: 2, seed: 7 }) * 0.6;
    const lines = Math.max(0, Math.sin((u * 40 + wob * 3) * Math.PI)) ** 10 * smooth(HB - 0.2, HB - 0.03, y) * 0.32;
    c = c.map((x, i) => lerp(x, DEEP[i] * 0.92, lines));
    // The water inside: a deeper, cooler band low down with a soft wavy top and a bright waterline.
    const wl = WATER(u);
    const under = 1 - smooth(wl - 0.035, wl + 0.006, y);
    const caustic = Math.max(0, fbm3(cu * 3.5, su * 3.5, y * 9, { octaves: 3, seed: 15 }) - 0.55) * 2.2;
    const water = [214, 40, 150].map((x, i) => x * (0.86 + 0.25 * caustic) + [0, 0, 8][i]);
    c = c.map((x, i) => lerp(x, water[i], under * 0.5));
    const line = Math.exp(-(((y - wl - 0.002) / 0.004) ** 2)) * 0.3;
    c = c.map((x, i) => lerp(x, [255, 190, 226][i], line));
    // A soft milky bloom over the latex.
    const bloom = fbm3(cu * 6, su * 6, y * 8, { octaves: 3, seed: 4 }) - 0.5;
    return c.map((x) => x * (1 + 0.05 * bloom));
  });
  // A soft painted sheen up on the screen-left front (the app adds its own specular on top).
  tex.ellipse(0.4 * TW, vOf(0.56) * TH, 70, 120, [255, 200, 228], 0.32, 1);
  tex.ellipse(0.385 * TW, vOf(0.6) * TH, 18, 34, [255, 236, 246], 0.5, 1);
  const latex = { name: 'latex', texture: tex, roughness: 0.35 };

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });

  // ---- the body -------------------------------------------------------------------------------------------
  const prof = curve.map(([r, y], j) => [j === 0 ? 0 : r, y]);
  const body = lathe(prof, { segments: 96, v: (t, y) => vOf(y) });
  // Latex is never quite perfect: a soft wobble, kept off the bottom patch, the neck and the face.
  body.map(([x, y, z]) => {
    const r = Math.hypot(x, z);
    if (r < 1e-6) return [x, y, z];
    const a = Math.atan2(z, x);
    const front = Math.exp(-((Math.atan2(Math.sin(a + Math.PI / 2), Math.cos(a + Math.PI / 2)) / 0.9) ** 2)) * Math.exp(-(((y - FACE_Y) / 0.22) ** 2));
    const w = (fbm3(x * 3 + 5, y * 3, z * 3, { octaves: 2, seed: 9 }) - 0.5) * 0.016 * smooth(0.03, 0.12, y) * (1 - smooth(0.75, 0.9, y)) * (1 - 0.85 * front);
    const k = (r + w) / r;
    return [x * k, y, z * k];
  });
  add(body, latex);

  // ---- the neck, the knot and the tail --------------------------------------------------------------------
  const knotUV = (m) => {
    for (let i = 0; i < m.vertexCount; i++) {
      m.uvs[i * 2] = clamp(m.uvs[i * 2]);
      m.uvs[i * 2 + 1] = KNOT_V;
    }
    return m;
  };
  // The neck: a short pinched tube from the body up into the knot.
  const KS = 1.3; // the knot is built small, then scaled up about the neck's top (chunkier, reads at phone size)
  const neckTop = HB + 0.042 * KS;
  add(lathe([[NECK[0], NECK[1]], [0.027 * KS, HB + 0.01 * KS], [0.023 * KS, HB + 0.021 * KS], [0.022 * KS, HB + 0.032 * KS], [0.026 * KS, neckTop]], { segments: 24, v: () => KNOT_V }), latex);
  const big = (m) => m.map(([x, y, z]) => [x * KS, neckTop + (y - neckTop) * KS, z * KS]);
  // The knot: a squashed lumpy bulb with the neck's loop wrapped round it (a fat ring, tilted).
  const KC = [0, neckTop + 0.03, 0];
  const knot = sphere(0.046, { segments: 28, rows: 16, scale: [1, 0.82, 0.9] }).map(([x, y, z]) => {
    const w = 1 + 0.18 * (noise3(x * 45, y * 45, z * 45, 3) - 0.5);
    return [x * w, y, z * w];
  }).transform({ translate: KC });
  add(knotUV(big(knot)), latex);
  const wrap = [];
  for (let k = 0; k <= 32; k++) {
    const a = (TAU * k) / 32;
    wrap.push([0.045 * Math.cos(a), 0.006 * Math.sin(2 * a), 0.041 * Math.sin(a)]);
  }
  add(knotUV(big(tube(wrap, 0.015, { segments: 12 }).transform({ rotate: [6, 0, 20], translate: [KC[0], KC[1] - 0.004, KC[2]] }))), latex);
  const knotTop = neckTop + (KC[1] + 0.046 * 0.82 - neckTop) * KS;
  // The tail: the leftover neck, up out of the knot and hooking over toward screen left, flaring to its rim.
  const T0 = [KC[0] - 0.006, KC[1] + 0.02, KC[2]];
  const HOOK = 0.032, hc = [T0[0] + HOOK, T0[1] + 0.012];
  const tail = [[T0[0], T0[1] - 0.012, T0[2]]];
  for (let k = 0; k <= 16; k++) {
    const t = k / 16, a = lerp(Math.PI, -0.16 * Math.PI, t);
    const rr = HOOK * (1 - 0.12 * t);
    tail.push([hc[0] + rr * Math.cos(a), hc[1] + rr * Math.sin(a) * 0.95, T0[2] - 0.016 * t]);
  }
  const tr = tail.map((_, k) => {
    const t = k / (tail.length - 1);
    return lerp(0.016, 0.0125, smooth(0, 0.5, t)) + 0.008 * smooth(0.78, 1, t);
  });
  add(knotUV(big(tube(tail, 0.015, { segments: 14, radii: tr }))), latex);
  // The rolled rim: a ring round the tail's open end.
  const end = tail[tail.length - 1], prev = tail[tail.length - 2];
  const d = [end[0] - prev[0], end[1] - prev[1], end[2] - prev[2]];
  const dl = Math.hypot(...d);
  const n = d.map((x) => x / dl);
  const e1 = (() => {
    const c = [n[1] * 0 - n[2] * 1, n[2] * 0 - n[0] * 0, n[0] * 1 - n[1] * 0]; // n x z
    const l = Math.hypot(...c) || 1;
    return c.map((x) => x / l);
  })();
  const e2 = [n[1] * e1[2] - n[2] * e1[1], n[2] * e1[0] - n[0] * e1[2], n[0] * e1[1] - n[1] * e1[0]];
  const rimR = tr[tr.length - 1] + 0.002, rimPath = [];
  for (let k = 0; k <= 24; k++) {
    const a = (TAU * k) / 24;
    rimPath.push([0, 1, 2].map((i) => end[i] + n[i] * 0.002 + (e1[i] * Math.cos(a) + e2[i] * Math.sin(a)) * rimR));
  }
  add(knotUV(big(tube(rimPath, 0.0075, { segments: 10 }))), latex);

  // ---- anchors and notes ----------------------------------------------------------------------------------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const H = max[1] - min[1], s = 1.15 / H;
  // The balloon's centre: the middle of its volume (low, since it sags), from the profile.
  let vol = 0, mom = 0;
  for (let i = 1; i < curve.length; i++) {
    const dy = curve[i][1] - curve[i - 1][1], r = (curve[i][0] + curve[i - 1][0]) / 2, y = (curve[i][1] + curve[i - 1][1]) / 2;
    vol += r * r * dy;
    mom += r * r * dy * y;
  }
  const CY = mom / vol;
  return {
    parts,
    face: FACE_Y / H,
    faceSize: 0.8,
    anchors: { knot: [0, knotTop, 0], centre: [0, CY, 0] },
    notes: {
      maxRadius: +(0.436 * s).toFixed(3),
      widestAt: +(0.31 * s).toFixed(3),
      bodyTop: +(HB * s).toFixed(3),
      contactPatchRadius: +(0.15 * s).toFixed(3),
      waterlinePainted: +(0.19 * s).toFixed(3),
      material: 'latex (one textured material; knot and tail use its deep-pink strip)',
    },
  };
}
