import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { brotliDecompressSync } from 'node:zlib';
import { resolve, join } from 'node:path';

const source = resolve('TripleMaze_WebGL');
const output = resolve('_site');
if (!existsSync(join(source, 'index.html'))) throw new Error('Missing WebGL export');
rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });
cpSync(source, output, { recursive: true });
// Pages cannot configure Content-Encoding for Unity Brotli files.
// Publish decoded files and update the generated HTML references instead.
const build = join(output, 'Build');
for (const name of readdirSync(build)) {
  if (!name.endsWith('.br')) continue;
  const decoded = brotliDecompressSync(readFileSync(join(build, name)));
  writeFileSync(join(build, name.slice(0, -3)), decoded);
  rmSync(join(build, name));
  console.log(`${name}: ${decoded.length} bytes decoded`);
}
const index = join(output, 'index.html');
writeFileSync(index, readFileSync(index, 'utf8').replace(/(TripleMaze_WebGL\.(?:data|framework\.js|wasm))\.br/g, '$1'));
writeFileSync(join(output, '.nojekyll'), '');
for (const file of ['TripleMaze_WebGL.data', 'TripleMaze_WebGL.framework.js', 'TripleMaze_WebGL.wasm', 'TripleMaze_WebGL.loader.js']) {
  if (!existsSync(join(build, file))) throw new Error(`Missing ${file}`);
}
console.log('GitHub Pages artifact ready in _site');
