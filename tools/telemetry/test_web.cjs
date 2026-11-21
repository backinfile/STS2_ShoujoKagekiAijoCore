// Data-integrity tests for the actual browser functions, with an in-memory DOM adapter.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(__dirname + '/../../web/telemetry/app.js', 'utf8').split("document.addEventListener('click',event=>{")[0];
const browserSource = fs.readFileSync(__dirname + '/../../web/telemetry/app.js', 'utf8');
const restoreCode = browserSource.slice(browserSource.indexOf('const urlState=comparisonURL;'), browserSource.indexOf("if(Object.hasOwn(VIEW_TITLES"));
const restoredControls = Object.fromEntries(['build', 'core_version', 'source_mod', 'language'].map(key => [key, {
  value: 'all', options: [{value: 'all'}], append(option) { this.options.push(option); }
}]));
vm.runInNewContext(restoreCode, {
  comparisonURL: new URLSearchParams('build=test-build&core_version=1.0.1&source_mod=Example&language=jpn'),
  document: {createElement: () => ({})},
  $: () => ({elements: {namedItem: key => restoredControls[key]}}),
});
assert.equal(restoredControls.build.value, 'test-build');
assert.equal(restoredControls.core_version.value, '1.0.1');
assert.equal(restoredControls.source_mod.value, 'Example');
assert.equal(restoredControls.language.value, 'jpn');
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
const context = vm.createContext({document, URLSearchParams, Blob, AbortController, setTimeout:()=>0, clearTimeout(){},
  location:{hash:''}, history:{replaceState(_state,_title,hash){context.savedHash=hash}},
  matchMedia:()=>({matches:false}), saved:null, savedHash:'', exports:0});
