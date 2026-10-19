/**
 * Pillar candle: a chunky ivory pillar whose top has melted into a shallow cupped pool with an uneven, slumped
 * rim, frozen drips running down the sides (one all the way into a puddle on the saucer), a short black wick, all
 * standing on a brass chamberstick saucer with a ring handle. In the app the flame goes on the wick tip.
 */
export default function build(kit) {
  const { Mesh, Canvas, loft, lathe, tube, sphere, ringAngle, fbm3, noise3, smooth, clamp, lerp, TAU, rng } = kit;

  // ---------- sizes ----------
  const RS = 0.35; // saucer radius
  const FLOOR = 0.016; // the saucer's dish floor, where the candle stands
  const R = 0.25; // candle radius
  const HT = 0.81; // nominal rim height
  const WALL = 0.042; // how far in the rim's inner edge is
  const POOL_Y = HT - 0.036; // the melt pool's surface
  const FACE_Y = 0.46; // the face's height (model units)
  const wrapPi = (x) => ((((x + Math.PI) % TAU) + TAU) % TAU) - Math.PI;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  const azimuth = (a) => wrapPi((3 * Math.PI) / 2 - a); // from the front, + toward screen right (-X)
  const faceMask = (phi, y) => (1 - smooth(0.45, 0.8, Math.abs(phi))) * (1 - smooth(0.16, 0.26, Math.abs(y - FACE_Y)));

  // The rim slumps lowest at the screen right, where the biggest drip ran over; lumpy all round.
  const LOW = 1.15;
  const rimTop = (phi) =>
    HT +
    (noise3(Math.cos(phi) * 2.5, Math.sin(phi) * 2.5, 0.5, 3) - 0.5) * 0.03 -
    0.034 * Math.exp(-((wrapPi(phi - LOW) / 0.5) ** 2)) -
    0.016 * Math.exp(-((wrapPi(phi + 2.2) / 0.35) ** 2));

  // Drips: azimuth, length down the side (from the rim), half-width, thickness.
  const drips = [
    { phi: LOW, len: 0.36, w: 0.046, t: 0.02, seed: 1 },
    { phi: LOW + 0.36, len: 0.16, w: 0.03, t: 0.014, seed: 2 },
    { phi: -0.98, len: 0.22, w: 0.034, t: 0.016, seed: 3 },
    { phi: -2.2, len: 0.3, w: 0.04, t: 0.017, seed: 4 },
    { phi: 2.55, len: 0.8, w: 0.036, t: 0.017, seed: 5 }, // all the way down into the saucer
    { phi: -2.85, len: 0.13, w: 0.03, t: 0.014, seed: 6 },
    { phi: 0.62, len: 0.075, w: 0.028, t: 0.012, seed: 7 },
    // short ones round the front, well above the face
    { phi: -0.22, len: 0.05, w: 0.026, t: 0.011, seed: 8 },
    { phi: 0.2, len: 0.1, w: 0.024, t: 0.011, seed: 9 },
    { phi: -0.58, len: 0.11, w: 0.026, t: 0.012, seed: 10 },
    { phi: -1.55, len: 0.07, w: 0.026, t: 0.011, seed: 11 },
    { phi: 1.95, len: 0.1, w: 0.028, t: 0.012, seed: 12 },
    { phi: -2.55, len: 0.06, w: 0.024, t: 0.01, seed: 13 },
  ];
  /** How far a drip stands off the wall at (phi, metres below the rim); also how much is drip (0..1). */
  const dripAt = (phi, below, grow = 1) => {
    let best = 0, cover = 0;
    for (const d of drips) {
      if (below < -0.03 || below > d.len + d.w * 2) continue;
      const wig = (noise3(below * 7, d.seed * 2.3, 0.5, 11) - 0.5) * 0.05 + (noise3(below * 22, d.seed, 1.5, 12) - 0.5) * 0.012;
      const across = wrapPi(phi - d.phi) * R - wig;
      const f = clamp(below / d.len);
      const w = grow * d.w * (1 - 0.35 * f) * (0.85 + 0.3 * noise3(below * 12, d.seed, 2.5, 13)) * (1 + 0.6 * (1 - smooth(-0.02, 0.05, below)));
      let k = 1 - (across / w) ** 2;
      let amp = d.t * (0.75 + 0.25 * f);
      if (below > d.len) k = -1;
      // the frozen bead at the end
      const bw = w * 1.35;
      const kb = 1 - (across / bw) ** 2 - ((below - d.len) / (bw * 1.2)) ** 2;
      if (kb > 0 && Math.sqrt(kb) * d.t * 1.35 > Math.sqrt(Math.max(0, k)) * amp) {
        k = kb;
        amp = d.t * 1.35;
      }
      if (k > 0) {
        const off = Math.sqrt(k) * amp;
        if (off > best) best = off;
        cover = Math.max(cover, smooth(0, 0.25, k));
      }
    }
    return { off: best, cover };
  };

  // ---------- the candle mesh: side (dense up top where the drips are), rounded lip, inner slope ----------
  const SEG = 144;
  const rows = [];
  const SIDE_LOW = 12, SIDE_HIGH = 54;
  for (let k = 0; k <= SIDE_LOW + SIDE_HIGH; k++) {
    const q = k <= SIDE_LOW ? 0.45 * (k / SIDE_LOW) : 0.45 + 0.55 * ((k - SIDE_LOW) / SIDE_HIGH);
    rows.push({ zone: 'side', q });
  }
  const LIPN = 7;
  for (let k = 1; k <= LIPN; k++) rows.push({ zone: 'lip', q: k / LIPN });
  const INN = 6;
  for (let k = 1; k <= INN; k++) rows.push({ zone: 'inner', q: k / INN });
  const LIP_R = 0.014;
  /** The undisplaced point of a row at azimuth phi: [radius, y, metres below the rim]. */
  const rowPoint = (row, phi) => {
    const top = rimTop(phi);
    if (row.zone === 'side') {
      const yEnd = top - LIP_R;
      const y = lerp(FLOOR - 0.004, yEnd, row.q);
      return [R, y, top - y];
    }
    if (row.zone === 'lip') {
      const ang = (row.q * Math.PI) / 2; // a quarter round over the outer edge, then the flat top
      return [R - LIP_R + LIP_R * Math.cos(ang), top - LIP_R + LIP_R * Math.sin(ang), -0.002 * row.q];
    }
    // inner: from the rim's top down a soft slope into the pool (and a little below it, hidden)
    const t = row.q;
    const r0 = R - LIP_R, r1 = R - WALL;
    return [lerp(r0, r1, smooth(0, 1, t)), lerp(top, POOL_Y - 0.008, t ** 1.4), 0];
  };
  const rings = rows.map((row) => {
    const ring = [];
    for (let i = 0; i <= SEG; i++) {
      const a = ringAngle(i % SEG, SEG);
      const phi = azimuth(a);
      let [r, y, below] = rowPoint(row, phi);
      if (row.zone !== 'inner') {
        const { off } = dripAt(phi, below);
        r += off;
        // a soft collar of overflowed wax just under the rim
        r += 0.006 * (1 - smooth(0.0, 0.045, below)) * (0.5 + noise3(Math.cos(a) * 5, Math.sin(a) * 5, 7.5, 14));
        // the wall itself: hardly perfect (a little wavy from the mould, softer up top)
        r += (fbm3(Math.cos(a) * 3, y * 6, Math.sin(a) * 3, { octaves: 2, seed: 21 }) - 0.5) * 0.006 * (1 - 0.7 * faceMask(phi, y));
      }
      ring.push([r * Math.cos(a), y, r * Math.sin(a)]);
    }
    return ring;
  });
  // texture v: side 1 (bottom) .. 0.22 (lip), lip .. 0.08, inner .. 0
  rows.forEach((row, j) => {
    rings[j].v = row.zone === 'side' ? lerp(1, 0.22, row.q) : row.zone === 'lip' ? lerp(0.22, 0.08, row.q) : lerp(0.08, 0, row.q);
  });
  const candle = loft(rings, { capBottom: true });

  // ---------- wax paint ----------
  const IVORY = [241, 223, 188], WARM = [255, 213, 158], DEEP = [208, 182, 142];
  const wax = new Canvas(1024, 1024, IVORY);
  wax.shade((u, v) => {
    const phi = (u - 0.5) * TAU;
    const top = rimTop(phi);
    let y, below, zone;
    if (v > 0.22) {
      const q = (1 - v) / 0.78;
      y = lerp(FLOOR, top - LIP_R, q);
      below = top - y;
      zone = 'side';
    } else {
      y = top;
      below = 0;
      zone = v > 0.08 ? 'lip' : 'inner';
    }
    const p = [Math.sin(phi) * R, y, Math.cos(phi) * R];
    // Warm glow toward the top (light through the thin wall) and toward the edges of the drips.
    let col = mix(DEEP, IVORY, smooth(0.0, 0.4, y));
    col = mix(col, WARM, smooth(0.45, 0.82, y) * 0.75);
    const mottle = fbm3(p[0] * 7, p[1] * 7, p[2] * 7, { octaves: 4, seed: 31 });
    col = mul(col, 0.96 + 0.08 * (mottle - 0.5));
    // faint pour lines round the pillar
    col = mul(col, 1 - 0.012 * smooth(0.75, 1, Math.sin(y * 90 + mottle * 9)));
    if (zone === 'side') {
      const { cover } = dripAt(phi, below);
      if (cover > 0) col = mix(col, mix([255, 238, 200], WARM, 0.45 + 0.3 * smooth(0.2, 0.0, below)), cover * 0.9);
      // a soft shadow just under and beside each drip, so they stand off the wall
      const halo = dripAt(phi, below - 0.007, 1.35).cover;
      col = mul(col, 1 - 0.1 * Math.max(0, halo - cover));
      // runs: very faint vertical streaks under the drips and the slumped rim
      const run = fbm3(p[0] * 60, p[1] * 3, p[2] * 60, { octaves: 2, seed: 33 });
      col = mul(col, 0.985 + 0.03 * run);
    } else {
      col = mix(col, [255, 238, 205], zone === 'lip' ? 0.5 : 0.3);
      if (zone === 'inner') col = mix(col, [232, 206, 160], (0.08 - v) / 0.08 * 0.6);
    }
    return col;
  });
  wax.speckle(900, [0.5, 1.3], [200, 180, 150], 0.12, 4);
  const waxMat = { name: 'wax', texture: wax, roughness: 0.72, emissive: [0.05, 0.035, 0.012] };
  const parts = [{ mesh: candle, material: waxMat }];

  // ---------- the pool: smooth, a touch glossier and more amber, sooty round the wick ----------
  const PR = R - WALL + 0.006;
  const pool = lathe([[PR, POOL_Y - 0.002], [PR * 0.75, POOL_Y - 0.003], [PR * 0.4, POOL_Y - 0.006], [PR * 0.12, POOL_Y - 0.009], [0, POOL_Y - 0.01]], { segments: 64 });
  const poolTex = new Canvas(256, 128, [236, 212, 168]);
  poolTex.shade((u, v) => {
    // v: 0 centre .. 1 edge (lathe rows run edge -> centre, v = 1 - j/(n-1))
    const rr = v;
    let col = mix([214, 180, 128], [240, 218, 176], smooth(0.05, 0.6, rr));
    col = mix(col, [110, 96, 80], (1 - smooth(0.03, 0.16, rr)) * 0.7);
    col = mix(col, [250, 232, 196], smooth(0.85, 1, rr) * 0.6);
    return col;
  });
  {
    const R0 = rng(8);
    for (let k = 0; k < 40; k++) poolTex.disc(R0() * 256, R0() * 40, lerp(0.6, 1.8, R0()), [60, 50, 44], 0.5, 0.5);
  }
  parts.push({ mesh: pool, material: { name: 'wax_pool', texture: poolTex, roughness: 0.5, emissive: [0.04, 0.026, 0.008] } });

  // ---------- the wick ----------
  const wickBase = [0, POOL_Y - 0.02, 0];
  // Just tall enough to peek over the rim from the front (it all counts toward the height).
  const wickPath = [wickBase, [0, POOL_Y, 0], [0.002, POOL_Y + 0.024, -0.001], [0.006, POOL_Y + 0.044, -0.002], [0.013, POOL_Y + 0.057, -0.003]];
  const wick = tube(wickPath, 0.0068, { segments: 10, radii: [0.0068, 0.0068, 0.0063, 0.0064, 0.0072] });
  const wickTip = [0.015, POOL_Y + 0.062, -0.003];
  const wickMat = { name: 'wick', color: [0.005, 0.004, 0.003], roughness: 0.95 };
  parts.push({ mesh: wick, material: wickMat });
  // the charred blob on its tip
  parts.push({ mesh: sphere(0.009, { segments: 10, rows: 6 }).transform({ translate: [0.0135, POOL_Y + 0.0575, -0.003] }), material: wickMat });

  // ---------- the drip's puddle on the saucer ----------
  const pd = drips[4];
  const pa = (3 * Math.PI) / 2 - pd.phi;
  const puddle = sphere(1, { segments: 24, rows: 10 }).transform({ scale: [0.05, 0.012, 0.042], translate: [(R + 0.022) * Math.cos(pa), FLOOR + 0.002, (R + 0.022) * Math.sin(pa)] });
  puddle.map(([x, y, z]) => [x, Math.max(FLOOR - 0.003, y), z]);
  parts.push({ mesh: puddle, material: waxMat });

  // ---------- the brass chamberstick saucer ----------
  const saucerProfile = [
    [0, 0.003],
    [0.27, 0.0, true],
    [0.315, 0.008],
    [0.338, 0.022],
    [RS - 0.004, 0.036],
    [RS, 0.044],
    [RS - 0.004, 0.051],
    [RS - 0.014, 0.052],
    [RS - 0.022, 0.046],
    [RS - 0.04, 0.032],
    [RS - 0.07, 0.021],
    [RS - 0.1, FLOOR, true],
    [0, FLOOR],
  ];
  const sv = (t) => t; // v = 0 bottom centre .. 1 the dish's centre
  const saucer = lathe(saucerProfile, { segments: 96, v: sv });
  const brass = new Canvas(512, 256, [208, 158, 78]);
  brass.shade((u, v) => {
    const a = u * TAU;
    const n = fbm3(Math.cos(a) * 3, v * 5, Math.sin(a) * 3, { octaves: 4, seed: 51 });
    let col = mix([170, 120, 52], [226, 184, 104], n);
    // polished rim, tarnish in the dish and round the candle's foot
    col = mix(col, [238, 204, 130], smooth(0.35, 0.5, v) * (1 - smooth(0.55, 0.7, v)) * 0.6);
    col = mix(col, [120, 90, 44], smooth(0.75, 1, v) * 0.45 * smooth(0.45, 0.65, n));
    const hammer = Math.sin(u * 900 + Math.sin(v * 60) * 3) * Math.sin(v * 300);
    return mul(col, 0.97 + 0.04 * hammer);
  });
  const brassMat = { name: 'brass', texture: brass, metallic: 0.8, roughness: 0.4 };
  parts.push({ mesh: saucer, material: brassMat });

  // The ring handle: a finger loop on the rim, back on the screen-left side (where the three-quarter view sees it).
  const HPHI = -2.25;
  const ha = (3 * Math.PI) / 2 - HPHI;
  const out = [Math.cos(ha), 0, Math.sin(ha)];
  const HR = 0.046, HT0 = 0.034;
  const centre = [out[0] * (RS + HR - 0.006), HT0 + HR * 0.55, out[2] * (RS + HR - 0.006)];
  // (made lying flat, so the tube's frame never flips, then stood up and turned to face outward)
  const loop = [];
  for (let k = 0; k <= 40; k++) {
    const t = (TAU * k) / 40;
    loop.push([HR * Math.cos(t), 0, HR * 0.9 * Math.sin(t)]);
  }
  const ring = tube(loop, 0.0095, { segments: 10 }).transform({ rotate: [-90, (Math.atan2(-out[2], out[0]) * 180) / Math.PI, 0], translate: centre });
  parts.push({ mesh: ring, material: brassMat });
  // a little thumb rest on top of the ring
  parts.push({
    mesh: sphere(1, { segments: 16, rows: 8 }).transform({ scale: [0.022, 0.006, 0.022], translate: [centre[0] + out[0] * 0.006, centre[1] + HR * 0.9 + 0.006, centre[2] + out[2] * 0.006] }),
    material: brassMat,
  });

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const s = 1.15 / (max[1] - min[1]);
  return {
    parts,
    face: (FACE_Y - min[1]) / (max[1] - min[1]),
    faceSize: 0.8,
    anchors: {
      wickTip,
      pool: [0, POOL_Y - 0.01, 0],
      saucerTop: [0, 0.052, -(RS - 0.008)],
    },
    notes:
      `Candle rim (outer) radius ${R.toFixed(3)} model units = ${(R * s).toFixed(3)} m in app space; the pool's ` +
      `radius ${((R - WALL) * s).toFixed(3)} m, its surface ${((HT - POOL_Y) * s).toFixed(3)} m below the nominal rim. ` +
      `The rim slumps lowest at the screen right. Saucer radius ${(RS * s).toFixed(3)} m; ring handle at the back left (+X).`,
  };
}
