/**
 * Toxic barrel: a dented 55-gallon steel drum in chipped hazard-yellow paint, rusty at the chimes, two rolling
 * hoops, a lid with a big bung cap (oozing) and a small one, a stencilled "TOXIC" between two black trefoils on the
 * lower front, and glowing green goo oozing out from under the lid rim and dripping down the sides. The face goes
 * between the hoops, which are kept plain yellow.
 */
export default function build(kit) {
  const { Mesh, Canvas, loft, lathe, tube, sphere, cylinder, ringAngle, fbm3, noise3, smooth, clamp, lerp, TAU, rng } = kit;

  // ---------- sizes ----------
  const R = 0.3, H = 0.88; // body radius, height to the top of the chime
  const HOOPS = [0.235, 0.635]; // rolling hoop heights
  const LID_Y = H - 0.018;
  const FACE_Y = 0.45;
  const wrapPi = (x) => ((((x + Math.PI) % TAU) + TAU) % TAU) - Math.PI;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  const azimuth = (a) => wrapPi((3 * Math.PI) / 2 - a);
  const angleOf = (phi) => (3 * Math.PI) / 2 - phi; // lathe angle for an azimuth
  /** The face's patch (between the hoops, front centre). */
  const faceMask = (phi, y) => (1 - smooth(0.5, 0.85, Math.abs(phi))) * (1 - smooth(0.13, 0.2, Math.abs(y - FACE_Y)));

  // ---------- the body's radius: the profile (hoops) plus dents ----------
  const hoopBump = (y) => {
    let b = 0;
    for (const yh of HOOPS) {
      const t = (y - yh) / 0.024;
      if (Math.abs(t) < 1) b = Math.max(b, 0.014 * (0.5 + 0.5 * Math.cos(Math.PI * t)) ** 0.6);
    }
    return b;
  };
  const DENTS = [
    { phi: 2.0, y: 0.47, r: 0.09, d: 0.02 },
    { phi: -1.55, y: 0.13, r: 0.07, d: 0.016 },
    { phi: -1.95, y: 0.76, r: 0.065, d: 0.016 },
    { phi: -2.7, y: 0.42, r: 0.06, d: 0.012 },
    { phi: 0.95, y: 0.12, r: 0.05, d: 0.008 },
  ];
  const dent = (phi, y) => {
    const p = [Math.cos(phi), y, Math.sin(phi)];
    let d = -0.013 * Math.max(0, fbm3(p[0] * 2.2, p[1] * 3.5, p[2] * 2.2, { octaves: 3, seed: 7 }) - 0.45) * 2;
    for (const dn of DENTS) {
      const q = Math.hypot(wrapPi(phi - dn.phi) * R, y - dn.y) / dn.r;
      if (q < 1) d -= dn.d * (0.5 + 0.5 * Math.cos(Math.PI * q));
    }
    // fade out near the chimes and hoops (stiff there), and mostly off the face's patch
    const stiff = smooth(0.035, 0.07, y) * (1 - smooth(H - 0.07, H - 0.035, y)) * (1 - hoopBump(y) / 0.014);
    return d * stiff * (1 - 0.8 * faceMask(phi, y));
  };
  const bodyR = (phi, y) => R + hoopBump(y) + dent(phi, y);
  /** Drops the points no triangle uses. */
  const compact = (mesh) => {
    const map = new Map(), pos = [], uv = [];
    mesh.indices = mesh.indices.map((i) => {
      if (!map.has(i)) {
        map.set(i, pos.length / 3);
        pos.push(mesh.positions[i * 3], mesh.positions[i * 3 + 1], mesh.positions[i * 3 + 2]);
        uv.push(mesh.uvs[i * 2], mesh.uvs[i * 2 + 1]);
      }
      return map.get(i);
    });
    mesh.positions = pos;
    mesh.uvs = uv;
    return mesh;
  };

  // ---------- the body mesh: bottom head, chimes, the side between them ----------
  const prof = [];
  const add = (r, y, kind = 'fixed') => prof.push({ r, y, kind });
  add(0, 0.014);
  add(R - 0.03, 0.014);
  add(R - 0.013, 0.005);
  add(R - 0.004, 0.0);
  add(R + 0.006, 0.002);
  add(R + 0.01, 0.01);
  add(R + 0.009, 0.02);
  add(R + 0.004, 0.028);
  // the side: rows closer together round the hoops
  for (let y = 0.034; y < H - 0.034; ) {
    add(R, y, 'side');
    const nearHoop = HOOPS.some((yh) => Math.abs(y - yh) < 0.034);
    y += nearHoop ? 0.0065 : 0.016;
  }
  add(R, H - 0.034, 'side');
  add(R + 0.004, H - 0.028);
  add(R + 0.009, H - 0.02);
  add(R + 0.01, H - 0.01);
  add(R + 0.006, H - 0.002);
  add(R - 0.002, H);
  add(R - 0.01, H - 0.004);
  add(R - 0.014, H - 0.012);
  add(R - 0.016, LID_Y - 0.006);
  const SEG = 120;
  const rings = prof.map((row) => {
    const ring = [];
    for (let i = 0; i <= SEG; i++) {
      const a = ringAngle(i % SEG, SEG);
      const phi = azimuth(a);
      let r = row.r;
      if (row.kind === 'side') r = bodyR(phi, row.y);
      else if (row.r > R - 0.02) r += (noise3(Math.cos(a) * 4, row.y * 3, Math.sin(a) * 4, 9) - 0.5) * 0.003; // a bashed chime
      ring.push([r * Math.cos(a), row.y, r * Math.sin(a)]);
    }
    ring.v = clamp(1 - row.y / H);
    return ring;
  });
  const body = loft(rings);

  // ---------- the stencil label (drawn flat, in metres, then wrapped on the drum) ----------
  const LBL = { w: 0.64, h: 0.2, y0: 0.03, px: 1100 }; // arc width and height (m), bottom edge, pixels per metre
  const label = new Canvas(Math.round(LBL.w * LBL.px), Math.round(LBL.h * LBL.px), [255, 255, 255]);
  {
    const M = (m) => m * LBL.px;
    const cy = M(LBL.h / 2);
    const trefoil = (cx, cyy, s) => {
      label.disc(cx, cyy, s * 0.18, [0, 0, 0]);
      for (const deg of [30, 150, 270]) {
        const c = (-deg * Math.PI) / 180; // canvas y is down
        const pts = [];
        for (let k = 0; k <= 16; k++) {
          const t = c - Math.PI / 6 + ((Math.PI / 3) * k) / 16;
          pts.push([cx + s * 0.3 * Math.cos(t), cyy + s * 0.3 * Math.sin(t)]);
        }
        for (let k = 16; k >= 0; k--) {
          const t = c - Math.PI / 6 + ((Math.PI / 3) * k) / 16;
          pts.push([cx + s * Math.cos(t), cyy + s * Math.sin(t)]);
        }
        label.poly(pts, [0, 0, 0]);
      }
    };
    const TS = M(0.056); // trefoil radius
    trefoil(M(0.064), cy, TS);
    trefoil(M(LBL.w - 0.064), cy, TS);
    // TOXIC, letter by letter so the stencil bridges can be cut in
    const th = M(0.084), top = cy - th / 2;
    const letters = ['T', 'O', 'X', 'I', 'C'];
    const widths = { T: 0.62, O: 0.72, X: 0.62, I: 0.08, C: 0.62 };
    const gap = th * 0.3;
    const total = letters.reduce((s, l) => s + widths[l] * th, 0) + gap * (letters.length - 1);
    let x = M(LBL.w / 2) - total / 2;
    const at = {};
    for (const l of letters) {
      const cx = x + (widths[l] * th) / 2;
      label.text(l, cx, top, th, [0, 0, 0], { weight: 0.24, a: 0.25 }); // overspray
      label.text(l, cx, top, th, [0, 0, 0], { weight: 0.2 });
      at[l] = cx;
      x += widths[l] * th + gap;
    }
    // bridges: the O's top and bottom, the T's stem under its bar, the C's top
    const W = [255, 255, 255];
    label.rect(at.O - th * 0.04, top - th * 0.2, at.O + th * 0.04, top + th * 0.12, W);
    label.rect(at.O - th * 0.04, top + th * 0.88, at.O + th * 0.04, top + th * 1.2, W);
    label.rect(at.T - th * 0.15, top + th * 0.1, at.T + th * 0.15, top + th * 0.17, W);
    label.rect(at.C + th * 0.02, top - th * 0.2, at.C + th * 0.08, top + th * 0.14, W);
  }
  const labelInk = (s, y) => {
    // s: metres across from the front centre (+ = screen right), y: height
    const lx = (s + LBL.w / 2) * LBL.px, ly = (LBL.y0 + LBL.h - y) * LBL.px;
    if (lx < 0 || ly < 0 || lx >= label.width - 1 || ly >= label.height - 1) return 0;
    const x0 = Math.floor(lx), y0 = Math.floor(ly), fx = lx - x0, fy = ly - y0;
    const g = (x, yy) => 1 - label.get(x, yy)[0] / 255;
    return lerp(lerp(g(x0, y0), g(x0 + 1, y0), fx), lerp(g(x0, y0 + 1), g(x0 + 1, y0 + 1), fx), fy);
  };

  // ---------- the drum's paint ----------
  const YELLOW = [244, 192, 26], STEEL = [74, 72, 70], RUST = [148, 70, 30], RUST2 = [102, 50, 26];
  const paint = new Canvas(1024, 1024, YELLOW);
  paint.shade((u, v) => {
    const phi = (u - 0.5) * TAU;
    const y = (1 - v) * H;
    const s = phi * R;
    const p = [Math.sin(phi) * R, y, Math.cos(phi) * R];
    const fm = faceMask(phi, y);
    // paint: a little roller texture and grime toward the bottom and in the hoops' creases
    const roll = fbm3(p[0] * 18, p[1] * 60, p[2] * 18, { octaves: 2, seed: 3 });
    const grime = fbm3(p[0] * 5, p[1] * 5, p[2] * 5, { octaves: 3, seed: 5 });
    let col = mul(YELLOW, 0.95 + 0.06 * (roll - 0.5) - 0.06 * (grime - 0.5) * (1 - fm));
    col = mix(col, [150, 112, 40], (1 - smooth(0.02, 0.2, y)) * 0.35 * (0.5 + grime));
    for (const yh of HOOPS) {
      const d = Math.abs(y - yh);
      col = mix(col, [176, 128, 30], (smooth(0.018, 0.026, d) * (1 - smooth(0.026, 0.04, d))) * 0.45);
    }
    // the stencil
    const ink = labelInk(s, y) * (0.92 - 0.25 * smooth(0.6, 0.75, fbm3(p[0] * 40, p[1] * 40, p[2] * 40, { octaves: 2, seed: 15 })));
    col = mix(col, [26, 24, 22], ink);
    // rims: rust on the chimes, rust creeping from them
    const nearRim = Math.max(1 - smooth(0.03, 0.075, y), smooth(H - 0.075, H - 0.028, y));
    const rustN = fbm3(p[0] * 14, p[1] * 14, p[2] * 14, { octaves: 4, seed: 21 });
    const rustAmt = smooth(0.42, 0.6, rustN + nearRim * 0.35) * nearRim;
    // rust streaks running down from the top chime, and up off the bottom one (not over the face)
    const streak = fbm3(p[0] * 90, p[1] * 2.5, p[2] * 90, { octaves: 2, seed: 23 });
    const streakAmt = smooth(0.6, 0.72, streak) * (smooth(H - 0.32, H - 0.05, y) * 0.55 + (1 - smooth(0.0, 0.14, y)) * 0.4) * (1 - fm);
    // chips: mostly at the rims and hoop edges, a few anywhere but the face
    let edge = nearRim;
    for (const yh of HOOPS) edge = Math.max(edge, 1 - smooth(0.012, 0.045, Math.abs(y - yh)));
    const chipN = fbm3(p[0] * 26, p[1] * 26, p[2] * 26, { octaves: 3, seed: 31 });
    const chipT = 0.765 - 0.17 * edge + 0.2 * fm;
    const chip = smooth(chipT, chipT + 0.012, chipN);
    const chipRim = smooth(chipT - 0.025, chipT, chipN) * (1 - chip);
    col = mix(col, mix(RUST, RUST2, rustN), clamp(streakAmt * 0.5));
    col = mix(col, [255, 226, 120], chipRim * 0.5);
    col = mix(col, mix(STEEL, RUST, smooth(0.5, 0.7, rustN)), chip);
    col = mix(col, mix(RUST, RUST2, smooth(0.4, 0.7, chipN)), rustAmt);
    // the chimes' own edge: worn to bare steel in places
    if (y > H - 0.012 || y < 0.006) col = mix(col, [110, 96, 82], 0.5);
    return col;
  });
  paint.speckle(1500, [0.5, 1.3], [90, 60, 20], 0.18, 41);
  const parts = [{ mesh: body, material: { name: 'drum_paint', texture: paint, roughness: 0.78 } }];

  // ---------- the lid ----------
  const lidProf = [
    [R - 0.009, LID_Y - 0.008],
    [R - 0.016, LID_Y],
    [R - 0.03, LID_Y + 0.001],
    [0.236, LID_Y + 0.001],
    [0.229, LID_Y + 0.005],
    [0.222, LID_Y + 0.001],
    [0.13, LID_Y + 0.002],
    [0.124, LID_Y + 0.0055],
    [0.118, LID_Y + 0.002],
    [0, LID_Y + 0.004],
  ];
  const lid = lathe(lidProf, { segments: 96 });
  for (let i = 0; i < lid.vertexCount; i++) {
    const [x, , z] = lid.point(i);
    lid.uvs[i * 2] = 0.5 - x / (2 * R);
    lid.uvs[i * 2 + 1] = 0.5 - z / (2 * R);
  }
  const lidTex = new Canvas(512, 512, YELLOW);
  lidTex.shade((u, v) => {
    const x = (0.5 - u) * 2 * R, z = (0.5 - v) * 2 * R;
    const rr = Math.hypot(x, z);
    const n = fbm3(x * 20, z * 20, 0.5, { octaves: 3, seed: 51 });
    let col = mul(YELLOW, 0.9 + 0.12 * (fbm3(x * 6, z * 6, 1.5, { octaves: 3, seed: 53 }) - 0.5));
    col = mix(col, [170, 120, 34], smooth(R - 0.05, R - 0.012, rr) * 0.6); // grime in the recess
    const chip = smooth(0.66, 0.675, n);
    col = mix(col, mix(STEEL, RUST, smooth(0.45, 0.65, fbm3(x * 9, z * 9, 2.5, { octaves: 2, seed: 55 }))), chip);
    col = mix(col, mix(RUST, RUST2, n), smooth(0.45, 0.62, n + smooth(R - 0.05, R - 0.01, rr) * 0.3) * smooth(R - 0.05, R - 0.015, rr));
    // scuffs
    const sc = Math.abs(Math.sin(x * 160 + z * 50 + n * 1.5));
    col = mul(col, 1 - 0.05 * smooth(0.985, 1, sc) * smooth(0.5, 0.6, fbm3(x * 4, z * 4, 3.5, { octaves: 2, seed: 57 })));
    return col;
  });
  parts.push({ mesh: lid, material: { name: 'drum_lid', texture: lidTex, roughness: 0.8 } });

  // ---------- bungs ----------
  const steel = { name: 'steel', color: [0.55, 0.55, 0.56], metallic: 0.8, roughness: 0.42 };
  const BIG = { phi: -0.78, r: R - 0.09 }, SMALL = { phi: 2.35, r: R - 0.075 };
  const posOn = (b) => [b.r * Math.cos(angleOf(b.phi)), LID_Y, b.r * Math.sin(angleOf(b.phi))];
  const bigP = posOn(BIG), smallP = posOn(SMALL);
  const bung = (p, rad, h) => {
    const m = new Mesh();
    // flange (a raised collar pressed into the lid), then the cap with a grip rim and a cross on top
    m.append(lathe([[0, -0.004], [rad * 1.45, -0.004, true], [rad * 1.45, 0.004], [rad * 1.3, 0.008, true], [rad * 1.05, 0.008, true], [rad, h * 0.25], [rad, h, true], [rad * 0.88, h + 0.002, true], [0, h + 0.002]], { segments: 28 }));
    for (const deg of [0, 90]) {
      const bar = cylinder(rad * 0.11, rad * 1.7, { segments: 8 }).transform({ translate: [0, -rad * 0.85, 0] });
      m.append(bar.transform({ rotate: [90, deg, 0], translate: [0, h + 0.002 + rad * 0.06, 0] }));
    }
    return m.transform({ translate: p });
  };
  const capH = 0.018;
  parts.push({ mesh: bung(bigP, 0.026, capH), material: steel });
  parts.push({ mesh: bung(smallP, 0.015, 0.012), material: steel });

  // ---------- the ooze ----------
  const OOZE_TOP = H - 0.024; // just under the top chime
  const oozes = [
    { phi: -1.16, len: 0.42, w: 0.04, t: 0.011, seed: 1 },
    { phi: 1.08, len: 0.26, w: 0.036, t: 0.01, seed: 2 },
    { phi: 1.62, len: 0.5, w: 0.045, t: 0.012, seed: 3 },
    { phi: -1.85, len: 0.2, w: 0.032, t: 0.009, seed: 4 },
    { phi: 2.45, len: 0.36, w: 0.04, t: 0.011, seed: 5 },
    { phi: -2.55, len: 0.56, w: 0.046, t: 0.012, seed: 6 },
    { phi: 3.05, len: 0.14, w: 0.03, t: 0.009, seed: 7 },
  ];
  /** Goo thickness at (phi, y): a band seeping out under the rim (none at the front centre) and the drips. */
  const goo = (phi, y) => {
    const below = OOZE_TOP - y;
    if (below < -0.002) return 0;
    let t = 0;
    const band = smooth(0.55, 0.95, Math.abs(phi)) * (0.6 + 0.6 * noise3(Math.cos(phi) * 3, Math.sin(phi) * 3, 0.5, 61));
    const bandH = 0.028 * band;
    if (below < bandH + 0.01) t = 0.008 * band * Math.sqrt(clamp(1 - (below / (bandH + 0.01)) ** 2));
    for (const o of oozes) {
      if (below > o.len + o.w * 2) continue;
      const wig = (noise3(below * 6, o.seed * 2.1, 0.5, 63) - 0.5) * 0.04;
      const across = wrapPi(phi - o.phi) * R - wig;
      const f = clamp(below / o.len);
      const w = o.w * (1 - 0.4 * f) * (0.8 + 0.4 * noise3(below * 10, o.seed, 2.5, 64)) * (1 + 0.7 * (1 - smooth(0, 0.06, below)));
      let k = below <= o.len ? 1 - (across / w) ** 2 : -1;
      let amp = o.t * (0.8 + 0.2 * f);
      const bw = w * 1.4;
      const kb = 1 - (across / bw) ** 2 - ((below - o.len) / (bw * 1.25)) ** 2;
      if (kb > 0 && Math.sqrt(kb) * o.t * 1.5 > Math.sqrt(Math.max(0, k)) * amp) {
        k = kb;
        amp = o.t * 1.5;
      }
      if (k > 0) t = Math.max(t, Math.sqrt(k) * amp);
    }
    return t;
  };
  const oozeMesh = new Mesh();
  {
    const OS = 160;
    const ys = [];
    for (let y = OOZE_TOP; y > OOZE_TOP - 0.66; y -= 0.008) ys.push(y);
    const grid = ys.map((y) => {
      const row = [];
      for (let i = 0; i <= OS; i++) {
        const a = ringAngle(i % OS, OS);
        const phi = azimuth(a);
        const t = goo(phi, y);
        // hidden just inside the paint where there's no goo; under the chime at the top row
        const r = bodyR(phi, y) + (t > 0 ? t + 0.0015 : -0.004);
        row.push({ i: oozeMesh.vertex([r * Math.cos(a), y, r * Math.sin(a)], [1 - i / OS, (OOZE_TOP - y) / 0.66]), t });
      }
      return row;
    });
    for (let j = 0; j + 1 < grid.length; j++)
      for (let i = 0; i < OS; i++) {
        const p = grid[j][i], q = grid[j][i + 1], r = grid[j + 1][i + 1], s = grid[j + 1][i];
        if (p.t <= 0 && q.t <= 0 && r.t <= 0 && s.t <= 0) continue;
        // same winding as loft (rows here run downward, so flipped)
        oozeMesh.tri(p.i, q.i, r.i).tri(p.i, r.i, s.i);
      }
    compact(oozeMesh);
  }
  // a puddle of goo welling up round the big bung, and a dollop slopping over the rim behind it
  const puddle = sphere(1, { segments: 32, rows: 10 }).transform({ scale: [0.07, 0.008, 0.06], translate: [bigP[0], LID_Y + 0.002, bigP[2]] });
  puddle.map(([x, y, z]) => {
    const n = fbm3(x * 30, 0.5, z * 30, { octaves: 2, seed: 71 });
    const dx = x - bigP[0], dz = z - bigP[2];
    const k = 0.8 + 0.5 * n;
    return [bigP[0] + dx * k, Math.max(LID_Y - 0.001, y), bigP[2] + dz * k];
  });
  oozeMesh.append(puddle);
  const oozeTex = new Canvas(256, 256, [120, 240, 40]);
  oozeTex.shade((u, v) => {
    const n = fbm3(u * 12, v * 12, 0.5, { octaves: 3, seed: 81 });
    return mix([70, 200, 30], [190, 255, 90], smooth(0.35, 0.7, n));
  });
  oozeTex.speckle(160, [1, 3], [235, 255, 180], 0.7, 83);
  parts.push({ mesh: oozeMesh, material: { name: 'ooze', texture: oozeTex, roughness: 0.45, emissive: [0.16, 0.42, 0.05] } });

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
      lid: [0, LID_Y + 0.004, 0],
      bung: [bigP[0], LID_Y + capH + 0.004, bigP[2]],
      lidRimFront: [0, H, -(R - 0.002)],
    },
    notes:
      `Drum radius ${(R * s).toFixed(3)} m in app space; the lid sits ${((H - LID_Y) * s).toFixed(3)} m below the chime top. ` +
      `Big bung (oozing) at the front left of the lid, small one at the back right. Hoops at ${HOOPS.map((h) => (h * s).toFixed(2)).join(' / ')} m.`,
  };
}
