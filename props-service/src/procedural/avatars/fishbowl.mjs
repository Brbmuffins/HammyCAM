/**
 * Goldfish bowl: a round glass bowl (about 85% of a sphere) with a flat bottom, a short neck and a flared, rolled
 * lip round a wide opening. Inside: a bed of bright aquarium gravel (sloping up toward the back), a little pastel
 * castle off to the left and a sprig of water weed off to the right, so the front-middle is clear for the face.
 * No water and no fish: the app adds its own animated water and goldfish.
 * Modelled at about app scale (1.15 m tall), so model units are roughly the app's metres.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, loft, box, sphere, cylinder, tube, extrude, ringAngle, rng, fbm2, smooth, lerp, clamp, TAU } = kit;

  // ---- key dimensions -------------------------------------------------------------------------------------
  const R = 0.6; // outer glass radius
  const T = 0.007; // glass thickness (only modelled at the lip)
  // Vertical stretch of the ball: 1 = a true sphere (the bowl is then a little wider than tall, about 1.06 : 1).
  // 1.15 or so makes it taller than wide if the app needs a narrower avatar; everything inside follows.
  const K = 1.15;
  const yc = 0.9 * R * K; // bowl centre: the flat bottom (y = 0) is 0.9 R below it
  const rs = (y) => R * Math.sqrt(Math.max(0, 1 - ((y - yc) / (R * K)) ** 2)); // outer glass radius at height y
  const ri = (y) => rs(y) - T - 0.003; // just inside the glass

  // ---- materials ------------------------------------------------------------------------------------------
  const glass = { name: 'glass', color: [0.85, 0.95, 1, 0.25], blend: true, roughness: 0.05 };

  // Bright aquarium-gravel colours.
  const GRAVEL = [[238, 86, 146], [64, 142, 232], [250, 206, 58], [96, 200, 92], [156, 96, 214], [242, 240, 232], [56, 206, 196], [250, 138, 52], [224, 64, 64]];
  // Pebble cells (jittered grid): which pebble a point is on, and how far from its edge. Shared by both textures.
  const cellHash = (i, j, k) => {
    let h = Math.imul(i, 374761393) ^ Math.imul(j, 668265263) ^ Math.imul(k + 7, 1442695041);
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
  };
  const pebbleAt = (x, y, size, period = 0) => {
    const gx = Math.floor(x / size), gy = Math.floor(y / size);
    let d1 = Infinity, d2 = Infinity, id = 0;
    for (let j = gy - 1; j <= gy + 1; j++)
      for (let i = gx - 1; i <= gx + 1; i++) {
        const hi = period ? ((i % period) + period) % period : i; // cells repeat round the bowl (no seam)
        const cx = (i + 0.15 + 0.7 * cellHash(hi, j, 1)) * size, cy = (j + 0.15 + 0.7 * cellHash(hi, j, 2)) * size;
        const sx = 0.8 + 0.4 * cellHash(hi, j, 3);
        const d = Math.hypot((x - cx) * sx, (y - cy) / sx);
        if (d < d1) [d2, d1, id] = [d1, d, cellHash(hi, j, 4)];
        else if (d < d2) d2 = d;
      }
    return { id, d1: d1 / size, gap: (d2 - d1) / size };
  };
  const pebbleColour = (x, y, size, period) => {
    const { id, d1, gap } = pebbleAt(x, y, size, period);
    const base = GRAVEL[Math.floor(id * GRAVEL.length)];
    const round = 0.6 + 0.5 * (1 - clamp(d1 * 1.5)) ** 0.8; // lit in the middle, darker at the rim
    const shine = clamp(1 - Math.hypot(d1 - 0.12, 0) * 6) * 0.25;
    const grout = smooth(0.03, 0.2, gap); // dark gaps between pebbles
    const k = round * lerp(0.12, 1, grout);
    return [base[0] * k + 255 * shine * grout, base[1] * k + 255 * shine * grout, base[2] * k + 255 * shine * grout];
  };
  const TOP = 0.5; // the top texture covers x, z in [-TOP, TOP]
  const topTex = new Canvas(512, 512, [0, 0, 0]);
  topTex.shade((u, v) => pebbleColour((u - 0.5) * 2 * TOP, (v - 0.5) * 2 * TOP, 0.03));
  const gravelTop = { name: 'gravel_top', texture: topTex, roughness: 0.8 };
  // The side band seen through the glass: u goes round (circumference), v down the bed's wall.
  const CIRC = TAU * 0.46, SIDE_H = 0.2;
  const sideTex = new Canvas(1024, 256, [0, 0, 0]);
  const SIDE_CELLS = Math.round(CIRC / 0.019);
  sideTex.shade((u, v) => pebbleColour(u * CIRC, v * SIDE_H, CIRC / SIDE_CELLS, SIDE_CELLS));
  const gravelSide = { name: 'gravel_side', texture: sideTex, roughness: 0.8 };

  // One palette texture for the pebbles, the castle and the weed (each part's uvs point at one swatch).
  const PAL = {
    wall: [196, 176, 230], wallDark: [160, 138, 204], roof: [246, 116, 140], door: [56, 40, 78], flag: [255, 212, 72],
    pole: [240, 236, 228], weed: [72, 192, 84], weedDark: [40, 140, 64],
  };
  GRAVEL.forEach((c, i) => (PAL['g' + i] = c));
  const palTex = new Canvas(64, 64, [255, 0, 255]);
  const swatch = {};
  Object.entries(PAL).forEach(([k, c], i) => {
    const x = (i % 4) * 16, y = Math.floor(i / 4) * 16;
    palTex.rect(x, y, x + 16, y + 16, c);
    swatch[k] = [(x + 8) / 64, (y + 8) / 64];
  });
  const deco = { name: 'aquarium', texture: palTex, roughness: 0.8 };
  const paint = (mesh, key) => {
    for (let i = 0; i < mesh.vertexCount; i++) [mesh.uvs[i * 2], mesh.uvs[i * 2 + 1]] = swatch[key];
    return mesh;
  };

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });

  /** A lathe-like surface from a function: f(t, angle) gives the point; rings run bottom/outside (t=0) up/in (t=1). */
  const surface = (J, n, f, opts) => {
    const rings = [];
    for (let j = 0; j <= J; j++) {
      const ring = [];
      for (let i = 0; i <= n; i++) ring.push(f(j / J, ringAngle(i % n, n)));
      rings.push(ring);
    }
    return loft(rings, opts);
  };

  // ---- the glass: flat bottom, the ball, a short neck and a rolled lip ---------------------------------
  const rb = rs(0);
  const profile = [[0, 0], [rb - 0.03, 0], [rb - 0.012, 0.0015], [rs(0.006) - 0.002, 0.006]];
  const aLo = Math.asin((0.008 - yc) / (R * K)), aHi = Math.asin(0.8);
  for (let k = 0; k <= 46; k++) {
    const a = aLo + ((aHi - aLo) * k) / 46;
    profile.push([R * Math.cos(a), yc + R * K * Math.sin(a)]);
  }
  const neckY = yc + 0.8 * R * K; // 0.6 R across here
  const lip = [
    // outer neck, pinching in a touch, then flaring out
    [0.349, neckY + 0.02], [0.344, neckY + 0.04], [0.346, neckY + 0.058], [0.356, neckY + 0.073], [0.372, neckY + 0.084], [0.388, neckY + 0.09],
    // the roll over the top
    [0.398, neckY + 0.096], [0.403, neckY + 0.104], [0.401, neckY + 0.112], [0.393, neckY + 0.117], [0.383, neckY + 0.116], [0.374, neckY + 0.11],
    // and down the inside of the neck (this inner wall shows through the front glass)
    [0.363, neckY + 0.099], [0.35, neckY + 0.086], [0.339, neckY + 0.07], [0.336, neckY + 0.052], [0.338, neckY + 0.034], [0.344, neckY + 0.016], [0.352, neckY - 0.002],
  ];
  profile.push(...lip);
  add(lathe(profile, { segments: 72 }), glass);
  const lipTop = neckY + 0.117, openingR = 0.336;

  // ---- the gravel bed -------------------------------------------------------------------------------------
  const CASTLE = [0.265, 0.15], WEED = [-0.25, 0.17];
  const bump = (x, z, [cx, cz], h, s) => h * Math.exp(-((x - cx) ** 2 + (z - cz) ** 2) / (2 * s * s));
  const bed = (x, z) => 0.125 + 0.045 * smooth(-0.4, 0.4, z) + bump(x, z, CASTLE, 0.012, 0.1) + (fbm2(x * 14, z * 14, { octaves: 3, seed: 6 }) - 0.5) * 0.014;
  const bedFloor = 0.012;
  const edgeOf = (a) => {
    let rho = 0.42;
    for (let k = 0; k < 4; k++) rho = ri(bed(rho * Math.cos(a), rho * Math.sin(a)));
    return [rho, bed(rho * Math.cos(a), rho * Math.sin(a))];
  };
  const N = 112, WALL = 8, TOPR = 30;
  const wall = surface(WALL, N, (t, a) => {
    const [, ye] = edgeOf(a);
    const y = lerp(bedFloor, ye, t);
    const r = ri(y);
    return [r * Math.cos(a), y, r * Math.sin(a)];
  }, { capBottom: true });
  for (let i = 0; i < wall.vertexCount; i++) {
    // u stays as loft made it (round the bowl); v measures metres down from the top edge of the band
    const y = wall.positions[i * 3 + 1];
    wall.uvs[i * 2 + 1] = clamp((0.2 - y) / SIDE_H);
  }
  add(wall, gravelSide);
  const top = surface(TOPR, N, (t, a) => {
    const [re] = edgeOf(a);
    const rho = re * (1 - t) ** 0.9;
    const x = rho * Math.cos(a), z = rho * Math.sin(a);
    return [x, bed(x, z), z];
  });
  for (let i = 0; i < top.vertexCount; i++) {
    const [x, , z] = top.point(i);
    top.uvs[i * 2] = 0.5 + x / (2 * TOP);
    top.uvs[i * 2 + 1] = 0.5 + z / (2 * TOP);
  }
  add(top, gravelTop);

  // Loose pebbles scattered on top (for a lumpy, chunky look), clear of the castle and the weed.
  const R3 = rng(41);
  const pebbles = new Mesh();
  let placed = 0;
  for (let tries = 0; placed < 200 && tries < 4000; tries++) {
    const x = (R3() * 2 - 1) * 0.44, z = (R3() * 2 - 1) * 0.44;
    const r = 0.016 + 0.01 * R3();
    const y = bed(x, z);
    if (Math.hypot(x, z) > ri(y + 0.02) - r - 0.006) continue;
    if (Math.hypot(x - CASTLE[0], z - CASTLE[1]) < 0.1 || Math.hypot(x - WEED[0], z - WEED[1]) < 0.035) continue;
    const p = sphere(r, { segments: 7, rows: 4, scale: [1, 0.62, 0.85] }).transform({ rotate: [R3() * 20 - 10, R3() * 360, R3() * 20 - 10], translate: [x, y + r * 0.22, z] });
    pebbles.append(paint(p, 'g' + Math.floor(R3() * GRAVEL.length)));
    placed++;
  }
  add(pebbles, deco);

  // ---- the castle (left, toward the back) ----------------------------------------------------------------
  const castle = new Mesh();
  const tower = (cx, cz, r, h, roofH, phase) => {
    castle.append(paint(lathe([[0, -0.03], [r, -0.03, true], [r, h, true], [r * 1.14, h + 0.006], [r * 1.14, h + 0.02, true], [0, h + 0.02]], { segments: 24 }).transform({ translate: [cx, 0, cz] }), 'wall'));
    for (let k = 0; k < 8; k++) {
      const a = (TAU * (k + 0.5)) / 8 + phase;
      castle.append(paint(box(r * 0.42, 0.018, 0.012, { round: 0.002, steps: 1 }).transform({ rotate: [0, (-a * 180) / Math.PI + 90, 0], translate: [cx + r * 1.06 * Math.cos(a), h + 0.028, cz + r * 1.06 * Math.sin(a)] }), 'wallDark'));
    }
    castle.append(paint(lathe([[0, h + 0.02], [r * 0.95, h + 0.02, true], [r * 0.5, h + 0.02 + roofH * 0.55], [0, h + 0.02 + roofH]], { segments: 24 }).transform({ translate: [cx, 0, cz] }), 'roof'));
    return h + 0.02 + roofH;
  };
  const [kx, kz] = CASTLE;
  const mainTop = tower(0, 0, 0.046, 0.15, 0.085, 0);
  tower(0.058, 0.05, 0.034, 0.1, 0.065, 0.3);
  // A wall between the towers with merlons.
  castle.append(paint(box(0.07, 0.07, 0.03, { round: 0.003, steps: 1 }).transform({ rotate: [0, -41, 0], translate: [0.03, 0.025, 0.026] }), 'wall'));
  // Arched door and slit windows on the side facing the camera (and the middle of the bowl).
  const arch = (w, h) => {
    const pts = [[w / 2, 0]];
    for (let k = 0; k <= 10; k++) pts.push([(w / 2) * Math.cos((Math.PI * k) / 10), h - w / 2 + (w / 2) * Math.sin((Math.PI * k) / 10)]);
    pts.push([-w / 2, 0]);
    return extrude(pts, 0.008);
  };
  const facing = 0.42; // the doorway is turned a little toward the middle of the bowl (screen right)
  const onTower = (mesh, r, y) => mesh.transform({ rotate: [0, (facing * 180) / Math.PI, 0], translate: [-Math.sin(facing) * (r - 0.002), y, -Math.cos(facing) * (r - 0.002)] });
  castle.append(paint(onTower(arch(0.03, 0.05), 0.046, -0.004), 'door'));
  castle.append(paint(onTower(arch(0.012, 0.026), 0.046, 0.09), 'door'));
  // A little flag on the main tower.
  castle.append(paint(tube([[0, mainTop - 0.01, 0], [0, mainTop + 0.05, 0]], 0.0025, { segments: 6 }), 'pole'));
  castle.append(paint(extrude([[0, 0], [0.04, -0.012], [0, -0.024]], 0.003).transform({ translate: [0.002, mainTop + 0.05, 0] }), 'flag'));
  const CS = 0.95;
  castle.transform({ scale: CS, rotate: [0, 0, 0], translate: [kx, bed(kx, kz) - 0.004, kz] });
  add(castle, deco);
  const castleTop = bed(kx, kz) + (mainTop + 0.05) * CS;

  // ---- the water weed (right, toward the back) ----------------------------------------------------------
  const weed = new Mesh();
  const W3 = rng(7);
  const [wx, wz] = WEED;
  const wy = bed(wx, wz) - 0.01;
  let weedTop = 0;
  const outward = Math.atan2(wz, wx); // fan the stems out away from the middle of the bowl
  for (let s = 0; s < 5; s++) {
    const h = [0.27, 0.2, 0.16, 0.23, 0.13][s], lean = 0.45 + 0.4 * W3(), dir = outward + (s - 2) * 0.75 + (W3() - 0.5) * 0.3;
    const ox = Math.cos(dir) * 0.012, oz = Math.sin(dir) * 0.012;
    const path = [];
    for (let k = 0; k <= 12; k++) {
      const t = k / 12;
      const sway = Math.sin(t * 3 + s) * 0.02 * t;
      path.push([wx + ox + Math.cos(dir) * lean * h * 0.5 * t * t + sway, wy + h * t * (1 - 0.12 * lean * t), wz + oz + Math.sin(dir) * lean * h * 0.5 * t * t]);
    }
    weed.append(paint(tube(path, 0.004, { segments: 6, radii: path.map((_, k) => 0.0045 * (1 - (k / 12) * 0.6)) }), 'weedDark'));
    weedTop = Math.max(weedTop, wy + h);
    // Pairs of little leaves up the stem, turning as they go.
    for (let k = 2; k <= 12; k++) {
      const p = path[k], tt = k / 12;
      for (const side of [0, Math.PI]) {
        const yaw = k * 1.3 + side + s;
        const len = 0.034 * (1 - 0.45 * tt);
        const leaf = sphere(1, { segments: 6, rows: 4, scale: [0.0105, len, 0.004] }).transform({ translate: [0, len * 0.9, 0] })
          .transform({ rotate: [0, 0, -55 + 15 * tt] }).transform({ rotate: [0, (yaw * 180) / Math.PI, 0], translate: p });
        weed.append(paint(leaf, k % 2 ? 'weed' : 'weedDark'));
      }
    }
  }
  add(weed, deco);

  // ---- anchors and notes ----------------------------------------------------------------------------------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const H = max[1] - min[1], s = 1.15 / H;
  const gravelY = bed(0, 0);
  return {
    parts,
    face: (yc + 0.03) / H,
    faceSize: 0.62,
    anchors: { bowlCentre: [0, yc, 0], opening: [0, lipTop, 0], gravelTop: [0, gravelY, 0] },
    notes: {
      bowlInnerRadius: +((R - T) * s).toFixed(3),
      ...(K !== 1 ? { bowlInnerHalfHeight: +((R * K - T) * s).toFixed(3) } : {}),
      openingRadius: +(openingR * s).toFixed(3),
      lipOuterRadius: +(0.403 * s).toFixed(3),
      waterLevel: +(0.78 * H * s).toFixed(3),
      waterRadiusAtLevel: +((rs(0.78 * H) - T) * s).toFixed(3),
      gravelTopFront: +(bed(0, -0.3) * s).toFixed(3),
      gravelTopBack: +(bed(0, 0.3) * s).toFixed(3),
      castleTop: +(castleTop * s).toFixed(3),
      weedTop: +(weedTop * s).toFixed(3),
      glassMaterial: 'glass',
      hollow: 'open glass shell, single wall except the rolled lip and the inside of the neck; flat glass bottom on the desk',
    },
  };
}
