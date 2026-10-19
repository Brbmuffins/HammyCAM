/**
 * Science-fair volcano: a lumpy papier-mache cone (newspaper strips under brushy brown/tan paint, greener lower
 * slopes) with a wide, rough, lava-painted crater, a few painted lava streaks running down the sides, a bubbling
 * lava pool down in the crater, a "1ST PRIZE" toothpick flag stuck in the left slope, all on a square plywood board
 * with paint splats on it. In the app it erupts out of the crater.
 */
export default function build(kit) {
  const { Mesh, Canvas, loft, box, tube, lathe, ringAngle, fbm3, noise3, smooth, clamp, lerp, TAU, rng } = kit;

  // ---------- sizes ----------
  const B = 0.8, T = 0.045; // board width and thickness
  const Hc = 0.875; // cone height above the board, up to the outer rim
  const Rb = 0.352, Rt = 0.15; // base radius, outer rim radius
  const LIP = 0.02; // the rolled rim lip's half-width
  const yRim = T + Hc;
  const Ri = Rt - 2 * LIP; // crater opening radius
  const FACE_H = 0.34 * Hc; // the face's height above the board
  const coneR = (h) => {
    const t = clamp(h / Hc);
    return Rt + (Rb - Rt) * (0.66 * (1 - t) + 0.34 * (1 - t) ** 3) + 0.02 * (1 - smooth(0, 0.08, h));
  };
  const wrapPi = (x) => ((((x + Math.PI) % TAU) + TAU) % TAU) - Math.PI;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  /** Azimuth from the front (+ toward screen right, -X) for a lathe angle. */
  const azimuth = (a) => wrapPi((3 * Math.PI) / 2 - a);
  /** The face's patch on the front of the cone (1 in the middle), kept smooth and plain. */
  const faceMask = (phi, h) => (1 - smooth(0.42, 0.85, Math.abs(phi))) * (1 - smooth(0.17, 0.3, Math.abs(h - FACE_H)));

  // ---------- the cone's profile: outer slope, rolled lip, crater wall, crater floor ----------
  const rows = [];
  const N1 = 52;
  for (let k = 0; k <= N1; k++) {
    const h = Hc * (k / N1);
    rows.push({ r: coneR(h), y: T + h - (k === 0 ? 0.004 : 0), zone: 'outer', h, rim: smooth(Hc - 0.12, Hc, h), lump: 1 });
  }
  const LIPN = 9;
  for (let k = 1; k <= LIPN; k++) {
    const ang = (Math.PI * k) / LIPN; // 0 = outside, PI = inside
    rows.push({ r: Rt - LIP + LIP * Math.cos(ang), y: yRim + LIP * 0.85 * Math.sin(ang), zone: 'lip', h: Hc, rim: 1, lump: 0.7 });
  }
  const wall = [
    [Ri - 0.003, yRim - 0.014, 1],
    [Ri - 0.009, yRim - 0.032, 0.95],
    [Ri - 0.018, yRim - 0.05, 0.85],
    [Ri - 0.03, yRim - 0.064, 0.6],
    [Ri - 0.05, yRim - 0.075, 0.3],
    [Ri * 0.45, yRim - 0.082, 0.1],
    [0, yRim - 0.086, 0],
  ];
  for (const [r, y, w] of wall) rows.push({ r, y, zone: 'wall', h: Hc, rim: w, lump: w * 0.6 });
  // Texture v follows the profile's length: 0 (top of the picture) is the crater floor, 1 the foot of the slope.
  const arc = [0];
  for (let j = 1; j < rows.length; j++) arc.push(arc[j - 1] + Math.hypot(rows[j].r - rows[j - 1].r, rows[j].y - rows[j - 1].y));
  const S = arc[arc.length - 1];
  rows.forEach((row, j) => (row.v = 1 - arc[j] / S));
  const sTop = arc[N1], sLipIn = arc[N1 + LIPN];

  const SEG = 128;
  const conePoint = (row, a) => {
    const c = Math.cos(a), s = Math.sin(a);
    const phi = azimuth(a);
    const base = [row.r * c, row.y, row.r * s];
    let amp = row.lump;
    if (row.zone === 'outer') amp *= (1 - 0.75 * faceMask(phi, row.h)) * (0.35 + 0.65 * smooth(0, 0.06, row.h));
    const big = fbm3(base[0] * 4.2, base[1] * 4.2, base[2] * 4.2, { octaves: 3, seed: 3 }) - 0.5;
    const small = fbm3(base[0] * 15, base[1] * 15, base[2] * 15, { octaves: 2, seed: 11 }) - 0.5;
    let r = row.r + (big * 0.12 + small * 0.028) * amp;
    let y = row.y;
    if (row.rim > 0) {
      // A rough, wavy, hand-made rim: up and down, in and out.
      y += row.rim * (noise3(c * 1.9 + 5, s * 1.9, 1.3, 21) - 0.5) * 0.085 + row.rim * (noise3(c * 7, s * 7, 2.2, 5) - 0.5) * 0.014;
      r += row.rim * (noise3(c * 3.2, s * 3.2, 4.1, 33) - 0.5) * 0.03;
    }
    if (row.r === 0) r = 0;
    return [r * c, y, r * s];
  };
  const rings = rows.map((row) => {
    const ring = [];
    for (let i = 0; i <= SEG; i++) ring.push(conePoint(row, ringAngle(i % SEG, SEG)));
    ring.v = row.v;
    return ring;
  });
  const cone = loft(rings);

  // ---------- the cone's paint ----------
  /** Height and radius at a distance s along the profile. */
  const along = (s) => {
    let lo = 0, hi = arc.length - 1;
    while (hi - lo > 1) {
      const m = (lo + hi) >> 1;
      if (arc[m] <= s) lo = m;
      else hi = m;
    }
    const f = clamp((s - arc[lo]) / (arc[hi] - arc[lo] || 1));
    return { r: lerp(rows[lo].r, rows[hi].r, f), y: lerp(rows[lo].y, rows[hi].y, f) };
  };
  const TW = 1024, TH = 1024;
  const tex = new Canvas(TW, TH, [150, 110, 70]);
  const GREEN = [86, 128, 50], GREEN2 = [58, 100, 42], BROWN = [128, 86, 50], TAN = [190, 150, 102], SCORCH = [92, 60, 40];
  const LAVA_CORE = [255, 196, 70], LAVA = [240, 92, 28], LAVA_EDGE = [168, 34, 18];

  // Lava streaks: azimuth they start from (+ = screen right), how far down (metres along the slope), width.
  // They stay off the front-centre, where the face goes (only short drools there, well above it).
  const streaks = [
    { phi: 1.22, len: 0.46, w: 0.05, seed: 1 },
    { phi: -1.12, len: 0.4, w: 0.046, seed: 2 },
    { phi: 1.75, len: 0.62, w: 0.055, seed: 3 },
    { phi: -2.0, len: 0.55, w: 0.05, seed: 4 },
    { phi: 2.75, len: 0.42, w: 0.046, seed: 5 },
    { phi: -2.85, len: 0.7, w: 0.06, seed: 6 },
    { phi: 0.42, len: 0.12, w: 0.034, seed: 7 },
    { phi: -0.3, len: 0.085, w: 0.03, seed: 8 },
    { phi: -0.62, len: 0.16, w: 0.03, seed: 9 },
  ];

  // Kid-painted pine trees round the green foot (none right under the face).
  const trees = [];
  {
    const R = rng(123);
    for (let k = 0; k < 40 && trees.length < 22; k++) {
      const phi = wrapPi(-Math.PI + (TAU * (k + R() * 0.6)) / 22);
      const size = lerp(0.07, 0.12, R());
      if (Math.abs(phi) < 0.5) continue;
      trees.push({ phi, h: lerp(0.012, 0.05, R()), size, lean: (R() - 0.5) * 0.25 });
    }
  }
  const treeAt = (phi, h, r) => {
    for (const tr of trees) {
      const x = wrapPi(phi - tr.phi) * r, y = h - tr.h;
      const H = tr.size, W = H * 0.36;
      if (y < -0.005 || y > H || Math.abs(x) > W * 1.2) continue;
      const xl = x - tr.lean * y;
      if (y < H * 0.3 && Math.abs(xl) < W * 0.13) return 'trunk';
      for (let k = 0; k < 3; k++) {
        const y0 = H * (0.22 + k * 0.22), th = H * 0.36;
        const hw = W * (1 - 0.24 * k) * (1 - (y - y0) / th);
        if (y >= y0 && y <= y0 + th && Math.abs(xl) < hw) return xl > hw * 0.15 ? 'tree' : 'treeLit';
      }
    }
    return null;
  };

  tex.shade((u, v) => {
    const phi = (u - 0.5) * TAU;
    const s = (1 - v) * S;
    const { r, y } = along(s);
    const h = y - T;
    const p = [r * Math.sin(phi), h, r * Math.cos(phi)];
    const blot = fbm3(p[0] * 5, p[1] * 5, p[2] * 5, { octaves: 3, seed: 2 });
    let col;
    if (s <= sTop + 0.012) {
      const t = h / Hc;
      const fm = faceMask(phi, h);
      // the grass line: wavy, with brushy upward flicks
      const xArc = phi * r * 90;
      const flick = Math.abs((xArc - Math.floor(xArc)) - 0.5) * 2;
      const gEdge = 0.12 + (blot - 0.5) * 0.16 - 0.03 * fm + 0.035 * flick * noise3(xArc * 0.13, 1, 2, 19);
      const green = 1 - smooth(gEdge - 0.008, gEdge + 0.008, t);
      // brown low down, warming to tan, scorched near the rim; uneven like hand-mixed paint
      const patch = fbm3(p[0] * 11 + 3, p[1] * 4, p[2] * 11, { octaves: 2, seed: 29 });
      col = mix(BROWN, TAN, smooth(0.08, 0.48, t + (blot - 0.5) * 0.35));
      col = mix(col, [150, 98, 58], smooth(0.55, 0.7, patch) * 0.55 * (1 - fm));
      col = mix(col, SCORCH, smooth(0.76, 1.0, t) * 0.65);
      col = mix(col, TAN, 0.3 * fm); // a calm, light patch for the face
      const leafy = fbm3(p[0] * 22, p[1] * 22, p[2] * 22, { octaves: 2, seed: 9 });
      col = mix(col, mix(GREEN, GREEN2, smooth(0.4, 0.65, leafy)), green);
      const tree = t < 0.25 ? treeAt(phi, h, r) : null;
      if (tree === 'trunk') col = [92, 60, 36];
      else if (tree === 'tree') col = [34, 76, 36];
      else if (tree === 'treeLit') col = [52, 104, 46];
    } else col = SCORCH;
    // Brush strokes running down the slope (two brushes), and the odd blotch.
    const strokes = fbm3(p[0] * 80, p[1] * 7, p[2] * 80, { octaves: 2, seed: 5 });
    const wide = fbm3(p[0] * 26, p[1] * 3.5, p[2] * 26, { octaves: 2, seed: 31 });
    const dabs = fbm3(p[0] * 30 + p[1] * 12, p[1] * 18, p[2] * 30, { octaves: 2, seed: 17 });
    col = mul(col, 0.8 + 0.2 * strokes + 0.16 * wide + 0.1 * (dabs - 0.5));
    return col;
  });

  // Newspaper strips under the paint: faint torn edges and rows of tiny print showing through.
  {
    const R = rng(77);
    const pxPerM = (s) => TW / (TAU * Math.max(0.12, along(s).r));
    for (let k = 0; k < 70; k++) {
      const u0 = R(), s0 = lerp(0.03, sTop - 0.04, R());
      const { y } = along(s0);
      const phi0 = (u0 - 0.5) * TAU;
      if (faceMask(phi0, y - T) > 0.02 || (Math.abs(phi0) < 1.1 && Math.abs(y - T - FACE_H) < 0.32)) continue;
      const cx = u0 * TW, cy = (1 - s0 / S) * TH;
      const sx = pxPerM(s0), sy = TH / S;
      const len = lerp(0.07, 0.16, R()), wid = lerp(0.025, 0.045, R());
      const ang = lerp(-1.2, 1.2, R()) + (R() < 0.5 ? 0 : Math.PI / 2);
      const ca = Math.cos(ang), sa = Math.sin(ang);
      const P = (lx, ly) => [cx + (lx * ca - ly * sa) * sx, cy + (lx * sa + ly * ca) * sy];
      // torn outline
      const outline = [];
      for (let q = 0; q <= 10; q++) outline.push([lerp(-len / 2, len / 2, q / 10), -wid / 2 + (R() - 0.5) * 0.004]);
      for (let q = 0; q <= 4; q++) outline.push([len / 2 + (R() - 0.5) * 0.006, lerp(-wid / 2, wid / 2, q / 4)]);
      for (let q = 10; q >= 0; q--) outline.push([lerp(-len / 2, len / 2, q / 10), wid / 2 + (R() - 0.5) * 0.004]);
      for (let q = 4; q >= 0; q--) outline.push([-len / 2 + (R() - 0.5) * 0.006, lerp(-wid / 2, wid / 2, q / 4)]);
      const pts = outline.map(([a, b]) => P(a, b));
      // A few strips at the sides and back were missed by the paintbrush altogether.
      const bare = Math.abs(phi0) > 1.7 && s0 > 0.18 && R() < 0.3;
      tex.poly(pts, bare ? [196, 189, 170] : [212, 206, 192], bare ? 0.8 : 0.05);
      tex.stroke(pts, 1.4, [235, 225, 200], bare ? 0.3 : 0.12);
      tex.stroke(pts.map(([a, b]) => [a + 1.2, b + 1.4]), 1.4, [60, 40, 25], bare ? 0.35 : 0.1);
      // print
      const ink = bare ? 0.55 : 0.14;
      for (let ly = -wid / 2 + 0.005; ly < wid / 2 - 0.004; ly += 0.0055) {
        let lx = -len / 2 + 0.006;
        if (R() < 0.12) {
          tex.stroke([P(lx, ly), P(Math.min(len / 2 - 0.006, lx + len * 0.6), ly)], 3, [40, 36, 34], ink * 1.2);
          ly += 0.004;
          continue;
        }
        while (lx < len / 2 - 0.008) {
          const wl = lerp(0.004, 0.014, R());
          const e = Math.min(len / 2 - 0.006, lx + wl);
          tex.stroke([P(lx, ly), P(e, ly)], 1.5, [40, 36, 34], ink);
          lx = e + 0.003;
        }
      }
      // paint brushed over the bare strip's edges
      if (bare) tex.stroke(pts, 4, mix(BROWN, TAN, 0.5), 0.45);
    }
  }

  // Lava: the crater's inside, the lip (spilling over in places), and the streaks.
  tex.shade((u, v) => {
    const phi = (u - 0.5) * TAU;
    const s = (1 - v) * S;
    const { r, y } = along(s);
    const h = y - T;
    const p = [r * Math.sin(phi), y, r * Math.cos(phi)];
    const n = fbm3(p[0] * 30, p[1] * 30, p[2] * 30, { octaves: 2, seed: 41 });
    // Where the painted lava starts on the lip: the inner half, spilling over in tongues.
    const spill = sTop + (sLipIn - sTop) * (0.55 - 0.45 * smooth(0.55, 0.8, noise3(Math.cos(phi) * 4, Math.sin(phi) * 4, 3, 45)));
    if (s > spill) {
      const deep = smooth(sLipIn, S * 0.98, s);
      let col = mix(LAVA, LAVA_CORE, smooth(0.45, 0.75, n) * 0.6 + deep * 0.3);
      if (s < spill + 0.01) col = mix(LAVA_EDGE, col, (s - spill) / 0.01);
      return col;
    }
    // Streaks down the outer slope (measured in metres, so they don't stretch with the texture).
    const fromTop = sTop - s; // metres below the rim
    let best = null;
    for (const st of streaks) {
      if (fromTop > st.len + st.w || fromTop < -0.02) continue;
      const wig = (noise3(fromTop * 9, st.seed * 3.1, 0.5, 51) - 0.5) * 0.28 + (noise3(fromTop * 25, st.seed, 1.5, 52) - 0.5) * 0.06;
      const centre = st.phi + wig * (Rt / Math.max(r, 0.1));
      const off = wrapPi(phi - centre) * r; // metres across
      const taper = (1 - 0.35 * smooth(0, st.len, fromTop)) * (1 + 0.8 * (1 - smooth(-0.01, 0.07, fromTop)));
      const halfW = (st.w / 2) * taper * (0.85 + 0.3 * noise3(fromTop * 14, st.seed, 2.5, 53));
      let d = Math.abs(off) / halfW;
      // a fat drip blob at the end
      const blobR = halfW * 1.45;
      const dEnd = Math.hypot(off, fromTop - st.len) / blobR;
      if (fromTop > st.len) d = dEnd;
      else d = Math.min(d, dEnd);
      if (d < 1 && (!best || d < best.d)) best = { d, along: fromTop / st.len, halfW };
    }
    if (best) {
      const { d } = best;
      let col = mix(LAVA_CORE, LAVA, smooth(0.0, 0.55, d) * 0.9 + best.along * 0.25);
      col = mix(col, LAVA_EDGE, smooth(0.72, 0.98, d));
      col = mul(col, 0.92 + 0.12 * n);
      return [...col, clamp((1 - d) * 18)];
    }
    return null;
  });

  // Little lava splats round the rim and streaks (messy painting).
  {
    const R = rng(91);
    for (let k = 0; k < 60; k++) {
      const st = streaks[Math.floor(R() * 6)];
      const phi = st.phi + (R() - 0.5) * 0.9;
      const fromTop = R() * st.len * 0.9;
      const s = sTop - fromTop;
      const { r, y } = along(s);
      if (faceMask(phi, y - T) > 0.01) continue;
      const cx = (0.5 + phi / TAU) * TW, cy = (1 - s / S) * TH;
      const rad = lerp(1.5, 5, R() ** 2);
      tex.ellipse(cx, cy, (rad * 0.16) / Math.max(r, 0.12), rad, LAVA, 0.9);
    }
  }

  // Grain, a darker foot where it meets the board, and shadow just inside the crater.
  tex.shade((u, v) => {
    const s = (1 - v) * S;
    const { y } = along(s);
    const g = Math.sin(u * 3100 + Math.sin(v * 900) * 2) * Math.sin(v * 2900);
    const foot = 1 - smooth(0, 0.035, y - T);
    const inside = smooth(sLipIn, sLipIn + 0.03, s) * (1 - smooth(sLipIn + 0.03, S, s));
    return [20, 12, 8, clamp(0.03 + 0.03 * g + foot * 0.35 + inside * 0.25)];
  });
  tex.speckle(1600, [0.6, 1.4], [70, 45, 30], 0.18, 5);
  tex.speckle(900, [0.6, 1.4], [235, 215, 180], 0.15, 6);

  const papier = { name: 'papier_mache', texture: tex, roughness: 0.92 };
  const parts = [{ mesh: cone, material: papier }];

  // ---------- the lava pool down in the crater ----------
  const poolY = yRim - 0.046;
  const wallR = (yy) => {
    for (let k = 0; k + 1 < wall.length; k++) {
      const [r0, y0] = wall[k], [r1, y1] = wall[k + 1];
      if (yy <= y0 && yy >= y1) return lerp(r0, r1, (y0 - yy) / (y0 - y1));
    }
    return Ri;
  };
  const poolR = wallR(poolY) + 0.012;
  const pool = lathe([[poolR, poolY - 0.006], [poolR * 0.8, poolY + 0.002], [poolR * 0.45, poolY + 0.005], [0, poolY + 0.006]], { segments: 48 });
  pool.map(([x, yy, z]) => [x, yy + (fbm3(x * 40, 1, z * 40, { octaves: 2, seed: 61 }) - 0.5) * 0.008, z]);
  const lavaTex = new Canvas(256, 128, LAVA);
  lavaTex.shade((u, v) => {
    const a = ringAngle(0, 1) + TAU * (1 - u), rr = v;
    const x = rr * Math.cos(a), z = rr * Math.sin(a);
    const n = fbm3(x * 5, z * 5, 0.3, { octaves: 4, seed: 64 });
    const crust = smooth(0.58, 0.66, fbm3(x * 3 + 2, z * 3, 1.7, { octaves: 3, seed: 66 }));
    let col = mix(LAVA_CORE, LAVA, smooth(0.3, 0.9, rr) * 0.8 + (n - 0.5));
    col = mix(col, [120, 30, 16], crust * 0.75);
    return col;
  });
  for (const [bx, by, br] of [[60, 40, 7], [150, 30, 5], [200, 70, 6], [100, 90, 4], [30, 100, 5]]) {
    lavaTex.disc(bx, by, br, [255, 225, 120], 0.9, 0.5);
    lavaTex.disc(bx - 1, by - 1, br * 0.45, [255, 250, 210], 0.8, 0.5);
  }
  parts.push({ mesh: pool, material: { name: 'lava', texture: lavaTex, roughness: 0.55, emissive: [0.32, 0.08, 0.01] } });

  // ---------- the plywood board ----------
  const STEPS = 2, NB = STEPS * 2;
  const board = box(B, T, B, { round: 0.006, steps: STEPS }).transform({ translate: [0, T / 2, 0] });
  const perFace = (NB + 1) * (NB + 1);
  const TOPV = 0.78;
  for (let i = 0; i < board.vertexCount; i++) {
    const f = Math.floor(i / perFace);
    const [x, y, z] = board.point(i);
    let uv;
    if (f === 4 || f === 5) uv = [(B / 2 - x) / B, ((B / 2 - z) / B) * TOPV];
    else {
      const along = f === 0 ? (B / 2 - x) / B : f === 1 ? (x + B / 2) / B : f === 2 ? (z + B / 2) / B : (B / 2 - z) / B;
      uv = [clamp(along), 0.8 + 0.2 * clamp((T - y) / T)];
    }
    board.uvs[i * 2] = uv[0];
    board.uvs[i * 2 + 1] = uv[1];
  }
  const ply = new Canvas(1024, 1024, [222, 192, 146]);
  ply.shade((u, v) => {
    if (v < 0.79) {
      const x = u * 8, z = (v / TOPV) * 8;
      const warp = fbm3(x * 0.6, z * 2.2, 0.5, { octaves: 3, seed: 71 });
      const grain = 0.5 + 0.5 * Math.sin((z + warp * 3.2) * 11);
      const fine = fbm3(x * 2, z * 40, 1.5, { octaves: 2, seed: 73 });
      const k = 0.9 + 0.08 * grain + 0.08 * (fine - 0.5);
      return [224 * k, 194 * k, 148 * k];
    }
    // the edge: plies of alternating grain
    const t = clamp((v - 0.8) / 0.2);
    const layer = Math.floor(t * 5);
    const base = layer % 2 ? [196, 160, 112] : [228, 200, 154];
    const glue = Math.abs(t * 5 - Math.round(t * 5)) < 0.06 ? 0.72 : 1;
    const fine = fbm3(u * 300, t * 6, 2.5, { octaves: 2, seed: 75 });
    const k = glue * (0.9 + 0.16 * (fine - 0.5));
    return [base[0] * k, base[1] * k, base[2] * k];
  });
  {
    // A couple of football-shaped plugs and paint splats round the volcano's foot.
    const R = rng(33);
    for (const [px, py] of [[120, 150], [880, 640], [900, 120]]) ply.ellipse(px, py, 34, 12, [210, 176, 128], 0.6, 0.15);
    const toPx = (x, z) => [((B / 2 - x) / B) * 1024, ((B / 2 - z) / B) * TOPV * 1024];
    const colours = [GREEN, BROWN, LAVA, TAN, LAVA];
    for (let k = 0; k < 46; k++) {
      const a = R() * TAU, rr = Rb + 0.01 + R() ** 1.5 * 0.2;
      const x = rr * Math.cos(a), z = rr * Math.sin(a);
      if (Math.abs(x) > B / 2 - 0.01 || Math.abs(z) > B / 2 - 0.01) continue;
      const [px, py] = toPx(x, z);
      const c = colours[Math.floor(R() * colours.length)];
      const rad = lerp(2, 11, R() ** 2);
      ply.disc(px, py, rad, c, 0.9, 0.2);
      for (let q = 0; q < 4; q++) ply.disc(px + (R() - 0.5) * rad * 4, py + (R() - 0.5) * rad * 4, rad * lerp(0.15, 0.35, R()), c, 0.9, 0.3);
    }
    // a drip of green paint over the front edge
    const [dx, dy] = toPx(0.24, -B / 2 + 0.02);
    ply.ellipse(dx, dy, 18, 7, GREEN, 0.95, 0.2);
    ply.ellipse(dx, 1024 * (TOPV - 0.004), 9, 6, GREEN, 0.95, 0.2);
    ply.stroke([[dx - 2, 0.8 * 1024], [dx - 2, 0.93 * 1024]], 8, GREEN, 0.95);
    ply.disc(dx - 2, 0.94 * 1024, 6, GREEN, 0.95, 0.2);
    const [lx, ly] = toPx(-0.2, -B / 2 + 0.03);
    ply.ellipse(lx, ly, 14, 6, LAVA, 0.95, 0.2);
  }
  parts.push({ mesh: board, material: { name: 'plywood', texture: ply, roughness: 0.88 } });

  // ---------- the toothpick flag in the left slope ----------
  const FPHI = -1.0, FH = 0.6 * Hc; // azimuth (screen left: the preview's three-quarter and side views look at that side) and height
  const fr = coneR(FH) - 0.004;
  const P0 = [-fr * Math.sin(FPHI), T + FH, -fr * Math.cos(FPHI)];
  const outward = [-Math.sin(FPHI), 0, -Math.cos(FPHI)];
  const norm = (a) => {
    const l = Math.hypot(...a);
    return a.map((x) => x / l);
  };
  const dir = norm([outward[0] * 0.42, 0.9, outward[2] * 0.42]);
  const pick = (k) => P0.map((p, d) => p + dir[d] * k);
  const PICK = 0.16;
  const pickTop = pick(PICK);
  parts.push({
    mesh: tube([pick(-0.035), pick(0), pick(PICK * 0.5), pickTop, pick(PICK + 0.012)], 0.0045, { segments: 8, radii: [0.0045, 0.0045, 0.0045, 0.0042, 0.0012] }),
    material: { name: 'toothpick', color: [0.86, 0.76, 0.58], roughness: 0.8 },
  });
  const FW = 0.135, FHt = 0.088;
  const flagTex = new Canvas(384, 256, [250, 247, 236]);
  flagTex.rect(0, 0, 384, 14, [214, 52, 46]);
  flagTex.rect(0, 242, 384, 256, [214, 52, 46]);
  flagTex.rect(358, 0, 384, 256, [40, 90, 176]); // the end wrapped round the toothpick (screen right)
  const star = (cx, cy, R0, col) => {
    const pts = [];
    for (let k = 0; k < 10; k++) {
      const a = -Math.PI / 2 + (Math.PI * k) / 5, rr = k % 2 ? R0 * 0.45 : R0;
      pts.push([cx + rr * Math.cos(a), cy + rr * Math.sin(a)]);
    }
    flagTex.poly(pts, col);
  };
  flagTex.text('1ST', 178, 36, 96, [206, 40, 40], { weight: 0.2 });
  flagTex.text('PRIZE', 178, 160, 58, [36, 74, 160], { weight: 0.2 });
  star(52, 84, 30, [244, 186, 40]);
  star(306, 84, 30, [244, 186, 40]);
  flagTex.speckle(300, [0.5, 1.2], [180, 170, 150], 0.2, 3);
  const flag = box(FW, FHt, 0.003, { round: 0.0012, steps: 4 });
  // The flag's frame: up along the toothpick; the flag runs out to screen left (+X), its far end swung back a
  // little so it faces both the camera and the three-quarter view.
  const yaw = 0.35;
  let side = [Math.cos(yaw), 0, Math.sin(yaw)];
  const dotp = side[0] * dir[0] + side[1] * dir[1] + side[2] * dir[2];
  side = norm(side.map((x, d) => x - dir[d] * dotp));
  const back = [side[1] * dir[2] - side[2] * dir[1], side[2] * dir[0] - side[0] * dir[2], side[0] * dir[1] - side[1] * dir[0]];
  flag.map(([x, y, z]) => {
    const lx = x + FW / 2 + 0.003, ly = y - FHt / 2 + 0.004;
    const lz = z + 0.006 * Math.sin((lx / FW) * Math.PI * 1.5);
    return [0, 1, 2].map((d) => pickTop[d] + side[d] * lx + dir[d] * ly + back[d] * lz);
  });
  parts.push({ mesh: flag, material: { name: 'flag_paper', texture: flagTex, roughness: 0.9 } });

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const appScale = 1.15 / (max[1] - min[1]);
  return {
    parts,
    face: (T + FACE_H - min[1]) / (max[1] - min[1]),
    faceSize: 0.86,
    anchors: { crater: [0, yRim, 0], boardTop: [0, T, -B / 2 + 0.03] },
    notes:
      `Crater opening radius ${Ri.toFixed(3)} model units = ${(Ri * appScale).toFixed(3)} m in app space ` +
      `(outer rim radius ${(Rt * appScale).toFixed(3)} m); the crater is a shallow bowl with a glowing lava pool ` +
      `${((yRim - poolY) * appScale).toFixed(3)} m below the rim. The board is ${(B * appScale).toFixed(2)} m square, ` +
      `${(T * appScale).toFixed(3)} m thick. Flag on the screen-left slope.`,
  };
}
