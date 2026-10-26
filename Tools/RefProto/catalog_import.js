// Builds the shared prototype furniture catalog from the user's asset export; not for release builds.
// Reads the original item catalog and the exported sprite list, then writes:
//   Assets/_Game/Resources/Content/malaysia/RefProto/furniture.json   furniture entries (merged by ContentLoader)
//   Assets/_Game/Resources/Content/malaysia/RefProto/floors.json      floor tiles (merged into theme floors)
//   Assets/_Game/Resources/Localization/RefProto/en.json              names (other languages fall back to English)
//   Tools/RefProto/art_map_catalog.json                               sprites for import.ps1 (included by art_map.json)
// Usage (project root):  node Tools/RefProto/catalog_import.js   then   powershell -File Tools/RefProto/import.ps1
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { execFileSync } = require('child_process');
const os = require('os');
// Windows' own tar reads zip files and drive letters (Git's GNU tar would take 'E:' for a remote host).
const tar = process.platform === 'win32' ? path.join(process.env.SystemRoot || 'C:/Windows', 'System32', 'tar.exe') : 'tar';

const root = path.resolve(__dirname, '..', '..');
const stoveBackViews = JSON.parse(fs.readFileSync(path.join(__dirname, 'art_map.json'), 'utf8')).stoveBackViews || {};
const exports_ = path.join(root, 'Builds', 'Exports');
const originalZip = path.join(exports_, 'GameAssets-0.9.143a-original.zip');
const fullZip = path.join(exports_, 'GameAssets-0.9.143a-full.zip');
for (const f of [originalZip, fullZip]) if (!fs.existsSync(f)) throw new Error('Asset export not found: ' + f);

// The item catalog (zlib-compressed XML inside the original archive).
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'refproto-'));
execFileSync(tar, ['-xf', originalZip, '-C', tmp, 'bin-xml']); // '[1]' in the name would be read as a pattern
const xml = zlib.inflateSync(fs.readFileSync(path.join(tmp, 'bin-xml', 'restaurant[1].bin'))).toString('utf8');

// How many frames each interior sprite has.
const frames = {};
const symbols = new Set();
for (const line of execFileSync(tar, ['-tf', fullZip], { maxBuffer: 1 << 28 }).toString().split(/\r?\n/)) {
  const symbol = line.match(/extracted\/interior\/sprites\/DefineSprite_\d+_([^/]+)\/\d+\.png$/);
  if (symbol) symbols.add(symbol[1]);
  const m = line.match(/extracted\/interior\/sprites\/DefineSprite_\d+_([A-Za-z0-9]+)\/(\d+)\.png$/);
  if (m) frames[m[1]] = Math.max(frames[m[1]] || 0, +m[2]);
}

const snake = s => s.replace(/([a-z0-9])([A-Z])/g, '$1_$2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1_$2').toLowerCase();
const furniture = [], floors = [], wallpapers = [], names = [], sprites = [];
const seen = new Set(); // some pieces are listed in two groups
let group = '';
const height = { Entertainment: 1.6, Toilet: 0.9, Sink: 1.0, Table: 0.75, Chair: 0.9, Decor: 1.2, Stove: 1.1, DrinkMachine: 2.0, WallDecor: 1.0 };

