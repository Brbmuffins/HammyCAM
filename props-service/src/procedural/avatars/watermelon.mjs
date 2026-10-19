/**
 * Watermelon: a whole melon standing on its end, deep green with jagged light-green stripes running pole to pole,
 * a pale yellow field spot on its side, a short dried stem nub on top. The front is a calmer mid-green so the dark
 * cartoon face reads. In the app it can split or burst into red flesh and seeds.
 */
export default function build(kit) {
  const { Mesh, Canvas, loft, lathe, ringAngle, fbm3, noise3, smooth, lerp, TAU } = kit;

  // ---------- shape: a superellipse egg, a little boxier and broader at the bottom so it stands ----------
  const RX = 0.34, RYL = 0.455, RYU = 0.505; // half-width, lower and upper half-heights
  const PL = 2.6, PU = 2.05; // superellipse powers (higher = flatter)
  const CY = RYL;
  const TOP = CY + RYU;
  const FACE_THETA = 0.12; // the face's latitude (radians above the equator)
  const wrapPi = (x) => ((((x + Math.PI) % TAU) + TAU) % TAU) - Math.PI;
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  /** Radius and height at latitude theta (-PI/2 bottom .. PI/2 top). */
  const profile = (theta) => {
    const c = Math.abs(Math.cos(theta)), s = Math.sin(theta);
    const p = s < 0 ? PL : PU;
    const broad = 1 + 0.025 * -Math.sin(theta); // a touch broader low down
    return { r: RX * broad * c ** (2 / p), y: CY + (s < 0 ? RYL : RYU) * Math.sign(s) * Math.abs(s) ** (2 / p) };
  };
  const faceZone = (phi, theta) => (1 - smooth(0.3, 1.05, Math.abs(phi))) * (1 - smooth(0.25, 0.8, Math.abs(theta - FACE_THETA)));

  const ROWS = 76, SEG = 128;
  const rings = [];
  for (let j = 0; j <= ROWS; j++) {
    const theta = -Math.PI / 2 + (Math.PI * j) / ROWS;
    const { r, y } = profile(theta);
    const ring = [];
    for (let i = 0; i <= SEG; i++) {
      const a = ringAngle(i % SEG, SEG);
      const c = Math.cos(a), s = Math.sin(a);
      const phi = wrapPi((3 * Math.PI) / 2 - a);
      // Gentle lumps (a real melon is never perfectly round), calmer where the face goes.
      const bump = (fbm3(c * 1.6, y * 2.2, s * 1.6, { octaves: 3, seed: 5 }) - 0.5) * 0.016 * (1 - 0.6 * faceZone(phi, theta));
      const rr = j === 0 || j === ROWS ? 0 : r + bump * (r / RX);
      ring.push([rr * c, y, rr * s]);
    }
    ring.v = (Math.PI / 2 - theta) / Math.PI;
    rings.push(ring);
  }
  const body = loft(rings);

  // ---------- the rind's paint ----------
  const DEEP = [30, 86, 38], MID = [70, 128, 56], LIGHT = [142, 188, 92], VEIN = [92, 146, 66];
  const NS = 16;
  const stripes = Array.from({ length: NS }, (_, k) => ({ phi: wrapPi(((k + 0.5) * TAU) / NS), k }));
  const FIELD = { phi: -1.85, theta: -0.28 }; // the field spot: screen-left side, below the middle
  const rind = new Canvas(1024, 1024, DEEP);
  rind.shade((u, v) => {
    const phi = (u - 0.5) * TAU;
    const theta = Math.PI / 2 - v * Math.PI;
    const { r, y } = profile(theta);
    const p = [Math.sin(phi) * Math.max(r, 0.02), y, Math.cos(phi) * Math.max(r, 0.02)];
    const fz = faceZone(phi, theta);
    const mottle = fbm3(p[0] * 9, p[1] * 9, p[2] * 9, { octaves: 4, seed: 11 });
    let col = mix(DEEP, MID, 0.22 + 0.5 * (mottle - 0.5) + 0.45 * fz);
    // Stripes: wobbling bands with jagged, flame-like edges, converging at the poles.
    let stripe = 0, veinK = 0;
    for (const st of stripes) {
      const wob = (noise3(theta * 2.2, st.k * 1.7, 0.5, 21) - 0.5) * 0.26 + (noise3(theta * 7, st.k * 2.3, 1.5, 22) - 0.5) * 0.05;
      const d = wrapPi(phi - st.phi - wob);
      if (Math.abs(d) > 0.4) continue;
      const half = 0.052 * (0.75 + 0.5 * noise3(theta * 3.2, st.k, 2.5, 23)) * (0.6 + 0.4 * smooth(0, 0.5, Math.cos(theta)));
      const side = d > 0 ? 1 : -1;
      // Sharp spikes off both edges, irregular in spacing and length (the classic jagged melon stripe).
      let jag = 0;
      for (const [freq, amp, off] of [[13, 0.055, 0.37], [23, 0.03, 0.11]]) {
        const ph = theta * freq + st.k * 0.61 + side * off + noise3(theta * 3, st.k, side, 24) * 2.2;
        const dist = Math.abs(ph - Math.floor(ph) - 0.5) * 2; // 0 at a spike's tip .. 1 between spikes
        jag = Math.max(jag, amp * Math.max(0, 1 - dist / 0.32) * (0.3 + 0.9 * noise3(theta * 8, st.k + side * 5, 3, 25)));
      }
      const edge = half + jag;
      const m = 1 - smooth(edge - 0.006, edge + 0.003, Math.abs(d));
      if (m > stripe) {
        stripe = m;
        veinK = Math.abs(d) / edge;
      }
    }
    const strength = 1 - 0.5 * fz;
    let sc = mix(LIGHT, VEIN, smooth(0.35, 0.7, fbm3(p[0] * 30, p[1] * 6, p[2] * 30, { octaves: 2, seed: 31 })) * 0.55 + veinK * 0.25);
    sc = mix(sc, mix(MID, LIGHT, 0.55), fz * 0.5);
    col = mix(col, sc, stripe * strength);
    // Field spot: where it lay in the field, pale butter yellow with ghosts of the stripes.
    const fd = Math.hypot(wrapPi(phi - FIELD.phi) / 0.4, (theta - FIELD.theta) / 0.38) + (fbm3(p[0] * 8, p[1] * 8, p[2] * 8, { octaves: 3, seed: 41 }) - 0.5) * 0.55;
    const field = 1 - smooth(0.35, 1.0, fd);
    const blotch = fbm3(p[0] * 26, p[1] * 26, p[2] * 26, { octaves: 2, seed: 43 });
    col = mix(col, mix([228, 212, 138], [168, 182, 96], smooth(0.1, 0.95, fd + (blotch - 0.5) * 0.4)), field * (0.95 - 0.45 * stripe));
    // Stem scar at the top, blossom end at the bottom.
    const top = Math.PI / 2 - theta;
    col = mix(col, [150, 150, 84], 1 - smooth(0.05, 0.1, top));
    col = mix(col, [110, 96, 60], 1 - smooth(0.03, 0.055, top));
    const bot = theta + Math.PI / 2;
    col = mix(col, [120, 120, 80], 1 - smooth(0.05, 0.09, bot));
    // A waxy bloom and fine grain.
    const grain = fbm3(p[0] * 120, p[1] * 120, p[2] * 120, { octaves: 2, seed: 51 });
    return mul(col, 0.94 + 0.12 * grain);
  });
  rind.speckle(2200, [0.5, 1.3], [170, 210, 120], 0.22, 9);
  rind.speckle(700, [0.6, 1.6], [20, 60, 26], 0.2, 10);

  const parts = [{ mesh: body, material: { name: 'rind', texture: rind, roughness: 0.78 } }];

  // ---------- the stem nub: a short dried stalk, bent over a little, with a paler cut end ----------
  const SH = 0.046; // its height above the melon's top
  const stem = lathe(
    [
      [0, TOP - 0.016],
      [0.034, TOP - 0.014],
      [0.024, TOP - 0.002],
      [0.018, TOP + 0.01],
      [0.0155, TOP + 0.022],
      [0.0145, TOP + 0.034],
      [0.0145, TOP + SH - 0.006, true],
      [0.011, TOP + SH - 0.001],
      [0, TOP + SH],
    ],
    { segments: 16, v: (t) => t },
  );
  // ridges along the stalk, then the bend
  stem.map(([x, y, z]) => {
    const a = Math.atan2(z, x), up = Math.max(0, y - TOP);
    const k = 1 + 0.1 * Math.cos(a * 6) * smooth(0, 0.01, up);
    const bend = 9 * up * up;
    return [x * k + bend, y - bend * 0.25, z * k + bend * 0.35];
  });
  const stemTex = new Canvas(64, 64, [116, 92, 52]);
  stemTex.shade((u, v) => {
    // v: 0 at the base .. 1 at the cut end
    const ridge = 0.85 + 0.25 * Math.abs(Math.sin(u * Math.PI * 6));
    let col = mul(mix([96, 110, 56], [118, 90, 50], smooth(0.05, 0.3, v)), ridge);
    col = mix(col, [176, 150, 96], smooth(0.8, 0.9, v));
    return col;
  });
  parts.push({ mesh: stem, material: { name: 'stem', texture: stemTex, roughness: 0.9 } });

  const tipUp = SH;
  const tip = [9 * tipUp * tipUp, TOP + SH - 9 * tipUp * tipUp * 0.25, 9 * tipUp * tipUp * 0.35];
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const faceY = CY + RYU * Math.sin(FACE_THETA) ** (2 / PU);
  const front = profile(FACE_THETA);
  return {
    parts,
    face: (faceY - min[1]) / (max[1] - min[1]),
    faceSize: 0.82,
    anchors: { stem: tip, front: [0, faceY, -front.r] },
    notes: `Solid melon (no opening). Body ${(2 * RX).toFixed(2)} wide x ${TOP.toFixed(2)} tall model units, stem nub ${SH.toFixed(3)} above it; field spot on the screen-left (+X) side, below the middle.`,
  };
}
