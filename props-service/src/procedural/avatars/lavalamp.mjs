/**
 * Lava lamp: the classic rocket. A tall tapered cone base in sparkly metallic purple (a rolled foot, a collar bead
 * at the top, and a ring of little star and dot cut-outs glowing with the bulb inside), a tall tapered glass bottle
 * that flares out of the collar and narrows toward the top, full of a glowing translucent magenta liquid (warmer
 * and brighter low down, near the bulb) with the wire coil lying on its floor, and a matching cone cap with a
 * rounded tip. No wax blobs here: the app floats its own glowing ones inside the liquid.
 * Modelled at about app scale (1.15 m tall), so model units are roughly the app's metres.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, tube, extrude, sphere, smooth, lerp, clamp, fbm3, rng, TAU } = kit;

  // ---- key dimensions -------------------------------------------------------------------------------------
  const BASE_R = 0.285, BASE_TOP = 0.335; // base cone: foot radius, top of the cone
  const CONE_TOP_R = 0.162;
  const COLLAR_TOP = 0.362;
  const G0 = 0.342, GR0 = 0.146; // the glass starts here, buried in the collar
  const GW = 0.535, GRW = 0.228; // the bottle's widest point
  const GT = 0.985, GRT = 0.124; // the top of the bottle (under the cap)
  const T = 0.008; // glass to liquid gap
  const LIQ_BOT = 0.366, LIQ_TOP = 0.945;
  const FACE_Y = 0.69;

  /** The bottle's outer radius at height y: flares out of the collar, then a long gentle taper. */
  const rg = (y) => {
    if (y <= GW) return GR0 + (GRW - GR0) * Math.sin((Math.PI / 2) * clamp((y - G0) / (GW - G0)));
    return GRW - (GRW - GRT) * clamp((y - GW) / (GT - GW)) ** 1.4;
  };
  const rl = (y) => rg(y) - T; // the liquid's radius
  const coneR = (y) => lerp(BASE_R - 0.016, CONE_TOP_R, clamp((y - 0.034) / (BASE_TOP - 0.034)));

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });

  // ---- materials ------------------------------------------------------------------------------------------
  const glass = { name: 'glass', color: [0.85, 0.95, 1, 0.25], blend: true, roughness: 0.05 };

  // Sparkly metallic purple: soft vertical brushing, fine metal flake, a little lighter toward the top.
  const metalTex = new Canvas(512, 512, [128, 62, 196]);
  metalTex.shade((u, v) => {
    const cu = Math.cos(u * TAU), su = Math.sin(u * TAU);
    const brush = fbm3(cu * 30, su * 30, v * 1.5, { octaves: 2, seed: 3 }) - 0.5;
    const k = 0.92 + 0.12 * (1 - v) + 0.1 * brush;
    return [128 * k, 62 * k, 196 * k];
  });
  metalTex.speckle(2600, [0.4, 1.0], [236, 200, 255], 0.55, 9).speckle(900, [0.4, 0.9], [70, 24, 120], 0.4, 10);
  const metal = { name: 'metal', texture: metalTex, roughness: 0.32, metallic: 0.6 };
  const chrome = { name: 'chrome', color: [0.72, 0.72, 0.76], roughness: 0.3, metallic: 0.85 }; // trim rings and the coil
  // The cap (everything above the bottle) has its own materials so the app can hide it when it blows off.
  const capMetal = { ...metal, name: 'cap' };
  const capChrome = { ...chrome, name: 'cap_chrome' };
  const glowMat = { name: 'bulbGlow', color: [1, 0.78, 0.36], emissive: [1, 0.62, 0.2], roughness: 0.6 };
  const holeMat = { name: 'holeDark', color: [0.012, 0.004, 0.02], roughness: 0.9 };

  // The liquid: hot magenta, warmer and brighter at the bottom (the bulb's right under it), deeper at the top.
  const liqTex = new Canvas(64, 512, [230, 40, 150]);
  liqTex.shade((u, v) => {
    const top = [140, 8, 118], mid = [222, 22, 150], low = [255, 92, 142];
    return v < 0.55 ? top.map((x, i) => lerp(x, mid[i], smooth(0, 0.55, v))) : mid.map((x, i) => lerp(x, low[i], smooth(0.55, 1, v)));
  });
  const liquid = { name: 'liquid', texture: liqTex, color: [1, 1, 1, 0.6], blend: true, doubleSided: true, emissive: [0.22, 0.02, 0.12], roughness: 0.15 };

  // ---- the base -------------------------------------------------------------------------------------------
  const baseV = (t, y) => clamp(1 - y / COLLAR_TOP);
  // A chrome rolled foot, the purple cone, and a chrome collar bead the bottle sits in.
  add(lathe([
    [0, 0], [BASE_R - 0.012, 0, true], [BASE_R - 0.004, 0.004], [BASE_R, 0.012], [BASE_R, 0.02], [BASE_R - 0.005, 0.028], [BASE_R - 0.014, 0.033, true],
    [coneR(0.034) - 0.004, 0.034],
  ], { segments: 96 }), chrome);
  const cone = [[coneR(0.034) - 0.004, 0.034, true]];
  for (let y = 0.04; y < BASE_TOP - 0.005; y += 0.02) cone.push([coneR(y), y]);
  cone.push([CONE_TOP_R, BASE_TOP, true], [CONE_TOP_R - 0.004, BASE_TOP + 0.002]);
  add(lathe(cone, { segments: 96, v: baseV }), metal);
  add(lathe([
    [CONE_TOP_R - 0.004, BASE_TOP + 0.002, true], [CONE_TOP_R + 0.008, BASE_TOP + 0.003], [CONE_TOP_R + 0.013, BASE_TOP + 0.01], [CONE_TOP_R + 0.014, BASE_TOP + 0.018],
    [CONE_TOP_R + 0.011, BASE_TOP + 0.024], [CONE_TOP_R + 0.004, COLLAR_TOP - 0.001], [CONE_TOP_R - 0.002, COLLAR_TOP, true],
    [GR0 + 0.004, COLLAR_TOP, true], [GR0 + 0.002, COLLAR_TOP - 0.01], [0, COLLAR_TOP - 0.01],
  ], { segments: 96 }), chrome);

  // Star and dot cut-outs round the top of the cone, glowing with the bulb (a dark rim round each).
  const tilt = (Math.atan((BASE_R - 0.016 - CONE_TOP_R) / (BASE_TOP - 0.034)) * 180) / Math.PI;
  const onCone = (mesh, a, y, lift) => {
    const r = coneR(y) + lift;
    const yaw = (Math.atan2(-Math.cos(a), -Math.sin(a)) * 180) / Math.PI;
    return mesh.transform({ rotate: [tilt, yaw, 0], translate: [r * Math.cos(a), y, r * Math.sin(a)] });
  };
  const star = (ro, ri, n = 5) => Array.from({ length: n * 2 }, (_, k) => {
    const a = Math.PI / 2 + (k * Math.PI) / n, rr = k % 2 ? ri : ro;
    return [rr * Math.cos(a), rr * Math.sin(a)];
  });
  const circle = (r, n = 16) => Array.from({ length: n }, (_, k) => [r * Math.cos((TAU * k) / n), r * Math.sin((TAU * k) / n)]);
  const glow = new Mesh(), rims = new Mesh();
  const CUTS = 10;
  for (let k = 0; k < CUTS; k++) {
    const a = -Math.PI / 2 + (TAU * k) / CUTS; // a star faces the front
    const big = star(0.024, 0.011), rim = star(0.031, 0.016);
    rims.append(onCone(extrude(rim, 0.004), a, 0.268, 0.0006));
    glow.append(onCone(extrude(big, 0.004), a, 0.268, 0.0014));
    const b = a + Math.PI / CUTS; // little dots between the stars, a touch lower
    rims.append(onCone(extrude(circle(0.0125), 0.004), b, 0.255, 0.0006));
    glow.append(onCone(extrude(circle(0.0085), 0.004), b, 0.255, 0.0014));
    // and a ring of tiny dots further down
    rims.append(onCone(extrude(circle(0.009), 0.004), a, 0.19, 0.0006));
    glow.append(onCone(extrude(circle(0.0058), 0.004), a, 0.19, 0.0014));
    rims.append(onCone(extrude(circle(0.009), 0.004), b, 0.19, 0.0006));
    glow.append(onCone(extrude(circle(0.0058), 0.004), b, 0.19, 0.0014));
  }
  add(rims, holeMat);
  add(glow, glowMat);

  // ---- the bottle -----------------------------------------------------------------------------------------
  const bottle = [];
  for (let y = G0; y < GT - 0.03; y += 0.016) bottle.push([rg(y), y]);
  // a rounded shoulder into the neck the cap hides
  bottle.push([rg(GT - 0.03), GT - 0.03], [rg(GT - 0.015) - 0.002, GT - 0.015], [rg(GT) - 0.008, GT], [rg(GT) - 0.018, GT + 0.008], [0.085, GT + 0.012], [0.085, GT + 0.04]);
  add(lathe(bottle, { segments: 80 }), glass);

  // ---- the liquid -----------------------------------------------------------------------------------------
  const liq = [[0, LIQ_BOT], [rl(LIQ_BOT + 0.01) - 0.016, LIQ_BOT], [rl(LIQ_BOT + 0.01) - 0.006, LIQ_BOT + 0.003], [rl(LIQ_BOT + 0.012), LIQ_BOT + 0.012]];
  for (let y = LIQ_BOT + 0.03; y < LIQ_TOP - 0.006; y += 0.016) liq.push([rl(y), y]);
  liq.push([rl(LIQ_TOP - 0.004), LIQ_TOP - 0.004], [rl(LIQ_TOP), LIQ_TOP, true], [rl(LIQ_TOP) * 0.6, LIQ_TOP - 0.002], [0, LIQ_TOP - 0.002]);
  add(lathe(liq, { segments: 80, v: (t, y) => clamp((LIQ_TOP - y) / (LIQ_TOP - LIQ_BOT)) }), liquid);

  // The wire coil lying on the bottle's floor (it breaks the wax up in a real lamp).
  const coil = [];
  for (let k = 0; k <= 160; k++) {
    const t = k / 160, a = t * TAU * 3.2;
    const r = lerp(0.03, rl(LIQ_BOT + 0.012) - 0.024, t);
    coil.push([r * Math.cos(a), LIQ_BOT + 0.009 + 0.004 * Math.sin(a * 2), r * Math.sin(a)]);
  }
  add(tube(coil, 0.0045, { segments: 8 }), chrome);

  // ---- the cap --------------------------------------------------------------------------------------------
  const C0 = GT - 0.004, CR0 = GRT + 0.013, CT = 1.108, CRT = 0.052;
  const capR = (y) => lerp(CR0, CRT, ((y - C0) / (CT - C0)) ** 0.92);
  add(lathe([[0, C0 + 0.012], [0.09, C0 + 0.012], [CR0 - 0.006, C0, true], [CR0 + 0.003, C0 + 0.003], [CR0 + 0.006, C0 + 0.01], [CR0 + 0.004, C0 + 0.018], [CR0 - 0.002, C0 + 0.022, true], [capR(C0 + 0.024) - 0.002, C0 + 0.024]], { segments: 80 }), capChrome);
  const cap = [[capR(C0 + 0.024) - 0.002, C0 + 0.024, true]];
  for (let y = C0 + 0.03; y < CT - 0.005; y += 0.016) cap.push([capR(y), y]);
  cap.push([CRT, CT, true], [CRT - 0.004, CT + 0.002]);
  add(lathe(cap, { segments: 80, v: (t, y) => clamp(1 - (y - C0) / (CT + 0.03 - C0)) * 0.25 }), capMetal);
  // the rounded chrome tip: a bead and a little dome
  const tip = [[CRT - 0.004, CT + 0.002, true], [CRT + 0.004, CT + 0.004], [CRT + 0.005, CT + 0.01], [CRT + 0.002, CT + 0.016], [CRT - 0.006, CT + 0.019, true]];
  for (let k = 1; k <= 10; k++) {
    const a = (Math.PI / 2) * (k / 10);
    tip.push([(CRT - 0.01) * Math.cos(a), CT + 0.019 + 0.016 * Math.sin(a)]);
  }
  add(lathe(tip, { segments: 64 }), capChrome);

  // ---- anchors and notes ----------------------------------------------------------------------------------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const H = max[1] - min[1], s = 1.15 / H;
  const mid = (LIQ_BOT + LIQ_TOP) / 2;
  const profile = [];
  for (let y = LIQ_BOT + 0.02; y <= LIQ_TOP + 1e-6; y += 0.05) profile.push([+(y * s).toFixed(3), +(rl(y) * s).toFixed(3)]);
  return {
    parts,
    face: FACE_Y / H,
    faceSize: 0.9,
    anchors: { bottleBottom: [0, LIQ_BOT, 0], bottleTop: [0, LIQ_TOP, 0] },
    notes: {
      liquidRadiusBottom: +(rl(LIQ_BOT + 0.02) * s).toFixed(3),
      liquidRadiusMiddle: +(rl(mid) * s).toFixed(3),
      liquidRadiusTop: +(rl(LIQ_TOP) * s).toFixed(3),
      liquidWidestAt: +(GW * s).toFixed(3),
      liquidRadiusWidest: +(rl(GW) * s).toFixed(3),
      liquidProfile: profile,
      liquidProfileNote: '[height, radius] pairs of the liquid (app metres), every ~5 cm from the bottom up',
      glassMaterial: 'glass',
      liquidMaterial: 'liquid (blend, alpha 0.6, double-sided, a little emissive)',
      waxColourSuggestion: 'warm yellow-orange reads best against the magenta liquid',
    },
  };
}
