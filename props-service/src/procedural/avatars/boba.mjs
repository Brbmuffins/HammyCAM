/**
 * Bubble tea: a tall, slightly tapered clear plastic cup (rolled top rim, a stacking ridge, a little foot ring and a
 * few beads of condensation below the tea line) full of creamy milk tea (a little milk foam on top, richer brown-sugar
 * colour and syrup streaks low down), a clear snap-on dome lid with moulded ribs and a raised collar round its straw
 * hole, and a big fat straw in spiralling pastel stripes going down through the hole deep into the cup, leaning a
 * little to the front-left.
 * No tapioca pearls here: the app simulates its own at the bottom of the cup.
 * Modelled at about app scale (1.15 m tall), so model units are roughly the app's metres.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, sphere, rng, fbm3, smooth, lerp, clamp, TAU } = kit;

  // ---- key dimensions -------------------------------------------------------------------------------------
  const RB = 0.218, RT = 0.282; // outer cup radius at the bottom (y = 0) and at the top of the wall (WT)
  const WT = 0.78; // top of the straight wall; the rolled rim sits on it
  const rg = (y) => lerp(RB, RT, clamp(y / WT)); // outer cup radius at height y (straight taper)
  const T = 0.006; // gap between the glass and the tea
  const TEA_BOT = 0.014, TEA_TOP = WT - 0.07; // tea volume (inside floor .. liquid surface)
  const rt = (y) => rg(y) - T; // tea radius at height y
  const SR = 0.047, SW = 0.005; // straw radius and wall
  const HOLE_R = 0.057; // the lid's straw hole (a little roomier than the leaning straw)
  const TILT = [-5, 0, -9]; // the straw's lean (Euler degrees): its top leans toward the camera (-Z) and screen left (+X)
  const L_ABOVE = 0.2, L_BELOW = WT + 0.1; // straw length above / below the hole
  const FACE_Y = 0.42;

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });

  // ---- materials ------------------------------------------------------------------------------------------
  const glass = { name: 'glass', color: [0.85, 0.95, 1, 0.25], blend: true, roughness: 0.05 };

  // Milk tea: v 0..0.2 is the foamy top (polar: v grows toward the rim), v 0.2..1 runs down the wall (top..bottom).
  const TW = 512, TH = 1024;
  const tea = new Canvas(TW, TH, [206, 152, 100]);
  const ring = (u) => [Math.cos(u * TAU), Math.sin(u * TAU)]; // noise sampled round a circle meets itself at the seam
  tea.shade((u, v) => {
    const [cu, su] = ring(u);
    if (v < 0.2) {
      // Foam on top: pale cream, a touch darker toward the middle where the tea shows through.
      const r = v / 0.2;
      const n = fbm3(cu * 3 * (0.3 + r), su * 3 * (0.3 + r), r * 2, { octaves: 3, seed: 21 });
      const k = 0.94 + 0.08 * (n - 0.5) - 0.06 * (1 - r);
      return [238 * k, 214 * k, 176 * k];
    }
    const d = (v - 0.2) / 0.8; // 0 at the liquid line .. 1 at the bottom
    const top = [226, 182, 134], mid = [208, 154, 102], low = [160, 98, 56];
    let c = d < 0.55 ? top.map((x, i) => lerp(x, mid[i], smooth(0, 0.55, d))) : mid.map((x, i) => lerp(x, low[i], smooth(0.55, 1, d)));
    // Milk marbling: soft pale swirls, strongest just under the foam, fading away down the cup.
    const warp = fbm3(cu * 1.4, su * 1.4, d * 3, { octaves: 3, seed: 4 }) * 4;
    const swirl = Math.max(0, Math.sin((d * 9 + warp) * Math.PI)) ** 6 * (1 - smooth(0.05, 0.45, d)) * 0.5;
    c = c.map((x, i) => lerp(x, [244, 222, 190][i], swirl));
    // Brown-sugar syrup streaks creeping up the wall from the bottom ("tiger" stripes), low down only.
    const wob = fbm3(cu * 2.2, su * 2.2, d * 4, { octaves: 3, seed: 9 });
    const stripe = Math.max(0, Math.sin((u * 19 + wob * 3.2) * Math.PI)) ** 3;
    const reach = smooth(0.5 + 0.24 * fbm3(cu * 3, su * 3, 0.5, { octaves: 2, seed: 13 }), 0.97, d);
    c = c.map((x, i) => lerp(x, [104, 52, 22][i], clamp(stripe * reach * 0.8 + smooth(0.9, 1, d) * 0.35)));
    const grain = fbm3(cu * 18, su * 18, d * 30, { octaves: 2, seed: 2 }) - 0.5;
    return c.map((x) => x * (1 + 0.05 * grain));
  });
  // The foam line: a soft, bubbly edge where the foam meets the wall, and bubbles on top.
  const R0 = rng(5);
  for (let k = 0; k < 340; k++) tea.disc(R0() * TW, 0.2 * TH + R0() * 0.035 * TH, 2 + R0() * 4, [240, 222, 190], 0.75, 0.5);
  tea.rect(0, 0.2 * TH, TW, 0.2 * TH + 10, [242, 224, 192], 0.85);
  for (let k = 0; k < 90; k++) tea.disc(R0() * TW, R0() * 0.19 * TH, 1.5 + R0() * 3, [252, 246, 232], 0.6, 0.4);
  const teaMat = { name: 'tea', texture: tea, roughness: 0.55 };

  // Straw: spiralling pastel stripes with thin cream separators (u round the straw, v along it, 0 = the top).
  const L = L_ABOVE + L_BELOW;
  const straw = new Canvas(256, 1024, [255, 255, 255]);
  const STRIPES = [[255, 150, 190], [140, 222, 190], [180, 158, 244]];
  const PITCH = 0.5; // metres of straw per full turn of the spiral
  straw.shade((u, v) => {
    const p = (((u * 2 + ((v * L) / PITCH) * 2) % 1) + 1) % 1; // two runs of the three colours round the straw
    const band = p * 3, i = Math.floor(band), f = band - i;
    const sep = smooth(0.8, 0.85, f) * (1 - smooth(0.95, 0.99, f)); // a thin cream line at the end of each band
    return STRIPES[i % 3].map((x) => lerp(x, 252, sep));
  });
  const strawMat = { name: 'straw', texture: straw, roughness: 0.4 };
  const strawInside = { name: 'strawInside', color: [0.2, 0.06, 0.1], roughness: 0.6 };

  // ---- the cup --------------------------------------------------------------------------------------------
  // A little foot ring under it, the tapered wall (rows every 2 cm), a stacking ridge, then the rolled rim and a
  // short stretch of the inner wall (the plastic's thickness shows through the front at the top).
  const cup = [
    [0, 0.009], [RB - 0.04, 0.009], [RB - 0.03, 0.004], [RB - 0.022, 0, true], [RB - 0.01, 0.001], [RB - 0.003, 0.006], [rg(0.016), 0.016],
  ];
  for (let y = 0.03; y < WT - 0.13; y += 0.02) cup.push([rg(y), y]);
  const ridgeY = WT - 0.095;
  const ridge = (y) => 0.004 * Math.exp(-(((y - ridgeY) / 0.006) ** 2));
  for (let y = WT - 0.13; y <= WT - 0.0099; y += 0.006) cup.push([rg(y) + ridge(y), y]);
  cup.push(
    [rg(WT), WT], [RT + 0.004, WT + 0.009], [RT + 0.011, WT + 0.013], [RT + 0.014, WT + 0.02], [RT + 0.012, WT + 0.027], [RT + 0.005, WT + 0.031],
    [RT - 0.002, WT + 0.029], [RT - 0.005, WT + 0.021], [RT - 0.006, WT + 0.008], [RT - 0.007, WT - 0.015],
  );
  add(lathe(cup, { segments: 80 }), glass);
  const RIM_TOP = WT + 0.031;

  // Condensation: little beads of water on the cup, only where the cold tea is behind them.
  const D = rng(17);
  const drops = new Mesh();
  for (let k = 0, placed = 0; placed < 44 && k < 600; k++) {
    const a = D() * TAU, y = 0.05 + D() * (TEA_TOP - 0.1);
    const phi = Math.atan2(Math.sin(a), Math.cos(a)); // the front is at -pi/2 (-Z)
    if (Math.abs(phi + Math.PI / 2) < 0.66 && Math.abs(y - FACE_Y) < 0.16) continue; // keep the face's patch clear
    const r = 0.006 + 0.009 * D() ** 2;
    const rad = rg(y) - r * 0.25;
    drops.append(sphere(r, { segments: 10, rows: 6, scale: [1, 1.25, 0.5] }).transform({ rotate: [0, ((Math.PI / 2 - a) * 180) / Math.PI, 0], translate: [rad * Math.cos(a), y, rad * Math.sin(a)] }));
    placed++;
  }
  add(drops, glass);

  // ---- the tea --------------------------------------------------------------------------------------------
  const teaWall = [[0, TEA_BOT], [rt(0.02) - 0.02, TEA_BOT], [rt(0.02) - 0.008, TEA_BOT + 0.002], [rt(0.02) - 0.002, TEA_BOT + 0.008]];
  for (let y = 0.03; y < TEA_TOP - 0.005; y += 0.02) teaWall.push([rt(y), y]);
  teaWall.push([rt(TEA_TOP - 0.004), TEA_TOP - 0.004], [rt(TEA_TOP) - 0.002, TEA_TOP + 0.002, true]); // the meniscus creeps up the wall
  const wallV = (t, y) => (y >= TEA_TOP ? 0.2 : 0.2 + 0.8 * clamp((TEA_TOP - y) / (TEA_TOP - TEA_BOT)));
  add(lathe(teaWall, { segments: 80, v: wallV }), teaMat);
  // The surface: a gentle dip from the meniscus to a flat middle (the polar foam part of the texture).
  const rs = rt(TEA_TOP) - 0.002;
  const surf = [[rs, TEA_TOP + 0.002], [rs - 0.012, TEA_TOP], [rs * 0.75, TEA_TOP - 0.001], [rs * 0.45, TEA_TOP - 0.001], [rs * 0.2, TEA_TOP - 0.001], [0, TEA_TOP - 0.001]];
  add(lathe(surf, { segments: 80, v: (t) => 0.196 * (1 - t) }), teaMat);

  // ---- the lid --------------------------------------------------------------------------------------------
  // It sits on the rim's roll: down the inside of its skirt, round the bottom lip, up the outside, over a bead and
  // a little shoulder, then the dome up to a raised collar round the straw hole, and a short way down the hole.
  const L0 = WT - 0.004; // the skirt's bottom edge
  const Rd = RT + 0.004, yd = L0 + 0.05, Hd = 0.118;
  const lid = [
    [RT + 0.008, L0 + 0.034], [RT + 0.016, L0 + 0.026], [RT + 0.017, L0 + 0.011], [RT + 0.018, L0 + 0.002, true], [RT + 0.023, L0], [RT + 0.026, L0 + 0.005, true],
    [RT + 0.026, L0 + 0.021], [RT + 0.026, L0 + 0.032, true], [RT + 0.03, L0 + 0.035], [RT + 0.031, L0 + 0.04], [RT + 0.028, L0 + 0.045, true], [RT + 0.016, L0 + 0.048], [Rd, yd, true],
  ];
  const collarR = HOLE_R + 0.017;
  const a1 = Math.acos(collarR / Rd);
  for (let k = 1; k <= 24; k++) {
    const a = (a1 * k) / 24;
    lid.push([Rd * Math.cos(a), yd + Hd * Math.sin(a)]);
  }
  const yTop = yd + Hd * Math.sin(a1);
  const HOLE = [0, yTop + 0.007, 0]; // centre of the straw hole, level with the top of its collar
  lid.push([collarR - 0.002, yTop + 0.004], [collarR - 0.006, HOLE[1] - 0.001], [HOLE_R + 0.006, HOLE[1] + 0.001], [HOLE_R + 0.002, HOLE[1] - 0.001], [HOLE_R, HOLE[1] - 0.006, true], [HOLE_R, HOLE[1] - 0.02]);
  add(lathe(lid, { segments: 80 }), glass);
  // A ring of little ribs round the dome's foot, the way snap-on lids are moulded.
  const ribs = new Mesh();
  for (let k = 0; k < 24; k++) {
    const a = (TAU * (k + 0.5)) / 24;
    const r0 = Rd * Math.cos(0.22) + 0.001, y0 = yd + Hd * Math.sin(0.22);
    ribs.append(sphere(1, { segments: 8, rows: 6, scale: [0.006, 0.017, 0.005] }).transform({ rotate: [-55, 0, 0] })
      .transform({ rotate: [0, ((Math.PI / 2 - a) * 180) / Math.PI, 0], translate: [r0 * Math.cos(a), y0, r0 * Math.sin(a)] }));
  }
  add(ribs, glass);

  // ---- the straw ------------------------------------------------------------------------------------------
  // Made standing on +Y with the hole at y = 0, then leant. The top end is open (a thin wall round a dark inside);
  // the bottom end is cut on a slant (it sits in the tea, among the app's pearls).
  const sprof = [[0, -L_BELOW], [SR, -L_BELOW, true]];
  for (let y = -L_BELOW + 0.04; y < L_ABOVE - 0.02; y += 0.04) sprof.push([SR, y]);
  sprof.push([SR, L_ABOVE - 0.008], [SR - 0.0008, L_ABOVE - 0.0015], [SR - SW / 2, L_ABOVE], [SR - SW + 0.0008, L_ABOVE - 0.0015]);
  const strawMesh = lathe(sprof, { segments: 40, v: (t, y) => clamp((L_ABOVE - y) / L) })
    .map(([x, y, z]) => (y <= -L_BELOW + 1e-6 ? [x, y + (x + SR) * 0.9, z] : [x, y, z]));
  const inner = lathe([[SR - SW + 0.0008, L_ABOVE - 0.0015], [SR - SW, L_ABOVE - 0.01], [SR - SW, L_ABOVE - 0.09], [0, L_ABOVE - 0.09]], { segments: 40 });
  const lean = { rotate: TILT, translate: HOLE };
  add(strawMesh.transform(lean), strawMat);
  add(inner.transform(lean), strawInside);
  const dir = new Mesh();
  dir.vertex([0, 1, 0]);
  const [dx, dy, dz] = dir.transform({ rotate: TILT }).point(0);
  const along = (l) => [HOLE[0] + dx * l, HOLE[1] + dy * l, HOLE[2] + dz * l];

  // ---- anchors and notes ----------------------------------------------------------------------------------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const H = max[1] - min[1], s = 1.15 / H;
  return {
    parts,
    face: FACE_Y / H,
    faceSize: 0.8,
    anchors: {
      strawTop: along(L_ABOVE),
      strawBottom: along(-L_BELOW + 0.03),
      cupBottom: [0, TEA_BOT, 0],
      teaTop: [0, TEA_TOP, 0],
    },
    notes: {
      cupInnerRadiusBottom: +(rt(TEA_BOT + 0.01) * s).toFixed(3),
      cupInnerRadiusAtTeaTop: +(rt(TEA_TOP) * s).toFixed(3),
      teaHeight: +((TEA_TOP - TEA_BOT) * s).toFixed(3),
      rimTop: +(RIM_TOP * s).toFixed(3),
      strawRadius: +(SR * s).toFixed(3),
      strawTiltDegrees: 'about 10 (top toward the front and screen left)',
      glassMaterial: 'glass (cup, condensation beads and lid)',
      teaMaterial: 'tea: opaque painted milk tea (foam on top, brown sugar low down); make it see-through in the app if the pearls must show',
    },
  };
}
