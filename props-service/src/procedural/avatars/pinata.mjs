/**
 * Star piñata: a puffy five-point star standing on two of its points like little legs, the other three up and
 * out as a head and arms. The shape is "inflated" from a rounded star outline (a Poisson solve, like a plush
 * toy), and the body is covered in rows of fringed crepe paper in party colours: a calm butter-yellow disc in the
 * middle (where the face goes), then fringed bands that start round and turn star-shaped toward the rim. Each band
 * is a real little shingle (it rises toward its fringed edge and steps down onto the next), and paper streamer
 * tassels spill off the three upper tips. No string (the app hangs it).
 */
export default function build(kit) {
  const { Mesh, Canvas, tube, sphere, smooth, clamp, lerp, TAU, fbm2, rng, computeNormals } = kit;

  // ---------- the outline: a rounded, chubby star ----------
  const RO = 0.5, RI = 0.24; // tip and valley radius of the sharp star
  const RT = 0.08, RV = 0.045; // fillet radii at the tips and valleys
  // Tips (degrees, counter-clockwise from the top): the arms raised a little, the legs a little closer than a
  // regular star's so it stands and isn't too wide; valleys half-way between. Squeezed a touch in x.
  const TIPS = [90, 158, 236, 304, 382, 450]; // (the last closes the loop)
  const SQX = 0.9;
  const corners = [];
  for (let k = 0; k < 10; k++) {
    const th = ((k % 2 ? (TIPS[(k - 1) / 2] + TIPS[(k + 1) / 2]) / 2 : TIPS[k / 2]) * Math.PI) / 180;
    const r = k % 2 ? RI : RO * [1.04, 1, 1.1, 1.1, 1][k / 2]; // top, arms, legs
    corners.push({ p: [SQX * r * Math.cos(th), r * Math.sin(th)], f: k % 2 ? RV : RT, th });
  }
  const dense = []; // the rounded outline, counter-clockwise (in x right / y up)
  const tipInfo = [];
  for (let k = 0; k < 10; k++) {
    const P = corners[k].p, A = corners[(k + 9) % 10].p, B = corners[(k + 1) % 10].p, r = corners[k].f;
    const n2 = (v) => {
      const l = Math.hypot(v[0], v[1]);
      return [v[0] / l, v[1] / l];
    };
    const u1 = n2([A[0] - P[0], A[1] - P[1]]), u2 = n2([B[0] - P[0], B[1] - P[1]]);
    const alpha = Math.acos(clamp(u1[0] * u2[0] + u1[1] * u2[1], -1, 1));
    const t = r / Math.tan(alpha / 2);
    const bis = n2([u1[0] + u2[0], u1[1] + u2[1]]);
    const C = [P[0] + (bis[0] * r) / Math.sin(alpha / 2), P[1] + (bis[1] * r) / Math.sin(alpha / 2)];
    const T1 = [P[0] + u1[0] * t, P[1] + u1[1] * t], T2 = [P[0] + u2[0] * t, P[1] + u2[1] * t];
    const a1 = Math.atan2(T1[1] - C[1], T1[0] - C[0]);
    let da = Math.atan2(T2[1] - C[1], T2[0] - C[0]) - a1;
    while (da > Math.PI) da -= TAU;
    while (da < -Math.PI) da += TAU;
    const STEPS = k % 2 ? 10 : 22;
    for (let s = 0; s <= STEPS; s++) dense.push([C[0] + r * Math.cos(a1 + (da * s) / STEPS), C[1] + r * Math.sin(a1 + (da * s) / STEPS)]);
    if (k % 2 === 0) {
      const d = [-bis[0], -bis[1]];
      tipInfo[k] = { centre: C, dir: d, tip: [C[0] + d[0] * r, C[1] + d[1] * r], low: [C[0], C[1] - r] };
    }
  }
  // Start the outline at the bottom valley (between the legs), where the texture seam hides.
  {
    const startCorner = 5, offset = [0, 1, 2, 3, 4].reduce((s, k) => s + (k % 2 ? 11 : 23), 0) + 5;
    dense.push(...dense.splice(0, offset));
  }
  const DN = dense.length;
  // Resample evenly by arc length.
  const cum = [0];
  for (let i = 1; i <= DN; i++) cum.push(cum[i - 1] + Math.hypot(dense[i % DN][0] - dense[i - 1][0], dense[i % DN][1] - dense[i - 1][1]));
  const PER = cum[DN];
  const N = 200;
  const outline = [];
  for (let i = 0, m = 0; i < N; i++) {
    const s = (PER * i) / N;
    while (cum[m + 1] < s) m++;
    const f = (s - cum[m]) / (cum[m + 1] - cum[m]);
    const a = dense[m], b = dense[(m + 1) % DN];
    outline.push([lerp(a[0], b[0], f), lerp(a[1], b[1], f)]);
  }
  outline.push(outline[0]);

  // ---------- inflation: solve  laplacian(u) = -1  inside the outline, height = K sqrt(u) ----------
  const xs = dense.map((p) => p[0]), ys = dense.map((p) => p[1]);
  const H = 0.004;
  const gx0 = Math.min(...xs) - 3 * H, gy0 = Math.min(...ys) - 3 * H;
  const NX = Math.ceil((Math.max(...xs) - gx0) / H) + 4, NY = Math.ceil((Math.max(...ys) - gy0) / H) + 4;
  const inPoly = (x, y) => {
    let c = false;
    for (let i = 0, j = DN - 1; i < DN; j = i++) {
      const [xi, yi] = dense[i], [xj, yj] = dense[j];
      if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) c = !c;
    }
    return c;
  };
  const inside = new Uint8Array(NX * NY);
  for (let j = 0; j < NY; j++) for (let i = 0; i < NX; i++) inside[j * NX + i] = inPoly(gx0 + i * H, gy0 + j * H) ? 1 : 0;
  const U = new Float64Array(NX * NY);
  for (let it = 0; it < 900; it++)
    for (let j = 1; j < NY - 1; j++)
      for (let i = 1; i < NX - 1; i++) {
        const k = j * NX + i;
        if (!inside[k]) continue;
        const gs = (U[k - 1] + U[k + 1] + U[k - NX] + U[k + NX] + H * H) / 4;
        U[k] += 1.9 * (gs - U[k]);
      }
  const uAt = (x, y) => {
    const fx = (x - gx0) / H, fy = (y - gy0) / H;
    const i = clamp(Math.floor(fx), 0, NX - 2), j = clamp(Math.floor(fy), 0, NY - 2);
    const tx = fx - i, ty = fy - j;
    const k = j * NX + i;
    return (U[k] * (1 - tx) + U[k + 1] * tx) * (1 - ty) + (U[k + NX] * (1 - tx) + U[k + NX + 1] * tx) * ty;
  };
  // Near the rim the raster is too coarse for sqrt(u): there u is modelled from the exact distance d to the
  // outline as g d - d^2 / 2, with the slope g read off the raster a little way in.
  const DELTA = 0.03;
  const gAt = dense.map((p, m) => {
    const a = dense[(m + DN - 1) % DN], b = dense[(m + 1) % DN];
    const tx = b[0] - a[0], ty = b[1] - a[1], l = Math.hypot(tx, ty);
    const nx = ty / l, ny = -tx / l; // outward (counter-clockwise outline)
    return (uAt(p[0] - nx * DELTA, p[1] - ny * DELTA) + (DELTA * DELTA) / 2) / DELTA;
  });
  const nearest = (x, y) => {
    let best = Infinity, g = 0;
    for (let m = 0; m < DN; m++) {
      const a = dense[m], b = dense[(m + 1) % DN];
      const dx = b[0] - a[0], dy = b[1] - a[1];
      const t = clamp(((x - a[0]) * dx + (y - a[1]) * dy) / (dx * dx + dy * dy || 1));
      const d = Math.hypot(x - a[0] - t * dx, y - a[1] - t * dy);
      if (d < best) {
        best = d;
        g = lerp(gAt[m], gAt[(m + 1) % DN], t);
      }
    }
    return [best, g];
  };
  const uSmooth = (x, y, onRim) => {
    if (onRim) return 0;
    const [d, g] = nearest(x, y);
    const model = Math.max(0, g * d - (d * d) / 2);
    return lerp(model, Math.max(0, uAt(x, y)), smooth(0.6 * DELTA, 1.4 * DELTA, d));
  };
  const DC = 0.165; // half-thickness at the middle
  const K = DC / Math.sqrt(uSmooth(0, 0, false));

  // ---------- the grid: a calm disc in the middle, then fringe bands out to the rim ----------
  const RC = 0.18; // radius of the calm disc behind the face
  const BANDS = [0, 0.2, 0.39, 0.57, 0.73, 0.87, 1];
  const NB = BANDS.length - 1;
  const VC = 0.1; // texture: the calm disc uses v 0..VC, the bands VC..1
  const bandV = (beta) => VC + (1 - VC) * beta;
  // Rows: { kind: 'calm', q } (q = r / RC) or { kind: 'band', beta, disp } (disp: 0..1 of the shingle rise).
  const rows = [];
  for (const q of [0, 0.18, 0.36, 0.54, 0.7, 0.84, 0.94, 1]) rows.push({ q, beta: null, disp: 0.6 * smooth(0.8, 1, q) ** 1.5, v: VC * q });
  for (let b = 0; b < NB; b++) {
    const fr = b === NB - 1 ? [0, 0.3, 0.55, 0.75, 0.88, 0.95, 0.985] : [0, 0.36, 0.7];
    for (const f of fr) {
      const beta = lerp(BANDS[b], BANDS[b + 1], f);
      rows.push({ beta, disp: b === NB - 1 ? Math.pow(f / 0.6, 0.7) * smooth(1, 0.6, f) + 0 : Math.pow(f, 0.75), v: bandV(beta), first: f === 0 });
    }
    // The band's fringed outer edge (raised), doubled by the next band's tucked-in start.
    const beta = BANDS[b + 1];
    rows.push({ beta, disp: b === NB - 1 ? 0 : 1, v: bandV(beta) - 0.002, rim: b === NB - 1 });
  }
  const dirs = outline.map((p) => {
    const l = Math.hypot(p[0], p[1]);
    return [p[0] / l, p[1] / l, l];
  });
  const rowPoint = (row, i) => {
    const [dx, dy, L] = dirs[i];
    if (row.beta === null) return [dx * RC * row.q, dy * RC * row.q];
    const rc = RC / L;
    const rho = lerp(rc, 1, row.beta);
    return [outline[i][0] * rho, outline[i][1] * rho];
  };
  // Precompute xy and heights per (row, column).
  const xy = rows.map((row) => outline.map((_, i) => rowPoint(row, i)));
  const zz = rows.map((row, j) => xy[j].map(([x, y]) => K * Math.sqrt(uSmooth(x, y, !!row.rim))));

  const sheet = (side) => {
    const mesh = new Mesh();
    const idx = rows.map((row, j) => outline.map((_, i) => mesh.vertex([xy[j][i][0], xy[j][i][1], side * zz[j][i]], [i / N, row.v])));
    for (let j = 0; j + 1 < rows.length; j++)
      for (let i = 0; i < N; i++) {
        const p = idx[j][i], q = idx[j][i + 1], r = idx[j + 1][i + 1], s = idx[j + 1][i];
        mesh.tri(p, q, r).tri(p, r, s);
      }
    // Rows go outward and columns counter-clockwise, so this winding faces -Z (the front); the back is flipped.
    // (Not outward(): the doubled shingle rows are still zero-area here and must keep their neighbours' winding.)
    return side < 0 ? mesh : mesh.flip();
  };
  const front = sheet(-1), back = sheet(1);

  // Shingle relief: push each band out along the surface normal by its rise (smooth normals shared by coincident
  // points, so the seam and the doubled rows move together).
  {
    const both = new Mesh().append(front).append(back);
    const nrm = computeNormals(both, 180);
    const amp = (x, y) => 0.0068 * (0.6 + 0.4 * clamp(Math.hypot(x, y) / 0.42));
    const perSheet = rows.length * (N + 1);
    for (const [mesh, off] of [[front, 0], [back, perSheet]]) {
      let k = 0;
      for (let j = 0; j < rows.length; j++)
        for (let i = 0; i <= N; i++, k++) {
          const p = mesh.point(k), n = [nrm[(off + k) * 3], nrm[(off + k) * 3 + 1], nrm[(off + k) * 3 + 2]];
          const a = amp(p[0], p[1]) * rows[j].disp;
          mesh.setPoint(k, [p[0] + n[0] * a, p[1] + n[1] * a, p[2] + n[2] * a]);
        }
    }
  }

  // ---------- crepe paper texture (u along the outline, v from the middle out) ----------
  const TW = 1024, TH = 1024;
  const CALM = [255, 218, 86];
  const COLOURS = [
    [255, 70, 150], // hot pink
    [255, 146, 28], // orange
    [40, 196, 214], // turquoise
    [128, 214, 58], // lime
    [168, 92, 232], // purple
    [255, 70, 150], // hot pink rim
  ];
  const crepe = new Canvas(TW, TH, CALM);
  const vpx = (v) => v * TH;
  const bandColour = (b) => (b < 0 ? CALM : COLOURS[b]);
  const edges = [0, ...BANDS.map(bandV)]; // calm 0..VC, then the bands
  const R0 = rng(5);
  // Fill: each band darker where it tucks under the fringe above it, lighter toward its own fringe.
  crepe.shade((u, v) => {
    let b = -1;
    while (b + 1 < NB && v >= bandV(BANDS[b + 1])) b++;
    const v0 = b < 0 ? 0 : bandV(BANDS[b]), v1 = b < 0 ? VC : bandV(BANDS[b + 1]);
    const f = (v - v0) / (v1 - v0);
    const tuck = b < 0 ? 1 : 0.8 + 0.2 * smooth(0, 0.45, f);
    const crinkle = Math.sin(v * TH * 0.7 + Math.sin(u * 60) * 1.1) * 0.5 + 0.5;
    const n = fbm2(u * 30, v * 24, { octaves: 2, seed: 3 }) - 0.5;
    const c = bandColour(b);
    const k = tuck * (0.97 + (b < 0 ? 0.015 : 0.035) * crinkle + n * 0.05);
    return [c[0] * k, c[1] * k, c[2] * k];
  });
  // Fringe: each band's edge is cut into tongues that lie over the start of the next band.
  const SLIT = TW / 146; // a whole number of tongues round, so the seam matches
  for (let b = -1; b < NB - 1; b++) {
    const vEdge = vpx(b < 0 ? VC : bandV(BANDS[b + 1]));
    const vNext = vpx(bandV(BANDS[b + 2])) - vEdge;
    const vOwn = vEdge - vpx(b < 0 ? 0 : bandV(BANDS[b]));
    const col = bandColour(b);
    // The shade the fringe casts, then each tongue: tapering, leaning a little, of uneven length, with the
    // cut running up into its own band's lip.
    crepe.rect(0, vEdge, TW, vEdge + vNext * 0.5, [40, 10, 40], 0.14);
    for (let x = 0; x < TW - 0.5; x += SLIT) {
      const len = vNext * (0.2 + 0.26 * R0() ** 1.5);
      const lean = (R0() - 0.5) * 3;
      const shade = 0.9 + 0.16 * R0();
      const c = col.map((v) => Math.min(255, v * shade));
      const x0 = x + 0.7, x1 = x + SLIT - 0.3, ex0 = x + 1.4 + lean, ex1 = x + SLIT - 1 + lean, ye = vEdge + len;
      crepe.poly([[x0 + 1, vEdge], [x1 + 1, vEdge], [ex1 + 1.2, ye + 2.5], [ex0 + 1.2, ye + 2.5]], [40, 10, 40], 0.16);
      crepe.poly([[x0, vEdge - 1.5], [x1, vEdge - 1.5], [ex1, ye], [(ex0 + ex1) / 2, ye + 1.6], [ex0, ye]], c);
      crepe.poly([[x0 + 1.5, vEdge], [x1 - 1.2, vEdge], [ex1 - 1, ye - 1], [ex0 + 1, ye - 1]], col.map((v) => Math.min(255, v * shade * 1.08)), 0.55);
      crepe.rect(x, vEdge - Math.min(vOwn * 0.18, 9), x + 0.8, vEdge, col.map((v) => v * 0.5), 0.6);
    }
  }
  // The rim band's own edge: short cuts toward the seam.
  for (let x = 0; x < TW - 0.5; x += SLIT) crepe.rect(x, TH - 6, x + 0.8, TH, col0(NB - 1).map((v) => v * 0.6), 0.35);
  function col0(b) {
    return bandColour(b);
  }
  crepe.speckle(900, [0.5, 1.2], [255, 255, 255], 0.16, 17);

  // ---------- streamer tassels on the three upper tips ----------
  const STREAM = [[255, 70, 150], [255, 214, 60], [56, 178, 250], [128, 214, 58], [168, 92, 232], [255, 146, 28], [255, 255, 255], [255, 86, 70]];
  const streamTex = new Canvas(256, 128, [255, 255, 255]);
  STREAM.forEach((c, k) => {
    streamTex.rect(k * 32, 0, k * 32 + 32, 128, c);
    for (let y = 0; y < 128; y += 3) streamTex.rect(k * 32, y, k * 32 + 32, y + 1, [0, 0, 0], 0.06);
    streamTex.rect(k * 32, 0, k * 32 + 3, 128, [0, 0, 0], 0.12).rect(k * 32 + 29, 0, k * 32 + 32, 128, [0, 0, 0], 0.12);
  });
  const ribbon = (path, width, colour, twist0, twist1) => {
    const m = new Mesh();
    const u0 = (colour * 32 + 4) / 256, u1 = (colour * 32 + 28) / 256;
    const L = [];
    for (let j = 0; j < path.length; j++) {
      const a = path[Math.max(0, j - 1)], b = path[Math.min(path.length - 1, j + 1)];
      const t = kit.vec.norm(kit.vec.sub(b, a));
      let w = kit.vec.norm(kit.vec.cross(t, [0, 0, 1]));
      // Twist the strip round its own length a little.
      const ang = lerp(twist0, twist1, j / (path.length - 1));
      const bn = kit.vec.cross(t, w);
      w = [0, 1, 2].map((d) => w[d] * Math.cos(ang) + bn[d] * Math.sin(ang));
      const hw = (width / 2) * (j === path.length - 1 ? 0.6 : 1);
      const v = j / (path.length - 1);
      L.push([m.vertex(path[j].map((x, d) => x - w[d] * hw), [u0, v]), m.vertex(path[j].map((x, d) => x + w[d] * hw), [u1, v])]);
    }
    for (let j = 0; j + 1 < L.length; j++) m.quad(L[j][0], L[j][1], L[j + 1][1], L[j + 1][0]);
    return m;
  };
  const tassels = [];
  const knots = [];
  const R1 = rng(42);
  for (const k of [0, 2, 8]) {
    const { tip, dir } = tipInfo[k];
    const P0 = [tip[0] - dir[0] * 0.014, tip[1] - dir[1] * 0.014, 0];
    knots.push(P0);
    const COUNT = k === 0 ? 13 : 10;
    for (let s = 0; s < COUNT; s++) {
      const path = [];
      const colour = (s * 3 + k) % STREAM.length;
      const width = 0.02 + 0.008 * R1();
      const ph = R1() * TAU;
      if (k === 0) {
        // A fountain: up and out, then flopping down round the top point.
        const phi = ((s + 0.5) / COUNT) * TAU + Math.PI / 2 + (R1() - 0.5) * 0.3;
        const reach = 0.11 + 0.06 * R1(), a = 0.2 + 0.08 * R1(), b = a + 0.1 + 0.07 * R1();
        for (let q = 0; q <= 14; q++) {
          const t = q / 14;
          const r = reach * t;
          path.push([P0[0] + Math.cos(phi) * r + 0.006 * Math.sin(t * 8 + ph), P0[1] + a * t - b * t * t, P0[2] + Math.sin(phi) * r * 0.9]);
        }
      } else {
        // Hanging off an arm tip: a little pop outward, then down, fanned front to back.
        const L = 0.17 + 0.11 * R1();
        const zf = lerp(-0.07, 0.07, s / (COUNT - 1)) + (R1() - 0.5) * 0.02;
        const out = 0.012 + 0.018 * R1();
        for (let q = 0; q <= 14; q++) {
          const t = q / 14;
          const pop = out * (1 - (1 - Math.min(1, t * 2.5)) ** 2);
          path.push([P0[0] + dir[0] * pop + 0.007 * Math.sin(t * 7 + ph), P0[1] + dir[1] * pop * 0.5 - L * t ** 1.15, P0[2] + zf * Math.pow(t, 0.6) + 0.005 * Math.sin(t * 9 + ph)]);
        }
      }
      tassels.push(ribbon(path, width, colour, (R1() - 0.5) * 0.8, (R1() - 0.5) * 2));
    }
  }
  const streamMat = { name: 'streamers', texture: streamTex, roughness: 0.9, doubleSided: true };
  const knotMeshes = knots.map((p) => {
    const m = sphere(0.022, { segments: 14, rows: 8 }).transform({ translate: p });
    for (let i = 0; i < m.vertexCount; i++) {
      m.uvs[i * 2] = (1 * 32 + 16) / 256; // yellow
      m.uvs[i * 2 + 1] = 0.5;
    }
    return m;
  });

  // ---------- stand it up: flatten the soles a touch, bottom on y = 0 ----------
  const parts = [
    { mesh: front, material: { name: 'crepe', texture: crepe, roughness: 0.92 } },
    { mesh: back, material: { name: 'crepe', texture: crepe, roughness: 0.92 } },
    ...tassels.map((mesh) => ({ mesh, material: streamMat })),
    ...knotMeshes.map((mesh) => ({ mesh, material: streamMat })),
  ];
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const minY0 = all.bounds().min[1];
  const SOLE = 0.022;
  for (const p of parts)
    p.mesh.map(([x, y, z]) => {
      const yy = y < minY0 + SOLE ? minY0 + SOLE - (minY0 + SOLE - y) * 0.3 : y;
      return [x, yy, z];
    });
  const floorY = minY0 + SOLE - SOLE * 0.3;
  for (const p of parts) p.mesh.transform({ translate: [0, -floorY, 0] });
  const lift = (p) => [p[0], p[1] - floorY, p[2] ?? 0];
  const top = Math.max(...parts.map((p) => p.mesh.bounds().max[1]));

  const face = -floorY / top; // the star's middle
  return {
    parts,
    face: +face.toFixed(3),
    faceSize: 0.56,
    anchors: {
      centre: lift([0, 0, 0]),
      topTip: lift(tipInfo[0].tip),
      leftTip: lift(tipInfo[8].tip), // screen left is +X
      rightTip: lift(tipInfo[2].tip),
      leftFoot: lift([tipInfo[6].low[0], floorY, 0]),
      rightFoot: lift([tipInfo[4].low[0], floorY, 0]),
    },
    notes:
      'Stands on two points (leftFoot/rightFoot are their soles). Streamer tassels hang off topTip/leftTip/rightTip ' +
      '(double-sided "streamers" material, light enough to flutter if the app jiggles them). Burst it from centre.',
  };
}
