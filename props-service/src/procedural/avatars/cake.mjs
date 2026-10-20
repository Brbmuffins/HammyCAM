/**
 * Birthday cake: a tall two-layer cake in pastel pink frosting with a squished-out band of cream-and-jam filling
 * between the layers, a white vanilla glaze over the top that runs down in drips (short nubs over the face, long
 * ones round the sides and back), a ring of raspberry-pink piped rosettes on the rim and white ones round the foot,
 * rainbow sprinkles on top, on the glaze and spilled on the plate, a finger swipe somebody stole off the screen-left
 * side, all on a mint plate. Five short spiral-striped candles stand on top; the app lights flames on the wicks.
 */
export default function build(kit) {
  const { Mesh, Canvas, loft, lathe, tube, sphere, ringAngle, fbm3, noise3, smooth, clamp, lerp, TAU, rng, vec } = kit;

  // ---------- sizes ----------
  const RP = 0.47; // plate radius
  const FLOOR = 0.016; // the plate's floor, where the cake stands
  const R = 0.38; // cake radius
  const Y1 = 0.312, Y2 = 0.372; // the filling band between the layers
  const YT = 0.84; // the top of the cake at the rim
  const RE = 0.036, RS = 0.024, RB = 0.022; // top edge, seam edges, foot rounding
  const DOME = 0.01; // the top bulges up a touch in the middle
  const FACE_Y = 0.566;
  const wrapPi = (x) => ((((x + Math.PI) % TAU) + TAU) % TAU) - Math.PI;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  const azimuth = (a) => wrapPi((3 * Math.PI) / 2 - a); // 0 at the front, + toward screen right (-X)
  const faceMask = (phi, y) => (1 - smooth(0.55, 0.85, Math.abs(phi))) * (1 - smooth(0.18, 0.26, Math.abs(y - FACE_Y)));
  const topY = (r) => YT + DOME * (1 - (Math.min(r, R - RE) / (R - RE)) ** 2);

  // ---------- the glaze and its drips ----------
  const GT = 0.004; // glaze thickness
  const glazeEdge = (phi) => RE + 0.013 + (noise3(Math.cos(phi) * 3, Math.sin(phi) * 3, 0.5, 5) - 0.5) * 0.016;
  // azimuth, length past the glaze's edge, half-width, thickness. Over the face only short nubs.
  const drips = [
    { phi: 0.02, len: 0.012, w: 0.026, t: 0.009 },
    { phi: 0.36, len: 0.01, w: 0.026, t: 0.009 },
    { phi: -0.34, len: 0.008, w: 0.024, t: 0.009 },
    { phi: 0.74, len: 0.06, w: 0.03, t: 0.011 },
    { phi: -0.72, len: 0.045, w: 0.03, t: 0.011 },
    { phi: 0.93, len: 0.13, w: 0.034, t: 0.012 },
    { phi: -0.9, len: 0.1, w: 0.034, t: 0.012 },
    { phi: 1.27, len: 0.2, w: 0.04, t: 0.013 },
    { phi: -1.28, len: 0.17, w: 0.038, t: 0.013 },
    { phi: 1.64, len: 0.08, w: 0.03, t: 0.011 },
    { phi: -1.68, len: 0.23, w: 0.042, t: 0.014 },
    { phi: 2.0, len: 0.15, w: 0.036, t: 0.012 },
    { phi: -2.05, len: 0.09, w: 0.032, t: 0.011 },
    { phi: 2.4, len: 0.22, w: 0.04, t: 0.013 },
    { phi: -2.45, len: 0.15, w: 0.036, t: 0.012 },
    { phi: 2.8, len: 0.1, w: 0.032, t: 0.011 },
    { phi: -2.82, len: 0.2, w: 0.038, t: 0.013 },
    { phi: 3.12, len: 0.12, w: 0.034, t: 0.012 },
  ].map((d, k) => ({ ...d, seed: k + 1 }));
  /** How far the glaze stands off the frosting at (phi, metres below the top), and how much of it is glaze (0..1). */
  const glazeAt = (phi, below, grow = 1) => {
    const ge = glazeEdge(phi);
    const b = below - ge; // below the glaze's edge
    const band = 1 - smooth(-0.002, 0.002, b);
    let best = GT * band, cover = band;
    for (const d of drips) {
      if (b < -0.04 || b > d.len + d.w * 2.5) continue;
      const wig = (noise3(b * 7, d.seed * 2.3, 0.5, 11) - 0.5) * 0.03;
      const across = wrapPi(phi - d.phi) * R - wig;
      const f = clamp(b / d.len);
      const w = grow * d.w * (1 - 0.3 * f) * (0.9 + 0.2 * noise3(b * 12, d.seed, 2.5, 13)) * (1 + 0.8 * (1 - smooth(-0.015, 0.03, b)));
      let k = 1 - (across / w) ** 2;
      let amp = lerp(GT, d.t, smooth(-0.02, 0.012, b));
      if (b > d.len) k = -1;
      // the bead at the end
      const bw = w * 1.3;
      const kb = 1 - (across / bw) ** 2 - ((b - d.len) / (bw * 1.15)) ** 2;
      if (kb > 0 && Math.sqrt(kb) * d.t * 1.3 > Math.sqrt(Math.max(0, k)) * amp) {
        k = kb;
        amp = d.t * 1.3;
      }
      if (k > 0) {
        const off = Math.sqrt(k) * amp;
        if (off > best) best = off;
        cover = Math.max(cover, smooth(0, 0.2, k));
      }
    }
    return { off: best, cover };
  };

  // ---------- the finger swipe (screen left, low on the bottom layer) ----------
  const SW = { phi: -1.36, y: 0.19, len: 0.05, slope: 0.1, hw: 0.03, depth: 0.016 };
  const swipeAt = (phi, y) => {
    const x = wrapPi(phi - SW.phi) * R;
    const yy = y - SW.y - x * SW.slope;
    const cx = clamp(x, -SW.len, SW.len);
    const d = Math.hypot((x - cx) * 0.9, yy) / (SW.hw * (1 + 0.25 * smooth(-SW.len, SW.len, x)));
    const groove = d < 1 ? Math.sqrt(1 - d * d) : 0;
    // frosting pushed up round the edge, piled thickest where the finger lifted off (x > 0)
    const lip = Math.exp(-(((d - 1.22) / 0.2) ** 2)) * (0.6 + 0.8 * smooth(0, SW.len * 1.3, x));
    return { groove, lip, up: yy / SW.hw };
  };

  // ---------- the cake's profile (bottom centre up to top centre) ----------
  const prof = [];
  const P = (r, y, zone, sharp = false) => prof.push({ r, y, zone, sharp });
  const y0 = FLOOR - 0.006;
  P(0, y0, 'under');
  P(R * 0.6, y0, 'under');
  P(R - RB, y0, 'under');
  for (let k = 1; k <= 4; k++) {
    const a = -Math.PI / 2 + (Math.PI / 2) * (k / 4);
    P(R - RB + RB * Math.cos(a), y0 + RB + RB * Math.sin(a), 'base');
  }
  {
    // rows every couple of cm, dense where the swipe is
    let y = y0 + RB;
    while (y < Y1 - RS - 0.012) {
      y += y > 0.15 && y < 0.235 ? 0.0085 : 0.024;
      P(R, Math.min(y, Y1 - RS - 0.006), 'base');
    }
  }
  P(R, Y1 - RS, 'base');
  for (let k = 1; k <= 5; k++) {
    const a = (Math.PI / 2) * (k / 5);
    P(R - RS + RS * Math.cos(a), Y1 - RS + RS * Math.sin(a), 'seam');
  }
  const FN = 8;
  for (let k = 0; k <= FN; k++) {
    const t = k / FN;
    P(R - RS - 0.004 + 0.022 * Math.sin(Math.PI * t) ** 0.7, lerp(Y1 + 0.001, Y2 - 0.001, t), 'filling', k === 0 || k === FN);
  }
  for (let k = 0; k <= 5; k++) {
    const a = -Math.PI / 2 + (Math.PI / 2) * (k / 5);
    P(R - RS + RS * Math.cos(a), Y2 + RS + RS * Math.sin(a), 'seam');
  }
  {
    let y = Y2 + RS;
    while (y < YT - RE - 0.006) {
      y += y < 0.535 ? 0.021 : 0.011;
      P(R, Math.min(y, YT - RE - 0.003), 'wall');
    }
  }
  P(R, YT - RE, 'wall');
  for (let k = 1; k <= 7; k++) {
    const a = (Math.PI / 2) * (k / 7);
    P(R - RE + RE * Math.cos(a), YT - RE + RE * Math.sin(a), 'edge');
  }
  for (let k = 1; k <= 7; k++) {
    const r = (R - RE) * (1 - k / 7);
    P(r, topY(r), 'top');
  }
  // texture v: by arc length, top centre 0 .. bottom centre 1 (the underside and the top get less of the picture)
  const weight = { under: 0.12, top: 0.55 };
  {
    let s = 0;
    prof[0].s = 0;
    for (let k = 1; k < prof.length; k++) {
      const a = prof[k - 1], b = prof[k];
      s += Math.hypot(b.r - a.r, b.y - a.y) * (weight[b.zone] ?? 1);
      b.s = s;
    }
    for (const p of prof) p.v = 1 - p.s / s;
  }

  // ---------- the cake mesh ----------
  const SEG = 104;
  const offsetAt = (p, phi) => {
    if (p.zone === 'wall' || p.zone === 'edge') {
      let off = glazeAt(phi, YT - p.y).off;
      if (p.zone === 'wall') off += (fbm3(Math.cos(phi) * 3, p.y * 7, Math.sin(phi) * 3, { octaves: 2, seed: 21 }) - 0.5) * 0.005 * (1 - 0.8 * faceMask(phi, p.y));
      return off;
    }
    if (p.zone === 'base') {
      const { groove, lip } = swipeAt(phi, p.y);
      return -SW.depth * groove + 0.0045 * lip + (fbm3(Math.cos(phi) * 3, p.y * 7, Math.sin(phi) * 3, { octaves: 2, seed: 22 }) - 0.5) * 0.005;
    }
    if (p.zone === 'filling') return (fbm3(Math.cos(phi) * 9, p.y * 30, Math.sin(phi) * 9, { octaves: 2, seed: 23 }) - 0.5) * 0.008;
    if (p.zone === 'seam') return (fbm3(Math.cos(phi) * 3, p.y * 7, Math.sin(phi) * 3, { octaves: 2, seed: 22 }) - 0.5) * 0.004;
    return 0;
  };
  const rings = [];
  for (const p of prof) {
    const ring = [];
    for (let i = 0; i <= SEG; i++) {
      const a = ringAngle(i % SEG, SEG);
      const r = p.r > 1e-6 ? p.r + offsetAt(p, azimuth(a)) : 0;
      ring.push([r * Math.cos(a), p.y, r * Math.sin(a)]);
    }
    ring.v = p.v;
    rings.push(ring);
    if (p.sharp) {
      const twin = ring.map((q) => [...q]);
      twin.v = p.v;
      rings.push(twin);
    }
  }
  const cake = loft(rings);

  // ---------- frosting paint ----------
  const PINK = [250, 180, 200], PINK_DEEP = [232, 138, 166], GLAZE = [255, 251, 244], GLAZE_SHADE = [236, 226, 222];
  const CREAM = [255, 243, 218], JAM = [206, 30, 66], CRUMB = [242, 204, 128];
  const TW = 1024, TH = 1024;
  const rowInfo = [];
  {
    // v = 1 - s / S, so walking the texture up from its bottom row walks the profile up from the bottom centre
    const S = prof[prof.length - 1].s;
    let j = 1;
    for (let py = TH - 1; py >= 0; py--) {
      const s = (1 - (py + 0.5) / TH) * S;
      while (j < prof.length - 1 && prof[j].s < s) j++;
      const a = prof[j - 1], b = prof[j];
      const t = clamp((s - a.s) / (b.s - a.s || 1));
      rowInfo[py] = { y: lerp(a.y, b.y, t), r: lerp(a.r, b.r, t), zone: t < 0.5 ? a.zone : b.zone };
    }
  }
  const frost = new Canvas(TW, TH, PINK);
  frost.shade((u, v) => {
    const phi = (u - 0.5) * TAU;
    const { y, r, zone } = rowInfo[Math.min(TH - 1, Math.floor(v * TH))];
    const p = [Math.sin(phi) * R, y, Math.cos(phi) * R];
    const mottle = fbm3(p[0] * 6, p[1] * 6, p[2] * 6, { octaves: 3, seed: 31 });
    const fm = faceMask(phi, y);
    if (zone === 'top' || zone === 'edge') {
      let col = mul(GLAZE, 0.985 + 0.03 * (mottle - 0.5));
      if (zone === 'top') col = mix(col, GLAZE_SHADE, 0.25 * smooth(0.6, 1, fbm3(p[0] * 3 + r * 9, r * 14, p[2] * 3, { octaves: 3, seed: 37 })));
      return col;
    }
    if (zone === 'filling') {
      const t = (y - Y1) / (Y2 - Y1);
      let col = Math.abs(t - 0.5) < 0.2 + 0.05 * (noise3(phi * 6, 0, 0, 41) - 0.5) ? JAM : CREAM;
      col = mul(col, 0.94 + 0.1 * mottle);
      // the edges of the band sit back in the shadow of the layers
      return mul(col, 1 - 0.18 * smooth(0.25, 0.5, Math.abs(t - 0.5)));
    }
    let col = mix(PINK_DEEP, PINK, smooth(0.0, 0.1, y));
    // spatula marks: faint horizontal sweeps (quieter where the face goes)
    const sweep = Math.sin(y * 150 + fbm3(p[0] * 2, p[1] * 2, p[2] * 2, { octaves: 2, seed: 33 }) * 12);
    col = mul(col, 1 + (0.014 * sweep + 0.06 * (mottle - 0.5)) * (1 - 0.7 * fm));
    if (zone === 'seam') col = mul(col, 0.93);
    if (zone === 'under') return mul(col, 0.8);
    if (zone === 'wall') {
      const below = YT - y;
      const { cover } = glazeAt(phi, below);
      // a soft shadow beside and under the glaze, so it stands off the pink
      const halo = glazeAt(phi, below - 0.006, 1.3).cover;
      col = mul(col, 1 - 0.13 * Math.max(0, halo - cover));
      if (cover > 0) col = mix(col, mix(GLAZE, GLAZE_SHADE, 0.3 * smooth(0.05, 0.2, below)), cover);
    }
    if (zone === 'base') {
      const { groove, lip, up } = swipeAt(phi, y);
      if (groove > 0) {
        // the cake shows through at the bottom of the groove, a thin smear of frosting on its sides
        const crumb = mul(CRUMB, 0.9 + 0.2 * fbm3(p[0] * 60, p[1] * 60, p[2] * 60, { octaves: 2, seed: 43 }));
        col = mix(mul(col, 1.04), crumb, smooth(0.62, 0.85, groove));
        col = mul(col, 1 - 0.18 * smooth(0.4, 0, groove) * smooth(0, 0.1, groove));
        // the groove's upper wall faces down, into its own shadow; the lower one catches the light
        col = mul(col, 1 - 0.3 * smooth(0.1, 0.85, up) + 0.08 * smooth(-0.1, -0.8, up));
      }
      col = mul(col, 1 + 0.05 * lip);
    }
    return col;
  });
  // little dark pores in the crumb
  {
    const R0 = rng(17);
    const baseRows = rowInfo.map((ri, q) => [ri, q]).filter(([ri]) => ri.zone === 'base');
    for (let k = 0; k < 500; k++) {
      const phi = SW.phi + (R0() - 0.5) * 0.45, y = SW.y + (R0() - 0.5) * 0.06;
      if (swipeAt(phi, y).groove < 0.75) continue;
      let best = null;
      for (const [ri, q] of baseRows) if (!best || Math.abs(ri.y - y) < Math.abs(best[0].y - y)) best = [ri, q];
      frost.disc((phi / TAU + 0.5) * TW, best[1], lerp(0.6, 1.6, R0()), [178, 128, 60], 0.6, 0.5);
    }
  }
  const frosting = { name: 'frosting', texture: frost, roughness: 0.82 };
  const parts = [{ mesh: cake, material: frosting }];

  // ---------- piped rosettes ----------
  const rosette = (r0, h, { ridges = 7, twist = 5, seg = 21 } = {}) =>
    lathe(
      [[0, -0.004], [r0 * 0.86, -0.004], [r0, 0.14 * h], [r0 * 0.96, 0.32 * h], [r0 * 0.8, 0.53 * h], [r0 * 0.56, 0.72 * h], [r0 * 0.3, 0.87 * h], [r0 * 0.1, 0.97 * h], [0, h]],
      { segments: seg, section: (t, a, r) => {
        const k = 1 + 0.2 * (1 - 0.5 * t) * Math.cos(ridges * a + twist * t);
        return [r * k * Math.cos(a), r * k * Math.sin(a)];
      } },
    );
  const pinkPiping = { name: 'piping_pink', color: [0.86, 0.16, 0.34], roughness: 0.8 };
  const whitePiping = { name: 'piping_white', color: [0.95, 0.9, 0.84], roughness: 0.82 };
  const TOPN = 15, TOPR = R - 0.05;
  for (let k = 0; k < TOPN; k++) {
    const a = ringAngle(k, TOPN) + Math.PI / TOPN; // none dead centre at the front
    const m = rosette(0.047, 0.054, { twist: 5 + (k % 3) }).transform({ rotate: [0, (k * 47) % 360, 0], translate: [TOPR * Math.cos(a), topY(TOPR) - 0.002, TOPR * Math.sin(a)] });
    parts.push({ mesh: m, material: pinkPiping });
  }
  const BASEN = 20, BASER = R + 0.012;
  for (let k = 0; k < BASEN; k++) {
    const a = ringAngle(k, BASEN);
    const m = rosette(0.039, 0.046, { seg: 16, twist: 4 + (k % 2) * 2 }).transform({ rotate: [0, (k * 61) % 360, 0], translate: [BASER * Math.cos(a), FLOOR, BASER * Math.sin(a)] });
    parts.push({ mesh: m, material: whitePiping });
  }

  // ---------- sprinkles ----------
  const SPR = [[236, 56, 64], [255, 150, 36], [255, 218, 40], [70, 200, 96], [56, 140, 240], [168, 88, 222], [255, 108, 178], [250, 250, 250]];
  const sprTex = new Canvas(64, 8, [255, 255, 255]);
  SPR.forEach((c, k) => sprTex.rect(k * 8, 0, k * 8 + 8, 8, c));
  const sprMat = { name: 'sprinkles', texture: sprTex, roughness: 0.6 };
  const capsule = (len, rad) => lathe([[0, -len / 2], [rad, -len / 2 + rad * 0.75], [rad, len / 2 - rad * 0.75], [0, len / 2]], { segments: 5 });
  /** A sprinkle lying at p on a surface with normal n, pointing along tangent t. */
  const sprinkle = (p, n, t, colour, R0) => {
    n = vec.norm(n);
    t = vec.norm(vec.sub(t, n.map((x) => x * vec.dot(t, n))));
    const b = vec.cross(n, t);
    const len = lerp(0.02, 0.028, R0()), rad = 0.0042;
    const m = capsule(len, rad).map(([x, y, z]) => [0, 1, 2].map((d) => p[d] + n[d] * (x + rad * 0.7) + t[d] * y + b[d] * z));
    for (let i = 0; i < m.uvs.length; i += 2) {
      m.uvs[i] = (colour + 0.5) / 8;
      m.uvs[i + 1] = 0.5;
    }
    return m;
  };
  const sprinkles = new Mesh();
  {
    const R0 = rng(5);
    const randT = () => {
      const a = R0() * TAU;
      return [Math.cos(a), (R0() - 0.5) * 0.3, Math.sin(a)];
    };
    // on top (inside the rosette ring, clear of the candles: checked below)
    const tops = [];
    for (let k = 0; k < 62; k++) {
      const rr = 0.27 * Math.sqrt(R0()), a = R0() * TAU;
      tops.push([rr * Math.cos(a), topY(rr), rr * Math.sin(a)]);
    }
    // down the glaze over the rim (the front ones sit well above the face)
    const edges = [];
    for (let k = 0; k < 96; k++) {
      const th = lerp(0.05, 1.25, R0()), a = R0() * TAU;
      const rr = R - RE + RE * Math.cos(th) + GT;
      edges.push({ p: [rr * Math.cos(a), YT - RE + RE * Math.sin(th), rr * Math.sin(a)], n: [Math.cos(th) * Math.cos(a), Math.sin(th), Math.cos(th) * Math.sin(a)] });
    }
    // spilled on the plate
    const plate = [];
    for (let k = 0; k < 22; k++) {
      const rr = lerp(0.428, 0.455, R0()), a = R0() * TAU;
      plate.push([rr * Math.cos(a), 0, rr * Math.sin(a)]);
    }
    sprinkles.tops = tops;
    sprinkles.edges = edges;
    sprinkles.plate = plate;
    sprinkles.R0 = R0;
    sprinkles.randT = randT;
  }

  // ---------- the plate ----------
  const plateProfile = [
    [0, 0.004], [RP - 0.07, 0.0, true], [RP - 0.035, 0.006], [RP - 0.012, 0.02], [RP - 0.002, 0.032], [RP, 0.037],
    [RP - 0.005, 0.042], [RP - 0.014, 0.041], [RP - 0.026, 0.034], [RP - 0.045, 0.025], [RP - 0.075, 0.019], [RP - 0.11, FLOOR, true], [0, FLOOR],
  ];
  const plateY = (rr) => {
    // the plate's top surface height at radius rr (inner part of the profile, from the dish out to the rim)
    const inner = plateProfile.slice(6).map(([a, b]) => [a, b]).reverse(); // centre .. rim top
    for (let k = 1; k < inner.length; k++) if (inner[k][0] >= rr) {
      const [ra, ya] = inner[k - 1], [rb, yb] = inner[k];
      return lerp(ya, yb, (rr - ra) / (rb - ra || 1));
    }
    return 0.042;
  };
  const plateMesh = lathe(plateProfile, { segments: 72 });
  parts.push({ mesh: plateMesh, material: { name: 'plate', color: [0.42, 0.77, 0.66], roughness: 0.55 } });

  // ---------- candles ----------
  const CR = 0.017, CH = 0.138;
  const CANDLE = [
    // [screen x (+ = screen right), depth z (+ = back), stripe colour, tilt]
    [-0.2, 0.035, [80, 160, 245], [3, 0, 4]],
    [-0.1, -0.13, [255, 112, 170], [-2, 0, 2]],
    [0, 0.12, [255, 206, 40], [2, 0, -1]],
    [0.1, -0.13, [96, 206, 120], [-3, 0, -2]],
    [0.2, 0.035, [176, 110, 232], [2, 0, -4]],
  ];
  const candleTex = new Canvas(320, 128, [252, 249, 242]);
  candleTex.shade((u, v) => {
    const k = Math.min(4, Math.floor(u * 5));
    const ul = (u * 5 - k - 0.06) / 0.88;
    const phase = ul + v * 1.7;
    const f = (((phase * 2) % 1) + 1) % 1;
    const col = CANDLE[k][2];
    if (f < 0.42) return mul(col, 0.97 + 0.06 * Math.sin(v * 40));
    return null;
  });
  const candleMat = { name: 'candles', texture: candleTex, roughness: 0.7 };
  const wickMat = { name: 'wick', color: [0.005, 0.004, 0.003], roughness: 0.95 };
  const wicks = {};
  const candleSpots = [];
  CANDLE.forEach(([sx, z, , tilt], k) => {
    const x = -sx; // screen right is -X
    const base = [x, topY(Math.hypot(x, z)) - 0.014, z];
    candleSpots.push([x, z]);
    const c = lathe([[0, 0], [CR, 0, true], [CR, 0.04], [CR, 0.08], [CR, CH - 0.007], [CR * 0.86, CH - 0.001], [CR * 0.5, CH], [0.004, CH - 0.002], [0, CH - 0.002]], { segments: 16, v: (t, y) => clamp(1 - y / CH) });
    for (let i = 0; i < c.uvs.length; i += 2) c.uvs[i] = (k + 0.06 + 0.88 * c.uvs[i]) / 5;
    const lean = (m) => m.transform({ rotate: tilt, translate: base });
    parts.push({ mesh: lean(c), material: candleMat });
    const bend = k % 2 ? 1 : -1;
    const wickPath = [[0, CH - 0.006, 0], [0, CH + 0.008, 0], [0.0015 * bend, CH + 0.017, 0], [0.0045 * bend, CH + 0.024, 0.001]];
    parts.push({ mesh: lean(tube(wickPath, 0.0032, { segments: 6 })), material: wickMat });
    parts.push({ mesh: lean(sphere(0.0042, { segments: 8, rows: 5 }).transform({ translate: [0.005 * bend, CH + 0.0255, 0.001] })), material: wickMat });
    const tip = new Mesh();
    tip.vertex([0.0055 * bend, CH + 0.029, 0.001]);
    lean(tip);
    wicks[`wick${k + 1}`] = tip.point(0);
  });

  // ---------- place the sprinkles ----------
  {
    const { tops, edges, plate, R0, randT } = sprinkles;
    const all = new Mesh();
    for (const p of tops) {
      if (candleSpots.some(([x, z]) => Math.hypot(p[0] - x, p[2] - z) < CR + 0.025)) continue;
      all.append(sprinkle(p, [0, 1, 0], randT(), Math.floor(R0() * 8), R0));
    }
    for (const { p, n } of edges) {
      const phi = azimuth(Math.atan2(p[2], p[0]));
      if (glazeAt(phi, YT - p[1]).cover < 0.5) continue;
      all.append(sprinkle(p, n, randT(), Math.floor(R0() * 8), R0));
    }
    for (const p of plate) {
      const rr = Math.hypot(p[0], p[2]);
      all.append(sprinkle([p[0], plateY(rr), p[2]], [0, 1, 0], randT(), Math.floor(R0() * 8), R0));
    }
    parts.push({ mesh: all, material: sprMat });
  }

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const s = 1.15 / (max[1] - min[1]);
  return {
    parts,
    face: (FACE_Y - min[1]) / (max[1] - min[1]),
    faceSize: 0.72,
    anchors: { ...wicks, top: [0, topY(0), 0] },
    notes:
      `Cake radius ${(R * s).toFixed(3)} m (plate ${(RP * s).toFixed(3)} m); top surface ${(topY(0) * s).toFixed(3)} m up. ` +
      `Five candles (material "candles", wicks are a separate black "wick" material; hide both together), ` +
      `${(CH * s).toFixed(3)} m tall, ${(CR * 2 * s).toFixed(3)} m thick. Frosting drips run from the white top glaze ` +
      `(short over the face). Finger swipe on the screen-left side of the bottom layer.`,
  };
}
