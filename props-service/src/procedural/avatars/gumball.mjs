/**
 * Gumball machine: a red cast-metal pedestal with a fluted bell body, a chrome coin mechanism on the front (a
 * round boss with a T-handle turn knob, a coin slot and a little "1¢" enamel plate), a chrome chute with a hinged
 * flap low down, a big glass globe on a chrome seat ring, and a red domed lid with a chrome knob.
 * No gumballs here: the app fills the globe with its own animated ones (they rest on the chrome floor inside).
 * Modelled at app scale (1.15 m tall), so model units are roughly the app's metres.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, box, cylinder, sphere, tube, smooth, TAU } = kit;

  // ---- key dimensions -------------------------------------------------------------------------------------
  const R = 0.31; // globe radius (single glass shell)
  const ringTop = 0.484; // top of the chrome seat ring the globe sits in
  const floorY = 0.48; // the chrome floor inside the globe (gumballs rest on it)
  const cutY = 0.47; // the glass ends here, buried inside the seat ring
  const rc = 0.19; // glass radius at cutY
  const yc = cutY + Math.sqrt(R * R - rc * rc); // globe centre
  const rt = 0.125; // glass radius where the lid covers it
  const topCut = yc + Math.sqrt(R * R - rt * rt);
  const rg = (y) => Math.sqrt(Math.max(0, R * R - (y - yc) ** 2)); // glass radius at height y

  // ---- materials ------------------------------------------------------------------------------------------
  const red = { name: 'red_enamel', color: [0.62, 0.012, 0.012], roughness: 0.5, metallic: 0.2 }; // glossy painted cast metal
  const chrome = { name: 'chrome', color: [0.7, 0.72, 0.77], roughness: 0.3, metallic: 0.85 };
  const glass = { name: 'glass', color: [0.85, 0.95, 1, 0.25], blend: true, roughness: 0.05 };
  const dark = { name: 'dark', color: [0.008, 0.006, 0.008], roughness: 0.9 };

  const label = new Canvas(256, 128, [246, 238, 214]);
  label.rect(0, 0, 256, 128, [176, 24, 28]);
  label.rect(9, 9, 247, 119, [246, 238, 214]);
  label.rect(16, 16, 240, 112, [176, 24, 28]);
  label.rect(19, 19, 237, 109, [246, 238, 214]);
  label.text('1¢', 128, 30, 68, [150, 18, 24], { weight: 0.2, spacing: 0.12 });
  for (const sx of [46, 210]) {
    // little five-point stars either side
    const pts = [];
    for (let k = 0; k < 10; k++) {
      const a = -Math.PI / 2 + (k * Math.PI) / 5, rr = k % 2 ? 7 : 16;
      pts.push([sx + rr * Math.cos(a), 66 + rr * Math.sin(a)]);
    }
    label.poly(pts, [214, 160, 40]);
  }
  label.shade((u, v) => [255, 255, 255, 0.08 * (1 - v)]); // a soft enamel sheen
  const labelMat = { name: 'label', texture: label, roughness: 0.6 };

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });

  /** Wraps a part made around the origin (front = -z) onto the base's round front: x becomes an arc. */
  const bend = (mesh, R0, y0) =>
    mesh.map(([x, y, z]) => {
      const rad = R0 - z, th = x / R0;
      return [rad * Math.sin(th), y + y0, -rad * Math.cos(th)];
    });

  // ---- the pedestal ---------------------------------------------------------------------------------------
  const bodyLo = 0.085, bodyHi = 0.4;
  const bodyR = (y) => {
    const t = (y - bodyLo) / (bodyHi - bodyLo);
    return 0.208 + 0.036 * (1 - t) ** 2.2 + 0.004 * t ** 3;
  };
  const profile = [
    [0, 0], [0.262, 0, true],
    [0.272, 0.006], [0.277, 0.017], [0.277, 0.029], [0.271, 0.039], [0.26, 0.046, true], // foot roll
    [0.25, 0.048], [0.248, 0.057, true], // step
    [0.252, 0.061], [0.256, 0.069], [0.253, 0.077], [0.245, 0.082, true], // bead
  ];
  for (let k = 0; k <= 20; k++) {
    const y = bodyLo + ((bodyHi - bodyLo) * k) / 20;
    profile.push([bodyR(y), y]);
  }
  profile.push(
    [0.222, 0.404, true], [0.229, 0.409], [0.232, 0.418], [0.229, 0.427], [0.221, 0.432, true], // top bead
    [0.215, 0.435], [0.212, 0.445, true], [0, 0.445],
  );
  const FLUTES = 24;
  const fluteMask = (y) => smooth(bodyLo, bodyLo + 0.03, y) * (1 - smooth(bodyHi - 0.03, bodyHi, y));
  add(
    lathe(profile, {
      segments: FLUTES * 5,
      section: (t, a, r, y) => {
        const g = 0.5 - 0.5 * Math.cos(FLUTES * a); // 0 on a rib, 1 in a groove (a rib faces the front)
        const rr = r * (1 - 0.045 * fluteMask(y) * g ** 2.2);
        return [rr * Math.cos(a), rr * Math.sin(a)];
      },
    }),
    red,
  );

  // Chrome seat ring the globe sits in, with the floor of the globe (gumballs rest on it).
  add(
    lathe([
      [0.205, 0.436, true], [0.214, 0.441], [0.222, 0.449], [0.225, 0.462], [0.223, 0.474], [0.217, 0.481], [0.21, ringTop, true],
      [rg(ringTop) - 0.004, ringTop, true], [rg(floorY + 0.002) - 0.005, floorY + 0.002, true],
      [0.15, floorY], [0.06, floorY - 0.001], [0.035, floorY - 0.003, true], [0.026, floorY - 0.008], [0, floorY - 0.009],
    ], { segments: 64 }),
    chrome,
  );

  // ---- the globe and its lid ------------------------------------------------------------------------------
  const a0 = Math.asin((cutY - yc) / R), a1 = Math.asin((topCut - yc) / R);
  const arc = [];
  for (let k = 0; k <= 32; k++) {
    const a = a0 + ((a1 - a0) * k) / 32;
    arc.push([R * Math.cos(a), yc + R * Math.sin(a)]);
  }
  add(lathe(arc, { segments: 64 }), glass);

  const ys = topCut - 0.012; // the lid's skirt reaches down over the glass to here
  add(
    lathe([
      [0, ys], [0.162, ys, true], [0.17, ys + 0.004], [0.174, ys + 0.012], [0.173, ys + 0.022], [0.166, ys + 0.03, true], // rim bead
      [0.16, ys + 0.034], [0.15, ys + 0.046], [0.132, ys + 0.062], [0.104, ys + 0.077], [0.07, ys + 0.087], [0.04, ys + 0.092, true],
      [0.028, ys + 0.095], [0.024, ys + 0.1], [0, ys + 0.1],
    ], { segments: 64 }),
    red,
  );
  // Chrome knob on top: a collar and a ball.
  const kb = ys + 0.096;
  const knobTop = kb + 0.068;
  add(
    lathe([
      [0, kb], [0.032, kb, true], [0.034, kb + 0.006], [0.03, kb + 0.012, true], [0.016, kb + 0.016], [0.015, kb + 0.022],
      [0.026, kb + 0.028], [0.034, kb + 0.04], [0.033, kb + 0.052], [0.025, kb + 0.062], [0.012, kb + 0.067], [0, knobTop],
    ], { segments: 40 }),
    chrome,
  );

  // ---- the coin mechanism on the front -------------------------------------------------------------------
  const plateY = 0.29, plateR0 = 0.212;
  const onPlate = (mesh, x, y, z) => bend(mesh.transform({ translate: [x, y, z] }), plateR0, plateY);
  add(onPlate(box(0.19, 0.22, 0.04, { round: 0.014, steps: 6 }), 0, 0, -0.002), chrome);
  // A raised rim round the plate's edge (a thin frame): four slim rounded bars.
  for (const [w, h, x, y] of [[0.19, 0.012, 0, 0.104], [0.19, 0.012, 0, -0.104], [0.012, 0.215, 0.089, 0], [0.012, 0.215, -0.089, 0]])
    add(onPlate(box(w, h, 0.012, { round: 0.005, steps: 3 }), x, y, -0.024), chrome);
  // The "1¢" enamel plate near the top, and the coin slot under it.
  add(onPlate(box(0.106, 0.052, 0.008, { round: 0.003, steps: 4 }), 0, 0.068, -0.025), labelMat);
  add(onPlate(box(0.062, 0.024, 0.01, { round: 0.005, steps: 3 }), 0, 0.028, -0.024), chrome);
  add(onPlate(box(0.044, 0.008, 0.006, { round: 0.003, steps: 1 }), 0, 0.028, -0.029), dark);
  // Corner screws.
  for (const sx of [-0.073, 0.073]) for (const sy of [-0.087, 0.087])
    add(onPlate(sphere(0.0075, { segments: 12, rows: 6, scale: [1, 1, 0.5] }), sx, sy, -0.022), chrome);

  // The round boss and the T-handle turn knob (rigid, so not bent).
  const bossY = plateY - 0.045;
  const turnToFront = { rotate: [-90, 0, 0] }; // a lathe's +Y axis becomes -Z (out of the front)
  add(
    lathe([[0, 0], [0.057, 0, true], [0.057, 0.018], [0.054, 0.024], [0.047, 0.027, true], [0.03, 0.027], [0, 0.027]], { segments: 48 })
      .transform(turnToFront).transform({ translate: [0, bossY, -0.222] }),
    chrome,
  );
  const hubFront = -0.222 - 0.027 - 0.04;
  add(
    lathe([[0, 0], [0.026, 0, true], [0.026, 0.024], [0.024, 0.034], [0.017, 0.039], [0, 0.04]], { segments: 32 })
      .transform(turnToFront).transform({ translate: [0, bossY, -0.249] }),
    chrome,
  );
  // The handle: a bow-tie bar (ends flare out), turned a little for character.
  const handle = box(0.152, 0.028, 0.022, { round: 0.009, steps: 5 }).map(([x, y, z]) => [x, y * (1 + 0.75 * (Math.abs(x) / 0.076) ** 2), z]);
  add(handle.transform({ rotate: [0, 0, 22], translate: [0, bossY, -0.266] }), chrome);

  // ---- the chute with its hinged flap --------------------------------------------------------------------
  const chuteY = 0.135, chuteR0 = bodyR(chuteY);
  add(bend(box(0.136, 0.094, 0.08, { round: 0.02, steps: 5 }).transform({ translate: [0, 0, -0.005] }), chuteR0, chuteY), chrome);
  const chuteFront = chuteR0 + 0.045; // radius of the housing's front face
  // The dark mouth (seen under the flap) and the flap itself, hinged at its top edge and swung out a little.
  add(bend(box(0.09, 0.05, 0.012, { round: 0.008, steps: 4 }).transform({ translate: [0, -0.006, -0.042] }), chuteR0, chuteY), dark);
  const hingeY = chuteY + 0.028, hingeZ = -chuteFront - 0.006;
  const flap = box(0.108, 0.066, 0.007, { round: 0.003, steps: 3 }).transform({ translate: [0, -0.033, 0] });
  flap.append(box(0.04, 0.012, 0.012, { round: 0.005, steps: 2 }).transform({ translate: [0, -0.058, -0.006] })); // lift tab
  add(flap.transform({ rotate: [16, 0, 0], translate: [0, hingeY, hingeZ] }), chrome);
  add(tube([[0.058, hingeY, hingeZ], [-0.058, hingeY, hingeZ]], 0.006, { segments: 10 }), chrome);

  // ---- anchors and notes ----------------------------------------------------------------------------------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const s = 1.15 / (max[1] - min[1]);
  const flapLow = hingeY - 0.066 * Math.cos((16 * Math.PI) / 180);
  return {
    parts,
    face: yc / (max[1] - min[1]),
    faceSize: 0.78,
    anchors: {
      globeCentre: [0, yc, 0],
      globeBottom: [0, floorY, 0],
      chuteMouth: [0, flapLow - 0.012, -chuteFront - 0.02],
      knob: [0, bossY, hubFront],
    },
    notes: {
      globeInnerRadius: +(R * s - 0.004).toFixed(3),
      globeFloorRadius: +((rg(floorY) - 0.006) * s).toFixed(3),
      glassMaterial: 'glass',
      hollow: 'globe is an open glass shell (no back/inner wall); floor is the chrome disc at globeBottom; lid underside visible through the glass',
    },
  };
}
