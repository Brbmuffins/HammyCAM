/**
 * Pepper shaker: a chunky diner-style shaker with a squat, egg-bellied cream ceramic body on a little foot, a
 * black band low down reading "PEPPER", and a domed silver screw cap (rolled collar ridges) with a punched
 * pattern of holes on top. The face goes on the cream belly above the band.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, smooth, clamp, lerp, TAU, fbm2, rng } = kit;

  // ---------- helpers ----------
  /** Catmull-Rom through [r, y] points, n samples per span (keeps the first and last). */
  const spline = (pts, n = 6) => {
    const out = [];
    for (let i = 0; i + 1 < pts.length; i++) {
      const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
      for (let k = 0; k < n; k++) {
        const t = k / n, t2 = t * t, t3 = t2 * t;
        out.push([0, 1].map((d) => 0.5 * (2 * p1[d] + (-p0[d] + p2[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t2 + (-p0[d] + 3 * p1[d] - 3 * p2[d] + p3[d]) * t3)));
      }
    }
    out.push([...pts[pts.length - 1]]);
    return out;
  };
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
  const blit = (dst, mask, cx, cy, sx, sy, colour, a = 1) => {
    const w = mask.width * sx, h = mask.height * sy, x0 = cx - w / 2, y0 = cy - h / 2;
    for (let y = Math.floor(y0); y < Math.ceil(y0 + h); y++)
      for (let x = Math.floor(x0); x < Math.ceil(x0 + w); x++) {
        const m = sampleR(mask, (x + 0.5 - x0) / sx - 0.5, (y + 0.5 - y0) / sy - 0.5);
        if (m > 0.004) dst.blend(((x % dst.width) + dst.width) % dst.width, y, colour, a * m);
      }
  };
  const gauss = (y, c, w) => Math.exp(-(((y - c) / w) ** 2));

  // ---------- body ----------
  const TOPY = 0.664; // the ceramic rim, under the cap
  const BAND = [0.088, 0.212];
  const bodyProfile = [
    [0, 0.008], [0.12, 0.008], [0.165, 0.006, true], [0.19, 0], [0.214, 0], [0.226, 0.008], [0.231, 0.022],
    [0.229, 0.034, true],
    ...spline([[0.229, 0.034], [0.262, 0.064], [0.306, 0.14], [0.33, 0.24], [0.326, 0.335], [0.301, 0.43], [0.258, 0.52], [0.22, 0.585], [0.204, 0.625]], 6).slice(1),
    [0.204, 0.64], [0.206, 0.65], [0.203, 0.659], [0.19, TOPY], [0.12, TOPY],
  ];
  const body = lathe(bodyProfile, { segments: 80, v: (t, y) => clamp(1 - y / TOPY) });

  // Cream glaze with a black band (pinstripes either side) and PEPPER in cream; an unglazed foot.
  const TW = 1024, TH = 512;
  const CREAM = [246, 238, 220], INK = [30, 28, 32];
  const glaze = new Canvas(TW, TH, CREAM);
  glaze.shade((u, v) => {
    const n = fbm2(u * 14, v * 8, { octaves: 3, seed: 5 }) - 0.5;
    return CREAM.map((c, i) => c * (1 + n * 0.035) - (i === 2 ? 4 : 0));
  });
  glaze.speckle(260, [0.6, 1.4], [150, 130, 110], 0.18, 21);
  const py = (y) => (1 - y / TOPY) * TH; // height -> texture row
  glaze.rect(0, py(BAND[1]), TW, py(BAND[0]), INK);
  glaze.rect(0, py(BAND[1] + 0.02), TW, py(BAND[1] + 0.012), INK);
  glaze.rect(0, py(BAND[0] - 0.012), TW, py(BAND[0] - 0.02), INK);
  // Thin cream lines just inside the band.
  glaze.rect(0, py(BAND[1] - 0.012), TW, py(BAND[1] - 0.016), CREAM, 0.9);
  glaze.rect(0, py(BAND[0] + 0.016), TW, py(BAND[0] + 0.012), CREAM, 0.9);
  // Texels at the band: horizontal = circumference / TW, vertical = TOPY / TH.
  const rBand = 0.29;
  const SX = (TOPY / TH) / ((TAU * rBand) / TW);
  const textH = ((BAND[1] - BAND[0]) * 0.52) / (TOPY / TH);
  const word = textMask('PEPPER', textH, 0.19);
  const midRow = py((BAND[0] + BAND[1]) / 2);
  const diamond = (cx) => glaze.poly([[cx, midRow - 13], [cx + 13 * SX, midRow], [cx, midRow + 13], [cx - 13 * SX, midRow]], CREAM);
  for (const cx of [TW / 2, 0, TW]) {
    blit(glaze, word, cx, midRow + 1, SX, 1, CREAM);
    diamond(cx - 205 * SX);
    diamond(cx + 205 * SX);
  }
  for (const cx of [TW / 4, (3 * TW) / 4]) {
    diamond(cx - 30 * SX);
    diamond(cx);
    diamond(cx + 30 * SX);
  }
  // Unglazed foot ring.
  glaze.rect(0, py(0.03), TW, TH, [206, 186, 156]);
  glaze.rect(0, py(0.034), TW, py(0.03), [222, 206, 180]);

  // ---------- cap ----------
  // A smooth band between two rolled beads, a fine groove round its middle, then a dome with a flattish top.
  const C0 = 0.626, C1 = 0.734, CR = 0.217; // collar
  const collarR = (y) => CR + 0.008 * gauss(y, C0 + 0.011, 0.0075) + 0.007 * gauss(y, C1 - 0.009, 0.0065) - 0.0025 * gauss(y, (C0 + C1) / 2, 0.003);
  const collarRows = [];
  for (let y = C0 + 0.004; y < C1 - 0.001; y += 0.003) collarRows.push([collarR(y), y]);
  const DOME0 = 0.744, DOMEH = 0.168, DR = 0.207;
  const dome = [];
  for (let k = 0; k <= 18; k++) {
    const a = (k / 18) * (Math.PI / 2), p = 2.35;
    dome.push([DR * Math.pow(Math.cos(a), 2 / p) * (k === 18 ? 0 : 1), DOME0 + DOMEH * Math.pow(Math.sin(a), 2 / p)]);
  }
  const capProfile = [
    [0.19, C0 + 0.006], [0.205, C0, true], [CR + 0.002, C0 + 0.002],
    ...collarRows,
    [CR, C1], [CR - 0.004, C1 + 0.004], [DR + 0.002, C1 + 0.006, true], [DR + 0.002, DOME0 - 0.002],
    ...dome,
  ];
  const cap = lathe(capProfile, { segments: 80 });
  // The dome gets a top-down print (the holes); the collar maps to a strip down the left edge of the texture.
  const top = DOME0 + DOMEH;
  const CV0 = C0, CV1 = DOME0;
  for (let i = 0; i < cap.vertexCount; i++) {
    const [x, y, z] = cap.point(i);
    if (y >= DOME0 - 0.0005) {
      cap.uvs[i * 2] = 0.5 - x / (2 * 0.24);
      cap.uvs[i * 2 + 1] = 0.5 + z / (2 * 0.24);
    } else {
      cap.uvs[i * 2] = 12 / 512;
      cap.uvs[i * 2 + 1] = clamp((CV1 - y) / (CV1 - CV0));
    }
  }
  // Chrome: painted soft reflections (bright sky on top, a dark horizon band low on the dome) so it reads as
  // metal even under flat light.
  const SILVER = [186, 192, 200];
  const metal = new Canvas(512, 512, SILVER);
  metal.shade((u, v) => {
    if (u < 26 / 512) {
      const y = lerp(CV1, CV0, v);
      const s = (y - CV0) / (CV1 - CV0);
      const k = 0.78 + 0.32 * smooth(0.15, 0.9, s) - 0.22 * Math.exp(-(((s - 0.42) / 0.12) ** 2)) + 0.18 * Math.exp(-(((s - 0.92) / 0.05) ** 2));
      return SILVER.map((c) => c * k);
    }
    const rho = (Math.hypot(u - 0.5, v - 0.5) * 0.48) / DR; // 0 at the top .. 1 at the dome's foot
    const swirl = 0.5 + 0.5 * Math.sin(rho * 520 + fbm2(u * 4, v * 4, { seed: 2 }) * 3);
    const k = 1.08 - 0.18 * smooth(0.2, 0.7, rho) - 0.26 * Math.exp(-(((rho - 0.86) / 0.07) ** 2)) + 0.1 * smooth(0.93, 1, rho);
    return SILVER.map((c) => c * k * (0.97 + 0.04 * swirl));
  });
  // Holes: centre, a ring of 6, a ring of 12 (world radii on the dome, seen from above).
  const toPx = (x, z) => [(0.5 - x / 0.48) * 512, (0.5 + z / 0.48) * 512];
  const holes = [[0, 0]];
  for (let k = 0; k < 6; k++) holes.push([0.058 * Math.cos((TAU * k) / 6), 0.058 * Math.sin((TAU * k) / 6)]);
  for (let k = 0; k < 12; k++) holes.push([0.114 * Math.cos((TAU * (k + 0.5)) / 12), 0.114 * Math.sin((TAU * (k + 0.5)) / 12)]);
  const holeR = (0.0135 / 0.48) * 512;
  for (const [x, z] of holes) {
    const [px, pz] = toPx(x, z);
    metal.disc(px, pz, holeR * 1.45, [240, 242, 246], 0.9, 0.4); // a punched rim
    metal.disc(px + 1, pz + 1, holeR * 1.05, [120, 120, 126], 1, 0.2);
    metal.disc(px, pz, holeR, [22, 20, 24], 1, 0.12);
  }
  // Specks of pepper dust round the holes.
  const R = rng(9);
  for (let k = 0; k < 120; k++) {
    const a = R() * TAU, r = Math.sqrt(R()) * 0.13;
    const [px, pz] = toPx(r * Math.cos(a), r * Math.sin(a));
    metal.disc(px, pz, 0.8 + R() * 1.6, [40, 36, 34], 0.55, 0.4);
  }

  const parts = [
    { mesh: body, material: { name: 'ceramic', texture: glaze, roughness: 0.8 } },
    { mesh: cap, material: { name: 'capMetal', texture: metal, roughness: 0.38, metallic: 0.85 } },
  ];
  return {
    parts,
    face: 0.455,
    faceSize: 0.8,
    anchors: { capTop: [0, top, 0], holes: [0, top - 0.004, 0] },
    notes: 'Domed metal cap on top: holes = centre of the hole pattern (pepper puffs out of here when shaken / sneezes), capTop = its peak.',
  };
}
