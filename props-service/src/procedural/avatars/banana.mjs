/**
 * Banana: a ripe banana standing on its dark flower end, slim and curved in a clear crescent (seen from the front
 * it leans out to screen left from the nub, bulges, and comes back toward screen right at the top), with the
 * classic soft-pentagon section and ridges, bright yellow peel with a few brown sugar spots. The top third is
 * already peeled: the bare creamy fruit (soft rounded tip, faint lengthwise strings) stands out of the peel on a
 * straight, nearly vertical axis that carries the curve on, and the peel ends in a level, slightly torn edge with
 * a cream inner rim. The app adds four code-built peel flaps hinged on that edge (anchor "hinge") that cover the
 * fruit when closed and flop open like a flower when the mouth opens; the fruit above the hinge is a plain body of
 * revolution about the hinge -> fruitTip axis whose radius only shrinks toward the tip, so the flaps can hug it.
 */
export default function build(kit) {
  const { Mesh, Canvas, loft, ringAngle, fbm3, noise3, smooth, clamp, lerp, TAU, rng, deg } = kit;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  const wrapPi = (x) => ((((x + Math.PI) % TAU) + TAU) % TAU) - Math.PI;
  const azimuth = (a) => wrapPi((3 * Math.PI) / 2 - a); // ring angle -> 0 at the front, + toward screen right (-X)

  // ---------- sizes ----------
  const YH = 0.64; // height of the peel's torn top edge (the flaps' hinge)
  const YTIP = 1.0; // height of the bare fruit's tip
  const T = 0.02; // peel thickness at the edge
  const LEAN0 = deg(46); // the flower end's lean from vertical (+: going up it heads toward screen left, +X)
  const LEANH = deg(-8); // ...and at the hinge, kept by the fruit's straight axis (toward screen right)
  const CURVE = 1.1; // how the bend is spread up the peel (>1: more of it low down, none left at the hinge)
  const BOWZ = 0.025; // the middle bulges a touch toward the camera too
  const FACE_Y = 0.31; // the face's height: mid-peel, where the curved body sits right under the model's middle
  const PEEL = [250, 212, 42]; // the peel's yellow (sRGB, as painted)

  // ---------- the centreline: a crescent swept up from the nub to the hinge, then straight on for the fruit ----------
  // h is the peel's own "height" parameter (0 at the nub, YH at the hinge), proportional to arc length along the curve.
  const leanAt = (h) => (h >= YH ? LEANH : h <= 0 ? LEAN0 : LEANH + (LEAN0 - LEANH) * (1 - h / YH) ** CURVE);
  const NC = 2048;
  const TAB = [[0, 0]];
  let K = 1; // arc length per unit of h
  {
    let x = 0, y = 0;
    const dh = YH / NC;
    for (let i = 0; i < NC; i++) {
      const th = leanAt((i + 0.5) * dh);
      x += Math.sin(th) * dh;
      y += Math.cos(th) * dh;
      TAB.push([x, y]);
    }
    K = YH / y;
    for (const p of TAB) (p[0] *= K), (p[1] *= K);
  }
  const czAt = (h) => -BOWZ * Math.sin(Math.PI * clamp(h / YH)) ** 2;
  const centre = (h) => {
    if (h >= YH) {
      const d = (h - YH) * K;
      return [TAB[NC][0] + Math.sin(LEANH) * d, TAB[NC][1] + Math.cos(LEANH) * d, 0];
    }
    if (h <= 0) return [Math.sin(LEAN0) * h * K, Math.cos(LEAN0) * h * K, 0];
    const f = (h / YH) * NC, i = Math.min(NC - 1, Math.floor(f)), t = f - i;
    return [lerp(TAB[i][0], TAB[i + 1][0], t), lerp(TAB[i][1], TAB[i + 1][1], t), czAt(h)];
  };
  /** The h where the centreline reaches world height y (the centreline climbs steadily). */
  const hAtY = (y) => {
    if (y >= YH) return YH + (y - YH) / (K * Math.cos(LEANH));
    let lo = 0, hi = YH;
    for (let k = 0; k < 40; k++) {
      const m = (lo + hi) / 2;
      if (centre(m)[1] < y) lo = m;
      else hi = m;
    }
    return (lo + hi) / 2;
  };

  // The peel's "radius" along the banana (the soft pentagon's flat-face distance): a small neat nub, a slim body.
  const radiusKeys = [
    [0.0, 0.000],
    [0.0015, 0.017],
    [0.004, 0.024],
    [0.008, 0.028],
    [0.014, 0.029],
    [0.021, 0.028],
    [0.029, 0.031],
    [0.039, 0.041],
    [0.052, 0.056],
    [0.068, 0.073],
    [0.088, 0.090],
    [0.112, 0.106],
    [0.14, 0.118],
    [0.175, 0.127],
    [0.22, 0.134],
    [0.27, 0.138],
    [0.33, 0.140],
    [0.39, 0.140],
    [0.45, 0.138],
    [0.51, 0.135],
    [0.57, 0.131],
    [YH, 0.127],
  ];
  const radiusAt = (h) => catmull(radiusKeys, h);
  /** How square the section is (sharpness of the pentagon's corners) and how proud the ridges stand. */
  const sharpAt = (h) => lerp(3, 13, smooth(0.05, 0.2, h)) - 9 * smooth(0.46, YH - 0.02, h);
  const ridgeAt = (h) => smooth(0.06, 0.18, h) * (1 - 0.8 * smooth(0.46, YH - 0.02, h));
  /** The soft pentagon (flat face to the front): radius factor at ring angle a. */
  const NORMALS = [0, 1, 2, 3, 4].map((k) => -Math.PI / 2 + (k * TAU) / 5);
  const cornerDist = (a) => Math.abs(wrapPi(a + Math.PI / 2 - Math.PI / 5 - Math.round((a + Math.PI / 2 - Math.PI / 5) / (TAU / 5)) * (TAU / 5)));
  const section = (a, h) => {
    const s = sharpAt(h);
    let sum = 0;
    for (const n of NORMALS) sum += Math.exp(s * (Math.cos(a - n) - 1));
    const soft = 1 + Math.log(sum) / s; // softmax of the five face distances
    // a crisp little ridge along each corner
    const ridge = 0.02 * ridgeAt(h) * Math.exp(-((cornerDist(a) / 0.06) ** 2));
    return 1 / soft + ridge;
  };
  // the torn top edge: irregular little teeth, a gentle wave, and four nicks where the flaps split (the diagonals)
  const SPLITS = [-Math.PI / 4, Math.PI / 4, (3 * Math.PI) / 4, (-3 * Math.PI) / 4];
  const edgeAt = (a) => {
    const ph = (a * 13) / Math.PI + (noise3(Math.cos(a) * 2, Math.sin(a) * 2, 0.5, 3) - 0.5) * 2.4;
    const tooth = Math.abs(ph - Math.floor(ph) - 0.5) * 2; // 0..1 saw
    let e = YH + (noise3(Math.cos(a) * 3, Math.sin(a) * 3, 1.5, 4) - 0.5) * 0.01 + (tooth - 0.5) * 0.006 * (0.5 + noise3(a * 3, 2.5, 0.5, 5));
    for (const s of SPLITS) e -= 0.01 * Math.exp(-((wrapPi(a - s) / 0.06) ** 2));
    return e;
  };

  /**
   * A point on the peel at ring angle a and parameter h: on the ring square to the centreline, or (level = 1)
   * on the level ring through the same centre point, so the top of the peel ends in a level edge.
   */
  const peelPoint = (a, h, inset = 0, level = 0, extra = 0) => {
    const r = radiusAt(h) * section(a, h) - inset + extra;
    const hh = h + (level * r * Math.cos(a) * Math.tan(leanAt(h))) / K;
    const c = centre(hh), th = leanAt(hh);
    return [c[0] + r * Math.cos(a) * Math.cos(th), c[1] - r * Math.cos(a) * Math.sin(th), c[2] + r * Math.sin(a)];
  };
  /** The level peel point at ring angle a whose height is y. */
  const peelAtY = (a, y, inset = 0) => {
    let h = hAtY(y);
    for (let k = 0; k < 5; k++) h += (y - peelPoint(a, h, inset, 1)[1]) / (K * Math.cos(leanAt(h)));
    return peelPoint(a, h, inset, 1);
  };
  const H_FACE = hAtY(FACE_Y);
  /** Calm (no lumps, no spots) on the front around the face. */
  const calmAt = (a, h) => 1 - 0.8 * (1 - smooth(0.5, 0.9, Math.abs(azimuth(a)))) * (1 - smooth(0.13, 0.2, Math.abs(h - H_FACE)));

  // ---------- the peel: the outer skin from the nub up to the torn edge, over the rim, a little way down inside ----------
  const SEG = 128;
  const outerHs = [0];
  for (const h of [0.0008, 0.0018, 0.003, 0.0045, 0.0065, 0.009, 0.012, 0.016, 0.021, 0.027, 0.034, 0.042, 0.051, 0.061, 0.072]) outerHs.push(h);
  for (let h = 0.084; h < YH - 0.03; h += 0.0095) outerHs.push(h);
  const levelAt = (h) => smooth(YH - 0.17, YH - 0.035, h);
  // arc length up the outer profile, for the texture's v
  const arcs = [0];
  for (let k = 1; k < outerHs.length; k++) arcs.push(arcs[k - 1] + Math.hypot((outerHs[k] - outerHs[k - 1]) * K, radiusAt(outerHs[k]) - radiusAt(outerHs[k - 1])));
  const ARC = arcs[arcs.length - 1] + 0.03;
  const V_RIM = 0.05; // texture rows above this are the cream inner peel
  const vOuter = (arc) => V_RIM + (1 - V_RIM) * (1 - arc / ARC);
  const hLastSide = outerHs[outerHs.length - 1];
  const lumpAt = (a, h) => (fbm3(Math.cos(a) * 2, h * 5, Math.sin(a) * 2, { octaves: 2, seed: 7 }) - 0.5) * 0.007 * calmAt(a, h) * smooth(0.02, 0.07, h);
  const rings = [];
  outerHs.forEach((h, j) => {
    const ring = [];
    for (let i = 0; i <= SEG; i++) {
      const a = ringAngle(i % SEG, SEG);
      // the peel's fine lengthwise lumpiness (calm round the face)
      ring.push(j === 0 ? centre(0) : peelPoint(a, h, 0, levelAt(h), lumpAt(a, h)));
    }
    ring.v = vOuter(arcs[j]);
    rings.push(ring);
  });
  // the last stretch below the edge follows the torn edge's height, then the rim rolls over into the inside
  const topRows = [
    { f: 0.5, inset: 0, dy: -0.008 },
    { f: 1, inset: 0, dy: -0.0025 },
    { f: 1, inset: T * 0.22, dy: 0.0005 },
    { f: 1, inset: T * 0.62, dy: 0.0 },
    { f: 1, inset: T, dy: -0.004 },
    { f: 1, inset: T * 1.04, dy: -0.06 },
  ];
  const yLastSide = (a) => peelPoint(a, hLastSide, 0, levelAt(hLastSide), lumpAt(a, hLastSide))[1];
  topRows.forEach((t, k) => {
    const ring = [];
    for (let i = 0; i <= SEG; i++) {
      const a = ringAngle(i % SEG, SEG);
      const y = lerp(yLastSide(a), edgeAt(a), t.f) + t.dy;
      ring.push(peelAtY(a, y, t.inset));
    }
    ring.v = k === 0 ? V_RIM + 0.012 : k === 1 ? V_RIM + 0.002 : lerp(V_RIM - 0.006, 0.0, (k - 2) / 3);
    rings.push(ring);
  });
  const peel = loft(rings);

  // Peel paint.
  const peelTex = new Canvas(1024, 1024, PEEL);
  const spots = [];
  {
    const R = rng(17);
    for (let k = 0; k < 90 && spots.length < 22; k++) {
      const phi = (R() * 2 - 1) * Math.PI, h = lerp(0.1, YH - 0.03, R() ** 0.8);
      // keep the front (the face) clean
      if (Math.abs(phi) < 1.0 && Math.abs(h - H_FACE) < 0.22) continue;
      spots.push({ phi, h, r: lerp(0.004, 0.012, R() ** 2), seed: k });
    }
    // a little cluster low on the screen-right side
    for (let k = 0; k < 6; k++) spots.push({ phi: 1.25 + R() * 0.5, h: 0.11 + R() * 0.07, r: lerp(0.003, 0.006, R()), seed: 100 + k });
  }
  const vToH = (v) => {
    // invert vOuter -> arc -> h
    const arc = (1 - (v - V_RIM) / (1 - V_RIM)) * ARC;
    let k = 1;
    while (k < arcs.length - 1 && arcs[k] < arc) k++;
    const f = clamp((arc - arcs[k - 1]) / (arcs[k] - arcs[k - 1] || 1));
    return k >= arcs.length - 1 && arc > arcs[arcs.length - 1] ? lerp(hLastSide, YH, clamp((arc - arcs[arcs.length - 1]) / 0.03)) : lerp(outerHs[k - 1], outerHs[k], f);
  };
  peelTex.shade((u, v) => {
    const a = Math.PI / 2 + TAU * (1 - u);
    const phi = azimuth(a);
    if (v < V_RIM) {
      // the torn rim and the inside of the peel: creamy white, fibrous
      const n = fbm3(Math.cos(a) * 9, v * 40, Math.sin(a) * 9, { octaves: 3, seed: 9 });
      return mul([250, 244, 212], 0.93 + 0.1 * n);
    }
    const h = vToH(v);
    const r = radiusAt(Math.max(0.01, h));
    const p = [Math.cos(a) * r, h, Math.sin(a) * r];
    let col = [...PEEL];
    // lengthwise streaks and a soft mottle
    const streak = fbm3(Math.cos(a) * 30, h * 2.5, Math.sin(a) * 30, { octaves: 3, seed: 11 });
    const mottle = fbm3(p[0] * 6, p[1] * 6, p[2] * 6, { octaves: 3, seed: 12 });
    col = mul(col, 0.95 + 0.08 * streak + 0.05 * (mottle - 0.5));
    // ridges: a slightly deeper, greener yellow along each corner
    col = mix(col, [214, 178, 36], (1 - smooth(0.02, 0.1, cornerDist(a))) * 0.55 * ridgeAt(h));
    // the flower end: olive, then brown, then a near-black nub
    col = mix(col, [196, 172, 48], (1 - smooth(0.06, 0.15, h)) * 0.75);
    col = mix(col, [112, 78, 34], 1 - smooth(0.032, 0.058, h));
    col = mix(col, [42, 29, 18], 1 - smooth(0.02, 0.031, h));
    // the nub's dry, wrinkly end
    if (h < 0.04) col = mul(col, 0.85 + 0.3 * fbm3(Math.cos(a) * 12, h * 60, Math.sin(a) * 12, { octaves: 2, seed: 14 }));
    // a hint of green-yellow just under the torn edge, where it was hidden
    col = mix(col, [236, 214, 72], smooth(YH - 0.08, YH, h) * 0.25);
    // sugar spots
    for (const s of spots) {
      const d = Math.hypot(wrapPi(phi - s.phi) * r, (h - s.h) * K);
      if (d > s.r * 2.2) continue;
      const wob = 1 + 0.3 * (noise3(phi * 30, h * 30, s.seed, 13) - 0.5);
      col = mix(col, [120, 72, 26], (1 - smooth(s.r * 0.5 * wob, s.r * wob, d)) * 0.85);
      col = mix(col, [70, 42, 18], (1 - smooth(0, s.r * 0.45, d)) * 0.6);
    }
    return col;
  });
  const parts = [{ mesh: peel, material: { name: 'peel', texture: peelTex, roughness: 0.78 } }];

  // ---------- the bare fruit: a body of revolution on a straight axis from inside the peel up to a soft round tip ----------
  const CH = centre(YH); // the axis at the hinge's height
  const AX = [Math.sin(LEANH), Math.cos(LEANH), 0]; // the fruit's axis (up toward the tip)
  const NX = [Math.cos(LEANH), -Math.sin(LEANH), 0]; // square to it, toward screen left
  const FL = (YTIP - YH) / Math.cos(LEANH); // along the axis from the hinge's height to the tip
  let minSec = Infinity, meanPeel = 0, minPeel = Infinity, maxPeel = 0;
  for (let i = 0; i < SEG; i++) {
    const a = ringAngle(i, SEG);
    minSec = Math.min(minSec, section(a, YH));
    const p = peelAtY(a, YH), d = Math.hypot(p[0] - CH[0], p[2] - CH[2]);
    meanPeel += d / SEG;
    minPeel = Math.min(minPeel, d);
    maxPeel = Math.max(maxPeel, d);
  }
  const FR0 = minSec * radiusAt(YH) - T - 0.003; // the fruit's radius at the hinge (just inside the peel)
  const FT0 = -0.07; // it starts this far down inside the peel
  /** The fruit's radius, a fraction of FR0, at axial fraction f (0 at the hinge, 1 at the tip): it only shrinks. */
  const FC = 0.55; // where the rounded tip begins
  const profile = (f) => {
    if (f <= 0) return 1;
    if (f >= 1) return 0;
    const taper = 1 - 0.36 * f ** 1.4;
    const cap = f < FC ? 1 : (1 - ((f - FC) / (1 - FC)) ** 2.2) ** (1 / 2.2);
    return taper * cap;
  };
  const fruitRadius = (t) => FR0 * profile(t / FL);
  const fruitPoint = (a, t, rr = fruitRadius(t)) => {
    // a faint five-sided facet, lined up with the peel's corners, fading out toward the tip
    const r = rr * (1 + 0.012 * Math.cos(5 * (a + Math.PI / 2 + Math.PI / 5)) * (1 - smooth(0.4, 0.85, t / FL)));
    const ox = r * Math.cos(a), oz = r * Math.sin(a);
    return [CH[0] + t * AX[0] + ox * NX[0], CH[1] + t * AX[1] + ox * NX[1], CH[2] + oz];
  };
  const fruitTs = [];
  for (let t = FT0; t < FL - 0.07; t += 0.009) fruitTs.push(t);
  for (const d of [0.07, 0.055, 0.042, 0.031, 0.022, 0.015, 0.0095, 0.0055, 0.0028, 0.001, 0]) fruitTs.push(FL - d);
  const FSEG = 96;
  const frings = fruitTs.map((t, j) => {
    const ring = [];
    for (let i = 0; i <= FSEG; i++) {
      const a = ringAngle(i % FSEG, FSEG);
      // soft, slightly uneven (bruise-free, but not machined)
      const n = (fbm3(Math.cos(a) * 3, t * 8, Math.sin(a) * 3, { octaves: 2, seed: 21 }) - 0.5) * 0.003 * smooth(0, 0.04, t) * (1 - smooth(FL - 0.08, FL - 0.02, t));
      ring.push(j === fruitTs.length - 1 ? fruitPoint(0, FL, 0) : fruitPoint(a, t, fruitRadius(t) + n));
    }
    ring.v = 0.06 + 0.94 * (1 - (t - FT0) / (FL - FT0));
    return ring;
  });
  const fruit = loft(frings, { capBottom: true });
  const R2 = rng(31);
  const STRINGS = [0, 1, 2, 3, 4].map((k) => ({ k, a: -Math.PI / 2 + (k + 0.3 + R2() * 0.4) * (TAU / 5), end: lerp(FL - 0.12, FL - 0.05, R2()) }));
  const fruitTex = new Canvas(1024, 1024, [250, 240, 196]);
  fruitTex.shade((u, v) => {
    if (v < 0.04) return [255, 250, 226]; // above the tip (not used by the mesh)
    const a = Math.PI / 2 + TAU * (1 - u);
    const t = FT0 + (1 - (v - 0.06) / 0.94) * (FL - FT0);
    const streak = fbm3(Math.cos(a) * 26, t * 3, Math.sin(a) * 26, { octaves: 3, seed: 23 });
    let col = mix([252, 240, 190], [240, 222, 160], smooth(0.3, 0.8, streak));
    // faint fibre lines along the length
    col = mul(col, 1 - 0.035 * smooth(0.6, 1, Math.sin(a * 23 + streak * 5)));
    // the strings: a few faint pale threads running up from the peel, fading toward the tip
    for (const st of STRINGS) {
      if (t > st.end) continue;
      const d = Math.abs(wrapPi(a - st.a - 0.06 * Math.sin(t * 40 + st.k))) * fruitRadius(t);
      const fade = 1 - smooth(st.end - 0.09, st.end, t);
      col = mix(col, [255, 252, 236], (1 - smooth(0.0013, 0.0036, d)) * 0.85 * fade);
      col = mul(col, 1 - 0.07 * (1 - smooth(0.0035, 0.006, d)) * smooth(0.003, 0.004, d) * fade);
    }
    // a creamier yellow toward the tip, and the little darker point at the very end
    col = mix(col, [246, 226, 150], smooth(FL - 0.13, FL - 0.01, t) * 0.5);
    col = mix(col, [226, 200, 140], smooth(FL - 0.01, FL - 0.002, t) * 0.5);
    // a shadow just above the peel's (level) rim
    const above = t * AX[1] + fruitRadius(t) * Math.cos(a) * NX[1];
    col = mul(col, 1 - 0.18 * (1 - smooth(0, 0.035, above)));
    return col;
  });
  const fruitMat = { name: 'fruit', texture: fruitTex, roughness: 0.66 };
  parts.push({ mesh: fruit, material: fruitMat });

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const height = max[1] - min[1];
  const sc = 1.15 / height;
  const m = (x) => (x * sc).toFixed(3);
  // the torn edge's mean height, and the hinge: the fruit's axis there
  let ysum = 0, elo = Infinity, ehi = -Infinity;
  for (let i = 0; i < SEG; i++) {
    const e = edgeAt(ringAngle(i, SEG));
    ysum += e / SEG;
    elo = Math.min(elo, e);
    ehi = Math.max(ehi, e);
  }
  const tHinge = (ysum - YH) / AX[1];
  const hinge = [CH[0] + tHinge * AX[0], ysum, CH[2]];
  const fruitTip = [CH[0] + FL * AX[0], CH[1] + FL * AX[1], CH[2]];
  const fruitLen = FL - tHinge;
  const fr = (f) => m(fruitRadius(tHinge + f * fruitLen));
  // the widest the peel gets (square to its centreline), against the whole height
  let widest = 0, widestH = 0;
  for (let h = 0.05; h < YH; h += 0.005) {
    let lo = Infinity, hi = -Infinity;
    for (let i = 0; i < SEG; i++) {
      const a = ringAngle(i, SEG), x = radiusAt(h) * section(a, h) * Math.cos(a);
      lo = Math.min(lo, x);
      hi = Math.max(hi, x);
    }
    if (hi - lo > widest) (widest = hi - lo), (widestH = h);
  }
  // where the body's middle is at the face's height, against the middle of the whole model (where the app puts the face)
  const faceOff = centre(H_FACE)[0] - (min[0] + max[0]) / 2;
  const srgb = PEEL.map((c) => +(c / 255).toFixed(3));
  return {
    parts,
    face: (FACE_Y - min[1]) / height,
    faceSize: 0.74,
    anchors: { hinge, fruitTip },
    notes:
      `FLAP NUMBERS (app space, metres): peel outer radius at the hinge (mean) ${m(meanPeel)} (round within ${m(minPeel)}..${m(maxPeel)}); fruit radius at the hinge ` +
      `${m(fruitRadius(tHinge))}; fruit length hinge -> tip ${m(fruitLen)}; fruit radius at 25% ${fr(0.25)}, 50% ${fr(0.5)}, ` +
      `75% ${fr(0.75)}, 90% ${fr(0.9)} of that length. The fruit is a body of revolution about the hinge -> fruitTip axis, ` +
      `tilted ${Math.abs((LEANH * 180) / Math.PI).toFixed(1)} deg from vertical (its tip toward screen right), radius ` +
      `only shrinking toward the tip. The torn edge is level, wobbles within ${m(elo - ysum)}..+${m(ehi - ysum)} m of the hinge height and dips a little at the four ` +
      `diagonals, where the flaps split; peel thickness ${m(T)} m. Widest ${m(widest)} m (1/${(height / widest).toFixed(2)} ` +
      `of the height) at ${((centre(widestH)[1] - min[1]) / height).toFixed(2)} of the height; the body's middle at the ` +
      `face is ${m(Math.abs(faceOff))} m ${faceOff > 0 ? 'screen left' : 'screen right'} of the model's middle. ` +
      `Peel colour (sRGB 0..1): [${srgb.join(', ')}]; inner peel/rim cream [0.98, 0.957, 0.831]. Materials: peel, fruit.`,
  };
}

/** Catmull-Rom through [[x, y], ...] (x increasing), clamped at the ends. */
function catmull(keys, x) {
  if (x <= keys[0][0]) return keys[0][1];
  if (x >= keys[keys.length - 1][0]) return keys[keys.length - 1][1];
  let k = 0;
  while (keys[k + 1][0] < x) k++;
  const p0 = keys[Math.max(0, k - 1)], p1 = keys[k], p2 = keys[k + 1], p3 = keys[Math.min(keys.length - 1, k + 2)];
  const t = (x - p1[0]) / (p2[0] - p1[0]);
  // tangents scaled to the (uneven) key spacing
  const m1 = ((p2[1] - p0[1]) / (p2[0] - p0[0] || 1)) * (p2[0] - p1[0]);
  const m2 = ((p3[1] - p1[1]) / (p3[0] - p1[0] || 1)) * (p2[0] - p1[0]);
  const t2 = t * t, t3 = t2 * t;
  return (2 * t3 - 3 * t2 + 1) * p1[1] + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p2[1] + (t3 - t2) * m2;
}
