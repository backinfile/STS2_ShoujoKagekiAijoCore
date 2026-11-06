// Data-integrity tests for the actual browser functions, with an in-memory DOM adapter.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(__dirname + '/../../web/telemetry/app.js', 'utf8').split("document.addEventListener('click',event=>{")[0];
const browserSource = fs.readFileSync(__dirname + '/../../web/telemetry/app.js', 'utf8');
const restoreCode = browserSource.slice(browserSource.indexOf('const urlState=comparisonURL;'), browserSource.indexOf("if(['cards','runs','compare'].includes(urlState.get('view')))"));
const restoredControls = Object.fromEntries(['build', 'core_version', 'source_mod'].map(key => [key, {
  value: 'all', options: [{value: 'all'}], append(option) { this.options.push(option); }
}]));
vm.runInNewContext(restoreCode, {
  comparisonURL: new URLSearchParams('build=test-build&core_version=1.0.1&source_mod=Example'),
  document: {createElement: () => ({})},
  $: () => ({elements: {namedItem: key => restoredControls[key]}}),
});
assert.equal(restoredControls.build.value, 'test-build');
assert.equal(restoredControls.core_version.value, '1.0.1');
assert.equal(restoredControls.source_mod.value, 'Example');
console.log('PASS dynamic filter URL restoration');
const nodes = new Map();
const node = id => {
  if (!nodes.has(id)) nodes.set(id, {
    value: id === 'group' ? 'none' : ['type', 'rarity', 'owner', 'outcome', 'character', 'starters'].includes(id) ? 'all' : '',
    disabled: false, innerHTML: '', textContent: '', hidden: true, dataset: {},
    classList: {add(){}, remove(){}, toggle(){}}, querySelectorAll(){return []},
    focus(){}, setAttribute(){},
  });
  return nodes.get(id);
};
const document = {getElementById:node, querySelectorAll(){return []}, activeElement:{tagName:'BODY',dataset:{}}};
const context = vm.createContext({document, URLSearchParams, Blob, setTimeout:()=>0, clearTimeout(){},
  location:{hash:''}, history:{replaceState(_state,_title,hash){context.savedHash=hash}},
  matchMedia:()=>({matches:false}), saved:null, savedHash:'', exports:0});