for (const line of xml.split(/\r?\n/)) {
  const g = line.match(/<group[^>]*\bname="([^"]+)"/);
  if (g) { group = g[1]; continue; }
  const m = line.match(/<item ([^>]*)\/>/);
  if (!m) continue;
  const a = {};
  for (const [, k, v] of m[1].matchAll(/(\w+)="([^"]*)"/g)) a[k] = v;
  if (a.invisible === 'true' || !a.cost || !a.className) continue; // hidden, or premium-currency only
  const n = frames[a.className] || 0;
  if (!n || n > 4) continue;
  const type = a.type || '';
  const id = 'ref_' + snake(a.className);
  if (seen.has(id)) continue;
  seen.add(id);
  const key = 'ref_' + snake(a.className);
  const price = parseInt(a.cost, 10);
  const unlock = a.unlockLevel ? parseInt(a.unlockLevel, 10) : 1;

  if (group === 'Floor Tile') {
    floors.push({ id, nameKey: 'floor.' + id, sprite: key });
    sprites.push({ key, group: 'interior', symbol: a.className, frame: 1 });
    names.push({ key: 'floor.' + id, value: a.name });
    continue;
  }
  if (group === 'Wallpaper') {
    // Frame 2 is drawn for the back-right wall (as the base wall01); the renderer mirrors it for the left.
    wallpapers.push({ id, nameKey: 'wallpaper.' + id, sprite: key });
    sprites.push({ key, group: 'interior', symbol: a.className, frame: Math.min(2, n) });
    names.push({ key: 'wallpaper.' + id, value: a.name });
    continue;
  }

  let kind, category;
  if (group === 'Table') { kind = 'Table'; category = 'Table'; }
  else if (group === 'Chair') { kind = 'Chair'; category = 'Chair'; }
  else if (group === 'Wall Decoration') { kind = 'WallDecor'; category = 'Wall'; }
  else if (group === 'Kitchen Appliance' || group === 'Drink') {
    kind = /drink/.test(type) || group === 'Drink' ? 'DrinkMachine' : 'Stove'; category = 'Kitchen';
  }
  else if (group === 'Decoration') { kind = 'Decor'; category = 'Decoration'; }
  else if (group === 'Outdoor Only') { kind = 'Decor'; category = 'Outdoor'; }
  else if (group === 'Functional' && /toilet/.test(type)) { kind = 'Toilet'; category = 'Functional'; }
  else if (group === 'Functional' && /sink/.test(type)) { kind = 'Sink'; category = 'Functional'; }
  else if (/interactive/.test(type)) { kind = 'Entertainment'; category = 'Functional'; }
  else continue; // doors and other systems come later

  // Frames: 4 = SE, SW, NW, NE views; 2 = the back-left and back-right versions (SW is the second);
  // 1 = a single view drawn facing SE, mirrored to our SouthWest convention.
  const sx = Math.max(1, parseInt(a.sizeX || '1', 10)), sy = Math.max(1, parseInt(a.sizeY || '1', 10));
  const entry = {
    id, nameKey: 'furniture.' + id, kind,
    sizeX: kind === 'WallDecor' ? 1 : Math.max(sx, sy), sizeY: kind === 'WallDecor' ? 1 : Math.min(sx, sy),
    height: height[kind], price, unlockLevel: unlock, sprite: key, category, placeholderColor: '#B08A60',
  };
  // Interactive machines face along their long axis (e.g. the pinball cabinet extends toward +Y).
  // Counters/tables have their long edge across +X; sorting both classes the same way rotates the blocked base.
  if (kind === 'Entertainment') { entry.sizeX = Math.min(sx, sy); entry.sizeY = Math.max(sx, sy); }
  if (kind === 'Chair' || kind === 'Toilet') entry.seatHeight = 0.35;
  if (/stackable/.test(type)) entry.stackable = true; // can stand on a table or counter
  if (/surface/.test(type)) entry.surface = true;     // other pieces can stand on it
  if (group === 'Outdoor Only') entry.outdoorDecor = true;
  if (kind === 'Toilet') entry.breakCount = parseInt(a.breakCount || '4', 10);
  if (kind === 'Entertainment') entry.breakCount = parseInt(a.breakCount || '0', 10);
  const front = { key, group: 'interior', symbol: a.className, frame: n === 1 ? 1 : 2 };
  if (n === 1) front.mirror = true;
  if (kind === 'Chair' || kind === 'Toilet') front.seatHeight = 0.25;
  if (kind === 'Stove' && n === 1) front.frontCells = 1; // stove art includes the mat the chef stands on
  sprites.push(front);
  if (n === 4 && kind !== 'WallDecor') {
    entry.spriteBack = key + '_back';
    sprites.push({ key: key + '_back', group: 'interior', symbol: a.className, frame: 4 });
  }
  if (kind === 'Stove' && n === 1) {
    const down = a.className.replace(/^Stove(\d+)$/, (_, digits) => 'Stove' + digits.padStart(2, '0')) + 'Down';
    const back = [...symbols].find(name => name === down || name.includes('.' + down + '_'));
    if (back) {
      entry.spriteBack = key + '_back';
      sprites.push({ key: key + '_back', group: 'interior', symbol: back, frame: 1 });
    }
    else if (stoveBackViews[a.className] && symbols.has(stoveBackViews[a.className].symbol)) {
      entry.spriteBack = key + '_back';
      sprites.push({ key: key + '_back', group: 'interior', ...stoveBackViews[a.className] });
    }
  }
  furniture.push(entry);
  names.push({ key: 'furniture.' + id, value: a.name });
}

const write = (file, data) => {
  const full = path.join(root, file);
  fs.mkdirSync(path.dirname(full), { recursive: true });
  fs.writeFileSync(full, JSON.stringify(data, null, 1));
};
write('Assets/_Game/Resources/Content/malaysia/RefProto/furniture.json', { furniture });
write('Assets/_Game/Resources/Content/malaysia/RefProto/floors.json', { floors });
write('Assets/_Game/Resources/Content/malaysia/RefProto/wallpapers.json', { wallpapers });
write('Assets/_Game/Resources/Localization/RefProto/en.json', { entries: names });
write('Tools/RefProto/art_map_catalog.json', { sprites });
fs.rmSync(tmp, { recursive: true, force: true });
const count = {};
for (const f of furniture) count[f.kind] = (count[f.kind] || 0) + 1;
console.log(`furniture ${furniture.length} ${JSON.stringify(count)}, floors ${floors.length}, wallpapers ${wallpapers.length}, sprites ${sprites.length}`);
