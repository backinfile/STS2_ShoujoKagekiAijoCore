'use strict';
const $ = id => document.getElementById(id);
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const pct = value => value == null ? '—' : (value * 100).toFixed(1) + '%';
const num = value => new Intl.NumberFormat('zh-CN').format(value ?? 0);
const date = value => value ? new Date(value * 1000).toLocaleString('zh-CN', {hour12:false}) : '—';
const OUTCOME = {win:'胜利',death:'死亡',abandon:'放弃'};
const CHARACTERS={KAREN:'爱城华恋',IRONCLAD:'铁甲战士',SILENT:'静默猎手',DEFECT:'故障机器人',REGENT:'储君',NECROBINDER:'亡灵契约师',WATCHER:'观者'};
const RARITIES=['基础','普通','罕见','稀有','先古','衍生','诅咒','状态','特殊','事件','任务','未知'];
const OWNERS=['karen','ironclad','silent','defect','regent','necrobinder','colorless','curse','status','event','token','quest','unknown'];
const STARTER_CURSES=new Set(['ASCENDERS_BANE','KAREN_SLEEPY']);
const CARD_SORTS={name:'卡牌名称',cost:'基础费用',type:'卡牌类型',pick_rate:'记录选中率',win_rate:'曾持有胜率',hold_rate:'持有覆盖率',recorded_offers:'选牌记录',picked:'记录选中次数',offered:'已知结果记录数',held:'持有席位',wins:'胜利席位',unknown_picked:'结果缺失数'};
const characterName=value=>CHARACTERS[model(value)]??model(value);
const SERIES = [{key:'pick_rate',label:'记录选中率',color:'#375d4c'},{key:'win_rate',label:'曾持有胜率',color:'#71859b'},{key:'hold_rate',label:'持有覆盖率',color:'#ae884b'}];
const state = {view:'cards',display:'table',sort:'held',order:-1,card:null,run:null,page:1,stats:null,runs:null,catalog:{},relics:{},history:null,seat:0,floor:1,loading:false,fresh:false,loadedQuery:null,catalogReady:false,historyId:null};
const primary={state,$,root:document};
let historyVersion=0, toastTimer;
const comparisons={};
const comparisonURL=new URLSearchParams(location.hash.slice(1));
const mobile = () => matchMedia('(max-width:800px)').matches;
const listOf = value => Array.isArray(value) ? value : [];
function toast(text){$('toast').textContent=text;$('toast').classList.add('visible');clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').classList.remove('visible'),2600);}
async function api(path,signal){
 const response=await fetch(path,{cache:path.startsWith('api/')?'no-store':'default',credentials:'same-origin',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});
 if(!response.ok)throw Error(response.status===401?'登录已失效，请刷新页面重新登录':'请求失败（'+response.status+'）');return response.json();
}
function params(scope=primary){return new URLSearchParams(new FormData(scope.$('filters')));}
function model(value){
  const full=typeof value==='object'?value?.model_full_id:null;
  const candidate=full??(typeof value==='object'?value?.id??value:value);
  const raw=typeof candidate==='string'?candidate:candidate?.Entry??candidate?.entry??'未知';
  if(full&&!(raw.startsWith('CARD.')&&raw.split('.').length===2))return raw;
  return (state.catalog[raw]||state.relics[raw]||raw.split('.').length>2)?raw:raw.split('.').at(-1).toUpperCase();
}
function meta(value){const id=model(value);return {id,...state.catalog[id],name:state.catalog[id]?.name??id};}
function tag(outcome){return '<span class="tag '+outcome+'">'+OUTCOME[outcome]+'</span>';}
function image(card,cls=''){return card.image?'<img class="'+cls+'" src="'+esc(card.image)+'" alt="" width="420" height="300" loading="lazy">':'<span class="card-placeholder '+cls+'" aria-hidden="true">?</span>';}
function title(card){return esc(card.name)+(card.current_upgrade_level>0?' +'+card.current_upgrade_level:'');}
function identity(card){return '<span class="card-ident">'+image(card)+'<span><strong>'+title(card)+'</strong><small>'+esc([card.owner_name,card.rarity,card.type,card.cost==null?'费用未知':card.cost<0?'特殊费用':card.cost+' 费'].filter(Boolean).join(' · '))+'</small></span></span>';}
function metric(label,value,fraction){return '<div class="metric-row"><span>'+label+'</span><div><strong>'+value+'</strong>'+(fraction?'<small>'+fraction+'</small>':'')+'</div></div>';}
function syncURL(scope=primary){const {state,$}=scope,p=params(scope);for(const key of ['search','owner','type','rarity','starters','group','character','outcome'])if($(key))p.set(key,$(key).value);for(const key of ['view','display','sort','order','page','card','run'])if(state[key]!=null)p.set(key,state[key]);if(state.historyId){p.set('history',state.historyId);p.set('seat',state.seat);p.set('floor',state.floor);}if(scope.onChange){scope.onChange(p.toString());return;}for(const side of ['left','right']){const saved=comparisons[side]?.saved??comparisonURL.get(side);if(saved)p.set(side,saved);}p.set('compareSide',$('comparison').dataset.side??'left');history.replaceState(null,'','#'+p);}
function updateCharacters(characters=[],selected=$('character').value,scope=primary){
 const {$}=scope;
 const choices=[...new Set([...Object.keys(CHARACTERS),...characters,selected])].filter(c=>c&&c!=='all');
 $('character').innerHTML='<option value="all">全部角色</option>'+choices.map(c=>'<option value="'+esc(c)+'">'+esc(characterName(c))+'</option>').join('');$('character').value=selected;
}
function exportState(scope=primary){const {state,$}=scope;$('export').disabled=state.loading||!state.fresh;$('export').textContent=state.view==='cards'?'导出筛选卡牌 CSV ↓':'导出当前页 CSV ↓';}
async function load(scope=primary,{force=true}={}){
 const {state,$}=scope;
 if(!scope.cardsOnly&&state.view==='compare'){render();return;}
 const cards=scope.cardsOnly||state.view==='cards',query=params(scope);query.set('character',$('character').value);
 if(!cards){query.set('page',state.page);query.set('outcome',$('outcome').value);}
 const key=(cards?'api/stats?':'api/runs?')+query;
 if(state.loading&&scope.pendingKey===key)return;
 scope.controller?.abort();scope.controller=new AbortController();scope.pendingKey=key;
 const version=scope.requestVersion=(scope.requestVersion??0)+1;
 scope.cache??=new Map();if(force)scope.cache.clear();
 state.loading=true;state.fresh=false;state.cardLimit=60;exportState(scope);$('connection').textContent='更新中…';$('connection').className='connection';$('refresh').disabled=true;
 render(scope);if(scope.cardsOnly)comparisonStatus();
 try{
  const cached=scope.cache.get(key),data=!force&&cached&&Date.now()-cached.time<30000?cached.data:await api(key,scope.controller.signal);
  if(version!==scope.requestVersion)return;
  scope.cache.set(key,{time:Date.now(),data});if(scope.cache.size>6)scope.cache.delete(scope.cache.keys().next().value);
  if(cards)state.stats=data;else{state.runs=data;state.page=data.page;query.set('page',state.page);}
  state.loadedQuery=new URLSearchParams(query);state.fresh=true;
  const filters=cards?data:data.filters??{};
  updateCharacters(filters.characters??[],$('character').value,scope);
  for(const [id,values] of [['build',filters.builds??[]],['coreVersion',filters.core_versions??[]],['sourceMod',filters.source_mods??[]]]){
   const control=$(id);if(control?.options){const selected=control.value;control.innerHTML='<option value="all">全部</option>'+[...new Set([...values,selected].filter(value=>value&&value!=='all'))].map(value=>'<option value="'+esc(value)+'">'+esc(value)+'</option>').join('');control.value=selected||'all';}
  }
  if(cards){const owner=$('owner');for(const card of data.cards)if(card.owner&&owner?.options&&!Array.from(owner.options).some(option=>option.value===card.owner))owner.add(new Option(card.owner_name??card.owner,card.owner));}
  $('error').hidden=true;$('connection').textContent='已更新';$('connection').className='connection ok';$('updated').textContent='更新于 '+new Date().toLocaleTimeString('zh-CN',{hour12:false});
  render(scope);syncURL(scope);
 }catch(error){if(version!==scope.requestVersion||error.name==='AbortError')return;$('connection').textContent='更新失败';$('error').hidden=false;$('error').textContent=(error.name==='TimeoutError'?'连接超时，请重试':error.message)+'。当前保留上次结果，刷新成功后可导出。';}
 finally{if(version===scope.requestVersion){state.loading=false;scope.pendingKey=null;$('refresh').disabled=false;exportState(scope);if(scope.cardsOnly)comparisonStatus();}}
}
function filteredCards(scope=primary){
 const {state,$}=scope;
 const q=$('search').value.trim().toLowerCase();
 return (state.stats?.cards??[]).filter(c=>(!q||(c.name+' '+c.id).toLowerCase().includes(q))&&($('owner').value==='all'||(c.owner??'unknown')===$('owner').value)&&($('type').value==='all'||c.type===$('type').value)&&($('rarity').value==='all'||c.rarity===$('rarity').value)&&($('starters').value!=='hide'||(c.rarity!=='基础'&&!STARTER_CURSES.has(c.id)))).sort((a,b)=>{
  if($('group').value==='rarity'){const rarity=c=>c.rarity??'未知',rank=c=>{const index=RARITIES.indexOf(rarity(c));return index<0?RARITIES.length:index;},diff=rank(a)-rank(b)||rarity(a).localeCompare(rarity(b),'zh-CN');if(diff)return diff;}
  if($('group').value==='owner'){const owner=c=>c.owner??'unknown',rank=c=>{const index=OWNERS.indexOf(owner(c));return index<0?OWNERS.length:index;},diff=rank(a)-rank(b)||owner(a).localeCompare(owner(b),'zh-CN');if(diff)return diff;}
  const av=a[state.sort],bv=b[state.sort];if(av==null&&bv!=null)return 1;if(av!=null&&bv==null)return -1;
  return ((typeof av==='number'&&typeof bv==='number'?av-bv:String(av??'').localeCompare(String(bv??''),'zh-CN'))*state.order)||a.id.localeCompare(b.id);
 });
}
function cardGroups(rows,scope=primary){
 const {$}=scope;
 const groups=[];
 for(const card of rows){const kind=$('group').value,key=kind==='rarity'?(card.rarity??'未知'):kind==='owner'?(card.owner??'unknown'):null,name=kind==='owner'?(card.owner_name??'其他'):key;let group=groups.at(-1);if(!group||group.key!==key){group={key,name,cards:[]};groups.push(group);}group.cards.push(card);}
 return groups;
}
function render(scope=primary){
 const {state,$}=scope;
 const s=state.stats;if(s){
 const cohort=s.character&&s.character!=='all'?characterName(s.character):'全部角色';
 $('summary').innerHTML=[['已收对局',num(s.runs)],['统计席位',num(s.seats)],['胜利 / 未胜',num(s.wins)+' / '+num(s.seats-s.wins)],['有记录卡牌',num(s.cards.length)]].map(([label,value])=>'<div><span>'+label+'</span><strong>'+value+'</strong></div>').join('')+'<div class="latest"><span>最近接收</span><strong>'+date(s.latest_received)+'</strong></div>';
 $('cohort').innerHTML='<span>'+esc(cohort)+' · '+num(s.seats)+' 个席位 · 排除 '+num(s.excluded)+' 个早期放弃席位'+(s.seats<20?' · 当前样本较少，请结合分母阅读':'')+'</span><span>'+(s.card_limit_reached?'已达到 4,096 种卡牌显示上限 · ':'')+'选牌结果缺失 '+num(s.missing_picked)+' 项</span>';
 }
 if(scope.cardsOnly){renderCards(scope);exportState(scope);return;}
 document.querySelectorAll('[data-view]').forEach(b=>b.setAttribute('aria-pressed',b.dataset.view===state.view));
 const compare=state.view==='compare';$('cardsView').hidden=state.view!=='cards';$('runsView').hidden=state.view!=='runs';$('compareView').hidden=!compare;$('filters').hidden=compare;const toggle=$('filters').previousElementSibling;if(toggle?.classList.contains('filter-toggle'))toggle.hidden=compare;$('summary').hidden=state.view!=='cards';$('error').hidden=compare||state.fresh;$('export').hidden=compare;$('pageTitle').textContent=compare?'对比模式':state.view==='cards'?'卡牌统计':'对局记录';
 if(compare)openComparison();
 if(state.view==='cards'){if(s)renderCards();else $('cards').innerHTML=empty('加载中','');}
 if(state.view==='runs'){if(state.runs)renderRuns();else $('runs').innerHTML=empty('加载中','');}
 exportState();
}
function empty(text='暂无收到的对局',sub='新对局上传后会出现在这里'){return '<div class="empty"><strong>'+text+'</strong><p>'+sub+'</p></div>';}
function renderCards(scope=primary){
 const {state,$}=scope;
 if(!state.stats)return;
 const focused=scope.root===document?document.activeElement:scope.root.contains(document.activeElement)?document.activeElement:null,focusSort=focused?.dataset.sort,focusCard=focused?.dataset.card;
 const allRows=filteredCards(scope),rows=allRows.slice(0,state.cardLimit??60),groups=cardGroups(rows,scope);$('sort').value=state.sort;$('sortOrder').textContent=state.order<0?'↓ 降序':'↑ 升序';$('sortLabel').textContent=$('group').value!=='none'?'组内排序':'排序';if(!rows.some(c=>c.id===state.card))state.card=rows[0]?.id??null;
 $('cardsView').querySelectorAll('[data-display]').forEach(b=>b.setAttribute('aria-pressed',b.dataset.display===state.display));
 if(!rows.length){$('cards').innerHTML=empty(state.stats.cards.length?'没有匹配的卡牌':'暂无卡牌数据',state.stats.cards.length?'调整搜索或卡牌筛选':'');$('cardDetail').innerHTML=empty('选择卡牌查看数据','');return;}
 const head=(key,label)=>'<th aria-sort="'+(state.sort===key?(state.order<0?'descending':'ascending'):'none')+'"><button data-sort="'+key+'">'+label+(state.sort===key?(state.order<0?' ↓':' ↑'):'')+'</button></th>';
 const rate=(c,key,numerator,denominator)=>'<td><span class="num">'+pct(c[key])+'</span><span class="fraction">'+numerator+' / '+denominator+'</span></td>';
 if(state.display==='table')$('cards').innerHTML='<table><caption hidden>卡牌统计</caption><thead><tr>'+head('name','卡牌')+head('pick_rate','记录选中率')+head('win_rate','曾持有胜率')+head('hold_rate','持有覆盖率')+head('recorded_offers','选牌记录')+head('held','持有席位')+'</tr></thead>'+groups.map(g=>'<tbody>'+(g.name?'<tr class="rarity-group"><th colspan="6" scope="rowgroup">'+esc(g.name)+'<span>'+g.cards.length+' 张</span></th></tr>':'')+g.cards.map(c=>'<tr data-card="'+esc(c.id)+'" class="'+(c.id===state.card?'selected':'')+'"><td><button class="card-name" data-card="'+esc(c.id)+'" aria-pressed="'+(c.id===state.card)+'">'+identity(c)+'</button></td>'+rate(c,'pick_rate',c.picked,c.offered)+rate(c,'win_rate',c.wins,c.held)+rate(c,'hold_rate',c.held,state.stats.seats)+'<td>'+num(c.recorded_offers)+'</td><td>'+num(c.held)+'</td></tr>').join('')+'</tbody>').join('')+'</table>';
 else $('cards').innerHTML='<div class="legend">'+SERIES.map(m=>'<span><i aria-hidden="true" style="--series:'+m.color+'"></i>'+m.label+'</span>').join('')+'</div><div class="chart-axis"><span>卡牌</span><span class="ticks"><span>0</span><span>25</span><span>50</span><span>75</span><span>100%</span></span></div>'+groups.map(g=>'<section class="chart-group"'+(g.name?' aria-label="'+esc(g.name)+'"':'')+'>'+(g.name?'<h3>'+esc(g.name)+'<span>'+g.cards.length+' 张</span></h3>':'')+g.cards.map(c=>'<button class="chart-row" data-card="'+esc(c.id)+'" aria-pressed="'+(c.id===state.card)+'">'+identity(c)+'<span class="bars">'+SERIES.map(m=>'<span class="track '+(c[m.key]==null?'no-data':'')+'" title="'+m.label+' '+(c[m.key]==null?'暂无有效样本':pct(c[m.key]))+'"><span class="fill" style="--value:'+(c[m.key]==null?0:c[m.key]*100)+'%;--series:'+m.color+';display:block"></span></span>').join('')+'</span><span class="chart-values">'+SERIES.map(m=>'<span>'+pct(c[m.key])+'<small>n='+num(m.key==='pick_rate'?c.offered:m.key==='win_rate'?c.held:state.stats.seats)+'</small></span>').join('')+'</span></button>').join('')+'</section>').join('');
 if(rows.length<allRows.length)$('cards').innerHTML+='<div class="card-more"><span>'+rows.length+' / '+allRows.length+' 张</span><button data-more>显示更多</button></div>';
 renderCardDetail(rows.find(c=>c.id===state.card),scope);
 if(focusSort||focusCard){const target=[...$('cards').querySelectorAll('button')].find(b=>(focusSort&&b.dataset.sort===focusSort)||(focusCard&&b.dataset.card===focusCard));target?.focus({preventScroll:true});}
}
function renderCardDetail(c,scope=primary){
 const {state,$}=scope;
 if(!c)return;
 const description='<span class="model-id">'+esc(c.id)+' · '+esc(c.owner_name??'其他')+'</span><div class="metrics">'+metric('记录选中率',pct(c.pick_rate),c.offered?c.picked+' / '+c.offered+' 项已知结果':'暂无已知结果的选牌记录')+metric('曾持有胜率',pct(c.win_rate),c.wins+' / '+c.held+' 席位')+metric('持有覆盖率',pct(c.hold_rate),c.held+' / '+state.stats.seats+' 席位')+metric('选牌记录',num(c.recorded_offers),c.recorded_picked+' 项已选 · '+c.unknown_picked+' 项结果缺失')+'</div>';
 $('cardDetail').innerHTML=image(c,'hero')+'<h2>'+esc(c.name)+'</h2>'+description;
 $('cardSheetTitle').textContent=c.name;$('cardSheetBody').innerHTML=image(c,'hero')+description;
}
function selectCard(id,scope=primary){
 const {state,$}=scope,c=filteredCards(scope).find(c=>c.id===id);if(!c)return;
 state.card=id;
 $('cards').querySelectorAll('[data-card]').forEach(node=>{const selected=node.dataset.card===id;if(node.tagName==='TR')node.classList.toggle('selected',selected);else node.setAttribute('aria-pressed',selected);});
 renderCardDetail(c,scope);syncURL(scope);
 if((scope.cardsOnly||mobile())&&!$('cardSheet').open)$('cardSheet').showModal();
}
function renderRuns(){
 const r=state.runs;if(!r)return;
 if(!r.items.some(run=>run.id===state.run))state.run=r.items[0]?.id??null;
 $('runs').innerHTML=r.items.length?'<table><caption hidden>已接收对局</caption><thead><tr><th>职业 / 人数</th><th>结果</th><th>进度</th><th>版本 / 进阶</th><th>对局 ID / 接收时间</th><th></th></tr></thead><tbody>'+r.items.map(run=>'<tr data-run="'+run.id+'" class="'+(run.id===state.run?'selected':'')+'"><td><button class="run-players" data-run="'+run.id+'">'+run.players.map(p=>esc(characterName(p.character))).join(' / ')+'</button><span class="fraction">'+run.player_count+' 人</span></td><td>'+tag(run.outcome)+'</td><td>'+run.floor+' 层</td><td>'+esc(run.build)+'<span class="fraction">进阶 '+run.ascension+'</span></td><td><button data-run="'+run.id+'">'+run.id.slice(0,12)+'</button><span class="fraction">'+date(run.received_at)+'</span></td><td><button data-history="'+run.id+'" aria-label="查看对局详情 '+run.id.slice(0,12)+'">↗</button></td></tr>').join('')+'</tbody></table>':($('character').value!=='all'||$('outcome').value!=='all'?empty('没有匹配的对局','调整职业、结果或上方筛选'):empty());
 renderRunDetail();
 const pages=Math.max(1,Math.ceil(r.total/r.page_size));
 $('pagination').innerHTML='<span>'+num(r.total)+' 局</span><div><button data-page="'+(state.page-1)+'" '+(state.page<=1?'disabled':'')+'>上一页</button><span>'+state.page+' / '+pages+'</span><button data-page="'+(state.page+1)+'" '+(state.page>=pages?'disabled':'')+'>下一页</button></div>';
}
function renderRunDetail(){
 const run=state.runs?.items.find(r=>r.id===state.run);
 $('runDetail').innerHTML=run?'<span class="tag">已接收</span><h2>'+OUTCOME[run.outcome]+' · 第 '+run.floor+' 层</h2><span class="model-id">'+run.id+'</span><div class="metrics">'+metric('游戏时间',date(run.start_time))+metric('游戏版本',esc(run.build))+metric('进阶 / 人数',run.ascension+' / '+run.player_count)+metric('时长',Math.floor(run.duration/60)+' 分 '+Math.floor(run.duration%60)+' 秒')+metric('选牌结果缺失',num(run.missing_picked)+' 项')+metric('重复上传',num(run.duplicates)+' 次')+'</div><button class="primary" data-history="'+run.id+'">查看对局详情 ↗</button><button class="secondary" data-raw="'+run.id+'">下载原始记录 ↓</button>':empty('选择对局查看数据','');
}
function openModal(title,html){$('modalTitle').textContent=title;$('modalBody').innerHTML=html;if(!$('modal').open)$('modal').showModal();}
function rules(){openModal('统计规则',[
 ['统计样本','按所选角色的玩家席位计数；全部角色汇总所有席位。同席位同模型只计一次。默认排除前 5 层放弃；早期死亡保留，其他放弃计入未胜。'],
 ['曾持有胜率','胜利且曾持有该模型的席位 ÷ 曾持有该模型的席位。依据终局牌组、耗尽、获取、移除、变牌和明确选中记录。仅无修正器的标准模式补入默认华恋起始牌，其他开局以实际记录为准。'],
 ['持有覆盖率','曾持有该模型的席位 ÷ 筛选后所选角色的全部统计席位。卡名、归属、类型、稀有度及初始牌筛选只影响卡牌行。'],
 ['初始牌与分组','隐藏初始牌会筛去基础稀有度的卡牌、进阶之灾及华恋的困意。按归属或稀有度分组时，排序字段与升降序仅调整各组内部；统计表、对比图和卡牌 CSV 使用同一顺序。'],
 ['角色筛选','同时筛选卡牌统计与对局列表。多人对局中任一席位属于所选角色就会列出，同局计一次；卡牌只统计该角色的席位。'],
 ['记录选中率','已选记录 ÷ 结果明确的选牌记录。包含奖励、商店及遗物等来源，不等同于免费奖励选取率；结果缺失不计入分母，无有效分母显示“—”。'],
 ['选牌记录','展示记录总数、明确选中数和结果缺失数。缺失结果保留未知，不根据终局牌组推断。'],
 ['时间与胜负','时间按服务器首次接收时间筛选。结果筛选只作用于对局列表，不改变卡牌胜率。']
 ].map(([title,text])=>'<details><summary>'+title+'</summary><p>'+text+'</p></details>').join(''));}
async function openHistory(id,restored=false){
 const version=++historyVersion;primary.historyController?.abort();primary.historyController=new AbortController();
 $('historyTitle').textContent='对局详情';$('historyBody').textContent='加载中';if(!$('history').open)$('history').showModal();
 try{const [data]=await Promise.all([api('api/runs/'+id,primary.historyController.signal),historyCatalog()]);if(version!==historyVersion)return;state.history=data;state.historyId=id;state.seat=restored?Math.min(state.seat,Math.max(0,data.history.players.length-1)):Math.max(0,data.history.players.findIndex(p=>model(p.character)==='KAREN'));if(!restored)state.floor=1;renderHistory();if(!$('history').open)$('history').showModal();syncURL();}
 catch(error){if(version===historyVersion&&error.name!=='AbortError')$('historyBody').textContent=error.message;}
}
function floors(){let floor=0;return listOf(state.history?.history.map_point_history).flatMap((entries,act)=>listOf(entries).filter(entry=>entry&&typeof entry==='object').map(entry=>({entry,act:act+1,floor:++floor})));}
const ROOM={unassigned:'未知房间',unknown:'未知房间',monster:'普通战斗',elite:'精英战斗',boss:'首领战斗',shop:'商店',treasure:'宝箱',event:'事件',restsite:'休息处',map:'地图',ancient:'先古'};
function roomName(entry){const rooms=listOf(entry.rooms);const value=rooms[0]?.room_type??entry.map_point_type;const names=rooms.length?['unassigned','monster','elite','boss','treasure','shop','event','restsite','map']:['unassigned','unknown','shop','treasure','restsite','monster','elite','boss','ancient'];return typeof value==='number'?names[value]??'unknown':String(value??'unknown').toLowerCase().replaceAll('_','');}
function roomIcon(entry){const room=roomName(entry);return ({restsite:'rest',unknown:'event',map:'event',unassigned:'event'}[room]??room);}
function groupPile(cards){const map=new Map();for(const card of listOf(cards).filter(Boolean)){const id=meta(card).id,level=card.current_upgrade_level??0,key=id+'+'+level;if(!map.has(key))map.set(key,{...meta(id),current_upgrade_level:level,count:0});map.get(key).count++;}return [...map.values()];}
function pileHTML(cards){const groups=groupPile(cards);return groups.length?'<div class="pile">'+groups.map(c=>'<button class="history-card" data-inspect="'+esc(c.id)+'">'+image(c)+'<span>'+title(c)+'<small>'+c.count+' 张</small></span></button>').join('')+'</div>':'<div class="empty">无记录卡牌</div>';}
function renderHistory(){
 const keepSeat=document.activeElement?.id==='historySeat';
 const {summary:r,history:h}=state.history,p=h.players[state.seat],all=floors();
 if(!all.some(f=>f.floor===state.floor))state.floor=all[0]?.floor??1;
 $('historyTitle').innerHTML='<strong>对局历史</strong><small>'+date(r.start_time)+' · '+esc(r.id.slice(0,12))+'</small>';
 const acts=[...new Set(all.map(f=>f.act))];
 $('historyBody').innerHTML='<div class="history-meta"><div><h2>'+esc(characterName(p.character))+' · '+OUTCOME[r.outcome]+'</h2><span>进阶 '+r.ascension+' · 第 '+r.floor+' 层 · '+Math.floor(r.duration/60)+' 分钟 · '+esc(r.build)+'</span></div><span class="spacer"></span><select id="historySeat" aria-label="查看玩家席位">'+h.players.map((p,i)=>'<option value="'+i+'" '+(i===state.seat?'selected':'')+'>'+esc(characterName(p.character))+' · '+esc(String(p.id))+'</option>').join('')+'</select><button class="raw-button" data-raw="'+r.id+'">原始记录 ↓</button></div><div class="history-grid"><div><h3>路线</h3>'+acts.map(act=>'<div class="route-act"><h4>第 '+act+' 幕</h4><div class="route">'+all.filter(f=>f.act===act).map(f=>'<button class="node '+(f.floor===state.floor?'selected':'')+'" data-floor="'+f.floor+'" aria-label="第 '+f.floor+' 层 '+(ROOM[roomName(f.entry)]??'未知房间')+'"><img src="assets/'+esc(roomIcon(f.entry))+'.png" alt=""><span>'+f.floor+'</span></button>').join('')+'</div></div>').join('')+(all.length?'':'<div class="empty">暂无路线记录</div>')+'<h3>遗物 · '+listOf(p.relics).length+'</h3><div class="relic-list">'+listOf(p.relics).filter(Boolean).map(relic=>{const id=model(relic.id??relic);return '<span>'+esc(state.relics[id]?.name??id)+'</span>';}).join('')+'</div><h3>终局牌组 · '+(p.deck?.length??0)+'</h3>'+pileHTML(p.deck)+((p.core_shine_exhaust_cards??p.karen_shine_exhaust_cards)?.length?'<h3>耗尽牌堆 · '+(p.core_shine_exhaust_cards??p.karen_shine_exhaust_cards).length+'</h3>'+pileHTML(p.core_shine_exhaust_cards??p.karen_shine_exhaust_cards):'')+'</div><aside id="floorDetail" class="history-side"></aside></div>';
 renderFloor();if(keepSeat)$('historySeat').focus({preventScroll:true});
}
function renderFloor(){
 const f=floors().find(f=>f.floor===state.floor),p=state.history.history.players[state.seat];
 if(!f){$('floorDetail').innerHTML='<h3>对局信息</h3><p>种子 '+esc(state.history.history.seed)+'</p><p>暂无楼层记录</p>';return;}
 const stat=listOf(f.entry.player_stats).find(s=>s&&String(s.player_id)===String(p.id));
 const list=(label,items)=>listOf(items).length?'<p>'+label+'</p><ul>'+items.filter(Boolean).map(item=>'<li>'+title({...meta(item.id??item),current_upgrade_level:item.current_upgrade_level})+'</li>').join('')+'</ul>':'';
 $('floorDetail').innerHTML='<h3>第 '+f.floor+' 层 · '+(ROOM[roomName(f.entry)]??'未知房间')+'</h3><p>第 '+f.act+' 幕</p>'+listOf(f.entry.rooms).filter(room=>room&&typeof room==='object').map(room=>'<p>'+esc(model(room.model_id??'UNKNOWN'))+(room.turns_taken?' · '+room.turns_taken+' 回合':'')+'</p>').join('')+(stat?metric('生命',esc(stat.current_hp??'—')+' / '+esc(stat.max_hp??'—'))+metric('金币',esc(stat.current_gold??'—'))+metric('承受伤害',esc(stat.damage_taken??'—'))+list('获得卡牌',stat.cards_gained)+list('移除卡牌',stat.cards_removed)+(listOf(stat.card_choices).length?'<p>选牌记录</p><ul>'+listOf(stat.card_choices).filter(c=>c&&c.card).map(c=>'<li>'+title({...meta(c.card.id),current_upgrade_level:c.card.current_upgrade_level})+' · '+(c.was_picked===true?'已选':c.was_picked===false?'未选':'结果缺失')+'</li>').join('')+'</ul>':'')+(listOf(stat.rest_site_choices).length?'<p>'+listOf(stat.rest_site_choices).map(esc).join(' · ')+'</p>':''):'<p>该席位未记录楼层数据</p>');
 document.querySelectorAll('[data-floor]').forEach(b=>b.classList.toggle('selected',Number(b.dataset.floor)===state.floor));
}
async function raw(id){
 try{const response=await fetch('api/runs/'+id+'/raw',{credentials:'same-origin'});if(!response.ok)throw Error('下载失败');save('core-'+id+'.json',await response.blob());}
 catch(error){toast(error.message);}
}
function save(name,blob){const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
function csv(scope=primary){
 const {state,$}=scope;
 if(!state.stats||state.loading||!state.fresh||!state.loadedQuery){toast('请先刷新并成功加载当前筛选结果');return;}
 const rows=state.view==='cards'?[['模型ID','卡名','归属','类型','稀有度','基础费用','记录选中次数','已知结果记录数','记录选中率','曾持有胜利席位','曾持有席位','曾持有胜率','统计席位','持有覆盖率','选牌条目','明确选中','选牌结果缺失'],...filteredCards(scope).map(c=>[c.id,c.name,c.owner_name??'其他',c.type,c.rarity,c.cost,c.picked,c.offered,pct(c.pick_rate),c.wins,c.held,pct(c.win_rate),state.stats.seats,pct(c.hold_rate),c.recorded_offers,c.recorded_picked,c.unknown_picked])]:[['接收时间','开始时间','结果','楼层','进阶','人数','版本','对局ID'],...(state.runs?.items??[]).map(r=>[date(r.received_at),date(r.start_time),OUTCOME[r.outcome],r.floor,r.ascension,r.player_count,r.build,r.id])];
 const cell=v=>{const text=String(v??'');return '"'+(/^[\u0000-\u0020]*[=+@\-]|^[\t\r\n]/.test(text)?"'"+text:text).replaceAll('"','""')+'"';};
 const filters=[...state.loadedQuery].filter(([key])=>state.view==='runs'||!['page','outcome'].includes(key));
 if(state.view==='cards')for(const key of ['search','owner','type','rarity','starters','group'])filters.push([key,$(key).value]);
 filters.push(['sort',state.sort],['order',state.order],['pick_policy','native_known_choices_all_sources']);
 save('core-'+state.view+'.csv',new Blob(['\ufeff'+[...filters,[],...rows].map(r=>r.map(cell).join(',')).join('\r\n')],{type:'text/csv;charset=utf-8'}));toast(state.view==='runs'?'已导出当前页对局':'已导出筛选后的卡牌');
}
function comparisonSides(){return matchMedia('(max-width:900px)').matches?[$('comparison').dataset.side??'left']:['left','right'];}
function comparisonStatus(){
 if(state.view!=='compare')return;
 const scopes=comparisonSides().map(side=>comparisons[side]).filter(Boolean),loading=scopes.some(scope=>scope.state.loading),fresh=scopes.length&&scopes.every(scope=>scope.state.fresh);
 $('refresh').disabled=loading;$('connection').textContent=loading?'更新中…':fresh?'已更新':'等待更新';$('connection').className='connection'+(fresh&&!loading?' ok':'');
}
function openComparison(){
 const sides=comparisonSides();for(const [side,scope] of Object.entries(comparisons))if(!sides.includes(side))scope.controller?.abort();
 for(const side of sides){
  if(comparisons[side]){load(comparisons[side],{force:false});continue;}
  const seed=params();for(const key of ['search','owner','type','rarity','starters','group'])seed.set(key,$(key).value);
  for(const key of ['display','sort','order','card'])if(state[key]!=null)seed.set(key,state[key]);
  const saved=comparisonURL.get(side),initial=new URLSearchParams(saved&&saved.length<=8000?saved:seed);
  const root=document.createElement('section');root.className='comparison-pane';root.setAttribute('aria-label',side==='left'?'左侧卡牌统计':'右侧卡牌统计');
  root.innerHTML='<header class="comparison-header"><h2>'+(side==='left'?'左侧':'右侧')+'</h2><span id="connection" class="connection" role="status">连接中</span><button id="refresh">刷新</button><button id="export" disabled>导出筛选卡牌 CSV ↓</button></header>';
  for(const id of ['filters','summary','error','cardsView','cardSheet']){const clone=$(id).cloneNode(true);clone.hidden=id==='error';clone.removeAttribute('open');root.append(clone);}
  const updated=document.createElement('div');updated.id='updated';updated.className='comparison-updated';root.append(updated);
  const controls=new Map();root.querySelectorAll('[id]').forEach(node=>{const id=node.id;controls.set(id,node);node.dataset.control=id;node.id='compare-'+side+'-'+id;});
  root.querySelectorAll('[aria-labelledby]').forEach(node=>node.setAttribute('aria-labelledby',node.getAttribute('aria-labelledby').split(' ').map(id=>controls.get(id)?.id??id).join(' ')));
  const scope={root,$:id=>controls.get(id),cardsOnly:true,state:{...state,view:'cards',stats:null,runs:null,loading:false,fresh:false,loadedQuery:null,historyId:null,page:1}};
  scope.onChange=value=>{scope.saved=value;syncURL();};comparisons[side]=scope;
  for(const [key,value] of initial){const control=controls.get(key)??scope.$('filters').elements.namedItem(key);if(!control)continue;if(control.tagName==='SELECT'){if((['build','core_version','source_mod'].includes(key)&&value.length<=(key==='source_mod'?180:120))||(key==='character'&&/^[A-Z0-9_:-]{1,180}$/.test(value))){if(![...control.options].some(o=>o.value===value))control.add(new Option(key==='character'?characterName(value):value,value));}if([...control.options].some(o=>o.value===value))control.value=value;}else if(key==='search')control.value=value.slice(0,180);}
  if(['table','chart'].includes(initial.get('display')))scope.state.display=initial.get('display');
  if(Object.hasOwn(CARD_SORTS,initial.get('sort')))scope.state.sort=initial.get('sort');
  scope.state.order=initial.get('order')==='1'?1:-1;scope.state.card=initial.get('card');
  scope.$('summary').innerHTML='';scope.$('cards').innerHTML=empty('加载中','');scope.$('cardDetail').innerHTML='';
  root.addEventListener('click',event=>{
   event.stopPropagation();const b=event.target.closest('button'),control=b?.dataset.control;
   if(b?.dataset.close){scope.$(b.dataset.close).close();return;}
   if(control==='refresh'){load(scope);return;}if(control==='export'){csv(scope);return;}if(control==='rules'){rules();return;}
   if(b?.hasAttribute('data-more')){scope.state.cardLimit=(scope.state.cardLimit??60)+60;renderCards(scope);return;}
   if(b?.dataset.display){scope.state.display=b.dataset.display;renderCards(scope);syncURL(scope);return;}
   if(control==='sortOrder'){scope.state.order=-scope.state.order;renderCards(scope);syncURL(scope);return;}
   if(b?.dataset.sort){scope.state.order=scope.state.sort===b.dataset.sort?-scope.state.order:-1;scope.state.sort=b.dataset.sort;renderCards(scope);syncURL(scope);return;}
   const card=event.target.closest('[data-card]');if(card)selectCard(card.dataset.card,scope);
  });
  const changed=event=>{event.stopPropagation();const id=event.target.dataset.control;if(scope.$('filters').contains(event.target)){syncURL(scope);load(scope);}else if(['search','owner','type','rarity','starters','group','sort'].includes(id)){if(id==='sort')scope.state.sort=event.target.value;renderCards(scope);syncURL(scope);}};
  root.addEventListener('change',changed);scope.$('search').addEventListener('input',changed);scope.$('filters').addEventListener('submit',event=>event.preventDefault());
  $('comparison').append(root);window.enhanceFilters?.(scope.$('filters'));syncURL(scope);load(scope,{force:false});
 }
}
async function refreshComparison(){
 $('refresh').disabled=true;await Promise.all(comparisonSides().map(side=>load(comparisons[side])));$('refresh').disabled=false;
 const fresh=comparisonSides().every(side=>comparisons[side]?.state.fresh);$('connection').textContent=fresh?'已更新':'更新失败';$('connection').className='connection'+(fresh?' ok':'');
}
document.addEventListener('click',event=>{
 if(event.target.closest('.comparison-pane'))return;
 const b=event.target.closest('button');
 if(b?.dataset.close){$(b.dataset.close).close();return;}
 if(b?.dataset.view){primary.controller?.abort();for(const scope of Object.values(comparisons))scope.controller?.abort();state.view=b.dataset.view;render();syncURL();if(state.view!=='compare')load(primary,{force:false});return;}
 if(b?.dataset.compareSide){$('comparison').dataset.side=b.dataset.compareSide;document.querySelectorAll('[data-compare-side]').forEach(button=>button.setAttribute('aria-pressed',button===b));openComparison();syncURL();return;}
 if(b?.dataset.display){state.display=b.dataset.display;renderCards();syncURL();return;}
 if(b?.id==='sortOrder'){state.order=-state.order;renderCards();syncURL();return;}
 if(b?.dataset.sort){state.order=state.sort===b.dataset.sort?-state.order:-1;state.sort=b.dataset.sort;renderCards();syncURL();return;}
 if(b?.dataset.history){openHistory(b.dataset.history);return;}
 if(b?.dataset.raw){raw(b.dataset.raw);return;}
 if(b?.hasAttribute('data-more')){state.cardLimit=(state.cardLimit??60)+60;renderCards();return;}
 if(b?.dataset.page){state.page=Number(b.dataset.page);load();return;}
 if(b?.dataset.floor){state.floor=Number(b.dataset.floor);renderFloor();syncURL();if(mobile())$('floorDetail').scrollIntoView({block:'nearest'});return;}
 if(b?.dataset.inspect){const c=meta(b.dataset.inspect);$('floorDetail').innerHTML=image(c,'hero')+'<h3>'+esc(c.name)+'</h3><p>'+esc(c.id)+'</p><p>'+esc([c.type,c.rarity].filter(Boolean).join(' · '))+'</p><button id="backFloor">返回楼层</button>';if(mobile())$('floorDetail').scrollIntoView({block:'nearest'});return;}
 if(b?.id==='backFloor'){renderFloor();return;}
 const card=event.target.closest('[data-card]');if(card){selectCard(card.dataset.card);return;}
 const run=event.target.closest('[data-run]');if(run){state.run=run.dataset.run;$('runs').querySelectorAll('[data-run]').forEach(row=>row.classList.toggle('selected',row.dataset.run===state.run));renderRunDetail();syncURL();if(mobile())openHistory(state.run);}
});
$('filters').addEventListener('submit',e=>e.preventDefault());
$('filters').addEventListener('change',()=>{state.page=1;syncURL();load();});
$('outcome').addEventListener('change',()=>{state.page=1;syncURL();load();});
document.addEventListener('change',event=>{if(event.target.id==='historySeat'){state.seat=Number(event.target.value);renderHistory();syncURL();}});
let searchTimer;for(const id of ['search','owner','type','rarity','starters','group'])$(id).addEventListener(id==='search'?'input':'change',()=>{clearTimeout(searchTimer);const update=()=>{state.cardLimit=60;renderCards();syncURL();};if(id==='search')searchTimer=setTimeout(update,150);else update();});
$('sort').innerHTML=Object.entries(CARD_SORTS).map(([key,label])=>'<option value="'+key+'">'+label+'</option>').join('');
$('sort').addEventListener('change',()=>{state.sort=$('sort').value;renderCards();syncURL();});
function refreshActive(){return state.view==='compare'?refreshComparison():load();}
$('refresh').addEventListener('click',refreshActive);
window.addEventListener('observatory:refresh',refreshActive);
matchMedia('(max-width:900px)').addEventListener('change',()=>{if(state.view==='compare')openComparison();});$('rules').addEventListener('click',rules);$('export').addEventListener('click',()=>csv());
$('history').addEventListener('close',()=>{historyVersion++;primary.historyController?.abort();state.historyId=null;syncURL();});
for(let i=0;i<=10;i++){const option=document.createElement('option');option.value=i;option.textContent='进阶 '+i;$('filters').elements.ascension.append(option);}
const urlState=comparisonURL;
for(const [key,value] of urlState){const control=$('filters').elements.namedItem(key);if(['build','core_version','source_mod'].includes(key)&&value!=='all'&&value.length<=(key==='source_mod'?180:120)){const option=document.createElement('option');option.value=value;option.textContent=value;control.append(option);}if(control&&[...control.options].some(o=>o.value===value))control.value=value;}
if(['cards','runs','compare'].includes(urlState.get('view')))state.view=urlState.get('view');
if(urlState.get('compareSide')==='right'){$('comparison').dataset.side='right';document.querySelectorAll('[data-compare-side]').forEach(b=>b.setAttribute('aria-pressed',b.dataset.compareSide==='right'));}
if(['table','chart'].includes(urlState.get('display')))state.display=urlState.get('display');
if(/^[A-Z0-9_:-]{1,180}$/.test(urlState.get('character')??''))updateCharacters([],urlState.get('character'));
for(const key of ['owner','type','rarity','starters','group','character','outcome']){const control=$(key),value=urlState.get(key);if([...control.options].some(option=>option.value===value))control.value=value;}
$('search').value=(urlState.get('search')??'').slice(0,180);
if(Object.hasOwn(CARD_SORTS,urlState.get('sort')))state.sort=urlState.get('sort');
state.order=urlState.get('order')==='1'?1:-1;state.page=Math.min(1000000,Math.max(1,parseInt(urlState.get('page'),10)||1));
state.card=urlState.get('card');state.run=urlState.get('run');state.seat=Math.min(15,Math.max(0,parseInt(urlState.get('seat'),10)||0));state.floor=Math.max(1,parseInt(urlState.get('floor'),10)||1);
let catalogPromise;
async function historyCatalog(){
 if(state.catalogReady)return;
 catalogPromise??=Promise.all([api('catalog.json'),api('relics.json')]).then(([catalog,relics])=>{state.catalog=catalog;state.relics=relics;state.catalogReady=true;}).catch(error=>{catalogPromise=null;throw error;});
 return catalogPromise;
}
async function boot(){
 render();const loading=load();
 api('characters.json').then(characters=>{Object.assign(CHARACTERS,characters);if(state.view==='runs')renderRuns();}).catch(()=>{});
 await loading;const historyId=urlState.get('history');if(/^[0-9a-f]{32}$/.test(historyId??''))await openHistory(historyId,true);
}
boot();
