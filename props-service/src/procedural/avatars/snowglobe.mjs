/**
 * Snow globe: a big glass ball (about 80% of a sphere showing) sitting in a turned dark-wood base with a gold band
 * and a brass "HAMMY" plaque on the front. Inside, low down: a snowy mound with a little pine tree off to the left
 * and a tiny red cottage with glowing windows off to the right, so the middle of the glass is clear for the face.
 * No snow or glitter here: the app swirls its own inside the glass.
 * Modelled at app scale (1.15 m tall), so model units are roughly the app's metres.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, loft, box, sphere, cylinder, extrude, ringAngle, fbm2, fbm3, smooth, lerp, clamp, TAU } = kit;

  // ---- key dimensions -------------------------------------------------------------------------------------
  const R = 0.45; // glass radius (single shell)
  const yc = 0.7; // globe centre (the glass top is at 1.15)
  const lipY = 0.442; // top of the base's lip; the glass shows from here up (about 79% of the ball)
  const cutY = 0.43; // the glass ends here, buried in the lip
  const rg = (y) => Math.sqrt(Math.max(0, R * R - (y - yc) ** 2)); // glass radius at height y

  // ---- materials ------------------------------------------------------------------------------------------
  const glass = { name: 'glass', color: [0.85, 0.95, 1, 0.25], blend: true, roughness: 0.05 };
  const gold = { name: 'gold', color: [0.78, 0.45, 0.1], roughness: 0.35, metallic: 0.85 };

  // Dark turned wood: wavy grain running round the base, a satin finish.
  const woodTex = new Canvas(1024, 256, [86, 42, 28]);
  woodTex.shade((u, v) => {
    // noise sampled round a circle in u, so the grain meets itself at the seam at the back
    const cu = Math.cos(u * TAU), su = Math.sin(u * TAU);
    const warp = fbm3(cu * 1.0, su * 1.0, v * 3, { octaves: 4, seed: 3 }) * 5;
    const ring = 0.5 + 0.5 * Math.sin((v * 34 + warp) * Math.PI);
    const fine = fbm3(cu * 14, su * 14, v * 12, { octaves: 2, seed: 8 });
    const k = 0.78 + 0.22 * ring ** 3 + 0.08 * (fine - 0.5);
    return [102 * k, 52 * k, 34 * k];
  });
  const wood = { name: 'wood', texture: woodTex, roughness: 0.8 };

  // Brass plaque: brushed, a bevel-ish frame line, engraved lettering, a screw at each end.
  const pl = new Canvas(512, 176, [206, 164, 76]);
  pl.shade((u, v) => {
    const brush = fbm2(u * 2, v * 120, { octaves: 2, seed: 5 });
    const k = 0.9 + 0.18 * (1 - v) + 0.1 * (brush - 0.5);
    return [214 * k, 170 * k, 80 * k];
  });
  pl.rect(0, 0, 512, 10, [150, 108, 40], 0.7).rect(0, 166, 512, 176, [130, 92, 34], 0.8);
  pl.rect(0, 0, 10, 176, [150, 108, 40], 0.7).rect(502, 0, 512, 176, [130, 92, 34], 0.8);
  pl.rect(24, 20, 488, 24, [120, 84, 30], 0.75).rect(24, 152, 488, 156, [120, 84, 30], 0.75);
  pl.rect(24, 20, 28, 156, [120, 84, 30], 0.75).rect(484, 20, 488, 156, [120, 84, 30], 0.75);
  pl.text('HAMMY', 256, 49, 80, [250, 226, 160], { weight: 0.2, spacing: 0.22, a: 0.5 }); // a lit lower edge to the engraving
  pl.text('HAMMY', 256, 47, 80, [70, 42, 16], { weight: 0.2, spacing: 0.22 });
  for (const sx of [50, 462]) {
    pl.disc(sx, 88, 12, [120, 84, 30]).disc(sx, 88, 9, [228, 196, 120]);
    pl.stroke([[sx - 6, 84], [sx + 6, 92]], 3, [110, 76, 26]);
  }
  const brass = { name: 'brass', texture: pl, roughness: 0.35, metallic: 0.75 };

  // Snow: soft white with faint blue speckle and a few sparkles.
  const snowTex = new Canvas(256, 256, [244, 247, 255]);
  snowTex.speckle(900, [0.6, 1.6], [190, 208, 240], 0.35, 11).speckle(160, [0.5, 1.1], [255, 255, 255], 0.9, 12);
  const snow = { name: 'snow', texture: snowTex, roughness: 0.9 };

  // One palette texture for the little scene (each part's uvs point at one swatch).
  const PAL = {
    pine: [44, 118, 74], pineDark: [30, 86, 56], trunk: [112, 72, 42], wall: [196, 64, 52], roof: [92, 56, 40],
    door: [74, 42, 30], trim: [244, 238, 228], brick: [150, 72, 56], chimneyDark: [60, 40, 36],
  };
  const palTex = new Canvas(64, 64, [255, 0, 255]);
  const swatch = {};
  Object.entries(PAL).forEach(([k, c], i) => {
    const x = (i % 4) * 16, y = Math.floor(i / 4) * 16;
    palTex.rect(x, y, x + 16, y + 16, c);
    swatch[k] = [(x + 8) / 64, (y + 8) / 64];
  });
  const scene = { name: 'scene', texture: palTex, roughness: 0.85 };
  const glow = { name: 'window_glow', color: [1, 0.84, 0.46], emissive: [0.85, 0.55, 0.18], roughness: 0.6 };
  const paint = (mesh, key) => {
    for (let i = 0; i < mesh.vertexCount; i++) [mesh.uvs[i * 2], mesh.uvs[i * 2 + 1]] = swatch[key];
    return mesh;
  };

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });

  /** A lathe-like surface from a function: f(t, angle) gives the point; rings run bottom/outside (t=0) up/in (t=1). */
  const surface = (J, n, f) => {
    const rings = [];
    for (let j = 0; j <= J; j++) {
      const ring = [];
      for (let i = 0; i <= n; i++) ring.push(f(j / J, ringAngle(i % n, n)));
      rings.push(ring);
    }
    return loft(rings);
  };

  // ---- the base -------------------------------------------------------------------------------------------
  const frustum = (y) => lerp(0.38, 0.344, (y - 0.062) / (0.238 - 0.062));
  add(
    lathe([
      [0, 0], [0.395, 0, true], [0.405, 0.006], [0.41, 0.018], [0.409, 0.032], [0.402, 0.042], [0.39, 0.048, true], // foot roll
      [0.384, 0.05], [0.382, 0.058, true],
      [0.38, 0.062], [frustum(0.15), 0.15], [0.344, 0.238, true], // the plinth (plaque goes here)
      [0.34, 0.262], [0.338, 0.287, true], // under the gold band
      [0.342, 0.3], [0.35, 0.322], [0.361, 0.348], [0.372, 0.376], [0.381, 0.402], [0.386, 0.422], [0.385, 0.433], [0.38, 0.44], [0.372, lipY, true], // cup
      [rg(lipY) - 0.004, lipY, true], [rg(lipY) - 0.006, lipY - 0.004], [0, lipY - 0.004],
    ], { segments: 96, v: (t, y) => 1 - y / lipY }),
    wood,
  );
  // Gold band round the waist and a thin gold bead on the lip where the glass sits.
  add(lathe([[0.336, 0.236, true], [0.348, 0.24], [0.354, 0.248], [0.355, 0.262], [0.354, 0.276], [0.348, 0.284], [0.336, 0.289, true]], { segments: 96 }), gold);
  add(lathe([[0.37, lipY - 0.002, true], [0.376, lipY], [0.378, lipY + 0.004], [0.374, lipY + 0.008], [0.368, lipY + 0.009], [rg(lipY + 0.008) - 0.003, lipY + 0.008, true], [rg(lipY + 0.004) - 0.004, lipY + 0.002]], { segments: 96 }), gold);

  // Brass plaque on the plinth front, following its round and its slope.
  const plaqueY = 0.15, pR0 = frustum(plaqueY), tilt = Math.atan((0.38 - 0.344) / (0.238 - 0.062));
  const plaque = box(0.29, 0.096, 0.018, { round: 0.004, steps: 8 }).transform({ translate: [0, 0, 0.001] }).map(([x, y, z]) => {
    const rad = pR0 - z, th = x / pR0;
    return [rad * Math.sin(th), y, -rad * Math.cos(th) + pR0];
  }).transform({ rotate: [(tilt * 180) / Math.PI, 0, 0], translate: [0, plaqueY, -pR0] });
  add(plaque, brass);

  // ---- the glass ------------------------------------------------------------------------------------------
  const a0 = Math.asin((cutY - yc) / R);
  const arc = [];
  for (let k = 0; k <= 40; k++) {
    const a = a0 + ((Math.PI / 2 - a0) * k) / 40;
    arc.push([k === 40 ? 0 : R * Math.cos(a), yc + R * Math.sin(a)]);
  }
  add(lathe(arc, { segments: 80 }), glass);

  // ---- the snowy mound ------------------------------------------------------------------------------------
  const TREE = [0.225, 0.1], TREE2 = [0.3, -0.04], HOUSE = [-0.215, 0.12];
  const bump = (x, z, [cx, cz], h, s) => h * Math.exp(-((x - cx) ** 2 + (z - cz) ** 2) / (2 * s * s));
  const ground = (x, z) => {
    const rho = Math.hypot(x, z);
    return lipY + 0.006 + 0.03 * (1 - clamp(rho / 0.37) ** 2) + 0.03 * smooth(-0.1, 0.35, z) * clamp(rho / 0.37) ** 2
      + bump(x, z, TREE, 0.022, 0.1) + bump(x, z, TREE2, 0.01, 0.06) + bump(x, z, HOUSE, 0.016, 0.11) + (fbm2(x * 9 + 3, z * 9, { octaves: 3, seed: 2 }) - 0.5) * 0.014;
  };
  const edgeRho = (a) => {
    let rho = 0.366;
    for (let k = 0; k < 4; k++) rho = rg(ground(rho * Math.cos(a), rho * Math.sin(a))) - 0.005;
    return rho;
  };
  add(
    surface(26, 96, (t, a) => {
      if (t === 0) return [0.356 * Math.cos(a), cutY + 0.002, 0.356 * Math.sin(a)]; // skirt, tucked inside the lip
      const rho = edgeRho(a) * (1 - (t - 1 / 26) / (1 - 1 / 26)) ** 0.85;
      const x = rho * Math.cos(a), z = rho * Math.sin(a);
      return [x, ground(x, z), z];
    }),
    snow,
  );

  // ---- two pine trees (left, toward the back) ------------------------------------------------------------
  let treeTop = [0, 0, 0];
  const pine = ([tx, tz], sc, seed) => {
    const ty = ground(tx, tz) - 0.008 * sc;
    add(paint(cylinder(0.013 * sc, 0.04 * sc, { segments: 12 }).transform({ translate: [tx, ty, tz] }), 'trunk'));
    const tiers = [[0.026, 0.07, 0.078], [0.064, 0.055, 0.066], [0.098, 0.04, 0.058]]; // [base height, radius, height]
    tiers.forEach(([b, r, h], k) => {
      [b, r, h] = [b * sc, r * sc, h * sc];
      const yb = ty + b, ph = k + seed;
      const tierAt = (t, a) => {
        const scallop = 1 + 0.12 * Math.cos(7 * a + ph) * (1 - t);
        const rr = r * (1 - t) ** 0.95 * scallop;
        return [tx + rr * Math.cos(a), yb + h * t - 0.01 * sc * (1 - t) ** 3 * (0.5 + 0.5 * Math.cos(7 * a + ph)), tz + rr * Math.sin(a)];
      };
      // Underside (from the trunk out to the rim), then the cone up to the tip.
      add(paint(surface(10, 42, (t, a) => {
        if (t < 0.2) { const rr = r * 0.6 * (t / 0.2); return [tx + rr * Math.cos(a), yb + 0.006 * sc - 0.004 * sc * (t / 0.2), tz + rr * Math.sin(a)]; }
        return tierAt((t - 0.2) / 0.8, a);
      }), k === 0 ? 'pineDark' : 'pine'), scene);
      // A snow cap on the top part of each tier with a drippy lower edge.
      add(surface(6, 42, (t, a) => {
        const t0 = 0.42 + 0.1 * Math.sin(5 * a + ph * 2) + 0.05 * Math.sin(11 * a + seed);
        const p = tierAt(lerp(t0, 1, t), a);
        const dx = p[0] - tx, dz = p[2] - tz, l = Math.hypot(dx, dz) || 1;
        const off = (0.0035 * (1 - t) + 0.001) * sc;
        return [p[0] + (dx / l) * off, p[1] + 0.003 * sc, p[2] + (dz / l) * off];
      }), snow);
      if (yb + h > treeTop[1]) treeTop = [tx, yb + h + 0.003 * sc, tz];
    });
  };
  pine(TREE, 1.25, 0);
  pine(TREE2, 0.8, 3);

  // ---- the cottage (right, toward the back), turned a little toward the camera ---------------------------
  const [hx, hz] = HOUSE, hy = ground(hx, hz) - 0.012;
  const house = [];
  const put = (mesh, material) => house.push({ mesh, material });
  const W = 0.11, D = 0.1, WH = 0.062, RH = 0.034; // width, depth, wall height, gable rise
  put(paint(extrude([[W / 2, 0], [W / 2, WH], [0, WH + RH], [-W / 2, WH], [-W / 2, 0]], D), 'wall'), scene);
  // Roof: two slabs along the slopes, each with a thick rounded layer of snow on top.
  const slope = Math.atan2(RH, W / 2), slab = Math.hypot(W / 2, RH) + 0.016;
  for (const side of [1, -1]) {
    const n = [side * Math.sin(slope), Math.cos(slope)]; // the slope's outward normal
    const mid = [side * (W / 4) + n[0] * 0.0055, WH + RH / 2 + n[1] * 0.0055, 0];
    const rot = { rotate: [0, 0, (-side * slope * 180) / Math.PI] };
    put(paint(box(slab, 0.009, D + 0.026, { round: 0.002, steps: 1 }).transform(rot).transform({ translate: mid }), 'roof'), scene);
    const up = [n[0] * 0.011, n[1] * 0.011, 0];
    put(box(slab - 0.006, 0.014, D + 0.016, { round: 0.006, steps: 2 }).transform(rot).transform({ translate: [mid[0] + up[0], mid[1] + up[1], 0] }), snow);
  }
  // Chimney on the left slope with a snow cap.
  put(paint(box(0.017, 0.05, 0.017, { round: 0.002, steps: 1 }).transform({ translate: [0.03, WH + RH - 0.004, 0.02] }), 'brick'), scene);
  put(box(0.024, 0.01, 0.024, { round: 0.004, steps: 2 }).transform({ translate: [0.03, WH + RH + 0.024, 0.02] }), snow);
  // Door, windows (glowing) and their white frames on the front; a window on each side.
  const fz = -D / 2;
  put(paint(box(0.026, 0.04, 0.006, { round: 0.002, steps: 1 }).transform({ translate: [-0.016, 0.02, fz] }), 'door'), scene);
  put(paint(box(0.032, 0.046, 0.004, { round: 0.001, steps: 1 }).transform({ translate: [-0.016, 0.022, fz + 0.001] }), 'trim'), scene);
  for (const [x, y] of [[0.028, 0.034], [0, WH + 0.012]]) {
    put(paint(box(0.026, 0.026, 0.004, { round: 0.001, steps: 1 }).transform({ translate: [x, y, fz] }), 'trim'), scene);
    put(box(0.019, 0.019, 0.006, { round: 0.001, steps: 1 }).transform({ translate: [x, y, fz - 0.0005] }), glow);
    put(paint(box(0.003, 0.02, 0.0075, { steps: 1 }).transform({ translate: [x, y, fz - 0.0005] }), 'trim'), scene);
    put(paint(box(0.02, 0.003, 0.0075, { steps: 1 }).transform({ translate: [x, y, fz - 0.0005] }), 'trim'), scene);
  }
  for (const sx of [1, -1]) {
    put(paint(box(0.004, 0.026, 0.03, { round: 0.001, steps: 1 }).transform({ translate: [sx * W / 2, 0.034, 0] }), 'trim'), scene);
    put(box(0.006, 0.019, 0.022, { round: 0.001, steps: 1 }).transform({ translate: [sx * (W / 2 + 0.0005), 0.034, 0] }), glow);
  }
  const houseYaw = -28; // its front turned toward the middle, showing its outer side wall too
  const HS = 1.25; // house scale
  for (const { mesh, material } of house) add(mesh.transform({ scale: HS, rotate: [0, houseYaw, 0], translate: [hx, hy, hz] }), material);
  const houseTop = hy + (WH + RH + 0.029) * HS;

  // Scene parts painted with a swatch were added without a material above: give them the scene material.
  for (const p of parts) if (!p.material) p.material = scene;

  // ---- anchors and notes ----------------------------------------------------------------------------------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const s = 1.15 / (max[1] - min[1]);
  const sceneTop = treeTop[1] > houseTop ? treeTop : [hx, houseTop, hz];
  return {
    parts,
    face: (yc + 0.025) / (max[1] - min[1]),
    faceSize: 0.7,
    anchors: { globeCentre: [0, yc, 0], sceneTop, plaque: [0, plaqueY, -pR0 - 0.008] },
    notes: {
      globeInnerRadius: +((R - 0.004) * s).toFixed(3),
      glassMaterial: 'glass',
      hollow: 'glass is a single outward shell from the base lip (y=' + (lipY * s).toFixed(3) + ') up over the top; the snowy floor sits just above the lip',
    },
  };
}
