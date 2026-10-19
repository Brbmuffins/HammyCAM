/**
 * Two-litre soda bottle: a chunky PET bottle on a five-footed petaloid base, gently ridged, with a wrap-round
 * "FIZZ BOMB" label (orange and blue, zig-zag lightning) low on the body. It is filled with amber cola up to the
 * shoulder; above the liquid line the shoulder and neck are clear plastic (material "glassBottle", swapped for
 * the app's see-through glass), topped with a knurled red screw cap and its tamper ring.
 * The face sits on the plain amber body between the label and the shoulder.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, smooth, clamp, lerp, TAU, fbm2, rng } = kit;

  // ---------- little helpers (texture stamping with non-square texels) ----------
  const solid = (rgb) => new Canvas(8, 8, rgb);
  /** White-on-black text mask, letters h px tall. */
  const textMask = (str, h, weight = 0.17) => {
    const pad = Math.ceil(h * 0.4);
    const m = new Canvas(Math.ceil(str.length * h * 0.95 + pad * 2), Math.ceil(h + pad * 2), [0, 0, 0]);
    m.text(str, m.width / 2, pad, h, [255, 255, 255], { weight });
    return m;
  };
  const sampleR = (m, x, y) => {
    const x0 = Math.floor(x), y0 = Math.floor(y), fx = x - x0, fy = y - y0;
    const at = (xx, yy) => (xx < 0 || yy < 0 || xx >= m.width || yy >= m.height ? 0 : m.px[(yy * m.width + xx) * 4] / 255);
    return (at(x0, y0) * (1 - fx) + at(x0 + 1, y0) * fx) * (1 - fy) + (at(x0, y0 + 1) * (1 - fx) + at(x0 + 1, y0 + 1) * fx) * fy;
  };
  /** Paints colour through a mask centred at (cx, cy), scaled by sx, sy (wraps round horizontally). */
  const blit = (dst, mask, cx, cy, sx, sy, colour, a = 1) => {
    const w = mask.width * sx, h = mask.height * sy, x0 = cx - w / 2, y0 = cy - h / 2;
    for (let y = Math.floor(y0); y < Math.ceil(y0 + h); y++)
      for (let x = Math.floor(x0); x < Math.ceil(x0 + w); x++) {
        const m = sampleR(mask, (x + 0.5 - x0) / sx - 0.5, (y + 0.5 - y0) / sy - 0.5);
        if (m > 0.004) dst.blend(((x % dst.width) + dst.width) % dst.width, y, colour, a * m);
      }
  };
  const gauss = (y, c, w) => Math.exp(-(((y - c) / w) ** 2));

  // ---------- shape (metres-ish; the app rescales to 1.15 m) ----------
  const R = 0.3; // body radius
  const LIQ = 0.7; // liquid line (on the shoulder)
  const LABEL = [0.148, 0.352]; // label band, low on the body
  const SH0 = 0.64, SH1 = 0.842, NECK = 0.084; // shoulder from SH0 (radius R) to SH1 (radius NECK)
  const SEG = 96;

  /** Body radius at height y (above the base): grooves, the label's shallow recess, ribs, then the shoulder. */
  const bodyR = (y) => {
    if (y >= SH0) {
      const s = clamp((y - SH0) / (SH1 - SH0));
      const f = 0.5 + 0.5 * Math.cos(Math.PI * Math.pow(s, 0.85));
      return NECK + (R - NECK) * f;
    }
    let r = R;
    r -= 0.007 * gauss(y, 0.136, 0.0055);
    r -= 0.0035 * (smooth(LABEL[0] - 0.006, LABEL[0], y) - smooth(LABEL[1], LABEL[1] + 0.006, y));
    r -= 0.0075 * gauss(y, 0.372, 0.0058) + 0.0075 * gauss(y, 0.395, 0.0058);
    r -= 0.006 * gauss(y, 0.622, 0.0065);
    return r;
  };
  const features = [0.136, LABEL[0], LABEL[1], 0.372, 0.395, 0.622];
  const rowsBetween = (y0, y1) => {
    const ys = [];
    let y = y0;
    while (y < y1 - 1e-6) {
      ys.push(y);
      const near = features.some((f) => Math.abs(y - f) < 0.016);
      y += y >= SH0 ? 0.011 : near ? 0.0028 : 0.02;
    }
    ys.push(y1);
    return ys;
  };

  // Petaloid base: a raised dimple in the middle, five feet round the rim (a valley faces the front).
  const base = [
    [0, 0.034], [0.04, 0.031], [0.085, 0.022], [0.13, 0.011], [0.17, 0.003], [0.198, 0], [0.225, 0.004],
    [0.25, 0.017], [0.27, 0.036], [0.285, 0.06], [0.295, 0.088], [0.2995, 0.112],
  ];
  const lowerProfile = [...base, ...rowsBetween(0.125, LIQ).map((y) => [bodyR(y), y])];
  const footAngle = (-54 * Math.PI) / 180;
  const petaloid = (mesh) =>
    mesh.map(([x, y, z]) => {
      if (y > 0.13) return [x, y, z];
      const r = Math.hypot(x, z);
      if (r < 1e-6) return [x, y, z];
      const th = Math.atan2(z, x);
      const lobe = Math.pow(0.5 + 0.5 * Math.cos(5 * (th - footAngle)), 1.3);
      const valley = 1 - lobe;
      const k = 1 - smooth(0.06, 0.13, y);
      const rr = r * (1 - 0.19 * valley * k * smooth(0.06, 0.2, r));
      const yy = y + valley * 0.065 * k * smooth(0.04, 0.19, r) * (1 - 0.6 * smooth(0.22, 0.3, r) * smooth(0.03, 0.1, y));
      return [(x / r) * rr, yy, (z / r) * rr];
    });

  // ---------- textures ----------
  // Soda: amber cola (light enough for the dark face to read), a foam line at the top, bubbles, a shine band.
  const SODA = [146, 74, 32];
  const soda = new Canvas(1024, 384, SODA);
  soda.shade((u, v) => {
    const y = (1 - v) * LIQ; // height on the bottle
    const deep = smooth(0.35, 0, y) * 0.22; // darker low down, where it's deepest
    const n = fbm2(u * 9, v * 5, { octaves: 3, seed: 3 }) - 0.5;
    // Shine bands (light from the upper left: screen left is u < 0.5).
    const shine = Math.exp(-(((u - 0.37) / 0.022) ** 2)) * 0.55 + Math.exp(-(((u - 0.425) / 0.008) ** 2)) * 0.3 + Math.exp(-(((u - 0.64) / 0.03) ** 2)) * 0.16;
    const c = [SODA[0] * (1 - deep + n * 0.05), SODA[1] * (1 - deep * 1.2 + n * 0.05), SODA[2] * (1 - deep * 1.3 + n * 0.05)];
    return c.map((x, i) => lerp(x, [236, 178, 112][i], clamp(shine)));
  });
  const R0 = rng(11);
  // Bubble streams rising up the body, and loose bubbles.
  for (let k = 0; k < 170; k++) {
    const u = R0(), v = Math.pow(R0(), 1.6);
    const r = lerp(1.6, 4.6, R0() ** 2);
    soda.disc(u * 1024, v * 384, r, [255, 214, 160], 0.6, 0.3);
    soda.disc(u * 1024, v * 384, r * 0.62, [150, 76, 34], 0.45, 0.5);
    soda.disc(u * 1024 - r * 0.35, v * 384 - r * 0.35, r * 0.3, [255, 250, 235], 0.9, 0.4);
  }
  // Foam line at the liquid surface.
  soda.rect(0, 0, 1024, 7, [238, 214, 170]);
  for (let x = 0; x < 1024; x += 3) soda.disc(x + R0() * 3, 6 + R0() * 4, 1.5 + R0() * 2.5, [244, 226, 190], 0.9, 0.3);

  // Label: 1024 px round the full circumference, 256 px up its height; texels are ~2.3x wider than tall.
  const LW = 1024, LH = 256;
  const labelH = LABEL[1] - LABEL[0];
  const circ = TAU * (R + 0.0015);
  const SX = (labelH / LH) / (circ / LW); // horizontal px per "square" px
  const ORANGE = [255, 118, 12], ORANGE2 = [255, 168, 34], BLUE = [28, 74, 196], NAVY = [18, 38, 110], WHITE = [255, 255, 255], YELLOW = [255, 226, 60];
  const label = new Canvas(LW, LH, ORANGE);
  label.shade((u, v) => {
    const glow = Math.exp(-(((v - 0.5) / 0.32) ** 2));
    return ORANGE.map((x, i) => lerp(x, ORANGE2[i], glow));
  });
  // A rising zig-zag pattern band behind everything (lightning streaks).
  const zig = (y0, amp, period, width, colour, a) => {
    const pts = [];
    for (let x = -period; x <= LW + period; x += period / 2) pts.push([x, y0 + ((x / (period / 2)) % 2 ? amp : -amp)]);
    label.stroke(pts.map(([x, y]) => [x, Math.round(y)]), width, colour, a);
  };
  zig(LH * 0.36, 16, 46, 7, ORANGE2, 0.9);
  zig(LH * 0.64, 16, 46, 7, ORANGE2, 0.9);
  // Blue bands top and bottom with white pinstripes.
  label.rect(0, 0, LW, 30, BLUE).rect(0, LH - 30, LW, LH, BLUE);
  label.rect(0, 34, LW, 38, WHITE).rect(0, LH - 38, LW, LH - 34, WHITE);
  zig(15, 6, 22, 3, [90, 140, 240], 1);
  zig(LH - 15, 6, 22, 3, [90, 140, 240], 1);
  /** A lightning bolt polygon centred on (cx, cy), size s (square px), leaning. */
  const bolt = (cx, cy, s, flip, fill, edge) => {
    const pts = [[0.15, -0.5], [0.42, -0.5], [0.12, -0.06], [0.36, -0.06], [-0.22, 0.52], [-0.02, 0.06], [-0.26, 0.06]];
    const P = pts.map(([x, y]) => [cx + (flip ? -x : x) * s * SX, cy + y * s]);
    const fat = pts.map(([x, y]) => [cx + (flip ? -x : x) * s * 1.18 * SX, cy + y * s * 1.12]);
    label.poly(fat, edge).poly(P, fill);
  };
  const brand = (cx) => {
    const fizz = textMask('FIZZ', 70, 0.2), bomb = textMask('BOMB', 70, 0.2);
    const fizzO = textMask('FIZZ', 70, 0.36), bombO = textMask('BOMB', 70, 0.36);
    // shadow, outline, fill
    blit(label, fizzO, cx + 6 * SX, 85 + 5, SX, 1, NAVY, 0.85);
    blit(label, bombO, cx + 6 * SX, 171 + 5, SX, 1, NAVY, 0.85);
    blit(label, fizzO, cx, 85, SX, 1, BLUE);
    blit(label, bombO, cx, 171, SX, 1, BLUE);
    blit(label, fizz, cx, 85, SX, 1, WHITE);
    blit(label, bomb, cx, 171, SX, 1, WHITE);
    bolt(cx - 205 * SX, LH / 2, 150, false, YELLOW, NAVY);
    bolt(cx + 205 * SX, LH / 2, 150, true, YELLOW, NAVY);
  };
  brand(LW / 2);
  brand(0);
  brand(LW);
  // Small print on the sides: "COLA" and "2 L".
  const small = (str, cx, cy, h) => blit(label, textMask(str, h, 0.2), cx, cy, SX, 1, WHITE);
  for (const cx of [LW * 0.25, LW * 0.75]) {
    label.disc(cx, LH / 2, 62 * SX, NAVY, 1).disc(cx, LH / 2, 56 * SX, BLUE, 1);
    small('2 L', cx, LH / 2 - 14, 40);
    small('COLA', cx, LH / 2 + 34, 20);
  }
  label.speckle(500, [0.5, 1.4], [255, 255, 255], 0.12, 5);

  // Cap: glossy red plastic.
  const cap = new Canvas(64, 64, [214, 34, 30]);

  // ---------- meshes ----------
  const sodaV = (t, y) => clamp(1 - y / LIQ);
  const lower = petaloid(lathe(lowerProfile, { segments: SEG, v: sodaV }));

  // Clear shoulder and neck above the liquid, with the neck support ring.
  const upperProfile = [
    ...rowsBetween(LIQ, SH1).map((y) => [bodyR(y), y]),
    [NECK, 0.858], [NECK + 0.001, 0.861, true], [0.122, 0.864, true], [0.124, 0.872], [0.122, 0.879, true], [0.081, 0.882, true],
    [0.081, 0.955, true], [0.06, 0.957], [0, 0.957],
  ];
  const upper = lathe(upperProfile, { segments: SEG });

  // Liquid surface (seen when the bottle tips or the camera's high): a foamy disc just inside the shoulder.
  const surf = new Mesh();
  {
    const r = bodyR(LIQ) - 0.006;
    const c = surf.vertex([0, LIQ, 0], [0.5, 0.004]);
    const ring = [];
    for (let i = 0; i <= SEG; i++) {
      const a = Math.PI / 2 + (TAU * i) / SEG;
      ring.push(surf.vertex([r * Math.cos(a), LIQ, r * Math.sin(a)], [i / SEG, 0.004]));
    }
    for (let i = 0; i < SEG; i++) surf.tri(c, ring[i + 1], ring[i]);
  }

  // Label band: a thin ring sitting in the recess.
  const labelMesh = lathe(
    [[R - 0.0045, LABEL[0] + 0.002], [R + 0.0015, LABEL[0] + 0.002, true], [R + 0.0015, LABEL[1] - 0.002, true], [R - 0.0045, LABEL[1] - 0.002]],
    { segments: SEG, v: (t, y) => clamp((LABEL[1] - 0.002 - y) / (labelH - 0.004)) },
  );

  // Screw cap (knurled side, rounded top edge, a shallow dish on top) and the tamper ring below it.
  const CR = 0.1, CY0 = 0.9, CY1 = 1.0;
  const knurl = (t, a, r, y) => {
    const k = y > CY0 + 0.006 && y < CY1 - 0.012 ? 1 + 0.045 * Math.pow(Math.abs(Math.cos(a * 18)), 3) : 1;
    return [r * k * Math.cos(a), r * k * Math.sin(a)];
  };
  const capMesh = lathe(
    [
      [0.082, CY0], [CR - 0.004, CY0, true], [CR, CY0 + 0.004], [CR, CY0 + 0.012], [CR, CY0 + 0.05], [CR, CY1 - 0.014],
      [CR - 0.003, CY1 - 0.005], [CR - 0.011, CY1], [CR - 0.022, CY1, true], [CR - 0.026, CY1 - 0.004, true], [0.03, CY1 - 0.004], [0, CY1 - 0.003],
    ],
    { segments: 144, section: knurl },
  );
  const ringMesh = lathe([[0.083, 0.884], [0.098, 0.884, true], [0.099, 0.89], [0.098, 0.895, true], [0.083, 0.895]], { segments: SEG });

  const glossy = (name, texture, roughness = 0.35) => ({ name, texture, roughness, metallic: 0 });
  const parts = [
    { mesh: lower, material: glossy('soda', soda) },
    { mesh: surf, material: glossy('soda', soda) },
    { mesh: upper, material: { name: 'glassBottle', color: [0.85, 0.95, 0.9, 0.35], roughness: 0.1, blend: true } },
    { mesh: labelMesh, material: glossy('label', label, 0.5) },
    { mesh: capMesh, material: glossy('cap', cap, 0.45) },
    { mesh: ringMesh, material: glossy('cap', cap, 0.45) },
  ];

  return {
    parts,
    face: 0.505,
    faceSize: 0.82,
    anchors: { cap: [0, CY1, 0], neckTop: [0, 0.957, 0], liquidTop: [0, LIQ, 0] },
    notes:
      'Amber cola to the shoulder (liquidTop); clear "glassBottle" shoulder and neck above it; red screw cap (cap = its top, ' +
      'neckTop = the bottle mouth under it, for a fizz geyser when the cap pops). Label "FIZZ BOMB" low on the body; face on the amber above it.',
  };
}
