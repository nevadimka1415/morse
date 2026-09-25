import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Иконка Windows: мятный круг и знак «· — —» (буква В), как на телефоне и в шапке окна.
// Края сглаживаются: каждый пиксель считается по сетке 4×4 точек.
const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const outputPath = resolve(scriptDirectory, '../src/MorseTrainer/Assets/MorseTrainer.ico');
const sizes = [16, 24, 32, 48, 64, 256];
const circleColor = [0x75, 0xe6, 0xb1]; // PrimaryBrush программы, RGB
const markColor = [0x1d, 0x25, 0x1f];
const samples = 4;

/** Фигуры знака в долях стороны: точка и два тире-капсулы по центру круга. */
function markShapes(size) {
  // На маленьких размерах промежутки шире, иначе точка и тире сливаются
  // Тире почти вдвое длиннее точки; на 16–24 px короче, чтобы знак не упирался в край круга
  const half = 0.07;
  const gap = Math.max(0.05, 1.5 / size);
  const dash = size <= 24 ? 0.22 : 0.27;
  const total = half * 2 + gap + dash + gap + dash;
  const left = 0.5 - total / 2;
  const dotCenter = left + half;
  const dashOne = left + half * 2 + gap;
  const dashTwo = dashOne + dash + gap;
  return { half, dotCenter, dashes: [[dashOne, dashOne + dash], [dashTwo, dashTwo + dash]] };
}

function insideCapsule(x, y, from, to, half) {
  // Капсула: отрезок от from + half до to − half, скруглённый радиусом half
  const nearestX = Math.min(Math.max(x, from + half), to - half);
  return Math.hypot(x - nearestX, y - 0.5) <= half;
}

function coverage(size, px, py) {
  const shapes = markShapes(size);
  let circleHits = 0;
  let markHits = 0;
  for (let sy = 0; sy < samples; sy += 1) {
    for (let sx = 0; sx < samples; sx += 1) {
      const x = (px + (sx + 0.5) / samples) / size;
      const y = (py + (sy + 0.5) / samples) / size;
      if (Math.hypot(x - 0.5, y - 0.5) > 0.46) {
        continue;
      }

      circleHits += 1;
      const onDot = Math.hypot(x - shapes.dotCenter, y - 0.5) <= shapes.half;
      const onDash = shapes.dashes.some(([from, to]) => insideCapsule(x, y, from, to, shapes.half));
      if (onDot || onDash) {
        markHits += 1;
      }
    }
  }

  const total = samples * samples;
  return { circle: circleHits / total, mark: circleHits === 0 ? 0 : markHits / circleHits };
}

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

  for (let y = 0; y < size; y += 1) {
    for (let x = 0; x < size; x += 1) {
      const { circle, mark } = coverage(size, x, y);
      const color = circleColor.map((channel, index) => Math.round(channel + (markColor[index] - channel) * mark));
      // Строки в BMP идут снизу вверх, пиксель — в порядке B, G, R, A
      const row = size - 1 - y;
      const offset = 40 + (row * size + x) * 4;
      image[offset] = color[2];
      image[offset + 1] = color[1];
      image[offset + 2] = color[0];
      image[offset + 3] = Math.round(circle * 255);
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
