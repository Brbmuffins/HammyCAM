/**
 * Feather pillow: a plump bed pillow standing on its end, in striped ticking, with corded piping round the seam,
 * puffy corners, soft wrinkles, and the classic "DO NOT REMOVE" tag hanging off the bottom corner.
 * In the app (FeatherPillow) it leaks the odd feather and bursts in a snowstorm of them.
 */
export default function build(kit) {
  const { Mesh, Canvas, tube, box, outward, fbm2, smooth, TAU } = kit;
  const W = 0.56, H = 0.74, D = 0.31; // width, height, thickness in the middle
  const cy = H / 2 + 0.012;

  /** The pillow's outline (front view): squarish, edges pulled in a little, puffy ears at the corners. */
  const outline = (phi) => {
    const c = Math.cos(phi), s = Math.sin(phi), p = 4.2;
    const r = 1 / Math.pow(Math.abs(c) ** p + Math.abs(s) ** p, 1 / p);
    const ear = 1 + 0.1 * Math.abs(Math.sin(2 * phi)) ** 10;
    const sag = 1 - 0.04 * Math.cos(2 * phi) ** 2;
    return r * ear * sag;
  };

  function sheet(side) {
    const mesh = new Mesh();
    const RINGS = 44, AROUND = 112;
    const rows = [];
    for (let j = 0; j <= RINGS; j++) {
      const rho = 1 - (1 - j / RINGS) ** 1.7; // finer toward the seam, where it curves most
      const row = [];
      for (let i = 0; i <= AROUND; i++) {
        const phi = (TAU * i) / AROUND;
        const R = outline(phi);
        const x = rho * R * Math.cos(phi) * (W / 2), y = cy + rho * R * Math.sin(phi) * (H / 2);
        // Plump in the middle, pinching to the seam; soft lumps and creases running in from the corners.
        const crease = 0.05 * Math.abs(Math.sin(2 * phi)) ** 6 * smooth(0.55, 1, rho);
        const lump = (fbm2(x * 7 + side * 3, y * 7, { octaves: 3, seed: 4 }) - 0.5) * 0.12;
        const z = side * (D / 2) * Math.pow(Math.max(0, 1 - rho ** 2.4), 0.5) * (1 + lump - crease);
        row.push(mesh.vertex([x, y, z]));
      }
      rows.push(row);
    }
    for (let j = 0; j < RINGS; j++)
      for (let i = 0; i < AROUND; i++) {
        const p = rows[j][i], q = rows[j][i + 1], r = rows[j + 1][i + 1], s = rows[j + 1][i];
        mesh.tri(p, q, r).tri(p, r, s);
      }
    outward(mesh, [0, cy, 0]);
    return mesh.planarUV({ min: [-W / 2 - 0.05, 0, 0], max: [W / 2 + 0.05, H + 0.03, 0] });
  }

  // Ticking: cream cotton with navy stripes of two widths, a fine weave, and soft shading in the creases.
  const fabric = new Canvas(512, 640, [236, 229, 212]);
  // Soft blue rather than navy, so the dark cartoon face still reads on top of the stripes.
  const NAVY = [96, 126, 182], PALE = [170, 186, 214];
  for (let x = 0; x < 512; x += 64) {
    fabric.rect(x + 8, 0, x + 15, 640, NAVY, 0.85);
    fabric.rect(x + 22, 0, x + 25, 640, NAVY, 0.85);
    fabric.rect(x + 44, 0, x + 46, 640, PALE, 0.7);
  }
  fabric.shade((u, v) => {
    const weave = (Math.sin(u * 512 * 1.6) * Math.sin(v * 640 * 1.6)) * 0.5 + 0.5;
    const blotch = fbm2(u * 6, v * 6, { octaves: 3, seed: 9 });
    return [255 * (0.9 + 0.1 * weave), 255 * (0.88 + 0.1 * weave), 255 * (0.84 + 0.1 * weave), 0.06 + 0.06 * blotch];
  });
  // Creases running in from the four corners (painted shading sells the softness).
  for (const [cx, cy0, dx, dy] of [[30, 30, 1, 1], [482, 30, -1, 1], [30, 610, 1, -1], [482, 610, -1, -1]])
    for (let k = 0; k < 3; k++) {
      const spread = (k - 1) * 0.35;
      const pts = Array.from({ length: 12 }, (_, t) => [cx + dx * t * 9 + spread * t * 6 * dy, cy0 + dy * t * 9 - spread * t * 6 * dx]);
      fabric.stroke(pts, 6, [120, 112, 100], 0.12);
    }
  const ticking = { name: 'ticking', texture: fabric, roughness: 0.95 };

  const parts = [{ mesh: sheet(-1), material: ticking }, { mesh: sheet(1), material: ticking }];

  // Corded piping round the seam.
  const seam = [];
  for (let i = 0; i <= 160; i++) {
    const phi = (TAU * i) / 160;
    const R = outline(phi) * 1.006;
    seam.push([R * Math.cos(phi) * (W / 2), cy + R * Math.sin(phi) * (H / 2), 0]);
  }
  parts.push({ mesh: tube(seam, 0.014, { segments: 10 }), material: { name: 'piping', color: [0.04, 0.07, 0.2], roughness: 0.9 } });

  // The tag, hanging off the bottom-right corner (screen right is -X), tilted out a little.
  const tag = new Canvas(160, 256, [250, 249, 244]);
  tag.rect(0, 0, 160, 18, [214, 60, 52]);
  tag.text('DO NOT', 80, 52, 30, [40, 40, 50], { weight: 0.15 });
  tag.text('REMOVE', 80, 98, 30, [40, 40, 50], { weight: 0.15 });
  for (let y = 150; y < 240; y += 16) tag.rect(18, y, 142, y + 4, [150, 150, 160], 0.8);
  const tagMesh = box(0.085, 0.13, 0.004, { round: 0.002, steps: 1 }).transform({ rotate: [8, -12, -14], translate: [-W / 2 + 0.005, 0.025, -0.07] });
  parts.push({ mesh: tagMesh, material: { name: 'tag', texture: tag, roughness: 0.9 } });

  // The seam corners the feathers leak from, and the middle of the pillow (the burst).
  const corner = (phi) => [outline(phi) * Math.cos(phi) * (W / 2), cy + outline(phi) * Math.sin(phi) * (H / 2), 0];
  return {
    parts,
    face: 0.56,
    faceSize: 0.82,
    anchors: { topLeftCorner: corner(Math.PI * 0.25), topRightCorner: corner(Math.PI * 0.75), centre: [0, cy, 0] },
  };
}
