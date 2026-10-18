/**
 * matte.mjs — make a built avatar less glossy and plasticky: rewrites its metallic-roughness texture so every
 * surface is rougher (the map's own variation kept: roughness' = min + (1 - min) * roughness) and, for things
 * that aren't metal (food), most of the metal taken out (metal' = metal * scale).
 *
 *   node src/matte.mjs <out/id.glb> [--rough-min 0.5] [--metal 0.15]
 *
 * Writes the GLB in place. Run it after `batch.mjs build` (which rewrites out/ from raw/), then copy to Unity.
 */
import sharp from 'sharp';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';

const args = process.argv.slice(2);
const file = args.find((a) => !a.startsWith('--'));
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 ? parseFloat(args[i + 1]) : fallback;
};
if (!file) {
  console.log('usage: node src/matte.mjs <out/id.glb> [--rough-min 0.5] [--metal 0.15]');
  process.exit(1);
}
const roughMin = opt('--rough-min', 0.5), metalScale = opt('--metal', 0.15);

const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const doc = await io.read(file);
const done = new Set();
let plain = 0;
for (const material of doc.getRoot().listMaterials()) {
  const texture = material.getMetallicRoughnessTexture();
  if (!texture) {
    // No map (SPAR3D models): the same remap on the material's own values.
    material.setRoughnessFactor(roughMin + (1 - roughMin) * material.getRoughnessFactor());
    material.setMetallicFactor(material.getMetallicFactor() * metalScale);
    plain++;
    continue;
  }
  if (done.has(texture)) continue;
  done.add(texture);
  const { data, info } = await sharp(Buffer.from(texture.getImage())).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  for (let i = 0; i < data.length; i += info.channels) {
    // glTF: green = roughness, blue = metalness.
    data[i + 1] = Math.round(255 * (roughMin + (1 - roughMin) * (data[i + 1] / 255)));
    data[i + 2] = Math.round(data[i + 2] * metalScale);
  }
  const image = sharp(data, { raw: { width: info.width, height: info.height, channels: info.channels } });
  const mime = texture.getMimeType();
  const encoded = mime === 'image/webp' ? await image.webp({ lossless: true }).toBuffer()
    : mime === 'image/jpeg' ? await image.jpeg({ quality: 92 }).toBuffer()
    : await image.png().toBuffer();
  texture.setImage(new Uint8Array(encoded));
}
await io.write(file, doc);
console.log(`${file}: ${done.size} texture(s) and ${plain} plain material(s) made matte (roughness from ${roughMin}, metal x${metalScale})`);
