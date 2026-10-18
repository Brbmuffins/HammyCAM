/**
 * Raw Tripo GLB → small, Unity-ready GLB.
 *
 * Same cleanup as Death Muffin's build step (dedup, quantize, prune) with one
 * required change: textures are PNG, not WebP. glTFast (Unity's runtime glTF
 * loader) lists EXT_texture_webp as never to be supported; it reads PNG/JPEG
 * and KTX2. It does read KHR_mesh_quantization, so quantize stays.
 *
 * Usage: node src/build.mjs <raw.glb> <out.glb> [texturePx]
 */
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { dedup, prune, quantize, textureCompress } from '@gltf-transform/functions';
import sharp from 'sharp';
import { statSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ensureDir } from './common.mjs';

const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
export const TEXTURE_PX = 512;

export async function buildProp(rawFile, outFile, size = TEXTURE_PX) {
  const doc = await io.read(rawFile);
  await doc.transform(
    dedup(),
    textureCompress({ encoder: sharp, targetFormat: 'png', resize: [size, size] }),
    quantize({ quantizeNormal: 10, quantizeTexcoord: 12, quantizePosition: 14 }),
    prune(),
  );
  const root = doc.getRoot();
  const textures = root.listTextures().map((t) => t.getMimeType());
  const webp = textures.includes('image/webp') || root.listExtensionsUsed().some((e) => e.extensionName === 'EXT_texture_webp');
  if (webp) throw new Error(`${rawFile}: WebP texture left in the output; glTFast cannot load it`);
  ensureDir(dirname(outFile));
  await io.write(outFile, doc);
  const tris = root
    .listMeshes()
    .flatMap((m) => m.listPrimitives())
    .reduce((n, p) => n + (p.getIndices()?.getCount() ?? 0) / 3, 0);
  return { tris, bytes: statSync(outFile).size, textures, extensions: root.listExtensionsUsed().map((e) => e.extensionName) };
}

const isCli = process.argv[1] && resolve(process.argv[1]).toLowerCase() === fileURLToPath(import.meta.url).toLowerCase();
if (isCli) {
  const [raw, out, px] = process.argv.slice(2);
  if (!raw || !out) {
    console.error('usage: node src/build.mjs <raw.glb> <out.glb> [texturePx]');
    process.exit(1);
  }
  console.log(JSON.stringify(await buildProp(raw, out, px ? Number(px) : TEXTURE_PX), null, 2));
}
