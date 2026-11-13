'use strict';
// Build a public, immutable snapshot from an MCP export; never read local-save config.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const vm = require('node:vm');
const zlib = require('node:zlib');
const [input, destination] = process.argv.slice(2);
if (!input || !destination) throw Error('Usage: node build-readonly.cjs <MCP-export.json> <output-directory>');
const here = __dirname, design = path.dirname(here), out = path.resolve(destination);
const read = file => fs.readFileSync(path.join(here, file), 'utf8');
const json = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const safe = value => JSON.stringify(value).replace(/</g, '\\u003c').replace(/\u2028/g, '\\u2028').replace(/\u2029/g, '\\u2029');
const original = json(path.resolve(input));
const stamp = new Date().toISOString();
const selectedMod=require('./mods.cjs').getMod(process.env.SHOUJO_WORKBENCH_MOD||'junna');
const snapshotPath=selectedMod.id==='junna'?'/snapshot.json':'/mods/'+selectedMod.id+'/snapshot.json';
const state = {
  schemaVersion: original.schemaVersion,
  workspaceId: original.workspaceId + ':readonly:' + original.revision,
  revision: original.revision, data: original.data, assets: {},
  history: [], undoStack: [], redoStack: [], seedBatches: [], appliedSeedBatches: [],
  ui: {tab: 'cards', pool: 'pool', sort: 'natural', selected: null}, exportedAt: stamp
};
fs.mkdirSync(path.join(out, 'assets'), {recursive: true});
const assets = {...json(path.join(here, 'assets.json')), ...original.assets};
const assetFiles = new Set();
for (const [id, value] of Object.entries(assets)) {
  if (value.startsWith('/assets/')) {
    assetFiles.add(path.basename(value));
    assets[id] = value;
    continue;
  }
  const match = /^data:([^;,]+);base64,([\s\S]+)$/.exec(value);
  if (!match) throw Error('Unsupported asset: ' + id);
  const bytes = Buffer.from(match[2], 'base64');
  const ext = {'image/png':'png','image/jpeg':'jpg','image/webp':'webp','image/gif':'gif','image/svg+xml':'svg','font/woff2':'woff2','font/woff':'woff','font/ttf':'ttf','application/font-woff':'woff','application/octet-stream':'bin'}[match[1]] || 'bin';
  const name = crypto.createHash('sha256').update(bytes).digest('hex').slice(0, 24) + '.' + ext;
  fs.writeFileSync(path.join(out, 'assets', name), bytes);
  assetFiles.add(name); assets[id] = '/assets/' + name;
}
let app = read('app.js');
function replaceOnce(before, after) {
  if (!app.includes(before)) throw Error('Read-only build anchor missing: ' + before.slice(0, 80));
  app = app.replace(before, after);
}
replaceOnce("const previewMode=new URLSearchParams(location.hash.slice(1)).get('preview')==='1';", 'const previewMode=false;');
app = app.replace(/  let connection=null;[^\n]+\n/, '  const connection=null;\n');
replaceOnce('function assetURL(id){const source=', 'function assetURL(id){return new URL(state.assets?.[id]||builtAssets[id]||builtAssets.defaultPortrait,location.href).href;}\n  function unusedAssetURL(id){const source=');
replaceOnce('function syncMirror(){', 'function syncMirror(){return true;');
replaceOnce('function persist(){', 'function persist(){return Promise.resolve();');
replaceOnce("function transact(label,mutate,kind='编辑'){", "function transact(label,mutate,kind='编辑'){return null;");
replaceOnce('function applyField(input){', 'function applyField(input){return;');
replaceOnce('function beginEdit(){', 'function beginEdit(){return;');
replaceOnce('function undo(){', 'function undo(){return;');
replaceOnce('function redo(){', 'function redo(){return;');
replaceOnce('function routeAction(action){', "function routeAction(action){if(!['upgrade','clear-filters','meta','copy-template','cards-by-term'].includes(action))return;");
const boot = app.indexOf('  let saved=null,mirror=null;');
const render = app.indexOf('  readHash();document.documentElement', boot);
if (boot < 0 || render < 0) throw Error('Read-only bootstrap anchor missing');
app = app.slice(0, boot) + '  state=clone(seed);state.data=normalizeData(state.data);\n' + app.slice(render);
const tail = app.indexOf('  const handoff=new URLSearchParams', boot);
if (tail < 0) throw Error('Read-only API anchor missing');
app = app.slice(0, tail) + "  window.__JUNNA_READONLY__={revision:state.revision,ready:true};\n})();\n";
// Browser draft storage and MCP transport are omitted from the published runtime.
app = app.replace(/  try\{oldDraftKey=sessionStorage[^\n]+\n/, '');
app = app.replace(/  async function connectLocalService\(\)\{[\s\S]*?\n  \}/, '  async function connectLocalService(){return false;}');
app = app.replace(/  async function saveHTML\(\)\{[\s\S]*?(?=  function imageDataURL)/, '  async function saveHTML(){}\n');
const guard = read('readonly-view.js');
const sources = ['mod-switcher.js','filter-state.js','arrow-focus.js','energy-wording.js','card-reference-wording.js','effect-statistics.js','route-statistics.js','route-statistics-view.js','pool-reference.js','native-card-pools.js','native-pool-comparison.js','card-model-view.js','fit-pool-view.js','event-view.js','potion-view.js','relic-view.js','power-view.js','card-hover-tips.js','workbench-domain.js','initial-config.js','mobile-layout.js'].map(read);
const script = [guard, read('pwa.js'), ...sources, app].join('\n');
new vm.Script(script);
const css = ['workbench.css','card-model.css','fit-pool.css','relic.css','power.css','card-hover-tips.css','readonly.css','initial-config.css','mobile.css','mod-switcher.css'].map(read).join('\n').replaceAll('__REGULAR_FONT__', assets['game-regular']).replaceAll('__BOLD_FONT__', assets['game-bold']);
const inlineData = (id, data) => '<script id="' + id + '" type="application/json">' + safe(data) + '</script>';
const optionalData = name => {const file=path.join(design,'data',name);return fs.existsSync(file)?json(file):null;};
let html = require('./mod-page.cjs').modPage(read('shell.html'),{modId:selectedMod.id})
  .replace('<!--PWA-->', () => require('./pwa.cjs').buildPwa(out))
  .replace('<!--STYLE-->', () => '<style>' + css + '</style>')
  .replace('<!--CONNECTION-->', '')
  .replace('<!--ASSETS-->', () => inlineData('workbench-assets', assets))
  .replace('<!--DATA-->', () => '<script id="workbench-data" type="application/json" data-src="'+snapshotPath+'">'+safe({ui:state.ui})+'</script>')
  .replace('<!--ROUTE_DATA-->', () => inlineData('workbench-route-data', json(path.join(here,'route-statistics-defaults.json'))))
  .replace('<!--APP-->', () => inlineData('workbench-battle-sim',optionalData('纯那箭矢等待补偿战斗模拟_20261103.json')) + inlineData('workbench-card-audit',optionalData('纯那全卡牌数值校验_20261103.json')) + '<script>' + script + '</script>')
  .replace('<head>', '<head><meta http-equiv="Content-Security-Policy" content="default-src \'none\'; manifest-src \'self\'; script-src \'self\'; style-src \'self\' \'unsafe-inline\'; img-src \'self\' data: blob:; font-src \'self\' data:; connect-src \'self\'; base-uri \'none\'; form-action \'none\'; object-src \'none\'"><meta name="robots" content="noindex,nofollow">')
  .replace('<body>', '<body class="readonly">')
  .replace('<div class="global-actions">', '<div class="global-actions"><span class="readonly-status">只读 · ' + new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Shanghai',dateStyle:'short'}).format(new Date(stamp)) + '</span>')
  .replace('title="编辑名称"', 'title="少女歌剧工作台" tabindex="-1"')
  .replace('aria-label="编辑栏"', 'aria-label="详情"');
if (/"token"\s*:|workbench-connection|JunnaWorkbenchMcp\.create/.test(html)) throw Error('Published snapshot contains connection information');
html = require('./deploy/static-runtime.cjs').externalize(html, out);
fs.writeFileSync(path.join(out,'snapshot.json'),safe({...state,workspaceId:original.workspaceId,assets:Object.fromEntries(Object.keys(original.assets).map(id=>[id,assets[id].startsWith('/')?assets[id]:'/'+assets[id]]))}));
fs.writeFileSync(path.join(out,'index.html'), html);
fs.writeFileSync(path.join(out,'index.html.gz'), zlib.gzipSync(html,{level:9}));
const manifest = {revision:original.revision,exportedAt:stamp,counts:Object.fromEntries(Object.entries(original.data).filter(([,v])=>Array.isArray(v)).map(([k,v])=>[k,v.length])),assets:assetFiles.size,htmlBytes:Buffer.byteLength(html),sha256:crypto.createHash('sha256').update(html).digest('hex')};
fs.writeFileSync(path.join(out,'manifest.json'), JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify(manifest));