vm.runInContext(source, context);
const run = code => vm.runInContext(code, context);
const cards = [
  {id:'KAREN_STRIKE',name:'打击',owner:'karen',owner_name:'华恋',rarity:'基础',type:'攻击',cost:1,held:1,wins:1,picked:0,offered:0,pick_rate:null,win_rate:1,hold_rate:1,recorded_offers:0,recorded_picked:0,unknown_picked:0},
  {id:'COORDINATE',name:'协作',owner:'colorless',owner_name:'无色',rarity:'普通',type:'技能',cost:1,held:1,wins:0,picked:1,offered:2,pick_rate:.5,win_rate:0,hold_rate:1,recorded_offers:3,recorded_picked:1,unknown_picked:1},
];
context.sample = {cards,seats:1,runs:1,wins:1,excluded:0,missing_picked:1,builds:[],latest_received:null};
(async()=>{
  run(`state.stats=sample;state.runs={items:[],page:1,total:0,page_size:20};
    params=()=>new URLSearchParams({ascension:'0'});save=(_name,blob)=>{saved=blob;exports++};
    api=async()=>{throw new Error('offline')};`);
  await run('load()');
  run('csv()');
  assert.equal(context.exports,0,'failed filter must not export stale rows');
  assert.equal(node('export').disabled,true);
  console.log('PASS stale export blocked');

  run(`api=async path=>path.startsWith('api/stats')?sample:{items:[],page:1,total:0,page_size:20};`);
  await run('load()');
  assert.equal(node('export').disabled,false);
  node('owner').value='colorless';
  assert.deepEqual(Array.from(run('filteredCards().map(c=>c.id)')),['COORDINATE']);
  run('csv();syncURL()');
  const csv=await context.saved.text();
  assert.ok(csv.includes('"ascension","0"'));
  assert.ok(csv.includes('"owner","colorless"'));
  assert.ok(csv.includes('"COORDINATE"')&&!csv.includes('"KAREN_STRIKE"'));
  assert.ok(context.savedHash.includes('owner=colorless'));
  console.log('PASS owner filter, URL and CSV agree');

  node('owner').value='all';
  run(`state.display='chart';renderCards()`);
  assert.ok(node('cards').innerHTML.includes('no-data'));
  assert.ok(node('cards').innerHTML.includes('n=2'));
  console.log('PASS chart distinguishes missing data and includes denominators');

  context.requests=[];
  run(`api=async path=>{requests.push(path);return path.startsWith('api/stats')?sample:{items:[],page:1,total:0,page_size:20}}`);
  node('character').value='SILENT';
  await run('load()');
  assert.ok(context.requests.find(path=>path.startsWith('api/runs?')).includes('character=SILENT'));
  assert.ok(context.requests.find(path=>path.startsWith('api/stats?')).includes('character=SILENT'));
  run('csv()');
  assert.ok((await context.saved.text()).includes('"character","SILENT"'));
  run('syncURL()');
  assert.ok(context.savedHash.includes('character=SILENT'));
  run("updateCharacters(['CUSTOM_CLASS'],'CUSTOM_CLASS')");
  assert.equal(node('character').value,'CUSTOM_CLASS');
  assert.ok(node('character').innerHTML.includes('value="CUSTOM_CLASS"'));
  run("updateCharacters([],'CUSTOM_CLASS')");
  assert.ok(node('character').innerHTML.includes('value="CUSTOM_CLASS"'),'selected profession stays available with no matching samples');
  node('character').value='all';
  console.log('PASS shared character query, custom professions and card CSV stay in sync');

  context.groupSample={...context.sample,seats:10,cards:[cards[0],
    {...cards[1],id:'COMMON_LOW',name:'普通低',cost:0,pick_rate:0,held:1},
    {...cards[1],id:'COMMON_HIGH',name:'普通高',cost:2,pick_rate:.8,held:4},
    {...cards[1],id:'RARE_LOW',name:'稀有低',rarity:'稀有',cost:1,pick_rate:.2,held:2},
    {...cards[0],id:'RARE_MISSING',name:'稀有未知',rarity:'稀有',cost:3,held:3}]};
  node('group').value='rarity';node('starters').value='hide';
  run(`state.stats=groupSample;state.sort='pick_rate';state.order=-1;state.display='table';renderCards()`);
  assert.deepEqual(Array.from(run('filteredCards().map(c=>c.id)')),['COMMON_HIGH','COMMON_LOW','RARE_LOW','RARE_MISSING']);
  assert.equal(node('sortLabel').textContent,'组内排序');
  assert.ok(node('cards').innerHTML.includes('scope="rowgroup"'));
  assert.equal((node('cards').innerHTML.match(/class="rarity-group"/g)||[]).length,2);
  assert.ok(!node('cards').innerHTML.includes('KAREN_STRIKE'));
  run('state.order=1;renderCards()');
  assert.deepEqual(Array.from(run('filteredCards().map(c=>c.id)')),['COMMON_LOW','COMMON_HIGH','RARE_LOW','RARE_MISSING']);
  assert.equal(run('state.stats.seats'),10,'card filters must keep the cohort denominator');
  run(`state.sort='cost';state.order=-1;state.display='chart';renderCards()`);
  assert.deepEqual(Array.from(run('filteredCards().map(c=>c.id)')),['COMMON_HIGH','COMMON_LOW','RARE_MISSING','RARE_LOW']);
  assert.equal((node('cards').innerHTML.match(/<h3>/g)||[]).length,2);
  assert.ok(node('cards').innerHTML.includes('no-data'));
  run('csv();syncURL()');
  const groupedCSV=await context.saved.text();
  assert.ok(groupedCSV.includes('"starters","hide"')&&groupedCSV.includes('"group","rarity"'));
  assert.ok(!groupedCSV.includes('KAREN_STRIKE')&&groupedCSV.includes('"character","SILENT"'));
  assert.ok(groupedCSV.indexOf('"COMMON_HIGH"')<groupedCSV.indexOf('"COMMON_LOW"'));
  assert.ok(groupedCSV.indexOf('"RARE_MISSING"')<groupedCSV.indexOf('"RARE_LOW"'));
  assert.ok(context.savedHash.includes('group=rarity')&&context.savedHash.includes('starters=hide'));
  console.log('PASS starter filter, rarity groups, in-group sort, chart and CSV agree');

  node('group').value='owner';node('starters').value='all';
  run("state.sort='cost';state.order=1;state.display='table';renderCards()");
  const ownerIds=Array.from(run('filteredCards().map(c=>c.id)'));
  assert.deepEqual(ownerIds,['KAREN_STRIKE','RARE_MISSING','COMMON_LOW','RARE_LOW','COMMON_HIGH']);
  assert.equal(node('sortLabel').textContent,'组内排序');
  assert.ok(node('cards').innerHTML.includes('华恋<span>2 张</span>'));
  assert.ok(node('cards').innerHTML.includes('无色<span>3 张</span>'));
  run("state.display='chart';renderCards();csv();syncURL()");
  assert.equal((node('cards').innerHTML.match(/<h3>/g)||[]).length,2);
  assert.deepEqual(Array.from(run('filteredCards().map(c=>c.id)')),ownerIds);
  const ownerCSV=await context.saved.text();
  assert.ok(ownerCSV.includes('"group","owner"')&&context.savedHash.includes('group=owner'));
  for(let i=1;i<ownerIds.length;i++)assert.ok(ownerCSV.indexOf('"'+ownerIds[i-1]+'"')<ownerCSV.indexOf('"'+ownerIds[i]+'"'));
  assert.equal(run('state.stats.seats'),10);
  node('starters').value='hide';
  console.log('PASS owner groups, in-group sort, chart, URL and CSV agree');

  node('rarity').value='基础';run('renderCards()');
  assert.deepEqual(Array.from(run('filteredCards()')),[]);
  assert.ok(node('cards').innerHTML.includes('没有匹配的卡牌'));
  node('rarity').value='all';node('starters').value='all';node('group').value='none';
  run('renderCards()');
  assert.equal(run('filteredCards().length'),5);
  assert.equal(node('sortLabel').textContent,'排序');
  assert.ok(!node('cards').innerHTML.includes('<h3>'));
  run('state.stats=sample');
  console.log('PASS empty combined filters and ungrouped reset');

  run(`state.view='runs';state.runs.items=[{id:'fixture',received_at:0,start_time:0,outcome:'win',floor:1,ascension:0,player_count:1,build:' \t=1+1'}];csv()`);
  assert.ok((await context.saved.text()).includes("' \t=1+1"));
  console.log('PASS CSV formula prefix escaped');

  context.legacy={summary:{id:'fixture',start_time:0,outcome:'win',ascension:0,floor:1,duration:10,build:'fixture'},
    history:{players:[{id:1,character:'KAREN',deck:[],relics:[null]}],map_point_history:[[{map_point_type:'event',rooms:[null],player_stats:[{player_id:1,rest_site_choices:'rest'}]}]]}};
  run('state.history=legacy;renderHistory()');
  console.log('PASS legacy display fields degrade without crashing');
})().catch(error=>{console.error(error);process.exitCode=1});
