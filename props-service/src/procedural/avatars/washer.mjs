/**
 * Front-loading washing machine: a chunky rounded white cabinet on four stubby feet, a silver control strip across
 * the top (detergent drawer top-left, a little cyan display, two buttons and a big program dial with its tick
 * ring), and low on the front a thick white porthole ring with a chrome bead round a domed glass window. Behind the
 * glass: a rubber gasket throat and a dark steel drum with holes, three lifters and a back wall. A drip of blue
 * detergent has run out of the drawer. The face goes on the plain white panel between the strip and the porthole.
 * In the app clothes tumble in the drum and suds pour out of the door.
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, tube, sphere, box, outward, clamp, lerp, smooth, TAU, fbm2, vec } = kit;

  // ---------- sizes ----------
  const W = 0.78, H = 1.0, D = 0.68; // the cabinet
  const FEET = 0.03; // the cabinet stands on its feet this high
  const ROUND = 0.075;
  const hx = W / 2, hy = H / 2, hz = D / 2;
  const CY = FEET + hy; // the cabinet's centre height
  const FRONT = -hz;
  const DY = 0.325; // porthole centre height
  const HOLE = 0.19; // the hole in the cabinet front (hidden under the door ring)
  const GLASS_R = 0.152, GLASS_OUT = 0.04; // the window and how far its dome stands proud of the front
  const STRIP = { y: 0.92, h: 0.125, w: 0.74, out: 0.012 }; // the control strip
  const STRIP_FRONT = FRONT - STRIP.out;
  const DRUM_R = 0.215, DRUM_BACK = 0.39; // drum radius, depth of its back wall behind the cabinet front
  const FACE_Y = 0.71;

  const white = { name: 'cabinet', color: [0.86, 0.87, 0.88], roughness: 0.55 };
  const chrome = { name: 'chrome', color: [0.56, 0.58, 0.62], roughness: 0.3, metallic: 0.85 };
  const rubber = { name: 'rubber', color: [0.02, 0.022, 0.026], roughness: 0.9 };
  const glass = { name: 'glass_door', color: [0.85, 0.95, 1, 0.25], roughness: 0.05, blend: true };

  // ---------- a rounded box with chosen sample rows (and optionally a round hole in its front) ----------
  /** Positions from -h to h: `dense` steps over each rounded band, about `step` apart across the flat middle. */
  const samples = (h, round, step, dense = 4) => {
    const inner = h - round, out = [];
    for (let k = 0; k < dense; k++) out.push(-h + (round * k) / dense);
    const n = Math.max(1, Math.round((2 * inner) / step));
    for (let k = 0; k <= n; k++) out.push(-inner + (2 * inner * k) / n);
    for (let k = dense - 1; k >= 0; k--) out.push(h - (round * k) / dense);
    return out;
  };
  function roundedBox(half, round, step, hole) {
    const mesh = new Mesh();
    const S = half.map((h) => samples(h, round, step));
    const inner = half.map((h) => h - round);
    const push = (p) => {
      const c = p.map((v, k) => clamp(v, -inner[k], inner[k]));
      const d = vec.norm(vec.sub(p, c));
      return [c[0] + d[0] * round, c[1] + d[1] * round, c[2] + d[2] * round];
    };
    const grid = (fixed, sign, a1, a2) => {
      const A = S[a1], B = S[a2], idx = [];
      for (let j = 0; j < B.length; j++)
        for (let i = 0; i < A.length; i++) {
          const p = [0, 0, 0];
          p[fixed] = sign * half[fixed];
          p[a1] = A[i];
          p[a2] = B[j];
          idx.push(mesh.vertex(push(p), [i / (A.length - 1), 1 - j / (B.length - 1)]));
        }
      for (let j = 0; j + 1 < B.length; j++)
        for (let i = 0; i + 1 < A.length; i++) {
          const a = idx[j * A.length + i], b = idx[j * A.length + i + 1], c = idx[(j + 1) * A.length + i + 1], d = idx[(j + 1) * A.length + i];
          mesh.quad(a, b, c, d);
        }
    };
    grid(2, 1, 0, 1); // back
    grid(0, 1, 2, 1); // +X
    grid(0, -1, 2, 1); // -X
    grid(1, 1, 0, 2); // top
    grid(1, -1, 0, 2); // bottom
    if (!hole) grid(2, -1, 0, 1);
    else {
      // the front as spokes from the hole's edge out to the box's outline (sampled exactly like the faces it meets)
      const [X, Y] = [S[0], S[1]];
      const rim = [];
      for (let i = X.length - 1; i >= 0; i--) rim.push([X[i], -half[1]]);
      for (let j = 1; j < Y.length; j++) rim.push([-half[0], Y[j]]);
      for (let i = 1; i < X.length; i++) rim.push([X[i], half[1]]);
      for (let j = Y.length - 2; j >= 1; j--) rim.push([half[0], Y[j]]);
      const K = 16, KR = 4;
      const spokes = rim.map(([bx, by]) => {
        const a = Math.atan2(by - hole.y, bx - hole.x);
        const h = [hole.x + hole.r * Math.cos(a), hole.y + hole.r * Math.sin(a)];
        const L = Math.hypot(bx - h[0], by - h[1]);
        const band = Math.min(round, L * 0.4);
        const out = [];
        for (let k = 0; k <= K; k++) {
          // from the hole out: K - KR even steps, then KR over the rounded band
          const d = k <= K - KR ? ((L - band) * k) / (K - KR) : L - band + (band * (k - (K - KR))) / KR;
          const t = d / L;
          const p = [lerp(h[0], bx, t), lerp(h[1], by, t), -half[2]];
          out.push(mesh.vertex(push(p), [0.5 - p[0] / (2 * half[0]), 0.5 - p[1] / (2 * half[1])]));
        }
        return out;
      });
      for (let i = 0; i < spokes.length; i++) {
        const A = spokes[i], B = spokes[(i + 1) % spokes.length];
        for (let k = 0; k < K; k++) mesh.quad(A[k], B[k], B[k + 1], A[k + 1]);
      }
    }
    return outward(mesh, [0, 0, 0]);
  }

  const parts = [];
  const add = (mesh, material) => parts.push({ mesh, material });
  /** A lathe turned to face the front (its +Y axis along -Z), centred at (x, y) on a surface at depth z. */
  const facing = (m, x, y, z) => m.transform({ rotate: [-90, 0, 0], translate: [x, y, z] });

  // ---------- cabinet ----------
  const cabinet = roundedBox([hx, hy, hz], ROUND, 0.035, { x: 0, y: DY - CY, r: HOLE }).transform({ translate: [0, CY, 0] });
  add(cabinet, white);
  for (const [fx, fz] of [[1, 1], [1, -1], [-1, 1], [-1, -1]])
    add(lathe([[0, 0], [0.034, 0, true], [0.036, 0.012], [0.03, FEET + 0.01, true], [0, FEET + 0.01]], { segments: 16 }).transform({ translate: [fx * (hx - 0.09), 0, fz * (hz - 0.09)] }), rubber);

  // ---------- porthole: white ring, chrome bead, domed glass ----------
  const ring = lathe(
    [[0.236, -0.006], [0.239, 0.008], [0.236, 0.022], [0.227, 0.034], [0.212, 0.043], [0.194, 0.047], [0.18, 0.045], [0.172, 0.038], [0.169, 0.028], [0.168, 0.0]],
    { segments: 72 },
  );
  add(facing(ring, 0, DY, FRONT), white);
  const bead = lathe([[0.171, 0.012], [0.173, 0.026], [0.168, 0.036], [0.16, 0.04], [0.152, 0.036], [0.148, 0.027], [0.148, 0.016]], { segments: 72 });
  add(facing(bead, 0, DY, FRONT), chrome);
  const dome = lathe([[GLASS_R, 0.026], [0.13, 0.031], [0.095, 0.036], [0.05, 0.039], [0, GLASS_OUT]], { segments: 64 });
  add(facing(dome, 0, DY, FRONT), glass);
  // the handle (screen right, -X): a chunky pull on the ring's edge, and the hinge on the other side
  add(box(0.05, 0.11, 0.05, { round: 0.02, steps: 3 }).transform({ translate: [-0.232, DY, FRONT - 0.024] }), white);
  add(box(0.012, 0.07, 0.014, { round: 0.005, steps: 2 }).transform({ translate: [-0.256, DY, FRONT - 0.03] }), chrome);
  for (const dy of [-0.09, 0.09]) add(box(0.04, 0.05, 0.03, { round: 0.012, steps: 2 }).transform({ translate: [0.236, DY + dy, FRONT - 0.012] }), chrome);

  // ---------- the gasket throat and the drum (wound to face inward) ----------
  const gasket = lathe([[HOLE + 0.004, 0.002], [0.186, -0.014], [0.174, -0.028], [0.166, -0.042], [0.17, -0.056], [0.186, -0.066], [0.2, -0.07]], { segments: 64 });
  add(facing(gasket, 0, DY, FRONT), rubber);
  const drumProfile = [[0.198, -0.068], [0.21, -0.078], [DRUM_R, -0.095]];
  for (let k = 1; k <= 6; k++) drumProfile.push([DRUM_R, -0.095 - ((DRUM_BACK - 0.105) * k) / 6]);
  drumProfile.push([DRUM_R - 0.012, -DRUM_BACK], [0.15, -DRUM_BACK], [0.09, -DRUM_BACK], [0.052, -DRUM_BACK], [0.045, -DRUM_BACK + 0.012], [0.02, -DRUM_BACK + 0.016], [0, -DRUM_BACK + 0.016]);
  const vDrum = (t) => t;
  const drum = lathe(drumProfile, { segments: 64, v: vDrum });
  // the drum's paint: dark brushed steel; u round, v along the profile (0 front lip .. 1 hub)
  const drumTex = new Canvas(512, 512, [56, 60, 67]);
  {
    const nWall = 6 / (drumProfile.length - 1); // v where the wall ends and the back wall starts
    drumTex.shade((u, v) => {
      const brushed = fbm2(u * 80, v * 3, { octaves: 2, seed: 3 });
      // darker the deeper in (the back wall and the hub darkest)
      let col = [56, 60, 67].map((c) => c * (0.88 + 0.24 * brushed) * (1 - 0.35 * smooth(0.1, 0.8, v)));
      return col;
    });
    // holes in staggered rows down the wall
    const v0 = 2 / (drumProfile.length - 1), v1 = 8 / (drumProfile.length - 1);
    for (let j = 0; j < 9; j++)
      for (let i = 0; i < 40; i++) {
        const u = (i + (j % 2) * 0.5) / 40, v = lerp(v0 + 0.02, v1 - 0.02, j / 8);
        drumTex.disc(u * 512, v * 512, 5.5, [104, 110, 120], 1, 0.3).disc(u * 512, v * 512 + 0.8, 4, [8, 8, 10], 1, 0.2);
      }
    // and two rings of them in the back wall, round a darker hub
    const vb = (k) => k / (drumProfile.length - 1);
    for (const [vv, n] of [[lerp(vb(9), vb(10), 0.45), 30], [lerp(vb(10), vb(11), 0.55), 18]])
      for (let i = 0; i < n; i++) drumTex.disc(((i + 0.5) / n) * 512, vv * 512, 5, [80, 86, 94], 1, 0.3).disc(((i + 0.5) / n) * 512, vv * 512, 3.6, [6, 6, 8], 1, 0.2);
    drumTex.rect(0, vb(12) * 512, 512, 512, [26, 28, 32]);
    for (let i = 0; i < 3; i++) drumTex.rect(((i + 0.5) / 3) * 512 - 10, vb(11.5) * 512, ((i + 0.5) / 3) * 512 + 10, 512, [60, 64, 72]);
    void nWall;
  }
  add(facing(drum, 0, DY, FRONT), { name: 'drum', texture: drumTex, roughness: 0.5, metallic: 0.4 });
  // three lifters along the drum wall
  for (const a of [-90, 30, 150]) {
    const lifter = box(0.045, 0.034, DRUM_BACK - 0.12, { round: 0.014, steps: 2 })
      .map(([x, y, z]) => [x, y - DRUM_R + 0.012, z + FRONT + 0.1 + (DRUM_BACK - 0.12) / 2])
      .transform({ rotate: [0, 0, a + 90], translate: [0, DY, 0] });
    add(lifter, { name: 'drum', texture: drumTex, roughness: 0.5, metallic: 0.4 });
  }

  // ---------- the control strip ----------
  const strip = box(STRIP.w, STRIP.h, 0.04, { round: 0.018, steps: 3 }).transform({ translate: [0, STRIP.y, STRIP_FRONT + 0.02] }).planarUV();
  const SX = (x) => ((STRIP.w / 2 - x) / STRIP.w) * 1024; // model x -> strip texture px (screen left = +X = 0)
  const SYp = (y) => ((STRIP.y + STRIP.h / 2 - y) / STRIP.h) * 174;
  const panel = new Canvas(1024, 174, [214, 218, 224]);
  panel.shade((u, v) => {
    const brushed = fbm2(u * 300, v * 6, { octaves: 2, seed: 7 });
    return [214, 218, 224].map((c) => c * (0.97 + 0.05 * brushed) * (1 - 0.06 * v));
  });
  const DIAL = { x: -0.25, y: STRIP.y, r: 0.05 };
  const DISP = { x: 0.025, y: STRIP.y + 0.012, w: 0.15, h: 0.058 };
  const BUTTONS = [{ x: -0.095, y: STRIP.y + 0.004, r: 0.017 }, { x: -0.142, y: STRIP.y + 0.004, r: 0.017 }];
  {
    // the dial's program ring: coloured ticks on the arcs either side of the knob
    const cx = SX(DIAL.x), cy = SYp(DIAL.y), pxm = 1024 / STRIP.w;
    const cols = [[64, 150, 230], [64, 150, 230], [80, 190, 120], [80, 190, 120], [240, 170, 50], [240, 120, 60], [220, 70, 90], [160, 100, 210]];
    let k = 0;
    for (const side of [1, -1])
      for (let i = 0; i < 4; i++) {
        const a = (side > 0 ? 0 : Math.PI) + lerp(-0.75, 0.75, i / 3) * side;
        const r0 = (DIAL.r + 0.02) * pxm, r1 = (DIAL.r + 0.031) * pxm;
        panel.stroke([[cx + r0 * Math.cos(a), cy - r0 * Math.sin(a)], [cx + r1 * Math.cos(a), cy - r1 * Math.sin(a)]], 7, cols[k++ % cols.length]);
      }
    // the display: a dark rounded window with cyan digits and a few status icons
    const x0 = SX(DISP.x + DISP.w / 2), x1 = SX(DISP.x - DISP.w / 2), y0 = SYp(DISP.y + DISP.h / 2), y1 = SYp(DISP.y - DISP.h / 2);
    const rr = 12;
    const roundRect = (a, b, c, d, r, col) => {
      panel.rect(a + r, b, c - r, d, col).rect(a, b + r, c, d - r, col);
      for (const [px, py] of [[a + r, b + r], [c - r, b + r], [a + r, d - r], [c - r, d - r]]) panel.disc(px, py, r, col);
    };
    roundRect(x0 - 4, y0 - 4, x1 + 4, y1 + 4, rr + 3, [150, 156, 166]);
    roundRect(x0, y0, x1, y1, rr, [18, 26, 34]);
    const CYAN = [90, 230, 250];
    const mid = (x0 + x1) / 2, dh = (y1 - y0) * 0.56, top = y0 + (y1 - y0) * 0.2;
    panel.text('1', mid - dh * 0.95, top, dh, CYAN, { weight: 0.16 });
    panel.text('25', mid + dh * 0.62, top, dh, CYAN, { weight: 0.16 });
    panel.disc(mid - dh * 0.36, top + dh * 0.3, 3.2, CYAN).disc(mid - dh * 0.36, top + dh * 0.72, 3.2, CYAN);
    // little status lights under the display
    for (const [i, col] of [[0, [90, 230, 250]], [1, [70, 90, 100]], [2, [70, 90, 100]]]) panel.disc(x0 + 22 + i * 26, y1 + 14, 4.5, col);
    // labels: a power ring under one button, a play triangle under the other
    const b0 = BUTTONS[0], b1 = BUTTONS[1];
    const bx0 = SX(b0.x), bx1 = SX(b1.x), by = SYp(b0.y - b0.r - 0.016);
    panel.poly([[bx0 - 6, by - 7], [bx0 - 6, by + 7], [bx0 + 7, by]], [70, 76, 86]);
    panel.rect(bx0 + 9, by - 7, bx0 + 12, by + 7, [70, 76, 86]).rect(bx0 + 15, by - 7, bx0 + 18, by + 7, [70, 76, 86]);
    panel.stroke(Array.from({ length: 17 }, (_, k) => {
      const a = lerp(-1.1, TAU * 0.5 + 1.1 + Math.PI, k / 16) - Math.PI / 2 + 0.9;
      return [bx1 + 7 * Math.cos(a), by + 7 * Math.sin(a)];
    }), 3, [70, 76, 86]);
    panel.stroke([[bx1, by - 9], [bx1, by - 2]], 3, [70, 76, 86]);
    // a thin groove where the strip meets the drawer
    panel.rect(SX(0.355) - 2, 0, SX(0.355), 174, [170, 176, 186]);
  }
  add(strip, { name: 'panel', texture: panel, roughness: 0.45, metallic: 0.3 });

  // the dial: a chrome skirt, a knurled white knob with a grip bar and a pointer
  const skirt = lathe([[0.064, -0.002], [0.065, 0.006], [0.061, 0.011], [0.054, 0.012], [0.05, 0.008]], { segments: 48 });
  add(facing(skirt, DIAL.x, DIAL.y, STRIP_FRONT), chrome);
  const knobProf = [[DIAL.r, 0.0], [DIAL.r, 0.012], [DIAL.r, 0.024], [DIAL.r * 0.97, 0.033], [DIAL.r * 0.85, 0.039], [DIAL.r * 0.5, 0.042], [0, 0.0425]];
  const knob = lathe(knobProf, {
    segments: 72,
    section: (t, a, r) => {
      const k = t < 0.55 ? 1 + 0.035 * Math.max(0, Math.cos(24 * a)) ** 2 : 1;
      return [r * k * Math.cos(a), r * k * Math.sin(a)];
    },
  });
  add(facing(knob, DIAL.x, DIAL.y, STRIP_FRONT), white);
  // the grip bar across the knob's face, turned to a program at the top-left
  const tiltDeg = 35;
  add(box(0.024, 0.088, 0.03, { round: 0.011, steps: 3 }).transform({ rotate: [0, 0, tiltDeg], translate: [DIAL.x, DIAL.y, STRIP_FRONT - 0.045] }), white);
  const pa = ((90 + tiltDeg) * Math.PI) / 180;
  add(sphere(0.007, { segments: 12, rows: 6 }).transform({ scale: [1, 1, 0.5], translate: [DIAL.x + 0.034 * Math.cos(pa), DIAL.y + 0.034 * Math.sin(pa), STRIP_FRONT - 0.058] }), { name: 'accent', color: [0.9, 0.18, 0.02], roughness: 0.5 });

  // buttons: chunky domed pills
  for (const [i, b] of BUTTONS.entries()) {
    const m = lathe([[b.r + 0.004, -0.002], [b.r + 0.004, 0.004], [b.r, 0.006, true], [b.r, 0.012], [b.r * 0.85, 0.017], [b.r * 0.45, 0.0195], [0, 0.02]], { segments: 28 });
    add(facing(m, b.x, b.y, STRIP_FRONT), i === 0 ? { name: 'accent_teal', color: [0.05, 0.55, 0.5], roughness: 0.5 } : white);
  }

  // ---------- the detergent drawer (top-left) ----------
  const DRAWER = { x: 0.24, y: STRIP.y, w: 0.215, h: 0.092, d: 0.03, out: 0.014 };
  const DRAWER_FRONT = STRIP_FRONT - DRAWER.out;
  const drawerMesh = box(DRAWER.w, DRAWER.h, DRAWER.d, { round: 0.014, steps: 3 }).transform({ translate: [DRAWER.x, DRAWER.y, DRAWER_FRONT + DRAWER.d / 2] }).planarUV();
  const dtex = new Canvas(512, 220, [238, 240, 242]);
  {
    dtex.shade((u, v) => [238, 240, 242].map((c) => c * (1 - 0.05 * v)));
    // the finger pull: a dark slot along the bottom edge
    dtex.rect(150, 170, 362, 190, [120, 124, 132]).disc(150, 180, 10, [120, 124, 132]).disc(362, 180, 10, [120, 124, 132]);
    dtex.rect(150, 168, 362, 172, [80, 84, 92], 0.6);
    // compartment marks
    const G = [96, 104, 116];
    dtex.text('I', 120, 52, 44, G, { weight: 0.18 });
    dtex.text('II', 256, 52, 44, G, { weight: 0.18 });
    // a little flower for the softener
    for (let k = 0; k < 5; k++) {
      const a = (k / 5) * TAU;
      dtex.disc(392 + 13 * Math.cos(a), 74 + 13 * Math.sin(a), 9, G);
    }
    dtex.disc(392, 74, 6, [238, 240, 242]);
  }
  add(drawerMesh, { name: 'drawer', texture: dtex, roughness: 0.5 });

  // ---------- flat things lying on surfaces: a gel drip, a sock ----------
  /** Catmull-Rom through points (any number of components), n samples per span. */
  const spline = (pts, n = 6) => {
    const out = [];
    for (let i = 0; i + 1 < pts.length; i++) {
      const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
      for (let k = 0; k < n; k++) {
        const t = k / n, t2 = t * t, t3 = t2 * t;
        out.push(p1.map((_, d) => 0.5 * (2 * p1[d] + (-p0[d] + p2[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t2 + (-p0[d] + 3 * p1[d] - 3 * p2[d] + p3[d]) * t3)));
      }
    }
    out.push([...pts[pts.length - 1]]);
    return out;
  };
  /** Turns a closed mesh right side out (by its signed volume). */
  const rightOut = (m) => {
    let vol = 0;
    for (let k = 0; k < m.indices.length; k += 3) {
      const a = m.point(m.indices[k]), b = m.point(m.indices[k + 1]), c = m.point(m.indices[k + 2]);
      vol += vec.dot(a, vec.cross(b, c));
    }
    return vol < 0 ? m.flip() : m;
  };
  /**
   * A flattened tube lying on a surface: keys are [x, y, z, nx, ny, nz, halfWidth, halfThickness] (n = the
   * surface's normal there); its flat underside sits on the surface.
   */
  const ribbon = (keys, { seg = 16, n = 6 } = {}) => {
    const pts = spline(keys, n);
    const rings = pts.map((k, j) => {
      const p = k.slice(0, 3), a = pts[Math.max(0, j - 1)], b = pts[Math.min(pts.length - 1, j + 1)];
      const t = vec.norm(vec.sub(b.slice(0, 3), a.slice(0, 3)));
      let nn = k.slice(3, 6);
      nn = vec.norm(vec.sub(nn, t.map((x) => x * vec.dot(nn, t))));
      const w = vec.cross(t, nn);
      const ring = [];
      for (let i = 0; i <= seg; i++) {
        const th = (TAU * (i % seg)) / seg, c = Math.cos(th), s = Math.sin(th);
        const cx = Math.sign(c) * Math.abs(c) ** 0.7, sy = Math.sign(s) * Math.abs(s) ** (s < 0 ? 0.35 : 0.9);
        ring.push([0, 1, 2].map((d) => p[d] + w[d] * k[6] * cx + nn[d] * k[7] * (1 + sy)));
      }
      return ring;
    });
    return rightOut(kit.loft(rings, { capBottom: true, capTop: true }));
  };

  // A drip of blue detergent oozing out from under the drawer, over the strip's edge and down the cabinet.
  {
    const dx = 0.302, dB = DRAWER.y - DRAWER.h / 2, sB = STRIP.y - STRIP.h / 2;
    const F = [0, 0, -1];
    const drip = ribbon([
      [dx - 0.002, dB + 0.012, DRAWER_FRONT, ...F, 0.024, 0.004],
      [dx, dB - 0.002, DRAWER_FRONT + 0.006, 0, -0.6, -0.8, 0.02, 0.006],
      [dx, dB - 0.012, STRIP_FRONT, ...F, 0.016, 0.006],
      [dx + 0.001, sB + 0.004, STRIP_FRONT, ...F, 0.014, 0.006],
      [dx + 0.001, sB - 0.006, FRONT + 0.004, 0, -0.6, -0.8, 0.013, 0.006],
      [dx + 0.002, sB - 0.02, FRONT, ...F, 0.011, 0.005],
      [dx + 0.002, 0.812, FRONT, ...F, 0.0095, 0.005],
      [dx + 0.002, 0.795, FRONT, ...F, 0.013, 0.007],
      [dx + 0.002, 0.784, FRONT, ...F, 0.009, 0.005],
    ], { n: 5 });
    const goo = { name: 'detergent', color: [0.02, 0.2, 0.9], roughness: 0.25 };
    add(drip, goo);
    add(sphere(0.0145, { segments: 16, rows: 10 }).transform({ scale: [1, 1.1, 0.55], translate: [dx + 0.002, 0.792, FRONT - 0.002] }), goo);
  }

  // A lost sock draped over the top-left edge (+X), its foot hanging down the side.
  {
    const top = FEET + H, ex = hx - ROUND, ey = top - ROUND; // the rounded edge's centre line
    const T = 0.0075, Wd = 0.056;
    const edge = (deg, z, w = Wd) => {
      const a = (deg * Math.PI) / 180;
      return [ex + ROUND * Math.sin(a), ey + ROUND * Math.cos(a), z, Math.sin(a), Math.cos(a), 0, w, T];
    };
    const keys = [
      [0.06, top, 0.2, 0, 1, 0, Wd * 1.04, T], // the cuff, lying on the top toward the back
      [0.15, top, 0.14, 0, 1, 0, Wd, T],
      [0.25, top, 0.07, 0, 1, 0, Wd, T],
      edge(15, 0.03),
      edge(50, 0.01),
      edge(85, -0.005),
      [hx, ey - 0.06, -0.01, 1, 0, 0, Wd, T],
      [hx, ey - 0.11, -0.018, 1, 0, 0, Wd * 1.05, T * 1.1],
      [hx, ey - 0.145, -0.03, 1, 0, 0, Wd * 1.16, T * 1.2], // the heel
      [hx, ey - 0.158, -0.07, 1, 0, 0, Wd * 1.02, T],
      [hx, ey - 0.162, -0.13, 1, 0, 0, Wd * 0.98, T],
      [hx, ey - 0.165, -0.175, 1, 0, 0, Wd * 0.88, T], // the toe
      [hx, ey - 0.166, -0.196, 1, 0, 0, Wd * 0.62, T * 0.9],
      [hx, ey - 0.166, -0.205, 1, 0, 0, Wd * 0.25, T * 0.7],
    ];
    const sock = ribbon(keys, { seg: 18, n: 6 });
    // knitted stripes along it, a ribbed white cuff, a red heel and toe
    const knit = new Canvas(64, 512, [250, 248, 240]);
    knit.shade((u, v) => {
      // v runs cuff (1) .. toe (0)
      const L = 1 - v;
      const rib = 0.94 + 0.06 * Math.sin(u * TAU * 9);
      if (L < 0.12) return [250, 248, 240].map((c) => c * (0.9 + 0.1 * Math.sin(u * TAU * 14)));
      const heel = Math.abs(L - 0.66) < 0.06, toe = L > 0.92;
      if (heel || toe) return [222, 52, 60].map((c) => c * rib);
      const band = Math.floor((L - 0.12) * 28) % 3;
      return (band === 0 ? [72, 170, 230] : band === 1 ? [255, 204, 64] : [250, 248, 240]).map((c) => c * rib);
    });
    add(sock, { name: 'sock', texture: knit, roughness: 0.95 });
  }

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const s = 1.15 / (max[1] - min[1]);
  return {
    parts,
    face: (FACE_Y - min[1]) / (max[1] - min[1]),
    faceSize: 0.64,
    anchors: {
      porthole: [0, DY, FRONT - GLASS_OUT],
      drawer: [DRAWER.x, DRAWER.y, DRAWER_FRONT],
    },
    notes:
      `Porthole inner (window) radius ${(0.148 * s).toFixed(3)} m (glass "glass_door", domed ${(GLASS_OUT * s).toFixed(3)} m proud of the front); ` +
      `door ring outer radius ${(0.239 * s).toFixed(3)} m; gasket throat radius ${(0.166 * s).toFixed(3)} m. ` +
      `Drum radius ${(DRUM_R * s).toFixed(3)} m, its back wall ${((DRUM_BACK + GLASS_OUT) * s).toFixed(3)} m behind the glass ` +
      `(${(DRUM_BACK * s).toFixed(3)} m behind the cabinet front): drum depth ${((DRUM_BACK - 0.07) * s).toFixed(3)} m. Drum is open/hollow ` +
      `(inward-facing, three lifters). Detergent drawer front at "drawer" (top-left), a blue drip runs from it.`,
  };
}
