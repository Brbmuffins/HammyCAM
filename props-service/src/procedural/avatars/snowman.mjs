/**
 * Snowman: two lumpy packed-snow balls (the head 75% of the body's width, big enough for the face), soft blue
 * shadows in the lumps and under each ball, a few clumps of snow fallen round the foot; a carrot nose between the
 * eyes and mouth, pointing at the camera with a jaunty kink; a black top hat tipped to one side with a red band and
 * a holly sprig; a knitted red-and-cream striped scarf round the neck with a knot and a fringed end hanging down the
 * front; three coal buttons. The app knocks the carrot, the buttons and the hat off (materials carrot, coal, hat).
 */
export default function build(kit) {
  const { Mesh, Canvas, lathe, loft, sphere, tube, extrude, bulge, fbm3, noise3, smooth, clamp, lerp, TAU, rng, vec } = kit;

  // ---------- sizes ----------
  const RB = 0.33, RYB = 0.3, YB = 0.246; // body: radius, half-height, centre (its bottom is pressed flat on y = 0)
  const RH = 0.245, RYH = 0.24; // head: 74% of the body's width
  const YH = YB + RYB - 0.055 + RYH; // head centre (sunk a little into the body)
  const FACE_Y = YH + 0.004;
  const FACE_SIZE = 0.86;
  // where the face's features land (preview.mjs: eyes at +0.023, mouth at -0.084, scaled to faceSize x 80% width)
  const faceScale = (FACE_SIZE * 0.8 * 2 * RH) / 0.26;
  const NOSE_Y = FACE_Y - 0.052 * faceScale; // between the bottom of the eyes and the top of the mouth

  // ---------- packed snow ----------
  const lumps = (p, seed) => (fbm3(p[0] * 4, p[1] * 4, p[2] * 4, { octaves: 2, seed }) - 0.5) * 0.034 + (fbm3(p[0] * 11, p[1] * 11, p[2] * 11, { octaves: 2, seed: seed + 5 }) - 0.5) * 0.01;
  const faceDir = vec.norm([0, (FACE_Y - YH) / RH, -1]);
  const faceMask = (p) => {
    const d = vec.norm([p[0], p[1] - YH, p[2]]);
    return 1 - smooth(0.5, 0.85, Math.acos(clamp(vec.dot(d, faceDir), -1, 1)));
  };
  /** A snowball by lathe: radius r, half-height ry, centre y c, pressed flat below y = floor. */
  const ballProfile = (r, ry, c, rows, floor = -Infinity) => {
    const prof = [];
    for (let j = 0; j <= rows; j++) {
      const th = -Math.PI / 2 + (Math.PI * j) / rows;
      prof.push([j === 0 || j === rows ? 0 : r * Math.cos(th), Math.max(floor, c + ry * Math.sin(th))]);
    }
    return prof;
  };
  const BROWS = 40, HROWS = 36, SEG = 72;
  const body = lathe(ballProfile(RB, RYB, YB, BROWS, 0.0), { segments: SEG });
  bulge(body, (p) => lumps(p, 3) * smooth(0.0, 0.06, p[1]), { axis: 'xyz', centre: [0, YB, 0] });
  const head = lathe(ballProfile(RH, RYH, YH, HROWS), { segments: SEG });
  bulge(head, (p) => lumps(p, 9) * (1 - 0.85 * faceMask(p)), { axis: 'xyz', centre: [0, YH, 0] });
  // the body's texture is the bottom half of the picture, the head's the top half
  for (let i = 1; i < body.uvs.length; i += 2) body.uvs[i] = 0.5 + 0.5 * body.uvs[i];
  for (let i = 1; i < head.uvs.length; i += 2) head.uvs[i] = 0.5 * head.uvs[i];

  // paint: white with blue in the hollows of the lumps, under each ball and where they meet; sparkles
  const SNOW = [247, 250, 255], SHADOW = [176, 198, 236];
  const snowTex = new Canvas(1024, 1024, SNOW);
  snowTex.shade((u, v) => {
    const isHead = v < 0.5;
    const vv = isHead ? v / 0.5 : (v - 0.5) / 0.5;
    const th = -Math.PI / 2 + Math.PI * (1 - vv);
    const a = Math.PI / 2 + TAU * (1 - u);
    const [r, ry, c] = isHead ? [RH, RYH, YH] : [RB, RYB, YB];
    const p = [r * Math.cos(th) * Math.cos(a), c + ry * Math.sin(th), r * Math.cos(th) * Math.sin(a)];
    const fm = isHead ? faceMask(p) : 0;
    const L = lumps(p, isHead ? 9 : 3) * (1 - 0.85 * fm);
    let shade = smooth(0.002, -0.014, L) * 0.7;
    // under the ball, and the body's top where the head sits on it
    shade += smooth(-0.15, -0.75, Math.sin(th)) * 0.6;
    if (!isHead) shade += smooth(YB + RYB - 0.12, YB + RYB - 0.02, p[1]) * 0.55;
    else shade += smooth(YH - RYH + 0.1, YH - RYH + 0.02, p[1]) * 0.35;
    const grain = fbm3(p[0] * 60, p[1] * 60, p[2] * 60, { octaves: 2, seed: 15 });
    shade += (grain - 0.5) * 0.18;
    shade *= 1 - 0.6 * fm;
    const t = clamp(shade);
    return [lerp(SNOW[0], SHADOW[0], t), lerp(SNOW[1], SHADOW[1], t), lerp(SNOW[2], SHADOW[2], t)];
  });
  snowTex.speckle(2600, [0.4, 1.1], [255, 255, 255], 0.8, 21);
  snowTex.speckle(900, [0.4, 1.0], [150, 180, 230], 0.35, 22);
  const snow = { name: 'snow', texture: snowTex, roughness: 0.9 };
  const parts = [{ mesh: body, material: snow }, { mesh: head, material: snow }];

  // a few clumps knocked off round the foot
  {
    const R0 = rng(4);
    for (const [a, d, s] of [[0.5, 0.36, 0.05], [0.95, 0.33, 0.032], [2.4, 0.35, 0.045], [-2.1, 0.34, 0.04], [-0.7, 0.37, 0.028]]) {
      const ang = -Math.PI / 2 + a; // a = 0 at the front
      const m = sphere(s, { segments: 14, rows: 8, scale: [1, 0.7, 1] });
      bulge(m, (p) => (noise3(p[0] * 40 + a, p[1] * 40, p[2] * 40, 7) - 0.5) * s * 0.6, { axis: 'xyz' });
      m.transform({ rotate: [0, R0() * 360, 0], translate: [d * Math.cos(ang), s * 0.45, d * Math.sin(ang)] });
      m.map(([x, y, z]) => [x, Math.max(0, y), z]);
      for (let i = 1; i < m.uvs.length; i += 2) m.uvs[i] = 0.55 + 0.4 * m.uvs[i];
      parts.push({ mesh: m, material: snow });
    }
  }

  // ---------- helpers ----------
  const surfB = (phi, y) => {
    // a point on the (undisplaced) body at azimuth phi (0 = front, + toward screen right) and height y, and its normal
    const q = clamp((y - YB) / RYB, -1, 1);
    const r = RB * Math.sqrt(1 - q * q);
    const p = [-r * Math.sin(phi), y, -r * Math.cos(phi)];
    return { p, n: vec.norm([p[0] / (RB * RB), (y - YB) / (RYB * RYB), p[2] / (RB * RB)]) };
  };
  const surfH = (phi, y) => {
    const q = clamp((y - YH) / RYH, -1, 1);
    const r = RH * Math.sqrt(1 - q * q);
    const p = [-r * Math.sin(phi), y, -r * Math.cos(phi)];
    return { p, n: vec.norm([p[0] / (RH * RH), (y - YH) / (RYH * RYH), p[2] / (RH * RH)]) };
  };
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
  const rightOut = (m) => {
    let vol = 0;
    for (let k = 0; k < m.indices.length; k += 3) {
      const a = m.point(m.indices[k]), b = m.point(m.indices[k + 1]), c = m.point(m.indices[k + 2]);
      vol += vec.dot(a, vec.cross(b, c));
    }
    return vol < 0 ? m.flip() : m;
  };
  const transformPoint = (p, opts) => {
    const m = new Mesh();
    m.vertex(p);
    m.transform(opts);
    return m.point(0);
  };

  // ---------- coal buttons ----------
  const coal = { name: 'coal', color: [0.012, 0.011, 0.012], roughness: 0.55 };
  const buttons = {};
  [0.43, 0.315, 0.2].forEach((y, k) => {
    const phi = [0.02, -0.03, 0.03][k];
    const { p, n } = surfB(phi, y);
    const s = 0.03 - k * 0.001;
    const m = sphere(s, { segments: 9, rows: 6, scale: [1, 0.9, 0.62] });
    // chunky and faceted, like lumps of coal
    bulge(m, (q) => (noise3(q[0] * 70 + k * 3, q[1] * 70, q[2] * 70, 31) - 0.5) * s * 0.7, { axis: 'xyz' });
    const yaw = (Math.atan2(-n[0], -n[2]) * 180) / Math.PI, pitch = (Math.asin(n[1]) * 180) / Math.PI;
    m.transform({ rotate: [0, 0, k * 40] });
    m.transform({ rotate: [pitch, yaw, 0], translate: [p[0] + n[0] * 0.006, p[1] + n[1] * 0.006, p[2] + n[2] * 0.006] });
    parts.push({ mesh: m, material: coal });
    buttons[`button${k + 1}`] = [p[0] + n[0] * 0.018, p[1] + n[1] * 0.018, p[2] + n[2] * 0.018];
  });

  // ---------- the carrot ----------
  const CL = 0.19, CRB = 0.026; // length (from the head's surface), base radius
  const carrotProf = [[0, -0.04], [CRB * 0.9, -0.04], [CRB, -0.03]];
  for (let k = 0; k <= 16; k++) {
    const y = (CL * k) / 16;
    const f = 1 - y / CL;
    const ridge = 1 + 0.05 * Math.sin(k * 2.1) * (k > 0 && k < 15 ? 1 : 0);
    carrotProf.push([Math.max(0.0035, CRB * Math.pow(f, 0.8) * ridge), y]);
  }
  carrotProf.push([0, CL + 0.004]);
  const carrotMesh = lathe(carrotProf, {
    segments: 18,
    section: (t, a, r) => {
      const wob = 1 + 0.07 * (noise3(Math.cos(a) * 2, t * 9, Math.sin(a) * 2, 41) - 0.5);
      return [r * wob * Math.cos(a), r * wob * Math.sin(a)];
    },
    v: (t, y) => clamp(1 - (y + 0.04) / (CL + 0.044)),
  });
  // a slight droop toward its tip, so it isn't a perfect cone
  carrotMesh.map(([x, y, z]) => [x + 0.06 * (Math.max(0, y) / CL) ** 2 * 0.25, y, z - 0.08 * (Math.max(0, y) / CL) ** 2 * 0.25]);
  const carrotTex = new Canvas(64, 256, [255, 128, 26]);
  carrotTex.shade((u, v) => {
    const y = (1 - v) * (CL + 0.044) - 0.04;
    const ring = 0.5 + 0.5 * Math.sin(y * 210 + Math.sin(u * TAU * 3) * 0.8);
    const n = fbm3(u * 8, v * 30, 0.5, { octaves: 2, seed: 44 });
    let col = [255, 128, 26].map((c) => c * (0.9 + 0.12 * n) * (1 - 0.14 * smooth(0.7, 1, ring)));
    if (y < 0) col = [214, 150, 70];
    return col;
  });
  for (let k = 0; k < 40; k++) {
    const R0 = rng(60 + k);
    const x = R0() * 64, y = 20 + R0() * 220;
    carrotTex.stroke([[x, y], [x + 6, y + 1.5]], 1.3, [190, 80, 10], 0.7);
  }
  const NOSE = { yaw: -14, pitch: 8 };
  const noseBase = (() => {
    const { p, n } = surfH(0, NOSE_Y);
    return [p[0] + n[0] * 0.004, p[1] + n[1] * 0.004, p[2] + n[2] * 0.004];
  })();
  const carrotT = { rotate: [-90 + NOSE.pitch, NOSE.yaw, 0], translate: noseBase };
  carrotMesh.transform(carrotT);
  parts.push({ mesh: carrotMesh, material: { name: 'carrot', texture: carrotTex, roughness: 0.75 } });
  const carrotTip = transformPoint([0.06 * 0.25, CL + 0.004, -0.08 * 0.25], carrotT);

  // ---------- the top hat ----------
  const hatTex = new Canvas(128, 64, [24, 23, 28]);
  hatTex.shade((u, v) => {
    if (u < 0.5) {
      const nap = fbm3(u * 40, v * 40, 0.5, { octaves: 3, seed: 51 });
      return [24, 23, 28].map((c) => c * (0.85 + 0.35 * nap));
    }
    if (u < 0.75) return v < 0.5 ? [206, 28, 40].map((c) => c * (0.92 + 0.08 * Math.sin(v * 60))) : [40, 140, 60];
    return [220, 24, 30];
  });
  const cell = (m, u, v) => {
    for (let i = 0; i < m.uvs.length; i += 2) {
      m.uvs[i] = u;
      m.uvs[i + 1] = v;
    }
    return m;
  };
  const HB = 0.19, HC = 0.118, HH = 0.15; // brim radius, crown radius, crown height
  const hatProf = [
    [0, 0.002], [HC, 0.002], [0.16, 0.004], [0.182, 0.009], [HB, 0.018], [HB + 0.002, 0.025], [HB - 0.006, 0.028], [0.172, 0.021], [0.15, 0.017],
    [HC + 0.004, 0.016, true], [HC, 0.03], [HC - 0.001, 0.07], [HC + 0.002, 0.11], [HC + 0.007, HH, true], [HC + 0.004, HH + 0.006], [HC - 0.01, HH + 0.008], [0, HH + 0.008],
  ];
  const hatMesh = lathe(hatProf, { segments: 56 });
  for (let i = 0; i < hatMesh.uvs.length; i += 2) hatMesh.uvs[i] *= 0.5; // the felt: left half of the picture
  // brim a touch wavy, the crown slightly dented, so it's been sat on
  hatMesh.map(([x, y, z]) => {
    const r = Math.hypot(x, z), a = Math.atan2(z, x);
    const dy = r > HC + 0.01 ? 0.012 * Math.sin(a * 2 + 0.6) * smooth(HC, HB, r) : 0;
    const dent = y > HH - 0.002 && r < HC ? -0.01 * Math.exp(-((r / 0.06) ** 2)) : 0;
    return [x, y + dy + dent, z];
  });
  const band = lathe([[HC + 0.0015, 0.018], [HC + 0.0045, 0.02], [HC + 0.0048, 0.046], [HC + 0.002, 0.049]], { segments: 56 });
  for (let i = 0; i < band.uvs.length; i += 2) {
    band.uvs[i] = 0.5 + 0.24 * band.uvs[i];
    band.uvs[i + 1] = 0.05 + 0.4 * band.uvs[i + 1];
  }
  // holly: two spiky leaves and three berries on the band, front-left
  const holly = new Mesh();
  {
    const leaf = [];
    for (let k = 0; k <= 20; k++) {
      const t = k / 20, a = t * TAU;
      const along = Math.cos(a) * 0.034 + 0.036, across = Math.sin(a) * 0.015 * (1 + 0.4 * Math.abs(Math.sin(a * 3.5)));
      leaf.push([along, across]);
    }
    const ha = -Math.PI / 2 + 0.55; // azimuth on the crown (0.55 rad toward screen left of the front)
    const out = [Math.cos(ha), 0, Math.sin(ha)];
    const yawDeg = (Math.atan2(-out[0], -out[2]) * 180) / Math.PI;
    for (const rot of [158, 22]) {
      const l = extrude(leaf, 0.004).transform({ rotate: [0, 0, rot], translate: [0, 0.034, 0] });
      l.transform({ rotate: [0, yawDeg, 0], translate: [out[0] * (HC + 0.008), 0, out[2] * (HC + 0.008)] });
      holly.append(cell(l, 0.69, 0.75));
    }
    for (const [dx, dy] of [[0, 0.036], [0.011, 0.03], [-0.009, 0.027]]) {
      const b = sphere(0.0085, { segments: 10, rows: 6 }).transform({ translate: [dx, dy, -0.006] });
      b.transform({ rotate: [0, yawDeg, 0], translate: [out[0] * (HC + 0.008), 0, out[2] * (HC + 0.008)] });
      holly.append(cell(b, 0.88, 0.5));
    }
  }
  const hat = new Mesh().append(hatMesh).append(band).append(holly);
  // sits on the head, tipped to screen left and back a little
  const HAT_TILT = [7, 0, -11];
  const up = transformPoint([0, 1, 0], { rotate: HAT_TILT });
  const seat = Math.sqrt(RH * RH - HC * HC) * (RYH / RH) - 0.004; // where the crown's rim meets the head
  const hatPos = [up[0] * seat, YH + up[1] * seat, up[2] * seat];
  hat.transform({ rotate: HAT_TILT, translate: hatPos });
  parts.push({ mesh: hat, material: { name: 'hat', texture: hatTex, roughness: 0.85 } });
  const hb = hat.bounds();
  const hatCentre = [0, 1, 2].map((d) => (hb.min[d] + hb.max[d]) / 2);

  // ---------- the scarf ----------
  const knit = new Canvas(256, 512, [200, 30, 40]);
  {
    // stripes across the scarf (along v), knitted V stitches
    knit.shade((u, v) => {
      const s = (v * 6) % 1;
      let col = s < 0.5 ? [206, 34, 44] : s < 0.62 ? [252, 244, 226] : s < 0.74 ? [52, 140, 84] : s < 0.86 ? [252, 244, 226] : [206, 34, 44];
      const cu = (u * 24) % 1, cv = (v * 96) % 1;
      const vee = Math.abs(cu - 0.5) * 2 - cv; // chevrons
      const stitch = 0.86 + 0.14 * smooth(-0.4, 0.4, Math.sin(vee * Math.PI * 2));
      const col2 = col.map((c) => c * stitch * (0.95 + 0.1 * fbm3(u * 30, v * 30, 0.5, { octaves: 2, seed: 61 })));
      return col2;
    });
  }
  const scarfMat = { name: 'scarf', texture: knit, roughness: 0.95 };
  // the wrap round the neck: a fat roll, dipping a little at the front
  const NECK_Y = YB + RYB - 0.035, NECK_R = 0.152;
  {
    const rings = [];
    const N = 72, S = 16;
    for (let j = 0; j <= N; j++) {
      const a = Math.PI / 2 + (TAU * j) / N; // starts at the back
      const front = -Math.sin(a); // 1 at the front (-Z)
      const c = [NECK_R * Math.cos(a), NECK_Y - 0.012 * front + 0.004 * Math.sin(a * 5), NECK_R * Math.sin(a)];
      const radial = [Math.cos(a), 0, Math.sin(a)];
      const ring = [];
      for (let i = 0; i <= S; i++) {
        const t = (TAU * i) / S;
        const bumpy = 1 + 0.06 * Math.sin(j * 1.7 + i * 0.9) * 0.5;
        const rr = 0.05 * bumpy, rv = 0.056 * bumpy;
        ring.push([c[0] + radial[0] * rr * Math.cos(t), c[1] + rv * Math.sin(t), c[2] + radial[2] * rr * Math.cos(t)]);
      }
      ring.v = j / N;
      rings.push(ring);
    }
    parts.push({ mesh: rightOut(loft(rings)), material: scarfMat });
  }
  // the knot (front, screen right) and the end hanging from it down the body
  const KNOT_PHI = 0.48;
  {
    const k = surfB(KNOT_PHI, NECK_Y - 0.02);
    const knot = sphere(0.055, { segments: 18, rows: 10, scale: [1, 0.85, 0.75] });
    bulge(knot, (p) => (noise3(p[0] * 30, p[1] * 30, p[2] * 30, 71) - 0.5) * 0.02, { axis: 'xyz' });
    const yaw = (Math.atan2(-k.n[0], -k.n[2]) * 180) / Math.PI;
    knot.transform({ rotate: [0, yaw, 0], translate: [k.p[0] + k.n[0] * 0.035, k.p[1], k.p[2] + k.n[2] * 0.035] });
    for (let i = 1; i < knot.uvs.length; i += 2) knot.uvs[i] *= 0.16;
    parts.push({ mesh: knot, material: scarfMat });
  }
  const tail = (phi0, phi1, y0, y1, w, lift, vScale) => {
    const keys = [];
    for (let s = 0; s <= 6; s++) {
      const t = s / 6;
      const { p, n } = surfB(lerp(phi0, phi1, t), lerp(y0, y1, t));
      const off = lift + 0.012 * Math.sin(t * Math.PI);
      keys.push([p[0] + n[0] * off, p[1] + n[1] * off, p[2] + n[2] * off, ...n, w * (1 + 0.15 * t), 0.012]);
    }
    const pts = spline(keys, 5);
    const rings = pts.map((k, j) => {
      const p = k.slice(0, 3), a = pts[Math.max(0, j - 1)], b = pts[Math.min(pts.length - 1, j + 1)];
      const t = vec.norm(vec.sub(b.slice(0, 3), a.slice(0, 3)));
      let nn = k.slice(3, 6);
      nn = vec.norm(vec.sub(nn, t.map((x) => x * vec.dot(nn, t))));
      const wd = vec.cross(t, nn);
      const ring = [];
      const S = 16;
      for (let i = 0; i <= S; i++) {
        const th = (TAU * (i % S)) / S, c = Math.cos(th), s = Math.sin(th);
        const cx = Math.sign(c) * Math.abs(c) ** 0.6;
        ring.push([0, 1, 2].map((d) => p[d] + wd[d] * k[6] * cx + nn[d] * k[7] * s));
      }
      ring.v = (j / (pts.length - 1)) * vScale;
      return ring;
    });
    const m = rightOut(loft(rings, { capBottom: true, capTop: true }));
    const end = pts[pts.length - 1];
    return { m, end, t: vec.norm(vec.sub(end.slice(0, 3), pts[pts.length - 2].slice(0, 3))), n: end.slice(3, 6), w: end[6] };
  };
  const tails = [tail(KNOT_PHI, 0.56, NECK_Y - 0.04, 0.27, 0.05, 0.016, 0.36), tail(KNOT_PHI + 0.06, 0.78, NECK_Y - 0.05, 0.36, 0.043, 0.006, 0.26)];
  for (const { m, end, t, n, w } of tails) {
    parts.push({ mesh: m, material: scarfMat });
    // fringe
    const wd = vec.cross(t, vec.norm(n));
    for (let f = 0; f < 6; f++) {
      const o = lerp(-0.8, 0.8, f / 5) * w;
      const base = [0, 1, 2].map((d) => end[d] + wd[d] * o);
      const tipP = [0, 1, 2].map((d) => base[d] + t[d] * 0.045 + n[d] * 0.004 * (f % 2));
      const strand = tube([base.map((x, d) => x - t[d] * 0.01), base, tipP], 0.0065, { segments: 6 });
      parts.push({ mesh: cell(strand, 0.5, 0.093), material: scarfMat });
    }
  }

  // ---------- result ----------
  const all = new Mesh();
  for (const p of parts) all.append(p.mesh);
  const { min, max } = all.bounds();
  const s = 1.15 / (max[1] - min[1]);
  return {
    parts,
    face: (FACE_Y - min[1]) / (max[1] - min[1]),
    faceSize: FACE_SIZE,
    anchors: {
      carrot: carrotTip,
      carrotBase: noseBase,
      hat: hatCentre,
      ...buttons,
      head: [0, YH, 0],
    },
    notes:
      `Hat: brim ${(2 * HB * s).toFixed(3)} m across, crown ${(2 * HC * s).toFixed(3)} m across and ${((HH + 0.008) * s).toFixed(3)} m tall ` +
      `(${((hb.max[1] - hb.min[1]) * s).toFixed(3)} m overall as worn), tipped ${-HAT_TILT[2]} deg to screen left and ${HAT_TILT[0]} deg back; ` +
      `the band and holly are in the "hat" material too. Carrot ${(CL * s).toFixed(3)} m long from the head (base radius ` +
      `${(CRB * s).toFixed(3)} m), pointing at the camera, its tip ${-NOSE.yaw} deg toward screen left and ${NOSE.pitch} deg up, with a slight droop. ` +
      `Head radius ${(RH * s).toFixed(3)} m, body radius ${(RB * s).toFixed(3)} m. Buttons are one "coal" material (all three).`,
  };
}
