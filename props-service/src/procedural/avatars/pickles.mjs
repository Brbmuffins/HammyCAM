/**
 * Pickle jar: a chunky round glass jar (thick base with a push-up, rounded shoulder, a bead under the lid) with a wide
 * green screw lid (rolled bottom curl, knurled grip ridges, a rounded top edge and the little vacuum button on top),
 * full of cloudy yellow-green brine. Low down in the brine: a couple of dill fronds and a dill flower head, garlic
 * cloves, peppercorns, mustard seeds and one red chilli. A cream paper label low on the front reads "DILL PICKLES"
 * with a cartoon pickle and a dill sprig. No pickles here: the app adds its own floating ones.
 * The face goes on the clear upper front, above the label.
 * Modelled at about app scale (1.15 m tall), so model units are roughly the app's metres.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, tube, sphere, outward, rng, fbm2, fbm3, smooth, lerp, clamp, TAU } = kit;

  // ---- key dimensions -------------------------------------------------------------------------------------
  const R = 0.33; // jar body radius (outside)
  const BODY_TOP = 0.79, SH = 0.09; // straight wall up to BODY_TOP, then a rounded shoulder SH tall into the neck
  const NR = 0.282; // neck radius
  const NECK_Y = BODY_TOP + SH;
  const T = 0.009; // glass thickness (glass to brine)
  const FLOOR = 0.032; // inside floor (thick glass base)
  const BRINE_TOP = 0.852;
  const LABEL = [0.095, 0.4]; // label band heights
  const LABEL_HALF = (74 * Math.PI) / 180; // half its arc round the front
  const FACE_Y = 0.635;
  /** Outer glass radius at height y (wall and shoulder). */
  const jarR = (y) => {
    if (y <= BODY_TOP) return R;
    const t = clamp((y - BODY_TOP) / SH);
    return NR + (R - NR) * Math.sqrt(Math.max(0, 1 - t * t));
  };
  const inR = (y) => jarR(y) - T;

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });

  // ---- materials ------------------------------------------------------------------------------------------
  const glass = { name: 'glass', color: [0.85, 0.95, 1, 0.25], blend: true, roughness: 0.05 };
  const lidMat = { name: 'lid', color: [0.03, 0.2, 0.06], metallic: 0.35, roughness: 0.38 };

  // Brine: yellow-green, a little cloudier and more golden low down where the spices settle.
  const brineTex = new Canvas(64, 512, [210, 218, 110]);
  brineTex.shade((u, v) => {
    const top = [196, 228, 64], low = [204, 204, 44];
    return top.map((x, i) => lerp(x, low[i], smooth(0.2, 1, v)));
  });
  const brine = { name: 'brine', texture: brineTex, color: [1, 1, 1, 0.48], blend: true, doubleSided: true, emissive: [0.05, 0.07, 0], roughness: 0.12 };

  // One palette texture for everything floating in the brine (each part's uvs point at one swatch).
  const PAL = {
    dill: [78, 128, 44], dillDark: [48, 92, 30], dillHead: [204, 190, 72], stem: [104, 140, 58],
    pepper: [50, 34, 28], pepperHi: [92, 66, 52], garlic: [242, 234, 212], garlicTip: [212, 172, 146],
    mustard: [218, 172, 72], chili: [204, 32, 24], chiliDark: [150, 18, 16], chiliStem: [86, 124, 44],
  };
  const palTex = new Canvas(64, 64, [255, 0, 255]);
  const swatch = {};
  Object.entries(PAL).forEach(([k, c], i) => {
    const x = (i % 4) * 16, y = Math.floor(i / 4) * 16;
    palTex.rect(x, y, x + 16, y + 16, c);
    swatch[k] = [(x + 8) / 64, (y + 8) / 64];
  });
  const pickling = { name: 'pickling', texture: palTex, roughness: 0.75 };
  const paint = (mesh, key) => {
    for (let i = 0; i < mesh.vertexCount; i++) [mesh.uvs[i * 2], mesh.uvs[i * 2 + 1]] = swatch[key];
    return mesh;
  };

  // ---- the label texture ----------------------------------------------------------------------------------
  const LW = 1024, LH = 384;
  const lab = new Canvas(LW, LH, [246, 236, 206]);
  lab.shade((u, v) => {
    const n = fbm2(u * 8, v * 3, { octaves: 4, seed: 12 });
    const k = 0.96 + 0.07 * (n - 0.5) - 0.05 * smooth(0.6, 1, Math.abs(u - 0.5) * 2); // older, darker toward the ends
    return [246 * k, 236 * k, 204 * k];
  });
  lab.speckle(700, [0.5, 1.3], [170, 150, 110], 0.25, 4);
  const RED = [196, 44, 36], GREEN = [40, 104, 44], DARK = [30, 52, 26], CREAM = [250, 242, 214];
  // Border: a red frame with a thin green line inside it, and a red banner across the top.
  lab.rect(0, 0, LW, 16, RED).rect(0, LH - 16, LW, LH, RED);
  lab.rect(26, 26, LW - 26, 30, GREEN).rect(26, LH - 30, LW - 26, LH - 26, GREEN);
  lab.rect(26, 26, 30, LH - 26, GREEN).rect(LW - 30, 26, LW - 26, LH - 26, GREEN);
  // Banner with notched ends, small text in it.
  const bx0 = 300, bx1 = 724, by0 = 44, by1 = 96;
  lab.poly([[bx0 - 40, by0 + 6], [bx0, by0], [bx1, by0], [bx1 + 40, by0 + 6], [bx1 + 22, (by0 + by1) / 2 + 3], [bx1 + 40, by1 + 6], [bx1, by1], [bx0, by1], [bx0 - 40, by1 + 6], [bx0 - 22, (by0 + by1) / 2 + 3]], [150, 30, 26]);
  lab.rect(bx0, by0, bx1, by1, RED);
  lab.text('EXTRA CRUNCHY', 512, 58, 26, CREAM, { weight: 0.18, spacing: 0.3 });
  // The name, with a dark drop shadow and a thick cream outline (sign-painter style).
  const title = (str, y, h) => {
    lab.text(str, 512 + 5, y + 6, h, DARK, { weight: 0.3, spacing: 0.2, a: 0.5 });
    lab.text(str, 512, y, h, CREAM, { weight: 0.34, spacing: 0.2 });
    lab.text(str, 512, y, h, GREEN, { weight: 0.19, spacing: 0.2 });
  };
  title('DILL', 118, 84);
  title('PICKLES', 228, 92);
  // A cartoon pickle on the left: a fat bumpy green gherkin with warts, a shine and a darker belly.
  const pickle = (cx, cy, len, wid, ang) => {
    const pts = [];
    for (let k = 0; k < 48; k++) {
      const t = (TAU * k) / 48;
      const bump = 1 + 0.06 * Math.sin(t * 9);
      const x = Math.cos(t) * len * 0.5, y = Math.sin(t) * wid * 0.5 * bump * (1 - 0.18 * Math.cos(t));
      pts.push([cx + x * Math.cos(ang) - y * Math.sin(ang), cy + x * Math.sin(ang) + y * Math.cos(ang)]);
    }
    lab.poly(pts.map(([x, y]) => [x + 4, y + 6]), DARK, 0.4);
    lab.poly(pts, [64, 132, 50]);
    lab.stroke([...pts, pts[0]], 5, DARK);
    const at = (s, o) => [cx + s * len * 0.5 * Math.cos(ang) - o * wid * 0.5 * Math.sin(ang), cy + s * len * 0.5 * Math.sin(ang) + o * wid * 0.5 * Math.cos(ang)];
    lab.stroke([at(-0.6, 0.35), at(0, 0.5), at(0.6, 0.35)], 9, [40, 96, 36], 0.6); // belly shadow
    lab.stroke([at(-0.55, -0.42), at(-0.1, -0.55), at(0.35, -0.45)], 7, [190, 230, 150], 0.85); // shine
    const W = rng(3);
    for (let k = 0; k < 11; k++) {
      const p = at(W() * 1.5 - 0.75, W() * 1.1 - 0.55);
      lab.disc(p[0], p[1], 4 + W() * 2, [40, 92, 34]).disc(p[0] - 1, p[1] - 1.5, 1.6, [150, 200, 110]);
    }
    lab.stroke([at(1.0, 0), at(1.12, -0.08)], 6, DARK); // the stalk end
  };
  pickle(168, 214, 210, 92, -0.42);
  // A dill sprig on the right: a stem fanning out into fine fronds, and a little umbrella of seeds.
  const dillArt = (x0, y0) => {
    const tip = [x0 + 30, y0 - 150];
    lab.stroke([[x0, y0], [x0 + 12, y0 - 70], tip], 5, GREEN);
    for (let k = 0; k < 9; k++) {
      const a = -Math.PI / 2 + (k - 4) * 0.27;
      const p = [tip[0] + Math.cos(a) * 62, tip[1] + 40 + Math.sin(a) * 62];
      lab.stroke([[tip[0], tip[1] + 40], p], 3, GREEN);
      lab.disc(p[0], p[1], 5, [196, 170, 54]).disc(p[0], p[1], 2.5, [230, 210, 90]);
    }
    for (let k = 0; k < 6; k++) {
      const yy = y0 - 22 - k * 14, side = k % 2 ? 1 : -1;
      const base = [x0 + 12 * (1 - (y0 - yy) / 120) + 6, yy];
      for (let f = 0; f < 4; f++) lab.stroke([base, [base[0] + side * (28 + f * 6), yy - 18 + f * 10]], 2.5, [62, 128, 48]);
    }
  };
  dillArt(846, 330);
  lab.text('NO. 9', 512, 330, 22, RED, { weight: 0.18, spacing: 0.3 });
  const labelMat = { name: 'label', texture: lab, roughness: 0.9 };
  const labelBack = { name: 'labelBack', color: [0.82, 0.78, 0.62], roughness: 0.95 };

  // ---- the jar --------------------------------------------------------------------------------------------
  const jar = [[0, 0.012], [0.12, 0.009], [R - 0.07, 0.004], [R - 0.045, 0, true], [R - 0.02, 0.002], [R - 0.006, 0.01], [R, 0.024]];
  for (let y = 0.04; y < BODY_TOP - 0.001; y += y < 0.36 ? 0.04 : 0.02) jar.push([R, y]); // finer where the face goes
  for (let k = 0; k <= 10; k++) {
    const y = BODY_TOP + (SH * k) / 10;
    jar.push([jarR(y), y]);
  }
  // a bead under the lid, the neck (hidden by the lid) and its lip
  jar.push([NR + 0.004, NECK_Y + 0.004], [NR + 0.01, NECK_Y + 0.009], [NR + 0.011, NECK_Y + 0.014], [NR + 0.006, NECK_Y + 0.019], [NR, NECK_Y + 0.022], [NR, 0.975], [NR - 0.006, 0.981], [NR - 0.012, 0.975], [NR - 0.013, 0.955]);
  add(lathe(jar, { segments: 80 }), glass);

  // ---- the brine ------------------------------------------------------------------------------------------
  const brineProf = [[0, FLOOR], [inR(0.05) - 0.03, FLOOR], [inR(0.05) - 0.01, FLOOR + 0.004], [inR(0.05) - 0.002, FLOOR + 0.014]];
  for (let y = FLOOR + 0.03; y < BRINE_TOP - 0.004; y += y < 0.36 ? 0.04 : 0.02) brineProf.push([inR(y), y]);
  brineProf.push([inR(BRINE_TOP - 0.003), BRINE_TOP - 0.003], [inR(BRINE_TOP) - 0.001, BRINE_TOP + 0.002, true], [inR(BRINE_TOP) - 0.012, BRINE_TOP], [inR(BRINE_TOP) * 0.6, BRINE_TOP - 0.001], [0, BRINE_TOP - 0.001]);
  add(lathe(brineProf, { segments: 80, v: (t, y) => clamp((BRINE_TOP - y) / (BRINE_TOP - FLOOR)) }), brine);

  // ---- the lid --------------------------------------------------------------------------------------------
  const LR = NR + 0.024, L0 = NECK_Y + 0.026; // lid radius, the bottom of its curl
  const K0 = L0 + 0.02, K1 = 1.002; // the knurled band
  const RIDGES = 56;
  const lidProf = [
    [NR + 0.004, L0 + 0.02], [NR + 0.008, L0 + 0.008], [LR - 0.012, L0], [LR - 0.003, L0 + 0.001], [LR + 0.003, L0 + 0.006], [LR + 0.004, L0 + 0.012], [LR + 0.001, L0 + 0.017, true],
    [LR, K0, true],
  ];
  for (let y = K0 + 0.012; y < K1 - 0.004; y += 0.012) lidProf.push([LR, y]);
  lidProf.push([LR, K1, true], [LR + 0.002, K1 + 0.004], [LR + 0.002, K1 + 0.011]);
  const lidTop = [
    [LR + 0.002, K1 + 0.011], [LR - 0.003, K1 + 0.017], [LR - 0.011, K1 + 0.02], [LR - 0.019, K1 + 0.019, true],
    [LR - 0.026, K1 + 0.014], [LR - 0.034, K1 + 0.012, true], [0.1, K1 + 0.012], [0.09, K1 + 0.01, true], [0.082, K1 + 0.013], [0.07, K1 + 0.019], [0.045, K1 + 0.023], [0, K1 + 0.024],
  ];
  const knurl = (t, a, r, y) => {
    const on = y > K0 + 0.004 && y < K1 - 0.004 ? 1 : 0;
    const k = 1 + on * 0.017 * (0.5 + 0.5 * Math.cos(RIDGES * a)) ** 1.5;
    return [r * k * Math.cos(a), r * k * Math.sin(a)];
  };
  add(lathe(lidProf, { segments: RIDGES * 3, section: knurl }), lidMat);
  add(lathe(lidTop, { segments: 84 }), lidMat);
  const LID_TOP = K1 + 0.024;

  // ---- the label (low on the front) ----------------------------------------------------------------------
  /** A strip of a cylinder round the front: u runs screen left to right, v top to bottom. */
  const band = (r, y0, y1, half, nA = 48, nY = 8) => {
    const m = new Mesh();
    const idx = [];
    for (let j = 0; j <= nY; j++)
      for (let i = 0; i <= nA; i++) {
        const u = i / nA, y = lerp(y1, y0, j / nY);
        const a = -Math.PI / 2 + half * (1 - 2 * u); // u = 0 on the screen-left (+X) side
        idx.push(m.vertex([r * Math.cos(a), y, r * Math.sin(a)], [u, j / nY]));
      }
    for (let j = 0; j < nY; j++)
      for (let i = 0; i < nA; i++) {
        const p = idx[j * (nA + 1) + i], q = idx[j * (nA + 1) + i + 1], s = idx[(j + 1) * (nA + 1) + i + 1], w = idx[(j + 1) * (nA + 1) + i];
        m.quad(p, q, s, w);
      }
    return outward(m, [0, (y0 + y1) / 2, 0]);
  };
  add(band(R + 0.0018, LABEL[0], LABEL[1], LABEL_HALF, 64, 10), labelMat);
  add(band(R + 0.0009, LABEL[0], LABEL[1], LABEL_HALF, 32, 4).flip(), labelBack); // its back, seen through the jar

  // ---- what floats in the brine ---------------------------------------------------------------------------
  const stuff = new Mesh();
  const S = rng(23);
  const floorAt = FLOOR + 0.002;
  const inside = (x, z, margin) => Math.hypot(x, z) < inR(0.1) - margin;
  // Keep the spices off the very front-middle (the label hides it anyway) and spread round the floor.
  const spot = (margin) => {
    for (let k = 0; k < 100; k++) {
      const a = S() * TAU, rr = Math.sqrt(S()) * (inR(0.1) - margin);
      const x = rr * Math.cos(a), z = rr * Math.sin(a);
      if (inside(x, z, margin)) return [x, z];
    }
    return [0, 0];
  };
  // Peppercorns: wrinkly little dark balls.
  for (let k = 0; k < 20; k++) {
    const [x, z] = spot(0.03), r = 0.011 + 0.003 * S();
    const pc = sphere(r, { segments: 10, rows: 6 });
    pc.map(([px, py, pz]) => {
      const w = 1 + 0.12 * (fbm3(px * 300 + k, py * 300, pz * 300, { octaves: 2, seed: 5 }) - 0.5);
      return [px * w, py * w, pz * w];
    });
    stuff.append(paint(pc.transform({ translate: [x, floorAt + r * 0.9 + S() * 0.02, z] }), k % 3 ? 'pepper' : 'pepperHi'));
  }
  // Mustard seeds: tiny golden beads.
  for (let k = 0; k < 28; k++) {
    const [x, z] = spot(0.02), r = 0.0055;
    stuff.append(paint(sphere(r, { segments: 6, rows: 3 }).transform({ translate: [x, floorAt + r + S() * 0.012, z] }), 'mustard'));
  }
  // Garlic cloves: plump crescents with a pointed, browner tip.
  const clove = (len, rot, at) => {
    const c = sphere(1, { segments: 14, rows: 10 }).map(([x, y, z]) => {
      const t = (y + 1) / 2; // 0 at the root end, 1 at the tip
      const taper = Math.sin(Math.PI * (0.12 + 0.88 * t)) ** 0.7 * (1 - 0.35 * t);
      const bend = 0.35 * (t - 0.5) ** 2;
      return [x * 0.45 * taper * len, y * 0.5 * len, (z * 0.38 * taper - bend) * len];
    });
    // the tip half browner
    for (let i = 0; i < c.vertexCount; i++) [c.uvs[i * 2], c.uvs[i * 2 + 1]] = swatch[c.point(i)[1] > len * 0.36 ? 'garlicTip' : 'garlic'];
    return c.transform({ rotate: rot, translate: at });
  };
  stuff.append(clove(0.072, [80, 20, 0], [0.12, floorAt + 0.022, 0.15]));
  stuff.append(clove(0.064, [70, 130, 10], [-0.17, floorAt + 0.02, 0.1]));
  stuff.append(clove(0.06, [95, -40, -15], [0.05, floorAt + 0.034, -0.13]));
  stuff.append(clove(0.066, [60, 200, 0], [-0.06, floorAt + 0.05, 0.2]));
  // A red chilli lying across the back.
  {
    const path = [];
    for (let k = 0; k <= 16; k++) {
      const t = k / 16;
      path.push([lerp(0.2, -0.04, t), floorAt + 0.07 + 0.05 * Math.sin(t * 2.2) - 0.03 * t, lerp(0.13, 0.25, t) + 0.03 * Math.sin(t * 3)]);
    }
    const radii = path.map((_, k) => 0.016 * Math.sin(Math.PI * (0.5 + 0.5 * (k / 16))) ** 0.6 + 0.0015);
    stuff.append(paint(tube(path, 0.016, { segments: 12, radii }), 'chili'));
    const p0 = path[0], d = [path[0][0] - path[1][0], path[0][1] - path[1][1], path[0][2] - path[1][2]];
    const l = Math.hypot(...d);
    stuff.append(paint(tube([p0, [p0[0] + (d[0] / l) * 0.01, p0[1] + (d[1] / l) * 0.01 + 0.004, p0[2] + (d[2] / l) * 0.01], [p0[0] + (d[0] / l) * 0.022, p0[1] + 0.016, p0[2] + (d[2] / l) * 0.02]], 0.007, { segments: 8, radii: [0.012, 0.006, 0.004] }), 'chiliStem'));
  }
  // Dill: fronds (a curving stem with branchlets of fine thread-like leaves) and one flower head (an umbrella of
  // little stalks ending in seed clusters).
  const DL = rng(31);
  const frond = (x0, z0, h, lean, dirA, seed) => {
    const R1 = rng(seed);
    const stem = [];
    for (let k = 0; k <= 10; k++) {
      const t = k / 10;
      stem.push([x0 + Math.cos(dirA) * lean * t * t, floorAt + 0.01 + h * t, z0 + Math.sin(dirA) * lean * t * t]);
    }
    stuff.append(paint(tube(stem, 0.004, { segments: 6, radii: stem.map((_, k) => 0.0045 * (1 - 0.5 * (k / 10))) }), 'stem'));
    for (let k = 3; k <= 10; k++) {
      const p = stem[k], side = (k % 2 ? 1 : -1) * (1.2 + R1() * 0.6);
      const a = dirA + side, len = 0.05 * (1 - 0.4 * (k / 10)) + 0.012 * R1();
      const tip = [p[0] + Math.cos(a) * len, p[1] + len * 0.6, p[2] + Math.sin(a) * len];
      stuff.append(paint(tube([p, [(p[0] + tip[0]) / 2, (p[1] + tip[1]) / 2 + 0.004, (p[2] + tip[2]) / 2], tip], 0.0018, { segments: 4 }), 'dill'));
      for (let f = 0; f < 5; f++) {
        const q = f / 4;
        const b = [lerp(p[0], tip[0], 0.3 + 0.7 * q), lerp(p[1], tip[1], 0.3 + 0.7 * q), lerp(p[2], tip[2], 0.3 + 0.7 * q)];
        const fa = a + (f % 2 ? 0.9 : -0.9) + (R1() - 0.5) * 0.4, fl = 0.022 + 0.01 * R1();
        stuff.append(paint(tube([b, [b[0] + Math.cos(fa) * fl, b[1] + fl * (0.4 + R1() * 0.5), b[2] + Math.sin(fa) * fl]], 0.0012, { segments: 4 }), f % 2 ? 'dill' : 'dillDark'));
      }
    }
  };
  frond(0.2, 0.12, 0.27, 0.06, 2.4, 41); // left, toward the back
  frond(-0.19, 0.16, 0.24, 0.07, 0.6, 43); // right, toward the back
  frond(0.0, 0.19, 0.3, 0.04, 1.7, 47); // middle back
  {
    // the flower head, leaning back-left
    const base = [0.1, floorAt + 0.01, 0.17], top = [0.13, floorAt + 0.33, 0.16];
    stuff.append(paint(tube([base, [0.11, floorAt + 0.18, 0.172], top], 0.004, { segments: 6 }), 'stem'));
    for (let k = 0; k < 14; k++) {
      const a = (TAU * k) / 14 + DL() * 0.3, spread = 0.055 + 0.015 * DL();
      const end = [top[0] + Math.cos(a) * spread, top[1] + 0.03 + 0.012 * DL(), top[2] + Math.sin(a) * spread];
      stuff.append(paint(tube([top, [lerp(top[0], end[0], 0.5), top[1] + 0.028, lerp(top[2], end[2], 0.5)], end], 0.0016, { segments: 4 }), 'stem'));
      for (let c = 0; c < 3; c++)
        stuff.append(paint(sphere(0.0055, { segments: 6, rows: 3 }).transform({ translate: [end[0] + (DL() - 0.5) * 0.012, end[1] + DL() * 0.008, end[2] + (DL() - 0.5) * 0.012] }), 'dillHead'));
    }
  }
  // Safety: nothing may touch the brine's skin (squeeze any stray leaf tip back in).
  stuff.map(([x, y, z]) => {
    const r = Math.hypot(x, z), lim = inR(y) - 0.014;
    return r > lim ? [(x * lim) / r, y, (z * lim) / r] : [x, y, z];
  });
  add(stuff, pickling);

  // ---- anchors and notes ----------------------------------------------------------------------------------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const H = max[1] - min[1], s = 1.15 / H;
  return {
    parts,
    face: FACE_Y / H,
    faceSize: 0.74,
    anchors: { jarCentre: [0, (FLOOR + BRINE_TOP) / 2, 0], brineTop: [0, BRINE_TOP, 0], lid: [0, LID_TOP, 0] },
    notes: {
      innerRadius: +(inR(0.4) * s).toFixed(3),
      innerRadiusAtBrineTop: +(inR(BRINE_TOP) * s).toFixed(3),
      brineBottom: +(FLOOR * s).toFixed(3),
      brineTop: +(BRINE_TOP * s).toFixed(3),
      brineHeight: +((BRINE_TOP - FLOOR) * s).toFixed(3),
      shoulderStarts: +(BODY_TOP * s).toFixed(3),
      labelBand: [+(LABEL[0] * s).toFixed(3), +(LABEL[1] * s).toFixed(3)],
      spicesUpTo: +((floorAt + 0.38) * s).toFixed(3),
      glassMaterial: 'glass',
      brineMaterial: 'brine (blend, alpha 0.48, double-sided, faintly emissive)',
    },
  };
}