vm.runInContext(source, context);
const run = code => vm.runInContext(code, context);
const cards = [
  {id:'KAREN_STRIKE',name:'打击',owner:'karen',owner_name:'华恋',rarity:'基础',type:'攻击',cost:1,held:1,wins:1,picked:0,offered:0,pick_rate:null,win_rate:1,hold_rate:1,recorded_offers:0,recorded_picked:0,unknown_picked:0},
  {id:'COORDINATE',name:'协作',owner:'colorless',owner_name:'无色',rarity:'普通',type:'技能',cost:1,held:1,wins:0,picked:1,offered:2,pick_rate:.5,win_rate:0,hold_rate:1,recorded_offers:3,recorded_picked:1,unknown_picked:1},
];
context.sample = {cards,seats:1,runs:1,wins:1,excluded:0,missing_picked:1,builds:[],latest_received:null};
run(`state.languageStats={language_total_runs:4,language_distribution:[{language:'zhs',runs:2,share:.5},{language:'eng',runs:1,share:.25},{language:'unknown',runs:1,share:.25}]};renderLanguages()`);
assert.ok(node('languageStats').innerHTML.includes('简体中文'));
assert.ok(node('languageStats').innerHTML.includes('4 局'));
assert.ok(node('languageStats').innerHTML.includes('50.0%'));
assert.ok(node('languageStats').innerHTML.includes('未知'));
run(`state.languageStats={language_total_runs:1,language_distribution:[{language:'<img src=x>',runs:1,share:1}]};renderLanguages()`);
assert.ok(!node('languageStats').innerHTML.includes('<img'));
assert.ok(node('languageStats').innerHTML.includes('&lt;img'));
run('state.languageStats={};renderLanguages()');
assert.match(node('languageStats').innerHTML,/没有匹配的语言统计/);
console.log('PASS language distribution labels, counts, percentages, escaping and empty state');
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
  assert.ok(!context.requests.some(path=>path.startsWith('api/runs?')),'cards view must not load runs');
  run("state.view='runs'");await run('load()');
  assert.ok(context.requests.find(path=>path.startsWith('api/runs?')).includes('character=SILENT'));
  run("state.view='cards'");await run('load()');
  assert.ok(context.requests.find(path=>path.startsWith('api/stats?')).includes('character=SILENT'));
  run('csv()');
  assert.ok((await context.saved.text()).includes('"character","SILENT"'));
  run('syncURL()');
  assert.ok(context.savedHash.includes('character=SILENT'));
  run("updateCharacters([],'all')");
  assert.ok(node('character').innerHTML.includes('<option value="JUNNA">星见纯那</option>'));
  assert.equal(run("characterName('CHARACTER.JUNNA')"),'星见纯那');
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

  const modeRuns = ['dread', 'blitz'].map(mode => ({
    id: mode, mode, received_at: 0, start_time: 0, outcome: 'death',
    floor: 2, duration: 10, ascension: 0, player_count: 1, build: 'v0.113.1',
    players: [{id: 1, character: 'IRONCLAD', deck: [], relics: []}]
  }));
  context.modeRuns = modeRuns;
  run('state.runs={items:modeRuns,page:1,total:2,page_size:20};renderRuns();csv()');
  const runCsv = await context.saved.text();
  assert.ok(runCsv.includes('"游戏模式"'));
  for (const [record, label] of [[modeRuns[0], 'Dread'], [modeRuns[1], 'Blitz']]) {
    assert.ok(node('runs').innerHTML.includes(label));
    assert.ok(runCsv.includes('"' + label + '"'));
    context.modeRun = record;
    run('state.run=modeRun.id;renderRunDetail();state.history={summary:modeRun,history:{players:modeRun.players}};renderHistory()');
    assert.ok(node('runDetail').innerHTML.includes(label));
    assert.ok(node('historyBody').innerHTML.includes(label));
  }
  console.log('PASS beta modes appear in run lists, details and CSV');


  context.pending=[];
  run("params=()=>new URLSearchParams({build:'slow'});api=(path,signal)=>new Promise(resolve=>pending.push({path,signal,resolve}));state.view='cards'");
  const slow=run('load()');run("params=()=>new URLSearchParams({build:'fast'})");const fast=run('load()');
  assert.equal(context.pending[0].signal.aborted,true,'new filters cancel the old request');
  context.pending[1].resolve(context.sample);await fast;
  context.pending[0].resolve({...context.sample,seats:999});await slow;
  assert.equal(run('state.stats.seats'),1,'late results cannot overwrite current filters');
  run("params=()=>new URLSearchParams({ascension:'0'});api=async()=>sample");
  console.log('PASS superseded requests are cancelled and late results ignored');

  context.largeSample={...context.sample,cards:Array.from({length:185},(_,i)=>({...cards[0],id:'CARD_'+i,name:'卡牌 '+i}))};
  node('owner').value='all';node('character').value='all';node('group').value='none';
  run("state.view='cards';state.display='table';state.stats=largeSample;state.cardLimit=60;renderCards()");
  assert.equal((node('cards').innerHTML.match(/<tr data-card=/g)||[]).length,60);
  assert.ok(node('cards').innerHTML.includes('data-more'));
  run('state.cardLimit=120;renderCards();csv()');
  assert.equal((node('cards').innerHTML.match(/<tr data-card=/g)||[]).length,120);
  assert.ok((await context.saved.text()).includes('CARD_184'),'CSV contains all filtered rows');
  console.log('PASS progressive rows preserve full CSV export');

  context.legacy={summary:{id:'fixture',start_time:0,outcome:'win',ascension:0,floor:1,duration:10,build:'fixture'},
    history:{players:[{id:1,character:'KAREN',deck:[],relics:[null]}],map_point_history:[[{map_point_type:'event',rooms:[null],player_stats:[{player_id:1,rest_site_choices:'rest'}]}]]}};
  run('state.history=legacy;renderHistory()');
  console.log('PASS legacy display fields degrade without crashing');

  context.roleResponse={rows:[
    {character:'KAREN',runs:4,seats:5,wins:2,deaths:3,abandons:0,avg_floor:12.2,win_rate:.4},
    {character:'JUNNA',runs:1,seats:1,wins:1,deaths:0,abandons:0,avg_floor:43,win_rate:1},
  ],filters:{characters:['KAREN','JUNNA'],languages:['zhs']}};
  run(`state.view='characters';api=async path=>{requests.push(path);return roleResponse;}`);
  await run('load()');
  const roles=node('characterStats');
  assert.ok(context.requests.some(path=>path.startsWith('api/character-stats?')&&!path.includes('outcome=')&&!path.includes('page=')));
  assert.equal(node('charactersView').hidden,false);
  assert.ok(roles.innerHTML.indexOf('爱城华恋')<roles.innerHTML.indexOf('星见纯那'));
  assert.ok(roles.innerHTML.includes('40.0%')&&roles.innerHTML.includes('12.2'));
  run("state.characterSort='win_rate';renderCharacterStats();syncURL();csv()");
  assert.ok(roles.innerHTML.indexOf('星见纯那')<roles.innerHTML.indexOf('爱城华恋'));
  assert.ok(context.savedHash.includes('characterSort=win_rate'));
  const roleCsv=await context.saved.text();
  assert.ok(roleCsv.indexOf('星见纯那')<roleCsv.indexOf('爱城华恋'));
  run('state.characterStats={rows:[]};renderCharacterStats()');
  assert.match(roles.innerHTML,/没有匹配的角色统计/);
  assert.equal(node('characterSort').disabled,true);
  console.log('PASS role view, numerical sorting, CSV and empty state');
  context.languageResponse={language_total_runs:2,language_distribution:[{language:'zhs',runs:1,share:.5},{language:'unknown',runs:1,share:.5}],filters:{languages:['unknown','zhs']}};
  run(`state.view='languages';api=async path=>{requests.push(path);return languageResponse;}`);
  await run('load()');
  assert.ok(context.requests.some(path=>path.startsWith('api/language-stats?')));
  assert.equal(node('languagesView').hidden,false);
  assert.equal(node('cardViews').hidden,true);
  run('csv()');
  const languageCsv=await context.saved.text();
  assert.ok(languageCsv.includes('"zhs","简体中文","1","50.0%"'));
  console.log('PASS dedicated language view and CSV');

  context.pending=[];
  run("api=(path,signal)=>new Promise(resolve=>pending.push({path,signal,resolve}));");
  const first=new AbortController(),second=new AbortController();
  context.firstSignal=first.signal;
  context.secondSignal=second.signal;
  const left=run("statisticsData('same-query',firstSignal,false)");
  const right=run("statisticsData('same-query',secondSignal,false)");
  assert.equal(context.pending.length,1,'comparison panes with the same query share one fetch');
  first.abort();
  assert.equal(context.pending[0].signal.aborted,false,'an active pane keeps the shared fetch alive');
  context.pending[0].resolve(context.sample);
  await Promise.all([left,right]);
  assert.equal(await run("statisticsData('same-query',secondSignal,false)"),context.sample);
  assert.equal(context.pending.length,1,'reselecting recent filters is served from cache');
  const refreshed=run("statisticsData('same-query',secondSignal,true)");
  assert.equal(context.pending.length,2,'explicit refresh bypasses cache');
  context.pending[1].resolve(context.sample);
  await refreshed;

  const timers=new Map();
  let timerId=0;
  context.setTimeout=(callback,delay)=>{if(delay===180){timers.set(++timerId,callback);return timerId;}return 0;};
  context.clearTimeout=id=>{timers.delete(id);};
  run("params=()=>new URLSearchParams({build:'debounce-before'});scheduleLoad()");
  run("params=()=>new URLSearchParams({build:'debounce-after'});scheduleLoad()");
  assert.equal(node('export').disabled,true,'pending filters disable export immediately');
  assert.equal(context.pending.length,2);
  assert.equal(timers.size,1,'consecutive changes reschedule the same pending load');
  const filteredLoad=[...timers.values()][0]();
  assert.equal(context.pending.length,3);
  assert.ok(context.pending[2].path.includes('build=debounce-after'));
  context.pending[2].resolve(context.languageResponse);
  await filteredLoad;
  console.log('PASS shared in-flight requests, recent cache, explicit refresh and coalesced filters');

  run("state.view='cards'");

})().catch(error=>{console.error(error);process.exitCode=1});
