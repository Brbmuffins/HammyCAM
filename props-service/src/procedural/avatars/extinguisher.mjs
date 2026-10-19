/**
 * Fire extinguisher: a chunky red cylinder with a domed top in a black rubber boot; a silver valve head on a hex
 * nut, with the squeeze lever over the carry handle (sticking out to screen right), a yellow safety-pin ring and a
 * little pressure gauge (green zone) on its front; a black hose loops off the valve to screen left and down to a
 * flared black nozzle horn clipped to the side of the body. A white "FIRE / A B C" label sits low on the front;
 * the face goes on the plain red above it.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, tube, box, loft, smooth, clamp, lerp, TAU, fbm2, rng } = kit;

  // ---------- helpers ----------
  const textMask = (str, h, weight = 0.17) => {
    const pad = Math.ceil(h * 0.4);
    const m = new Canvas(Math.ceil(str.length * h * 0.95 + pad * 2), Math.ceil(h + pad * 2), [0, 0, 0]);
    m.text(str, m.width / 2, pad, h, [255, 255, 255], { weight });
    return m;
  };
  /** Catmull-Rom through 3D points, n samples per span. */
  const spline3 = (pts, n = 8) => {
    const out = [];
    for (let i = 0; i + 1 < pts.length; i++) {
      const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
      for (let k = 0; k < n; k++) {
        const t = k / n, t2 = t * t, t3 = t2 * t;
        out.push([0, 1, 2].map((d) => 0.5 * (2 * p1[d] + (-p0[d] + p2[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t2 + (-p0[d] + 3 * p1[d] - 3 * p2[d] + p3[d]) * t3)));
      }
    }
    out.push([...pts[pts.length - 1]]);
    return out;
  };
  /** A flat bar (rounded-rectangle section, halfT thick in the path's plane, halfW wide along z) along an XY path. */
  const bar = (path, { z = 0, halfT, halfW, power = 4, segments = 16 }) => {
    const rings = [];
    for (let j = 0; j < path.length; j++) {
      const a = path[Math.max(0, j - 1)], b = path[Math.min(path.length - 1, j + 1)];
      const tx = b[0] - a[0], ty = b[1] - a[1], l = Math.hypot(tx, ty) || 1;
      const nx = -ty / l, ny = tx / l;
      const ht = Array.isArray(halfT) ? halfT[j] : halfT;
      const ring = [];
      for (let i = 0; i <= segments; i++) {
        const th = (TAU * (i % segments)) / segments;
        const c = Math.sign(Math.cos(th)) * Math.abs(Math.cos(th)) ** (2 / power), s = Math.sign(Math.sin(th)) * Math.abs(Math.sin(th)) ** (2 / power);
        ring.push([path[j][0] + nx * ht * c, path[j][1] + ny * ht * c, z + halfW * s]);
      }
      rings.push(ring);
    }
    const m = loft(rings, { capBottom: true, capTop: true });
    // Make it face outward (the ring handedness depends on the path direction).
    const [i0, i1, i2] = m.indices;
    const A = m.point(i0), B = m.point(i1), C = m.point(i2);
    const n = kit.vec.cross(kit.vec.sub(B, A), kit.vec.sub(C, A));
    const mid = [(A[0] + B[0] + C[0]) / 3, (A[1] + B[1] + C[1]) / 3, (A[2] + B[2] + C[2]) / 3];
    const ctr = [(path[0][0] + path[1][0]) / 2, (path[0][1] + path[1][1]) / 2, z];
    if (kit.vec.dot(n, kit.vec.sub(mid, ctr)) < 0) m.flip();
    return m;
  };
  /** A ring (torus) of radius R, tube r, round the axis through centre, in the plane spanned by e1, e2. */
  const torus = (centre, e1, e2, R, r, { segments = 40, tubeSeg = 10 } = {}) => {
    const path = [];
    for (let i = 0; i <= segments; i++) {
      const a = (TAU * i) / segments;
      path.push([0, 1, 2].map((d) => centre[d] + R * (Math.cos(a) * e1[d] + Math.sin(a) * e2[d])));
    }
    return tube(path, r, { segments: tubeSeg });
  };
  const at = (m, translate, rotate = [0, 0, 0]) => m.transform({ rotate, translate });

  // ---------- dimensions ----------
  const R = 0.24; // body radius
  const BOOT = 0.052; // black rubber foot
  const CYL1 = 0.62; // top of the cylinder, where the dome starts
  const DOMEH = 0.115, NECKR = 0.074;
  const LABEL = [0.08, 0.3]; // label band, low on the front
  const LABEL_ANG = 0.86; // half-angle (radians) the label covers round the front
  const HZ = -0.07; // the hose and horn run a little in front of the side

  // ---------- body ----------
  const domeRows = [];
  {
    const aEnd = Math.acos(NECKR / R);
    for (let k = 0; k <= 14; k++) {
      const a = (aEnd * k) / 14;
      domeRows.push([R * Math.cos(a), CYL1 + DOMEH * Math.sin(a)]);
    }
  }
  const neckY = CYL1 + DOMEH * Math.sin(Math.acos(NECKR / R));
  // Rows every couple of centimetres up the cylinder: the app measures the width at the face's height from them.
  const cylRows = [];
  for (let y = BOOT; y < CYL1 - 0.01; y += 0.02) cylRows.push([R, y]);
  const bodyProfile = [[0, BOOT - 0.01], [R - 0.01, BOOT - 0.01], ...cylRows, [R, CYL1 - 0.004], ...domeRows, [NECKR - 0.004, neckY + 0.004], [0, neckY + 0.004]];
  const bodyMesh = lathe(bodyProfile, { segments: 80, v: (t, y) => clamp(1 - y / (neckY + 0.004)) });
  // Paint: glossy-ish fire-engine red, a weld seam where the dome meets the cylinder, faint wear.
  const RED = [206, 34, 30];
  const paint = new Canvas(512, 512, RED);
  paint.shade((u, v) => {
    const n = fbm2(u * 10, v * 10, { octaves: 3, seed: 8 }) - 0.5;
    return RED.map((c) => c * (1 + n * 0.05));
  });
  const vRow = (y) => (1 - y / (neckY + 0.004)) * 512;
  paint.rect(0, vRow(CYL1 + 0.004), 512, vRow(CYL1 - 0.002), [150, 20, 20], 0.7);
  paint.rect(0, vRow(CYL1 - 0.002), 512, vRow(CYL1 - 0.006), [240, 90, 80], 0.35);

  // Rubber boot: a ring round the base with a lip.
  const boot = lathe([[0, 0.004], [R - 0.02, 0.004], [R - 0.008, 0, true], [R + 0.006, 0.002], [R + 0.01, 0.014], [R + 0.01, BOOT - 0.006], [R + 0.006, BOOT], [R - 0.002, BOOT + 0.002], [R - 0.004, BOOT - 0.006]], { segments: 80 });

  // ---------- label: a curved patch on the front ----------
  const LR = R + 0.0016;
  const labelMesh = new Mesh();
  {
    const NU = 40, NV = 6, rows = [];
    for (let j = 0; j <= NV; j++) {
      const y = lerp(LABEL[0], LABEL[1], j / NV), row = [];
      for (let i = 0; i <= NU; i++) {
        const a = -Math.PI / 2 + lerp(LABEL_ANG, -LABEL_ANG, i / NU); // i = 0 at screen left (+X)
        row.push(labelMesh.vertex([LR * Math.cos(a), y, LR * Math.sin(a)], [i / NU, 1 - j / NV]));
      }
      rows.push(row);
    }
    for (let j = 0; j < NV; j++) for (let i = 0; i < NU; i++) labelMesh.quad(rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]);
    kit.outward(labelMesh, [0, (LABEL[0] + LABEL[1]) / 2, 0]);
  }
  const LWpx = 768, LHpx = Math.round((768 * (LABEL[1] - LABEL[0])) / (2 * LABEL_ANG * LR));
  const lab = new Canvas(LWpx, LHpx, RED);
  {
    const W = LWpx, H = LHpx, m = 10, rad = 34;
    const roundRect = (x0, y0, x1, y1, r, col) => {
      lab.rect(x0 + r, y0, x1 - r, y1, col).rect(x0, y0 + r, x1, y1 - r, col);
      for (const [cx, cy] of [[x0 + r, y0 + r], [x1 - r, y0 + r], [x0 + r, y1 - r], [x1 - r, y1 - r]]) lab.disc(cx, cy, r, col);
    };
    roundRect(m, m, W - m, H - m, rad, [250, 250, 246]);
    // A red header with FIRE in white, a thin black rule under it.
    roundRect(m + 14, m + 14, W - m - 14, m + 132, 22, [214, 36, 30]);
    lab.text('FIRE', W / 2, m + 32, 82, [255, 255, 255], { weight: 0.2 });
    // Little flame pictograms either side of the word.
    const flame = (cx, cy, s, col) => {
      const pts = [];
      for (let k = 0; k <= 40; k++) {
        const t = (k / 40) * TAU;
        // a teardrop with a flick: round bottom, pointed top leaning right
        const r = s * (0.5 + 0.5 * Math.pow(Math.max(0, Math.sin(t / 2)), 0.6));
        const yy = -Math.cos(t) * s * 0.95, xx = Math.sin(t) * r * 0.62;
        pts.push([cx + xx + (yy < 0 ? -yy * 0.18 : 0), cy + yy * (yy < 0 ? 1.15 : 0.55)]);
      }
      lab.poly(pts, col);
    };
    for (const cx of [W / 2 - 196, W / 2 + 196]) {
      flame(cx, m + 86, 54, [255, 255, 255]);
      flame(cx - 30, m + 104, 26, [255, 255, 255]);
      flame(cx + 3, m + 100, 28, [255, 196, 40]);
    }
    lab.rect(m + 30, m + 146, W - m - 30, m + 151, [30, 30, 34]);
    // Classes: green triangle A, red square B, blue circle C.
    const cy = m + 238, s = 74;
    const icon = (cx, kind) => {
      const col = kind === 'A' ? [40, 160, 70] : kind === 'B' ? [214, 40, 34] : [36, 96, 200];
      if (kind === 'A') lab.poly([[cx, cy - s * 0.62], [cx + s * 0.66, cy + s * 0.5], [cx - s * 0.66, cy + s * 0.5]], col);
      else if (kind === 'B') roundRect(cx - s * 0.52, cy - s * 0.52, cx + s * 0.52, cy + s * 0.52, 8, col);
      else lab.disc(cx, cy, s * 0.56, col);
      lab.text(kind, cx, cy - s * 0.27 + (kind === 'A' ? s * 0.12 : 0), s * 0.5, [255, 255, 255], { weight: 0.22 });
    };
    icon(W / 2 - 170, 'A');
    icon(W / 2, 'B');
    icon(W / 2 + 170, 'C');
    // Instructions in small type and a row of grey "fine print" lines.
    lab.text('PULL - AIM - SQUEEZE', W / 2, m + 300, 30, [30, 30, 34], { weight: 0.17 });
    for (let k = 0; k < 2; k++) lab.rect(m + 70, m + 350 + k * 16, W - m - 70 - k * 120, m + 357 + k * 16, [170, 170, 176]);
  }

  // ---------- valve head ----------
  const SILVER = { name: 'metal', color: [0.76, 0.77, 0.8], roughness: 0.38, metallic: 0.85 };
  const BLACK = { name: 'rubber', color: [0.006, 0.006, 0.007], roughness: 0.9 };
  const hex = (t, a, r) => {
    const k = r / Math.cos(((((a % (Math.PI / 3)) + Math.PI / 3) % (Math.PI / 3)) - Math.PI / 6));
    return [k * Math.cos(a), k * Math.sin(a)];
  };
  const nut = lathe([[0, neckY - 0.01], [0.074, neckY - 0.01, true], [0.074, neckY + 0.022, true], [0.066, neckY + 0.026, true], [0, neckY + 0.026]], { segments: 48, section: hex });
  const VY0 = neckY + 0.024, VH = 0.104, VW = 0.176, VD = 0.124;
  const valveBody = at(box(VW, VH, VD, { round: 0.03, steps: 3 }), [0, VY0 + VH / 2, 0]);
  const boss = at(box(0.08, 0.044, 0.086, { round: 0.016, steps: 2 }), [0.022, VY0 + VH + 0.012, 0]);
  const valveTop = VY0 + VH + 0.034;

  // ---------- hose and nozzle horn (screen left, +X) ----------
  const HORN_TOP = [0.262, 0.53, HZ], HORN_MOUTH = [0.294, 0.335, HZ];
  const hoseR = 0.021;
  const hosePath = spline3([
    [VW / 2 + 0.02, VY0 + 0.045, 0], [0.13, VY0 + 0.04, -0.008], [0.21, VY0 + 0.008, -0.022], [0.268, 0.7, -0.034],
    [0.272, 0.62, HZ], [HORN_TOP[0] - 0.001, HORN_TOP[1] + 0.03, HZ], [HORN_TOP[0], HORN_TOP[1] + 0.004, HZ],
  ], 8);
  const hose = tube(hosePath, hoseR, { segments: 14 });
  const outlet = at(lathe([[0, 0], [0.026, 0, true], [0.026, 0.05, true], [0.03, 0.052], [0.03, 0.066, true], [0, 0.066]], { segments: 24 }), [VW / 2 - 0.022, VY0 + 0.045, 0], [0, 0, -90]);
  // Horn: a flared cone, hollow at the mouth, built mouth-down along +Y then tilted onto its axis.
  const hornLen = Math.hypot(HORN_TOP[0] - HORN_MOUTH[0], HORN_TOP[1] - HORN_MOUTH[1]);
  const hornProfile = [];
  hornProfile.push([0, hornLen - 0.03]);
  for (let k = 8; k >= 0; k--) {
    const s = k / 8;
    hornProfile.push([0.015 + 0.033 * Math.pow(1 - s, 1.7), 0.006 + s * (hornLen - 0.04)]);
  }
  hornProfile.push([0.053, 0, true], [0.058, 0.004], [0.059, 0.014, true]);
  for (let k = 1; k <= 10; k++) {
    const s = k / 10;
    hornProfile.push([0.024 + 0.033 * Math.pow(1 - s, 1.7), 0.014 + s * (hornLen - 0.014)]);
  }
  hornProfile.push([0.028, hornLen, true], [0.028, hornLen + 0.022, true], [0, hornLen + 0.022]);
  const tilt = (Math.atan2(HORN_MOUTH[0] - HORN_TOP[0], HORN_TOP[1] - HORN_MOUTH[1]) * 180) / Math.PI;
  const horn = at(lathe(hornProfile, { segments: 36 }), HORN_MOUTH, [0, 0, tilt]);
  // Clip: a band round the horn with a little bracket back to the body.
  const clipY = 0.445;
  const clipT = (HORN_TOP[1] - clipY) / (HORN_TOP[1] - HORN_MOUTH[1]);
  const clipX = lerp(HORN_TOP[0], HORN_MOUTH[0], clipT);
  const hornRAt = 0.024 + 0.033 * Math.pow(clipT, 1.7);
  const clipBand = at(lathe([[hornRAt + 0.002, -0.016], [hornRAt + 0.008, -0.014, true], [hornRAt + 0.008, 0.014, true], [hornRAt + 0.002, 0.016]], { segments: 36 }), [clipX, clipY, HZ], [0, 0, tilt]);
  const bracket = at(box(0.05, 0.036, 0.045, { round: 0.008, steps: 2 }), [Math.sqrt(R * R - HZ * HZ) + 0.01, clipY, HZ * 0.95]);

  // ---------- lever and carry handle (screen right, -X), reaching as far out as the horn ----------
  const sideMax = Math.max(...[hose, horn, clipBand, bracket].map((m) => m.bounds().max[0]));
  // The squeeze lever rises from its pivot over the level carry handle (the classic open "V"); both end in a
  // little downturn. Their length is trimmed so they reach exactly as far as the horn side, keeping the body
  // in the middle of the bounds (the app centres the face there).
  const leverY = VY0 + VH + 0.03, RISE = 0.05, handleY = VY0 + 0.028;
  const levers = (L) => {
    const leverPath = spline3([[0.06, leverY - 0.004], [-0.03, leverY + 0.004], [-0.14, leverY + RISE * 0.5], [-(L - 0.06), leverY + RISE], [-(L - 0.012), leverY + RISE - 0.012], [-L, leverY + RISE - 0.036]], 6);
    const handlePath = spline3([[-0.06, handleY], [-0.13, handleY + 0.002], [-(L - 0.05), handleY + 0.002], [-(L - 0.008), handleY - 0.01], [-L, handleY - 0.032]], 6);
    const gripPath = spline3([[-0.13, handleY + 0.002], [-(L - 0.075), handleY + 0.002], [-(L - 0.035), handleY - 0.001]], 5);
    return {
      lever: bar(leverPath, { halfT: 0.016, halfW: 0.036 }),
      handle: bar(handlePath, { halfT: 0.014, halfW: 0.032 }),
      grip: bar(gripPath, { halfT: 0.021, halfW: 0.037, power: 3 }),
    };
  };
  let lv = levers(sideMax);
  const overshoot = -Math.min(...Object.values(lv).map((m) => m.bounds().min[0])) - sideMax;
  lv = levers(sideMax - overshoot);
  const { lever, handle, grip } = lv;

  // ---------- safety pin and ring (front, screen right of the gauge) ----------
  const PIN = [-0.04, VY0 + VH - 0.022];
  const pin = at(lathe([[0, 0], [0.0055, 0, true], [0.0055, 0.15, true], [0, 0.15]], { segments: 12 }), [PIN[0], PIN[1], 0.072], [-90, 0, 0]);
  const ringR = 0.03;
  const ring = torus([PIN[0] - 0.004, PIN[1] - ringR + 0.006, -VD / 2 - 0.026], [1, 0, 0], [0, 0.94, -0.34], ringR, 0.0065);
  const tag = at(box(0.012, 0.03, 0.006, { round: 0.003, steps: 1 }), [PIN[0] + 0.004, PIN[1] - 0.06, -VD / 2 - 0.03], [0, 0, 18]);

  // ---------- pressure gauge (front, screen left) ----------
  const G = [0.036, VY0 + VH * 0.47, -VD / 2], GR = 0.037;
  const bezel = at(lathe([[0, -0.002], [GR, -0.002, true], [GR + 0.003, 0.006], [GR + 0.001, 0.016, true], [GR - 0.004, 0.016, true], [GR - 0.005, 0.012, true], [0, 0.012]], { segments: 40 }), G, [-90, 0, 0]);
  const dial = new Mesh();
  {
    const c = dial.vertex([G[0], G[1], G[2] - 0.0125], [0.5, 0.5]);
    const rim = [];
    for (let i = 0; i <= 40; i++) {
      const a = (TAU * i) / 40, x = Math.cos(a), y = Math.sin(a);
      rim.push(dial.vertex([G[0] + x * (GR - 0.005), G[1] + y * (GR - 0.005), G[2] - 0.0125], [0.5 - x * 0.5, 0.5 - y * 0.5]));
    }
    for (let i = 0; i < 40; i++) dial.tri(c, rim[i], rim[i + 1]);
    kit.outward(dial, [G[0], G[1], 0]);
  }
  const gface = new Canvas(256, 256, [248, 248, 244]);
  {
    const C = 128, arc = (r0, r1, a0, a1, col) => {
      const pts = [];
      for (let k = 0; k <= 24; k++) pts.push([C + r1 * Math.cos(lerp(a0, a1, k / 24)), C - r1 * Math.sin(lerp(a0, a1, k / 24))]);
      for (let k = 24; k >= 0; k--) pts.push([C + r0 * Math.cos(lerp(a0, a1, k / 24)), C - r0 * Math.sin(lerp(a0, a1, k / 24))]);
      gface.poly(pts, col);
    };
    // Screen left of the dial is u = 0 (texture x = 0): angles measured as usual, up = +y on the image's top.
    arc(78, 110, deg(200), deg(140), [220, 50, 40]); // low: red
    arc(78, 110, deg(140), deg(40), [50, 170, 70]); // ok: green
    arc(78, 110, deg(40), deg(-20), [220, 50, 40]); // over: red
    for (let k = 0; k <= 10; k++) {
      const a = deg(lerp(200, -20, k / 10));
      gface.stroke([[C + 70 * Math.cos(a), C - 70 * Math.sin(a)], [C + 112 * Math.cos(a), C - 112 * Math.sin(a)]], 5, [30, 30, 34]);
    }
    const na = deg(78);
    gface.stroke([[C - 18 * Math.cos(na), C + 18 * Math.sin(na)], [C + 100 * Math.cos(na), C - 100 * Math.sin(na)]], 9, [30, 30, 34]);
    gface.disc(C, C, 16, [30, 30, 34]).disc(C, C, 6, [200, 200, 205]);
    gface.disc(C, C, 126, [40, 40, 44], 0, 0); // (keeps the edge clean)
  }
  function deg(d) {
    return (d * Math.PI) / 180;
  }

  const solid = (rgb) => new Canvas(8, 8, rgb);
  const parts = [
    { mesh: bodyMesh, material: { name: 'redPaint', texture: paint, roughness: 0.62 } },
    { mesh: labelMesh, material: { name: 'label', texture: lab, roughness: 0.85 } },
    { mesh: boot, material: BLACK },
    { mesh: nut, material: SILVER },
    { mesh: valveBody, material: SILVER },
    { mesh: boss, material: SILVER },
    { mesh: lever, material: SILVER },
    { mesh: handle, material: SILVER },
    { mesh: outlet, material: SILVER },
    { mesh: pin, material: SILVER },
    { mesh: bezel, material: SILVER },
    { mesh: grip, material: BLACK },
    { mesh: hose, material: BLACK },
    { mesh: horn, material: BLACK },
    { mesh: clipBand, material: BLACK },
    { mesh: bracket, material: BLACK },
    { mesh: ring, material: { name: 'pinRing', texture: solid([255, 204, 0]), roughness: 0.7 } },
    { mesh: tag, material: { name: 'pinRing', texture: solid([255, 204, 0]), roughness: 0.7 } },
    { mesh: dial, material: { name: 'gauge', texture: gface, roughness: 0.6 } },
  ];

  // Nozzle: the horn's mouth, and a point up its axis (tip - back = the way it points: down, a touch outward).
  const up = [HORN_TOP[0] - HORN_MOUTH[0], HORN_TOP[1] - HORN_MOUTH[1]];
  const ul = Math.hypot(up[0], up[1]);
  return {
    parts,
    face: 0.5,
    faceSize: 0.74,
    anchors: {
      nozzle: HORN_MOUTH,
      nozzleBack: [HORN_MOUTH[0] + (up[0] / ul) * 0.08, HORN_MOUTH[1] + (up[1] / ul) * 0.08, HZ],
      valve: [0, valveTop, 0],
      gauge: [G[0], G[1], G[2] - 0.0125],
    },
    notes:
      'Nozzle horn hangs mouth-down, clipped to the screen-left side (nozzle = mouth, nozzleBack up its axis). Lever + carry handle stick out to ' +
      'screen right (they balance the horn so the body stays centred). Yellow pin ring on the valve front; gauge face at "gauge".',
  };
}
