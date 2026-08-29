import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const outputPath = resolve(scriptDirectory, '../src/MorseTrainer/Assets/MorseTrainer.ico');
const sizes = [16, 32, 48, 256];

function createImage(size) {
  const xorSize = size * size * 4;
  const maskRowBytes = Math.ceil(size / 32) * 4;
  const maskSize = maskRowBytes * size;
  const image = Buffer.alloc(40 + xorSize + maskSize);

  image.writeUInt32LE(40, 0);
  image.writeInt32LE(size, 4);
  image.writeInt32LE(size * 2, 8);
  image.writeUInt16LE(1, 12);
  image.writeUInt16LE(32, 14);
  image.writeUInt32LE(0, 16);
  image.writeUInt32LE(xorSize, 20);

  const center = (size - 1) / 2;
  const circleRadius = size * 0.46;
  const dotRadius = Math.max(1, size * 0.065);
  const markY = size * 0.5;

  for (let y = 0; y < size; y += 1) {
    for (let x = 0; x < size; x += 1) {
      const distance = Math.hypot(x - center, y - center);
      const insideCircle = distance <= circleRadius;
      const dotOne = Math.hypot(x - size * 0.29, y - markY) <= dotRadius;
      const dotTwo = Math.hypot(x - size * 0.45, y - markY) <= dotRadius;
      const dash = x >= size * 0.57 && x <= size * 0.79 && Math.abs(y - markY) <= dotRadius * 0.72;
      const isMark = insideCircle && (dotOne || dotTwo || dash);

      const color = isMark
        ? [29, 37, 31, 255]
        : insideCircle
          ? [177, 230, 117, 255]
          : [0, 0, 0, 0];
      const row = size - 1 - y;
      const offset = 40 + (row * size + x) * 4;
      image[offset] = color[2];
      image[offset + 1] = color[1];
      image[offset + 2] = color[0];
      image[offset + 3] = color[3];
    }
  }

  return image;
}

const images = sizes.map(createImage);
const directorySize = 6 + sizes.length * 16;
const totalSize = directorySize + images.reduce((sum, image) => sum + image.length, 0);
const icon = Buffer.alloc(totalSize);
icon.writeUInt16LE(0, 0);
icon.writeUInt16LE(1, 2);
icon.writeUInt16LE(sizes.length, 4);

let imageOffset = directorySize;
for (let index = 0; index < sizes.length; index += 1) {
  const size = sizes[index];
  const image = images[index];
  const entryOffset = 6 + index * 16;
  icon[entryOffset] = size === 256 ? 0 : size;
  icon[entryOffset + 1] = size === 256 ? 0 : size;
  icon[entryOffset + 2] = 0;
  icon[entryOffset + 3] = 0;
  icon.writeUInt16LE(1, entryOffset + 4);
  icon.writeUInt16LE(32, entryOffset + 6);
  icon.writeUInt32LE(image.length, entryOffset + 8);
  icon.writeUInt32LE(imageOffset, entryOffset + 12);
  image.copy(icon, imageOffset);
  imageOffset += image.length;
}

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, icon);
console.log(`Generated ${outputPath}`);
