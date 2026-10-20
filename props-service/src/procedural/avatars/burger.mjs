/**
 * Cheeseburger: a big chunky stack standing upright. A tall golden domed top bun with sesame seeds only on its
 * crown (the front stays plain for the face), a wavy double frill of lettuce sticking out all round, two
 * overlapping tomato slices, a square cheese slice melted over the patty's edge in drooping corners and drips, a
 * thick craggy grilled patty with char marks, and a flat bottom bun. Every layer is its own material (bun_top,
 * lettuce, tomato, cheese, patty, bun_bottom), so the app can wobble, slide or drip from each one.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, sphere, fbm3, noise3, smooth, clamp, lerp, TAU, rng } = kit;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  const wrapPi = (x) => ((((x + Math.PI) % TAU) + TAU) % TAU) - Math.PI;
  const parts = [];

  // ---------- heights (model units, the stack is ~1 tall) ----------
  const BB_TOP = 0.212; // bottom bun's cut top
  const P_BOT = 0.196, P_TOP = 0.388; // patty
  const CH_Y = 0.397; // cheese top over the patty's middle
  const TO_BOT = 0.398, TO_TOP = 0.474; // tomato slices
  const LE_Y = 0.484; // lettuce
  const TB_BASE = 0.508, TB_TOP = 1.0; // top bun underside and crown
  const TB_R = 0.446;

  /** Profile-indexed lookup for a lathe (t = j / total, like kit.lathe): [r, y] at fraction t. */
  const profileLerp = (profile, t) => {
    const q = clamp(t) * (profile.length - 1);
    const k = Math.min(profile.length - 2, Math.floor(q)), f = q - k;
    return [lerp(profile[k][0], profile[k + 1][0], f), lerp(profile[k][1], profile[k + 1][1], f)];
  };
  const azimuth = (x, z) => Math.atan2(z, x); // lathe angle: x = r cos a, z = r sin a; the front is a = -PI/2

  // ---------- bottom bun ----------
  const bbProfile = [
    [0, 0.0],
    [0.33, 0.0],
    [0.385, 0.006],
    [0.414, 0.022],
    [0.43, 0.05],
    [0.436, 0.085],
    [0.434, 0.122],
    [0.426, 0.157],
    [0.41, 0.186],
    [0.385, 0.204],
    [0.35, BB_TOP],
    [0.2, BB_TOP + 0.002],
    [0, BB_TOP + 0.003],
  ];
  const bunBottom = lathe(bbProfile, { segments: 96, v: (t) => 1 - t });
  bunBottom.map(([x, y, z]) => {
    const r = Math.hypot(x, z);
    if (r < 1e-6) return [x, y, z];
    const k = 1 + (fbm3(x * 6, y * 6, z * 6, { octaves: 2, seed: 2 }) - 0.5) * 0.02;
    return [x * k, y, z * k];
  });
  const bbTex = new Canvas(512, 256, [222, 150, 72]);
  bbTex.shade((u, v) => {
    const t = 1 - v;
    const [r, y] = profileLerp(bbProfile, t);
    const a = u * TAU;
    const p = [Math.cos(a) * r, y, Math.sin(a) * r];
    const n = fbm3(p[0] * 8, p[1] * 8, p[2] * 8, { octaves: 4, seed: 4 });
    // golden sides, a deeper bake on the shoulder, pale underneath; the cut top is soft crumb
    let col = mix([238, 196, 128], [214, 136, 60], smooth(0.01, 0.08, y));
    col = mix(col, [190, 108, 44], smooth(0.12, 0.19, y) * 0.55);
    col = mix(col, [242, 214, 160], smooth(0.84, 0.9, t));
    return mul(col, 0.94 + 0.12 * n);
  });
  parts.push({ mesh: bunBottom, material: { name: 'bun_bottom', texture: bbTex, roughness: 0.86 } });

  // ---------- patty: thick, craggy, grilled ----------
  // a rounded slab: flat middles, a boxy rounded side with plenty of rows for the crags
  const P_MID = (P_BOT + P_TOP) / 2, P_HH = (P_TOP - P_BOT) / 2, P_R = 0.452, P_RC = 0.3;
  const pProfile = [[0, P_BOT], [0.18, P_BOT]];
  for (let k = 0; k <= 24; k++) {
    const ph = -Math.PI / 2 + (Math.PI * k) / 24;
    const c = Math.cos(ph), sn = Math.sin(ph);
    pProfile.push([P_RC + (P_R - P_RC) * Math.abs(c) ** (2 / 3.2), P_MID + P_HH * Math.sign(sn) * Math.abs(sn) ** (2 / 3.2)]);
  }
  pProfile.push([0.18, P_TOP + 0.002], [0, P_TOP + 0.003]);
  const crag = (x, y, z) => {
    const big = fbm3(x * 7, y * 9, z * 7, { octaves: 3, seed: 11 }) - 0.5;
    const chunk = Math.abs(noise3(x * 22, y * 26, z * 22, 12) - 0.5) * 2; // ridged crumbs
    return big * 0.026 + (0.5 - chunk) * 0.011;
  };
  const patty = lathe(pProfile, { segments: 120, v: (t) => 1 - t });
  patty.map(([x, y, z]) => {
    const r = Math.hypot(x, z);
    if (r < 1e-6) return [x, y, z];
    const side = smooth(0.3, 0.42, r); // crag the rim, keep the middle flatter
    const d = crag(x, y, z) * side;
    const k = (r + d) / r;
    return [x * k, y + (y > (P_BOT + P_TOP) / 2 ? 1 : -1) * d * 0.25 * (1 - smooth(0.4, 0.45, r)), z * k];
  });
  const patTex = new Canvas(1024, 256, [112, 60, 34]);
  patTex.shade((u, v) => {
    const t = 1 - v;
    const [r, y] = profileLerp(pProfile, t);
    const a = u * TAU;
    const p = [Math.cos(a) * r, y, Math.sin(a) * r];
    const d = crag(...p);
    const n = fbm3(p[0] * 30, p[1] * 30, p[2] * 30, { octaves: 3, seed: 13 });
    let col = mix([58, 30, 17], [132, 76, 44], smooth(-0.014, 0.012, d));
    col = mul(col, 0.85 + 0.3 * n);
    // grill marks: diagonal charred bars across the side
    // (short slanted dashes, each its own length and height, broken up where the crust is lumpy)
    const ph = a * (16 / TAU) + (y - P_BOT) * 6;
    const m = Math.round(ph), f = Math.abs(ph - m);
    const mr = noise3(m * 7.3, 0.5, 0.5, 17), mc = P_MID + (noise3(m * 3.1, 2.5, 0.5, 18) - 0.5) * 0.05;
    const half = 0.035 + 0.035 * mr;
    const along = 1 - smooth(half - 0.012, half, Math.abs(y - mc));
    const broken = smooth(0.25, 0.5, noise3(p[0] * 14, p[1] * 14, p[2] * 14, 15));
    const mark = (1 - smooth(0.07, 0.13 * (0.8 + 0.4 * mr), f)) * along * (0.5 + 0.5 * broken);
    col = mix(col, [34, 18, 12], mark * 0.85);
    // a seared top crust
    col = mix(col, mul(col, 0.8), smooth(0.86, 0.95, t));
    return col;
  });
  patTex.speckle(2600, [0.6, 1.6], [176, 112, 72], 0.35, 21); // juicy glints
  patTex.speckle(1800, [0.6, 1.8], [40, 20, 12], 0.4, 22); // charred crumbs
  parts.push({ mesh: patty, material: { name: 'patty', texture: patTex, roughness: 0.62 } });

  // ---------- cheese: a square slice melted over the rim, corners and drips hanging down ----------
  const R0 = 0.39, RHO = 0.08; // where the cheese starts to roll over the patty's edge, and the roll's radius
  const S = 0.43, RC = 0.06, ROT = (20 * Math.PI) / 180; // the slice's half-side, corner rounding, turn
  const drips = [
    { a: -1.32, len: 0.13, w: 0.034 }, // front, a little to screen left (+X)
    { a: -2.42, len: 0.07, w: 0.03 },
    { a: 1.75, len: 0.1, w: 0.035 },
    { a: 0.32, len: 0.065, w: 0.028 },
    { a: 2.75, len: 0.05, w: 0.026 },
  ];
  const sliceEdge = (a) => {
    // distance from the middle to the rounded square's edge along angle a (the square turned by ROT)
    const b = a - ROT;
    const dx = Math.cos(b), dz = Math.sin(b);
    let lo = 0, hi = 1;
    const sd = (t) => {
      const qx = Math.abs(dx * t) - (S - RC), qz = Math.abs(dz * t) - (S - RC);
      return Math.hypot(Math.max(qx, 0), Math.max(qz, 0)) + Math.min(Math.max(qx, qz), 0) - RC;
    };
    for (let k = 0; k < 40; k++) {
      const m = (lo + hi) / 2;
      if (sd(m) < 0) lo = m;
      else hi = m;
    }
    return lo;
  };
  const dripAmount = (a, L0) => {
    let e = 0, near = 0;
    for (const d of drips) {
      const x = (wrapPi(a - d.a) * L0) / d.w;
      const k = Math.exp(-(x ** 4));
      if (d.len * k > e) e = d.len * k;
      near = Math.max(near, k);
    }
    return { e, near };
  };
  const cheeseEdge = (a) => {
    const L0 = sliceEdge(a);
    return L0 + dripAmount(a, L0).e;
  };
  /** The draped point for a flat-slice point at distance r, angle a, lifted off the patty by lift. */
  const drape = (r, a, lift) => {
    const s = r - R0;
    let rr, y;
    if (s <= 0) {
      rr = r;
      y = CH_Y + lift;
    } else if (s < (Math.PI / 2) * RHO) {
      const ph = s / RHO;
      rr = R0 + (RHO + lift) * Math.sin(ph);
      y = CH_Y - RHO + (RHO + lift) * Math.cos(ph);
    } else {
      rr = R0 + RHO + lift;
      y = CH_Y - RHO - (s - (Math.PI / 2) * RHO);
    }
    return [rr * Math.cos(a), y, rr * Math.sin(a)];
  };
  // angles: even round, denser through each drip
  let angs = [];
  for (let k = 0; k < 128; k++) angs.push((TAU * k) / 128 - Math.PI);
  for (const d of drips) for (let k = -9; k <= 9; k++) angs.push(wrapPi(d.a + (k / 9) * (d.w / 0.42) * 1.7));
  angs.sort((p, q) => p - q);
  angs = angs.filter((x, i) => i === 0 || x - angs[i - 1] > 0.004);
  angs.push(angs[0] + TAU);
  const cheese = new Mesh();
  const CH_IN = 3, CH_OUT = 14, THICK = 0.012;
  const crow = [];
  const edgeTop = [], edgeBot = [];
  for (let i = 0; i < angs.length; i++) {
    const a = angs[i];
    const L = cheeseEdge(a);
    const L0 = sliceEdge(a);
    const { near } = dripAmount(a, L0);
    const col = [];
    for (let k = 0; k <= CH_IN + CH_OUT; k++) {
      const r = k <= CH_IN ? (R0 - 0.01) * (k / CH_IN) : lerp(R0 - 0.01, L, ((k - CH_IN) / CH_OUT) ** 0.9);
      // a fat bead toward each drip's end
      const bead = 0.007 * near * Math.exp(-(((L - r) / 0.035) ** 2));
      const lift = 0.004 + bead + (fbm3(Math.cos(a) * 4, r * 6, Math.sin(a) * 4, { octaves: 2, seed: 31 }) - 0.5) * 0.004;
      const p = drape(r, a, lift);
      col.push(cheese.vertex(p, [(a + Math.PI) / TAU, r / 0.65]));
      if (k === CH_IN + CH_OUT) {
        edgeTop.push(cheese.vertex(p, [(a + Math.PI) / TAU, 0.98]));
        // the slice's thickness: the edge turns in toward the patty and a little up under itself
        const q = drape(r - THICK * 0.6, a, lift - THICK);
        edgeBot.push(cheese.vertex(q, [(a + Math.PI) / TAU, 0.98]));
      }
    }
    crow.push(col);
  }
  for (let i = 0; i + 1 < angs.length; i++) {
    for (let k = 0; k < CH_IN + CH_OUT; k++) {
      const a = crow[i][k], b = crow[i + 1][k], c = crow[i + 1][k + 1], d = crow[i][k + 1];
      cheese.tri(a, b, c).tri(a, c, d);
    }
    cheese.quad(edgeTop[i], edgeTop[i + 1], edgeBot[i + 1], edgeBot[i]);
  }
  orient(cheese, (c, n) => n[1] * 0.3 + (n[0] * c[0] + n[2] * c[2]) / (Math.hypot(c[0], c[2]) + 0.05));
  const chTex = new Canvas(256, 256, [252, 188, 40]);
  chTex.shade((u, v) => {
    const n = fbm3(u * 9, v * 9, 0.3, { octaves: 3, seed: 33 });
    let col = mix([255, 198, 52], [246, 164, 30], smooth(0.55, 0.95, v)); // more orange where it's thin and melted
    return mul(col, 0.97 + 0.06 * n);
  });
  parts.push({ mesh: cheese, material: { name: 'cheese', texture: chTex, roughness: 0.42 } });
  // the front drip's tip, for the app's sauce drips
  const fd = drips[0];
  const cheeseTip = drape(cheeseEdge(fd.a), fd.a, 0.006);

  // ---------- tomato: two thick overlapping slices ----------
  const tomProfile = (rad, th) => [
    [0, 0],
    [rad - 0.03, 0],
    [rad - 0.008, 0.004],
    [rad, th * 0.3],
    [rad + 0.002, th * 0.5],
    [rad, th * 0.7],
    [rad - 0.008, th - 0.004],
    [rad - 0.03, th],
    [0, th],
  ];
  const tomTex = new Canvas(512, 256, [220, 64, 42]);
  tomTex.shade((u, v) => {
    // v: 0 top centre .. 1 bottom centre; the side band is v 0.25..0.75
    const a = u * TAU;
    const n = fbm3(Math.cos(a) * 3, v * 6, Math.sin(a) * 3, { octaves: 3, seed: 41 });
    const sideness = smooth(0.2, 0.3, v) * (1 - smooth(0.7, 0.8, v));
    let col = mix([232, 92, 64], [214, 50, 34], sideness);
    // the cut faces: seed chambers (paler jelly, little seeds) round a red core
    const rr = v < 0.5 ? v / 0.25 : (1 - v) / 0.25; // 0 centre .. 1 rim
    if (sideness < 1) {
      const ch = Math.cos(a * 5) * 0.5 + 0.5;
      const jelly = smooth(0.35, 0.55, rr) * (1 - smooth(0.78, 0.88, rr)) * smooth(0.3, 0.7, ch);
      col = mix(col, [246, 136, 96], jelly * (1 - sideness));
      if (jelly > 0.4 && noise3(a * 14, rr * 20, 0.5, 43) > 0.72) col = mix(col, [250, 220, 150], 0.7 * (1 - sideness));
    }
    return mul(col, 0.95 + 0.1 * n);
  });
  const tomMat = { name: 'tomato', texture: tomTex, roughness: 0.55 };
  const T_TH = TO_TOP - TO_BOT;
  for (const [cx, cz, rad, tilt, lift] of [
    [0.07, -0.015, 0.385, 2.2, 0.004],
    [-0.065, 0.02, 0.39, -2, 0.0],
  ]) {
    const prof = tomProfile(rad, T_TH);
    const v = (t) => 1 - t;
    const slice = lathe(prof, { segments: 72, v });
    slice.transform({ rotate: [0, 0, tilt], translate: [cx, TO_BOT + lift, cz] });
    parts.push({ mesh: slice, material: tomMat });
  }

  // ---------- lettuce: two ruffled frills, two-sided, sticking out all round ----------
  const lettuceMat = { name: 'lettuce', texture: lettuceTexture(), roughness: 0.75 };
  for (const L of [
    { y: LE_Y + 0.012, rOut: 0.466, waves: 13, amp: 0.028, phase: 0.0, droop: 0.008, seed: 51 },
    { y: LE_Y - 0.004, rOut: 0.48, waves: 15, amp: 0.024, phase: 0.9, droop: 0.014, seed: 52 },
  ])
    parts.push({ mesh: frill(L), material: lettuceMat });

  function frill({ y, rOut, waves, amp, phase, droop, seed }) {
    const mesh = new Mesh();
    const NA = 168, rIn = 0.26;
    const radial = [0, 0.3, 0.55, 0.72, 0.84, 0.93, 1];
    const pt = (a, q, side) => {
      const edge = rOut + 0.012 * Math.sin(a * 7 + seed) + 0.008 * (noise3(Math.cos(a) * 3, Math.sin(a) * 3, seed, 53) - 0.5) * 2;
      const r = lerp(rIn, edge, q);
      const out = smooth(0.35, 1, q);
      const wob = Math.sin(a * waves + phase + 0.8 * Math.sin(a * 3 + seed)) + 0.35 * Math.sin(a * waves * 2.1 + seed);
      const yy = y + amp * out ** 1.4 * wob - droop * out ** 2 + side * 0.0035;
      return [r * Math.cos(a), yy, r * Math.sin(a)];
    };
    const top = [], bot = [];
    for (let i = 0; i <= NA; i++) {
      const a = (TAU * i) / NA;
      top.push(radial.map((q) => mesh.vertex(pt(a, q, 1), [i / NA, q * 0.98])));
      bot.push(radial.map((q) => mesh.vertex(pt(a, q, -1), [i / NA, q * 0.98])));
    }
    const R = radial.length - 1;
    for (let i = 0; i < NA; i++)
      for (let k = 0; k < R; k++) {
        mesh.tri(top[i][k], top[i + 1][k], top[i + 1][k + 1]).tri(top[i][k], top[i + 1][k + 1], top[i][k + 1]);
        mesh.tri(bot[i][k], bot[i + 1][k + 1], bot[i + 1][k]).tri(bot[i][k], bot[i][k + 1], bot[i + 1][k + 1]);
      }
    // the frill's edge
    for (let i = 0; i < NA; i++) mesh.quad(top[i][R], top[i + 1][R], bot[i + 1][R], bot[i][R]);
    // which way is up: the top sheet's faces must point up (+y)
    let score = 0;
    for (let i = 0; i < NA; i++) {
      const p = mesh.point(top[i][1]), q = mesh.point(top[i + 1][1]), r = mesh.point(top[i + 1][2]);
      const e1 = [q[0] - p[0], q[1] - p[1], q[2] - p[2]], e2 = [r[0] - p[0], r[1] - p[1], r[2] - p[2]];
      score += e1[2] * e2[0] - e1[0] * e2[2];
    }
    if (score < 0) mesh.flip();
    return mesh;
  }
  function lettuceTexture() {
    const c = new Canvas(1024, 128, [120, 186, 64]);
    c.shade((u, v) => {
      // v: 0 inside .. 1 the frilly edge; veins run outward
      const a = u * TAU;
      const n = fbm3(Math.cos(a) * 6, v * 3, Math.sin(a) * 6, { octaves: 3, seed: 55 });
      let col = mix([78, 150, 40], [128, 200, 70], smooth(0.2, 0.9, v));
      col = mix(col, [160, 220, 100], smooth(0.9, 1, v) * 0.5);
      const vein = Math.abs(Math.sin(a * 42 + n * 3));
      col = mix(col, [190, 230, 140], (1 - smooth(0.0, 0.12, vein)) * 0.45 * (1 - smooth(0.7, 1, v)));
      return mul(col, 0.94 + 0.12 * n);
    });
    return c;
  }

  // ---------- top bun: a tall golden dome, sesame seeds on its crown only ----------
  const DOME_P = 2.35;
  const domeAt = (th) => {
    // th: 0 at the dome's widest (low) edge .. PI/2 the crown
    const c = Math.cos(th), s = Math.sin(th);
    return [TB_R * c ** (2 / DOME_P), TB_BASE + 0.05 + (TB_TOP - TB_BASE - 0.05) * s ** (2 / DOME_P)];
  };
  const tbProfile = [
    [0, TB_BASE],
    [0.33, TB_BASE],
    [0.39, TB_BASE + 0.004],
    [0.424, TB_BASE + 0.016],
    [0.441, TB_BASE + 0.034],
  ];
  const DOME_ROWS = 34;
  for (let j = 0; j <= DOME_ROWS; j++) {
    const th = (Math.PI / 2) * (j / DOME_ROWS) ** 0.95;
    const [r, y] = domeAt(th);
    tbProfile.push([j === DOME_ROWS ? 0 : r, y]);
  }
  const tbV = (t) => 0.04 + 0.96 * (1 - t); // texture rows below v = 0.03 are kept for the seeds
  const bunTop = lathe(tbProfile, { segments: 104, v: tbV });
  bunTop.map(([x, y, z]) => {
    const r = Math.hypot(x, z);
    if (r < 1e-6) return [x, y, z];
    const k = 1 + (fbm3(x * 4, y * 4, z * 4, { octaves: 2, seed: 61 }) - 0.5) * 0.018;
    return [x * k, y, z * k];
  });
  const tbTex = new Canvas(512, 512, [224, 150, 70]);
  tbTex.shade((u, v) => {
    if (v < 0.03) return [248, 236, 204]; // sesame
    const t = 1 - (v - 0.04) / 0.96;
    const [r, y] = profileLerp(tbProfile, t);
    const a = u * TAU;
    const p = [Math.cos(a) * r, y, Math.sin(a) * r];
    const n = fbm3(p[0] * 6, p[1] * 6, p[2] * 6, { octaves: 4, seed: 63 });
    const h = (y - TB_BASE) / (TB_TOP - TB_BASE);
    // pale where it touched its neighbours in the tray, golden on the sides, deeper brown on the crown
    let col = mix([242, 210, 150], [230, 158, 74], smooth(0.02, 0.16, h));
    col = mix(col, [198, 112, 42], smooth(0.45, 0.98, h) * 0.85);
    return mul(col, 0.95 + 0.1 * n);
  });
  const bunTopMat = { name: 'bun_top', texture: tbTex, roughness: 0.8 };
  parts.push({ mesh: bunTop, material: bunTopMat });

  // sesame seeds: little flat teardrops lying on the crown, scattered by a jittered spiral
  const R = rng(71);
  const seedsMesh = new Mesh();
  const domePoint = (th, a) => {
    const [r, y] = domeAt(th);
    return [r * Math.cos(a), y, r * Math.sin(a)];
  };
  const placed = [];
  for (let k = 0; k < 400 && placed.length < 40; k++) {
    // uniform-ish over the cap above th0
    const th = lerp(0.62, 1.52, Math.sqrt(R()));
    const a = R() * TAU;
    const p = domePoint(th, a);
    if (placed.some((q) => Math.hypot(q[0] - p[0], q[1] - p[1], q[2] - p[2]) < 0.062)) continue;
    placed.push(p);
    // the surface frame there
    const e = 0.002;
    const pth = domePoint(Math.min(Math.PI / 2, th + e), a), pa = domePoint(th, a + e);
    const tUp = norm3(sub3(pth, p)), tRound = norm3(sub3(pa, p));
    const nrm = norm3(cross3(tUp, tRound));
    const out = nrm[1] < 0 ? nrm.map((x) => -x) : nrm;
    const spin = R() * TAU;
    const dir = norm3(add3(scale3(tUp, Math.cos(spin)), scale3(tRound, Math.sin(spin))));
    const side = cross3(out, dir);
    const seed = sphere(1, { segments: 7, rows: 4 });
    seed.map(([x, y, z]) => {
      // a teardrop: longer one way (x), flat (y), pointed at one end
      const taper = 1 - 0.35 * smooth(-0.2, 1, x);
      const lx = x * 0.017, ly = y * 0.0045, lz = z * 0.0085 * taper;
      return [
        p[0] + dir[0] * lx + out[0] * (ly + 0.002) + side[0] * lz,
        p[1] + dir[1] * lx + out[1] * (ly + 0.002) + side[1] * lz,
        p[2] + dir[2] * lx + out[2] * (ly + 0.002) + side[2] * lz,
      ];
    });
    for (let i = 0; i < seed.vertexCount; i++) seed.uvs[i * 2 + 1] = 0.012;
    // its winding follows the frame's handedness
    const det = dot3(cross3(dir, out), side);
    if (det < 0) seed.flip();
    seedsMesh.append(seed);
  }
  parts.push({ mesh: seedsMesh, material: bunTopMat });

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const sc = 1.15 / (max[1] - min[1]);
  let frillR = 0;
  for (const p of parts) if (p.material === lettuceMat) for (let i = 0; i < p.mesh.vertexCount; i++) frillR = Math.max(frillR, Math.hypot(p.mesh.point(i)[0], p.mesh.point(i)[2]));
  const FACE_Y = 0.715;
  return {
    parts,
    face: (FACE_Y - min[1]) / (max[1] - min[1]),
    faceSize: 0.53,
    anchors: {
      topBunBase: [0, TB_BASE, 0],
      cheese: cheeseTip,
      patty: [0, (P_BOT + P_TOP) / 2, 0],
    },
    notes:
      `Layers (app space, metres from the floor): bun_bottom 0-${(BB_TOP * sc).toFixed(3)}, patty ` +
      `${(P_BOT * sc).toFixed(3)}-${(P_TOP * sc).toFixed(3)} (radius ${(P_R * sc).toFixed(3)}), cheese on top at ` +
      `${(CH_Y * sc).toFixed(3)} draping to the patty's side, tomato ${(TO_BOT * sc).toFixed(3)}-${(TO_TOP * sc).toFixed(3)}, ` +
      `lettuce ~${(LE_Y * sc).toFixed(3)} (frill out to ${(frillR * sc).toFixed(3)} radius), bun_top ${(TB_BASE * sc).toFixed(3)}-1.150 ` +
      `(radius ${(TB_R * sc).toFixed(3)}). Sesame seeds are part of bun_top. The cheese anchor is the tip of the ` +
      `front drip (screen left of centre); other drips hang at the screen right front corner, the sides and the back.`,
  };
}

const sub3 = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const add3 = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const scale3 = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const dot3 = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross3 = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm3 = (a) => {
  const l = Math.hypot(a[0], a[1], a[2]) || 1;
  return [a[0] / l, a[1] / l, a[2] / l];
};

/**
 * Makes a mesh face the right way: test(centroid, normal) should be positive for an outward face; if most faces
 * say otherwise, every triangle is flipped.
 */
function orient(mesh, test) {
  let score = 0;
  for (let k = 0; k < mesh.indices.length; k += 3) {
    const [a, b, c] = [mesh.indices[k], mesh.indices[k + 1], mesh.indices[k + 2]].map((i) => mesh.point(i));
    const n = cross3(sub3(b, a), sub3(c, a));
    const l = Math.hypot(...n);
    if (l < 1e-12) continue;
    const cen = [(a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3, (a[2] + b[2] + c[2]) / 3];
    score += Math.sign(test(cen, [n[0] / l, n[1] / l, n[2] / l]));
  }
  if (score < 0) mesh.flip();
  return mesh;
}
