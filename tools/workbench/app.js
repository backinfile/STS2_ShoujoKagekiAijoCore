(async () => {
  'use strict';
  const $=id=>document.getElementById(id),clone=x=>JSON.parse(JSON.stringify(x));
  const escape=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const uid=()=>globalThis.crypto?.randomUUID?.()||'u_'+Date.now().toString(36)+'_'+Math.random().toString(36).slice(2);
  const safeJSON=x=>JSON.stringify(x).replace(/</g,'\\u003c').replace(/\u2028/g,'\\u2028').replace(/\u2029/g,'\\u2029');
  const seedElement=$('workbench-data'),embeddedSeed=JSON.parse(seedElement.textContent);
  let seed=embeddedSeed;
  if(seedElement.dataset.src){
    const response=await fetch(seedElement.dataset.src,{cache:'no-cache'});
    if(!response.ok)throw Error('工作台数据载入失败');
    seed={...await response.json(),...(embeddedSeed.ui?{ui:embeddedSeed.ui}:{})};
  }
  const builtAssets=JSON.parse($('workbench-assets').textContent),flowDefaults=JSON.parse($('workbench-route-data').textContent);
  const previewMode=new URLSearchParams(location.hash.slice(1)).get('preview')==='1';
  const workspaceId=seed.workspaceId+(previewMode?':card-ui-preview':''),mirrorKey='junna-workbench:'+workspaceId,uiKey=mirrorKey+':ui';
  let connection=null;if(!previewMode)try{const c=JSON.parse($('workbench-connection')?.textContent||'{}');if(c.workspaceId===workspaceId&&/^http:\/\/127\.0\.0\.1:\d+$/.test(c.baseURL)&&/^[a-f0-9]{64}$/.test(c.token))connection=c;}catch(_){}
  const serviceDisabledKey=mirrorKey+':local-service-disabled',serviceFingerprintKey=mirrorKey+':local-service-fingerprint';
  const {ENTITY_COLLECTIONS:COLLECTIONS,entityList,rootEntity,assignRoot}=JunnaWorkbenchDomain;
  const TYPES=['攻击','技能','能力','箭矢','状态','诅咒','任务'],RARITIES=['基础','普通','罕见','稀有','远古','事件','衍生','状态','诅咒','任务'],CARD_POOLS=['角色','无色','事件','衍生','状态','诅咒','任务'];
  const KINDS=['关键词','扳机','效果','状态','数值','条件','对象','规则','突破'];
  const STYLE_CATEGORIES=['卡牌设计','牌面表述','契合与命名','卡图与提示','工作台与复核','未分类'];
  const LABELS={cards:'卡牌',terms:'词条',mechanisms:'机制',archetypes:'流派',partnerGroups:'知识组',styles:'风格约定',fitPool:'契合',relics:'遗物',powers:'Power',root:'制作台'};
  const FIELD_LABELS={content:'内容',name:'名称',id:'编号',text:'牌面',upgradedText:'升级牌面',cost:'费用',upgradedCost:'升级费用',turns:'回合数',upgradedTurns:'升级回合数',type:'类型',rarity:'稀有度',cardPool:'卡池',system:'体系',pool:'入池',termIds:'词条',archetypeIds:'流派',keywords:'关键词',definition:'定义',template:'卡面约定',rules:'规则',templates:'描述约定',notes:'备注',role:'卡位',direction:'配合方向',art:'插画',arrowFocus:'箭头聚焦',naming:'出处',partnerCardIds:'知识关系',grantedCardIds:'同组牌',grantedCardCounts:'发放数量',level:'层级',title:'名称',kind:'类别',unit:'单位',systems:'适用体系',sources:'来源',trashedAt:'删除时间',meta:'规则数据'};
  FIELD_LABELS.reservedAt='备用时间';Object.assign(FIELD_LABELS,{category:'分类',mainCardId:'主牌',mainCardCount:'主牌数量',extraHoverTips:'额外悬停提示',knowledgeBackground:'知识背景',theme:'主题',original:'原文',source:'出处',sourceUrl:'来源链接',cardIds:'实装卡牌'});
  let state=clone(seed),db=null,saveQueue=Promise.resolve(),pending=null,editTimer,toastTimer,fitTimer,searchTimer;
  let persistedRecords=new Set(),persistedAssets=new Set(),lastSaveError=null;
  let diskHandle=null,diskFingerprint=null,diskGranted=false,diskConflict=false,diskError=null,diskWriting=false,diskDirty=false,diskSuspended=false,diskQueue=Promise.resolve();
  const blobURLs=new Map();
  const TAB_FILTER_DEFAULTS={search:'',kind:'all',type:'all',rarity:'all',cardPool:'all',pool:'all',cost:'all',archetype:'all',term:'all',breakthrough:'all',sort:'natural',descending:false,upgraded:false,routePosition:'all',routePositions:null,routeExcludedPositions:null,fitTheme:'all',fitStatus:'all',powerSystem:'all',powerOrigin:'all',mechanismFilter:'all',flowRoute:'all'};
  let ui={tab:'cards',...clone(TAB_FILTER_DEFAULTS),fitTheme:'all',fitStatus:'all',powerSystem:'all',powerOrigin:'all',modelSection:'quick',modelRarity:'普通',modelExhaust:false,overviewScope:'cards',overviewAll:false,overviewBy:['rarity','system'],flowScope:'cards',flowRoute:'all',flowOnlyRewards:false,flowDrill:null,selected:null,mechanismFilter:'all',...(seed.ui||{})};
  const mobileLayout=JunnaMobileLayout.create({dom:$,closeInspector:closeEditor,fitCards});
  const flowView=createJunnaRouteStatisticsView({data:()=>state.data,ui:()=>ui,defaults:flowDefaults,dom:$,escape,formatCardText:energyText,table:overviewTable,cardScope:card=>cardTab(card),flush:flushEdit,render:renderAll,transact});
  const modelView=createJunnaCardModelView({data:()=>state.data,ui:()=>ui,dom:$,escape,render:renderAll,cardHTML,fitCards});
  const fitPoolView=JunnaFitPool.create({data:()=>state.data,ui:()=>ui,dom:$,escape,assetURL,field,area,checkedPicker,render:renderAll,flush:flushEdit});
  const potionView=JunnaPotions.create({data:()=>state.data,ui:()=>ui,dom:$,escape,assetURL,energyText,field,area,selectField});
  const eventView=JunnaEvents.create({data:()=>state.data,ui:()=>ui,dom:$,escape,assetURL,field,area,selectField,checkedPicker,flush:flushEdit,transact});
  const relicView=JunnaRelics.create({data:()=>state.data,ui:()=>ui,dom:$,escape,assetURL,energyText,field,area});
  const powerView=JunnaPowers.create({data:()=>state.data,ui:()=>ui,dom:$,escape,assetURL,energyText,field,area,flush:flushEdit,render:renderAll});
  const initialView=JunnaInitialConfig.create({data:()=>state.data,dom:$,escape,transact,toast});
  const cardHoverTips=JunnaCardHoverTips.create({cardByUid:key=>find('cards',key),escape});
  let knowledgeView=null;
  const knowledgeFaces=new Map();let knowledgeWheelAt=-Infinity;
  try{Object.assign(ui,JSON.parse(localStorage.getItem(uiKey)||'{}'));}catch(_){}
  if(!ui.tabFilters||typeof ui.tabFilters!=='object'||Array.isArray(ui.tabFilters))ui.tabFilters={};
  if(ui.selected?.collection==='cards'&&ui.selected.cardSlot)knowledgeFaces.set(ui.selected.cardOrigin||ui.selected.uid,ui.selected.cardSlot);
  if(ui.type==='言葉'||ui.type==='\u661f\u77e2')ui.type='箭矢';
  function toast(message){clearTimeout(toastTimer);$('toast').textContent=message;$('toast').hidden=false;toastTimer=setTimeout(()=>$('toast').hidden=true,3200);}
  function assetURL(id){const source=state.assets?.[id]||builtAssets[id]||builtAssets.defaultPortrait;if(source.startsWith('/assets/')||source.startsWith('https://'))return source;if(blobURLs.has(source))return blobURLs.get(source);const comma=source.indexOf(','),type=source.slice(5,source.indexOf(';')),binary=atob(source.slice(comma+1)),bytes=new Uint8Array(binary.length);for(let i=0;i<binary.length;i++)bytes[i]=binary.charCodeAt(i);const url=URL.createObjectURL(new Blob([bytes],{type}));blobURLs.set(source,url);return url;}
  function field(label,key,value,extra=''){return '<label class="field"><span class="field-label">'+escape(label)+'</span><input data-field="'+escape(key)+'" value="'+escape(value)+'" '+extra+'></label>';}
  function area(label,key,value,rows=4,extra=''){return '<label class="field"><span class="field-label">'+escape(label)+'</span><textarea data-field="'+escape(key)+'" rows="'+rows+'" '+extra+'>'+escape(value)+'</textarea></label>';}
  function options(values,current,allLabel){return (allLabel?'<option value="all">'+allLabel+'</option>':'')+values.map(x=>{const v=typeof x==='object'?x.value:x,label=typeof x==='object'?x.label:x;return '<option value="'+escape(v)+'"'+(String(v)===String(current)?' selected':'')+'>'+escape(label)+'</option>';}).join('');}
  function selectField(label,key,values,value,extra=''){return '<label class="field"><span class="field-label">'+escape(label)+'</span><select data-field="'+key+'" '+extra+'>'+options(values,value)+'</select></label>';}
  const find=(collection,key)=>(['relics','powers'].includes(collection)?state.data.meta[collection]:state.data[collection])?.find(e=>e.uid===key||e.id===key);
  const current=()=>ui.selected?find(ui.selected.collection,ui.selected.uid):null;
  const normalizeData=JunnaWorkbenchDomain.create({seed,uid,JunnaFitPool});
  function patchesBetween(before,after){
    const patches=[];
    for(const collection of COLLECTIONS){const b=new Map(entityList(before,collection).map((e,i)=>[e.uid,{e,i}])),a=new Map(entityList(after,collection).map((e,i)=>[e.uid,{e,i}]));for(const key of new Set([...b.keys(),...a.keys()])){const old=b.get(key),next=a.get(key);if(JSON.stringify(old?.e??null)!==JSON.stringify(next?.e??null))patches.push({collection,uid:key,before:old?clone(old.e):null,after:next?clone(next.e):null,beforeIndex:old?.i??-1,afterIndex:next?.i??-1});}}
    for(const key of ['title','meta'])if(JSON.stringify(rootEntity(before,key))!==JSON.stringify(rootEntity(after,key)))patches.push({collection:'root',uid:key,before:clone(rootEntity(before,key)),after:clone(rootEntity(after,key))});
    return patches;
  }
  function applyPatches(patches,side,fieldsOnly=false){for(const p of patches){if(p.collection==='root'){if(!['title','meta'].includes(p.uid))throw Error('记录中的字段无效');assignRoot(state.data,p.uid,p[side]);continue;}if(!COLLECTIONS.includes(p.collection))throw Error('记录中的条目类型无效');const list=entityList(state.data,p.collection,true),at=list.findIndex(e=>e.uid===p.uid);if(fieldsOnly&&p.before&&p.after&&at>=0){const value={...list[at]};for(const {key}of changedFields(p)){if(Object.hasOwn(p[side],key))value[key]=clone(p[side][key]);else delete value[key];}list[at]=value;continue;}if(at>=0)list.splice(at,1);if(p[side])list.splice(Math.min(p[side+'Index']<0?list.length:p[side+'Index'],list.length),0,clone(p[side]));}state.data=normalizeData(state.data);}
  function syncMirror(){try{localStorage.setItem(mirrorKey,JSON.stringify(state));return true;}catch(_){if(db)try{localStorage.setItem(mirrorKey,JSON.stringify({...state,assets:{}}));return true;}catch(_){}return false;}}
  async function openDB(){return new Promise((resolve,reject)=>{const request=indexedDB.open('junna-mod-workbench',2);request.onupgradeneeded=()=>{for(const name of ['documents','records','assets','files'])if(!request.result.objectStoreNames.contains(name))request.result.createObjectStore(name);};request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});}
  const requestValue=request=>new Promise((resolve,reject)=>{request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});
  async function readDB(){const tx=db.transaction(['documents','records','assets'],'readonly'),range=IDBKeyRange.bound(workspaceId+':',workspaceId+':\uffff');const [doc,records,assets]=await Promise.all([requestValue(tx.objectStore('documents').get(workspaceId)),requestValue(tx.objectStore('records').getAll(range)),requestValue(tx.objectStore('assets').getAll(range))]);if(!doc)return null;persistedRecords=new Set(records.map(r=>r.id));persistedAssets=new Set(assets.map(a=>a.id));return {...doc,history:records.sort((a,b)=>a.seq-b.seq),assets:Object.fromEntries(assets.map(a=>[a.id,a.value]))};}
  let mcp=null,mcpStatus={ready:false,pending:0,busy:false},recoveredDraft=null,mcpHasSnapshot=false,mcpConnecting=!!connection;
  let historyOffset=0,historyLoading=false,historyQueryTotal=0,historyReloadPending=false,historyFilterTimer;
  const mcpClientId=uid(),draftSessionKey=mirrorKey+':mcp-draft-key';let oldDraftKey=null;
  try{oldDraftKey=sessionStorage.getItem(draftSessionKey);sessionStorage.setItem(draftSessionKey,mcpClientId);}catch(_){}
  function updateDiskState(){
    const b=$('file-state');
    if(mcp){diskGranted=mcpStatus.ready;diskConflict=!!mcpStatus.conflict;diskDirty=!!(mcpStatus.pending||pending);diskWriting=mcpStatus.busy;diskError=mcpStatus.error;b.textContent=diskConflict?(mcpStatus.conflict.code==='VERSION_CONFLICT'?'版本冲突':'保存失败'):!diskGranted?(mcpConnecting?'MCP 连接中':'MCP 未连接'):diskWriting?'MCP 保存中':diskDirty?'MCP 待保存':'MCP 已保存';b.dataset.state=diskConflict||!diskGranted&&!mcpConnecting?'error':mcpConnecting||diskDirty||diskWriting?'pending':'saved';b.title=diskError?.message||'MCP';}
    else {b.textContent=previewMode?'预览':connection?'MCP 连接中':'只读副本';b.dataset.state=connection?'pending':'saved';}
    $('read-local-file').disabled=!mcp;$('bind-local-file').hidden=true;$('disconnect-local-file').hidden=true;
  }
  function localBundle(){return {...clone(state),ui:clone(ui),exportedAt:new Date().toISOString()};}
  function storeMcpDraft(draft,backup=false){
    if(!mcp)return;
    const key=workspaceId+':mcp-draft:'+(backup?mcpClientId+':backup:'+Date.now():mcpClientId);
    const value={...draft,assets:Object.assign({},...draft.outbox.map(item=>item.assets||{})),history:draft.outbox.map(item=>item.record),undoStack:clone(state.undoStack),redoStack:clone(state.redoStack)};
    const empty=!backup&&!draft.outbox.length;
    saveQueue=saveQueue.catch(()=>{}).then(()=>new Promise((resolve,reject)=>{
      if(!db){try{empty?localStorage.removeItem(key):localStorage.setItem(key,JSON.stringify(value));resolve();}catch(error){reject(error);}return;}
      const tx=db.transaction('documents','readwrite');
      if(empty)tx.objectStore('documents').delete(key);else tx.objectStore('documents').put(value,key);
      tx.oncomplete=()=>resolve();tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error||Error('草稿暂存失败'));
    })).then(()=>{lastSaveError=null;$('save-state').textContent='已暂存';}).catch(error=>{lastSaveError=error;$('save-state').textContent='草稿暂存失败';});
  }
  function receiveMcpSnapshot(bundle,changes){
    const undoStack=mcpHasSnapshot?(state.undoStack||[]):[],redoStack=mcpHasSnapshot?(state.redoStack||[]):[],history=mcpHasSnapshot?(state.history||[]):[],assets=state.assets||{};mcpHasSnapshot=true;state={...clone(bundle),history:[...new Map([...history,...bundle.history].map(record=>[record.id,record])).values()].sort((a,b)=>a.seq-b.seq),assets:{...assets,...bundle.assets},undoStack,redoStack};
    restoreRecoveredDraft();recoveredDraft=null;
    if(changes?.length&&changes.every(change=>change.collection==='cards')&&['cards','reserve','trash'].includes(ui.tab)){
      renderSidebar();renderWorkspace();if(changes.some(change=>change.uid===ui.selected?.uid))renderInspector();
    }else renderAll();
  }
  function restoreRecoveredDraft(){if(recoveredDraft?.outbox?.length){if(recoveredDraft.data)state.data=normalizeData(recoveredDraft.data);else for(const item of recoveredDraft.outbox)applyPatches(item.record.patches,'after',true);state.assets={...state.assets,...recoveredDraft.assets};state.history.push(...recoveredDraft.history.filter(r=>!state.history.some(x=>x.id===r.id)));state.undoStack=recoveredDraft.undoStack||[];state.redoStack=recoveredDraft.redoStack||[];}}
  async function connectLocalService(){
    if(!connection)return false;
    if(!mcp)mcp=JunnaWorkbenchMcp.create({connection,initialSnapshot:seed.versions?{bundle:seed,versions:seed.versions}:null,clientId:mcpClientId,uuid:uid,getAssets:()=>state.assets,onSnapshot:receiveMcpSnapshot,onCommit:receiveMcpCommit,onStatus:status=>{const wasConflict=!!mcpStatus.conflict;mcpStatus=status;updateChrome();if(status.conflict&&!wasConflict)showMcpConflict();},onDraft:storeMcpDraft});
    mcpConnecting=true;updateDiskState();
    try{const connected=await mcp.start(recoveredDraft);if(!connected)toast(mcpStatus.error?.message||'MCP 未连接');return connected;}
    finally{mcpConnecting=false;updateDiskState();}
  }
  function receiveMcpCommit(id,result){
    const at=state.history.findIndex(record=>record.id===id);
    if(at>=0){if(result.record)state.history[at]=result.record;else state.history.splice(at,1);}
    else if(result.record){state.history.push(result.record);state.undoStack.push(id);state.redoStack=[];}
    if(!result.record){state.undoStack=state.undoStack.filter(key=>key!==id);state.redoStack=state.redoStack.filter(key=>key!==id);}
    state.revision=Math.max(state.revision,result.revision);persist();
  }
  function queueDiskSave(){if(mcp){diskQueue=mcp.flush();return diskQueue;}return Promise.resolve();}
  async function saveLocalFile(){flushEdit();if(!mcp)return connectLocalService();if(mcpStatus.conflict)return showMcpConflict();await queueDiskSave();if(!mcpStatus.pending)await mcp.pull();updateDiskState();}
  async function readLocalFile(){flushEdit();if(!mcp)return connectLocalService();if(mcpStatus.pending)return mcpStatus.conflict?showMcpConflict():saveLocalFile();await mcp.pull();}
  async function bindLocalFile(){return saveLocalFile();}
  async function disconnectLocalFile(){return saveLocalFile();}
  async function restoreLocalFile(){return connectLocalService();}
  let conflictRows=[];
  function conflictFields(value,fields){return fields?Object.fromEntries(fields.filter(field=>Object.hasOwn(value||{},field)).map(field=>[field,value[field]])):value;}
  function discardPendingHistory(ids){state.history=state.history.filter(record=>!ids.has(record.id));state.undoStack=state.undoStack.filter(id=>!ids.has(id));state.redoStack=state.redoStack.filter(id=>!ids.has(id));renderAll();persist();}
  async function showMcpConflict(){
    if(!mcpStatus.conflict)return;try{flushEdit();conflictRows=await mcp.proposals();$('mcp-conflict-title').textContent=mcpStatus.conflict.code==='VERSION_CONFLICT'?'版本冲突':'保存失败';const body=$('mcp-conflict-body');body.innerHTML=conflictRows.map((p,i)=>'<section class="mcp-conflict-entry"><h3>'+escape(p.local?.name||p.before?.name||p.uid)+'</h3><details><summary>你的修改</summary><pre>'+escape(JSON.stringify(conflictFields(p.local,p.fields),null,2))+'</pre></details><details><summary>当前内容</summary><pre>'+escape(JSON.stringify(conflictFields(p.before,p.fields),null,2))+'</pre></details><label class="field"><span class="field-label">合并内容</span><textarea data-merge-index="'+i+'" rows="6">'+escape(JSON.stringify(conflictFields(p.after,p.fields),null,2))+'</textarea></label></section>').join('');if(!$('mcp-conflict-dialog').open)$('mcp-conflict-dialog').showModal();}catch(error){toast(error.message);}
  }
  $('mcp-export-draft').addEventListener('click',()=>download(JSON.stringify({draft:mcp.draft(),data:state.data,assets:state.assets},null,2),state.data.title+'_冲突草稿.json','application/json;charset=utf-8'));
  $('mcp-use-server').addEventListener('click',async()=>{try{const ids=new Set(mcp.draft().outbox.map(entry=>entry.record.id));await mcp.replace([]);discardPendingHistory(ids);$('mcp-conflict-dialog').close();}catch(error){toast(error.message);}});
  $('mcp-merge-submit').addEventListener('click',async()=>{try{const changes=conflictRows.map((p,i)=>{const edited=JSON.parse($('mcp-conflict-body').querySelector('[data-merge-index="'+i+'"]').value);let after=edited;if(p.fields){if(!edited||typeof edited!=='object'||Array.isArray(edited))throw Error('请填写字段对象');after={...p.before,...edited};for(const field of p.fields)if(!Object.hasOwn(edited,field))delete after[field];}return {collection:p.collection,uid:p.uid,expectedVersion:p.expectedVersion,before:p.before,after,afterIndex:p.afterIndex};});const ids=new Set(mcp.draft().outbox.map(entry=>entry.record.id));$('mcp-conflict-dialog').close();await mcp.replace(changes);discardPendingHistory(ids);}catch(error){toast(error.message);}});
  function persist(){
    if(mcp){queueDiskSave();return saveQueue;}
    const snapshot=clone(state),mirrored=syncMirror();$('save-state').textContent='暂存中';queueDiskSave();
    saveQueue=saveQueue.catch(()=>{}).then(async()=>{
      if(!db){if(!mirrored)throw Error('浏览器存储空间不足，请导出保存');return;}
      await new Promise((resolve,reject)=>{const tx=db.transaction(['documents','records','assets'],'readwrite'),{history,assets,...doc}=snapshot;tx.objectStore('documents').put(doc,workspaceId);for(const record of history)if(!persistedRecords.has(record.id))tx.objectStore('records').put(record,workspaceId+':'+record.id);for(const [id,value]of Object.entries(assets))if(!persistedAssets.has(id))tx.objectStore('assets').put({id,value},workspaceId+':'+id);tx.oncomplete=()=>{for(const r of history)persistedRecords.add(r.id);for(const id of Object.keys(assets))persistedAssets.add(id);resolve();};tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error||Error('保存事务中止'));});
    }).then(()=>{lastSaveError=null;if(snapshot.revision===state.revision)$('save-state').textContent='已暂存';}).catch(error=>{lastSaveError=error;$('save-state').textContent='暂存失败';toast(error.message);});return saveQueue;
  }
  function record(before,label,kind='编辑',stack=true,reference=null){
    state.data=normalizeData(state.data);const patches=patchesBetween(before,state.data);if(!patches.length)return null;
    const entry={id:uid(),seq:++state.revision,at:new Date().toISOString(),label,kind,patches,reference};state.history.push(entry);state.historyTotal=Math.max(state.historyTotal||0,state.history.length-1)+1;if(mcp)mcp.enqueue(entry);if(stack){state.undoStack.push(entry.id);state.redoStack=[];}persist();updateChrome();return entry;
  }
  function transact(label,mutate,kind='编辑'){if(!previewMode&&(!mcpStatus.ready||mcpStatus.conflict||mcpStatus.resolving)){toast(mcpStatus.conflict?'请先处理版本冲突':mcpStatus.resolving?'MCP 保存中':'MCP 未连接');return null;}flushEdit();const before=clone(state.data);try{mutate(state.data);const r=record(before,label,kind);renderAll();return r;}catch(error){state.data=before;renderAll();toast(error.message);return null;}}
  function beginEdit(){if(!pending){mcp?.invalidateSnapshots();pending={before:clone(state.data),label:(LABELS[ui.selected?.collection]||'制作台')+' · '+(current()?.name||state.data.title)};}}
  function flushEdit(){clearTimeout(editTimer);if(!pending)return;const edit=pending;pending=null;try{const entry=record(edit.before,edit.label);if(entry?.patches.length===1&&entry.patches[0].collection==='cards'&&['cards','reserve','trash'].includes(ui.tab)){
      const patch=entry.patches[0],before=patch.before,after=patch.after;
      const stableLayout=before&&after&&['id','name','cost','upgradedCost','type','rarity','cardPool','system','pool','reservedAt','trashedAt','termIds','archetypeIds','keywords','partnerCardIds'].every(field=>JSON.stringify(before[field])===JSON.stringify(after[field]));
      const nodes=$('work-area').querySelectorAll('[data-card-face="'+CSS.escape(patch.uid)+'"]');
      if(stableLayout&&nodes.length){for(const node of nodes)node.outerHTML=cardEntryHTML(find('cards',node.dataset.cardOrigin),ui.tab==='trash');fitCards();}else renderWorkspace();
    }else renderWorkspace();if(['styles','flows','fits','events','potions','relics','powers'].includes(ui.tab))renderSidebar();}catch(error){state.data=edit.before;renderAll();toast(error.message);}}
  function updateChrome(){$('workspace-title').textContent='少女歌剧工作台';document.title='少女歌剧工作台 · '+state.data.title;$('history-count').textContent=Math.max(state.historyTotal||0,state.history.length);$('undo').disabled=!state.undoStack.length||!!mcpStatus.resolving;$('redo').disabled=!state.redoStack.length||!!mcpStatus.resolving;$('editor-form').inert=!!mcpStatus.resolving;updateDiskState();}
  function rememberTabFilters(){ui.tabFilters??={};ui.tabFilters[ui.tab]=Object.fromEntries(Object.keys(TAB_FILTER_DEFAULTS).map(key=>[key,clone(ui[key]??TAB_FILTER_DEFAULTS[key])]));}
  function selectTab(tab){if(tab===ui.tab)return;rememberTabFilters();Object.assign(ui,clone(TAB_FILTER_DEFAULTS),{pool:tab==='cards'?'pool':'all'},clone(ui.tabFilters[tab]||{}),{tab});if(ui.type==='言葉'||ui.type==='\u661f\u77e2')ui.type='箭矢';}
  function saveUI(push=false){rememberTabFilters();try{localStorage.setItem(uiKey,JSON.stringify(ui));}catch(_){}const params=new URLSearchParams();if(previewMode)params.set('preview','1');for(const key of ['tab','search','kind','type','rarity','cardPool','pool','cost','archetype','term','sort','fitTheme','fitStatus','powerSystem','powerOrigin','mechanismFilter'])if(ui[key]&&(ui[key]!=='all'||key==='pool'&&ui.tab==='cards')&&(key!=='sort'||ui.sort!=='natural'))params.set(key,JunnaFilters.encode(key==='tab'&&['reserve','trash'].includes(ui.tab)?'cards':ui[key]));if(['reserve','trash'].includes(ui.tab))params.set('cardCategory',ui.tab);if(ui.routePositions?.length)params.set('positions',ui.routePositions.join(','));if(ui.routeExcludedPositions?.length)params.set('excludePositions',ui.routeExcludedPositions.join(','));if(ui.tab==='overview'){if(ui.overviewScope!=='cards')params.set('overviewScope',ui.overviewScope);params.set('overviewBy',overviewDimensions().map(d=>d.key).join(',')||'none');if(ui.overviewAll)params.set('overviewAll','1');}if(ui.tab==='flows'){if(ui.flowScope!=='cards')params.set('flowScope',ui.flowScope);if(ui.flowRoute!=='all')params.set('flowRoute',JunnaFilters.encode(ui.flowRoute));if(ui.flowOnlyRewards)params.set('flowRewards','1');}if(ui.upgraded)params.set('upgraded','1');if(ui.descending)params.set('descending','1');const hash=params.toString();if(location.hash.slice(1)!==hash&&!new URLSearchParams(location.hash.slice(1)).has('handoff'))history[push?'pushState':'replaceState'](null,'','#'+hash);}
  const cardTab=card=>card.trashedAt?'trash':card.reservedAt?'reserve':'cards';
  // 总览沿用现有编辑台：左栏选择范围与分组要素，中栏交叉分组，右栏统计当前范围。
  // 数量按不同牌面统计；知识组另列发放张数。所有数字从当前数据即时计算。
  const OVERVIEW_DIMENSIONS=[{key:'rarity',label:'稀有度',order:RARITIES},{key:'type',label:'卡牌类型',order:TYPES},{key:'cardPool',label:'卡池',order:CARD_POOLS},{key:'cost',label:'费用'},{key:'upgradedCost',label:'升级费用'},{key:'system',label:'体系'},{key:'role',label:'卡位'},{key:'breakthrough',label:'突破限制',order:['突破限制牌','常规牌']},{key:'pool',label:'入池',order:['入池','不入池']}];
  const overviewDimensions=()=>OVERVIEW_DIMENSIONS.filter(d=>Array.isArray(ui.overviewBy)&&ui.overviewBy.includes(d.key));
  function overviewStats(){
    const scope=['cards','reserve','trash'].includes(ui.overviewScope)?ui.overviewScope:'cards',cards=ui.overviewAll?JunnaPoolReference.poolFaces(state.data,scope):state.data.cards.filter(c=>cardTab(c)===scope&&c.pool),pool=cards.filter(c=>c.pool),ids=new Set(cards.map(c=>c.id));
    const groups=state.data.partnerGroups.map(g=>({group:g,members:[g.mainCardId,...g.grantedCardIds].map(id=>state.data.cards.find(c=>c.id===id)).filter(c=>c&&ids.has(c.id))})).filter(g=>g.members.length);
    return {scope,cards,pool,nonPool:cards.filter(c=>!c.pool),groups};
  }
  function overviewTable(title,headers,rows){return '<section class="overview-section"><h2>'+escape(title)+'</h2><div class="overview-table-wrap"><table class="overview-table" aria-label="'+escape(title)+'"><thead><tr>'+headers.map(h=>'<th scope="col">'+escape(h)+'</th>').join('')+'</tr></thead><tbody>'+rows.join('')+'</tbody></table></div></section>';}
  function overviewReference(stats){
    if(stats.scope!=='cards')return '';
    const includeAll=Boolean(ui.overviewAll),reference=JunnaPoolReference.compute(state.data,{includeAll});
    const rowHTML=row=>'<tr><th scope="row">'+escape(row.name)+'</th><td>'+row.current+(row.unit||'张')+'</td><td>'+JunnaPoolReference.range(row)+'</td><td>'+JunnaPoolReference.difference(row)+'</td></tr>';
    const table=(title,dimension,rows,unknown=0)=>overviewTable(title,[dimension,'当前','建议','差额'],rows.map(rowHTML).concat(unknown?['<tr><th scope="row">未分类</th><td>'+unknown+'张</td><td>—</td><td>—</td></tr>']:[]));
    const routeRows=reference.routes.map(route=>'<tr><th scope="row">'+escape(route.name)+'</th><td>'+route.current+'张</td><td>'+JunnaPoolReference.range(route)+(route.primary?'（主体系可更多）':'')+'</td><td>'+route.breakthroughs+'张</td><td>'+JunnaPoolReference.range({min:route.breakthroughMin,max:route.breakthroughMax})+'</td></tr>');
    return '<details class="overview-reference" open><summary>'+(includeAll?'全部牌面统计':'完整卡池参考')+' · '+reference.total+'张</summary><div class="overview-grid">'+table('费用','费用',reference.costs,reference.unknownCosts)+table('稀有度','稀有度',reference.rarities)+table('卡牌类型 · 含箭矢','类型',reference.types,reference.unknownTypes)+table('卡池结构','项目',reference.structure)+'<div class="overview-comparison">'+overviewTable(includeAll?'各体系统计':'各体系参考',includeAll?['体系','当前牌面','建议牌面','当前突破牌','建议突破牌']:['体系','当前奖励牌','建议奖励牌','当前突破牌','建议突破牌'],routeRows)+'</div></div></details>';
  }
  function overviewComparison(stats){
    const dimensions=overviewDimensions(),names=state.data.meta.systemNames||{},breakthroughIds=new Set(state.data.terms.filter(t=>t.kind==='突破').map(t=>t.id)),groups=new Map(),collator=new Intl.Collator('zh-CN',{numeric:true});
    const value=(card,key)=>key==='breakthrough'?(card.termIds.some(id=>breakthroughIds.has(id))?'突破限制牌':'常规牌'):key==='pool'?(card.pool?'入池':'不入池'):key==='cardPool'?(card.cardPool||'角色'):String((key==='type'&&card.type==='箭矢'?card.arrowSubtype:card[key])??'')||'未分类';
    for(const card of stats.cards){const values=dimensions.map(d=>value(card,d.key)),key=JSON.stringify(values);if(!groups.has(key))groups.set(key,{values,count:0});groups.get(key).count++;}
    const sorted=[...groups.values()].sort((a,b)=>{for(let i=0;i<dimensions.length;i++){const d=dimensions[i],order=d.order||(d.key==='system'?Object.keys(names):null),rank=v=>{const index=order?.indexOf(v);return index>=0?index:order?.length||0;},result=(order?rank(a.values[i])-rank(b.values[i]):0)||collator.compare(a.values[i],b.values[i]);if(result)return result;}return 0;});
    const share=count=>{const percent=stats.cards.length?count/stats.cards.length*100:0;return '<span class="overview-share"><i aria-hidden="true" style="width:'+percent.toFixed(2)+'%"></i><span>'+percent.toFixed(1)+'%</span></span>';};
    const rows=sorted.map(group=>'<tr>'+(dimensions.length?group.values.map((v,i)=>{const key=dimensions[i].key,label=key==='system'?names[v]||v:key==='cost'||key==='upgradedCost'?v+'费':v;return i===0?'<th scope="row" class="overview-dimension">'+escape(label)+'</th>':'<td class="overview-dimension">'+escape(label)+'</td>';}).join(''):'<th scope="row">合计</th>')+'<td>'+group.count+'</td><td>'+share(group.count)+'</td></tr>');
    const columns=Math.max(1,dimensions.length);
    if(!rows.length)rows.push('<tr><td colspan="'+(columns+2)+'" class="overview-empty">暂无卡牌</td></tr>');
    else if(dimensions.length)rows.push('<tr class="overview-total"><th scope="row" colspan="'+columns+'">合计</th><td>'+stats.cards.length+'</td><td>'+share(stats.cards.length)+'</td></tr>');
    return '<div class="overview-comparison">'+overviewTable('分组对比',[...(dimensions.length?dimensions.map(d=>d.label):['类别']),'数量','占比'],rows)+'</div>';
  }
  function overviewNativeComparison(stats){
    if(stats.scope!=='cards')return '';
    const comparison=JunnaNativePoolComparison.compute(stats.cards,{dimensions:overviewDimensions(),includeAll:Boolean(ui.overviewAll),modName:document.querySelector('#mod-select option:checked')?.textContent||'纯那'});
    const {dimensions,columns}=comparison;
    const cells=counts=>counts.map((count,i)=>{const percent=columns[i].total?count/columns[i].total*100:0;return '<td><span class="overview-share"><i aria-hidden="true" style="width:'+percent.toFixed(2)+'%"></i><span>'+count+' · '+percent.toFixed(1)+'%</span></span></td>';}).join('');
    const rows=comparison.rows.map(group=>'<tr>'+(dimensions.length?group.values.map((value,i)=>{const label=['cost','upgradedCost'].includes(dimensions[i].key)?value+'费':value;return i===0?'<th scope="row">'+escape(label)+'</th>':'<td>'+escape(label)+'</td>';}).join(''):'<th scope="row">合计</th>')+cells(group.counts)+'</tr>');
    if(dimensions.length)rows.push('<tr class="overview-total"><th scope="row" colspan="'+dimensions.length+'">合计</th>'+cells(columns.map(c=>c.total))+'</tr>');
    return '<div class="overview-groups overview-native">'+overviewTable('本体五职业对比 · '+(ui.overviewAll?'职业牌面':'奖励牌'),[...(dimensions.length?dimensions.map(d=>d.label):['类别']),...columns.map(c=>c.name)],rows)+'</div>';
  }
  function renderOverview(){
    const stats=overviewStats();
    const groupRows=stats.groups.map(({group,members})=>{
      const copies=c=>JunnaWorkbenchDomain.groupCardCount(group,c.id);
      return '<tr><th scope="row">'+escape(state.data.cards.find(c=>c.id===group.mainCardId)?.name||group.title)+'</th><td>'+members.length+'</td><td>'+members.reduce((n,c)=>n+copies(c),0)+'</td><td class="overview-members">'+members.map(c=>'<button type="button" class="text-button" data-open-card="'+escape(c.uid)+'">'+escape(c.name)+(copies(c)>1?' ×'+copies(c):'')+'</button>').join('')+'</td></tr>';
    });
    const groups=overviewTable('知识牌组',['牌组','牌面','发放张数','成员'],groupRows.length?groupRows:['<tr><td colspan="4" class="overview-empty">暂无知识牌组</td></tr>']);
    return '<div class="overview-grid">'+overviewReference(stats)+overviewComparison(stats)+overviewNativeComparison(stats)+'<div class="overview-groups">'+groups+'</div></div>';
  }
  function renderOverviewInspector(){
    const stats=overviewStats(),count=match=>stats.cards.filter(match).length,average=(list,key)=>{const values=list.map(c=>c[key]).filter(v=>v!=='X'&&Number.isFinite(Number(v)));return values.length?(values.reduce((n,v)=>n+Number(v),0)/values.length).toFixed(2):'—';};
    const groupedIds=new Set(stats.groups.flatMap(g=>g.members.map(c=>c.id))),copies=stats.groups.reduce((n,{group,members})=>n+members.reduce((v,c)=>v+JunnaWorkbenchDomain.groupCardCount(group,c.id),0),0);
    const facts=[['伤害牌',count(c=>/造成\d+点伤害/.test(c.text))],['格挡牌',count(c=>/获得\d+点格挡/.test(c.text))],['群体伤害',count(c=>/对所有敌人造成/.test(c.text))],['抽牌',count(c=>/抽\d+张|抽取[^。]*牌/.test(c.text))],['获得能量',count(c=>/获得\d+点能量/.test(c.text))],['箭矢',count(c=>c.type==='箭矢')],['3费及以上',count(c=>c.cost!=='X'&&Number(c.cost)>=3)],['升级减费',count(c=>c.cost!=='X'&&c.upgradedCost!=='X'&&Number(c.upgradedCost)<Number(c.cost))]];
    $('inspector').hidden=false;$('inspector-title').textContent='统计';$('close-inspector').hidden=true;$('preview').innerHTML='';$('preview').removeAttribute('tabindex');$('preview').removeAttribute('aria-label');$('inspector-footer').hidden=true;
    $('editor-form').innerHTML='<section class="overview-section"><h2>平均耗能</h2><table class="overview-table" aria-label="平均耗能"><thead><tr><th scope="col">牌面</th><th scope="col">'+(ui.overviewAll?'全部牌面':'入池牌')+'</th></tr></thead><tbody><tr><th scope="row">基础</th><td>'+average(stats.cards,'cost')+'</td></tr><tr><th scope="row">升级</th><td>'+average(stats.cards,'upgradedCost')+'</td></tr></tbody></table></section><section class="overview-section"><h2>效果构成 · 基础</h2><dl class="overview-facts">'+facts.map(([name,n])=>'<div><dt>'+escape(name)+'</dt><dd>'+n+'</dd></div>').join('')+'</dl></section><section class="overview-section"><h2>知识牌组</h2><dl class="overview-facts"><div><dt>牌组</dt><dd>'+stats.groups.length+'</dd></div><div><dt>牌面</dt><dd>'+groupedIds.size+'</dd></div><div><dt>发放张数</dt><dd>'+copies+'</dd></div></dl></section>';
  }
  function readHash(){const p=new URLSearchParams(location.hash.slice(1));if(p.has('handoff'))return;const requested=p.get('tab')||'cards',tab=requested==='cards'&&['reserve','trash'].includes(p.get('cardCategory'))?p.get('cardCategory'):requested==='fits'&&p.get('fitTheme')==='职业药水'?'potions':requested;selectTab(['initial','overview','flows','mechanisms','terms','models','cards','powers','relics','potions','events','fits','reserve','styles','trash'].includes(tab)?tab:'cards');const linked=[...p.keys()].some(key=>key!=='preview');if(linked)Object.assign(ui,clone(TAB_FILTER_DEFAULTS),{pool:ui.tab==='cards'?'pool':'all'});for(const k of ['search','kind','type','rarity','cardPool','pool','cost','archetype','term','sort','fitTheme','fitStatus','powerSystem','powerOrigin','mechanismFilter'])if(p.has(k))ui[k]=['tab','search','sort'].includes(k)?p.get(k):JunnaFilters.decode(p.get(k));if(linked&&(ui.tab==='overview'||p.has('overviewScope')))ui.overviewScope=p.get('overviewScope')||'cards';if(!['cards','reserve','trash'].includes(ui.overviewScope))ui.overviewScope='cards';if(p.has('overviewBy'))ui.overviewBy=p.get('overviewBy').split(',');if(p.has('overviewBy')||p.has('overviewAll'))ui.overviewAll=p.get('overviewAll')==='1';if(linked&&ui.tab==='flows'){ui.flowScope=p.get('flowScope')||'cards';ui.flowRoute=JunnaFilters.decode(p.get('flowRoute')||'all');ui.flowOnlyRewards=p.get('flowRewards')==='1';if(!['cards','reserve','trash'].includes(ui.flowScope))ui.flowScope='cards';}const legacy=!p.has('positions')&&!p.has('excludePositions')&&ui.routePositions===null;ui.routePositions=JunnaRouteStatistics.normalizePositions(p.get('positions')||p.get('routePosition')||(legacy?ui.routePosition:ui.routePositions));ui.routeExcludedPositions=JunnaRouteStatistics.normalizePositions(p.get('excludePositions')||ui.routeExcludedPositions);const oldBreakthrough=p.get('breakthrough')||(legacy?ui.breakthrough:'all');if(oldBreakthrough==='only')ui.routePositions.push('breakthrough');if(oldBreakthrough==='ordinary')ui.routeExcludedPositions.push('breakthrough');ui.routePositions=JunnaRouteStatistics.normalizePositions(ui.routePositions);ui.routeExcludedPositions=JunnaRouteStatistics.normalizePositions(ui.routeExcludedPositions).filter(tag=>!ui.routePositions.includes(tag));ui.routePosition=ui.breakthrough='all';if(linked){ui.upgraded=p.get('upgraded')==='1';ui.descending=p.get('descending')==='1';}if(ui.term==='U_TERM_001'&&!state.data.terms.some(t=>t.id===ui.term))ui.term='KW_PARTNER';}
  function filteredCards(){const term=ui.search.trim().toLowerCase();let list=state.data.cards.filter(c=>{
    const cost=ui.upgraded?c.upgradedCost:c.cost,routes=state.data.archetypes.filter(route=>JunnaFilters.matches(ui.archetype,route.id));
    return cardTab(c)===ui.tab&&(
      JunnaFilters.matches(ui.type,c.type)&&JunnaFilters.matches(ui.rarity,c.rarity)&&
      JunnaFilters.matches(ui.cardPool,c.cardPool||'角色')&&JunnaFilters.matches(ui.pool,c.pool?'pool':'granted')&&
      JunnaFilters.matches(ui.cost,cost,(value,option)=>option==='3+'?value!=='X'&&Number(value)>=3:String(value)===option)&&
      JunnaFilters.matches(ui.archetype,c.archetypeIds)&&JunnaFilters.matches(ui.term,c.termIds)&&
      JunnaRouteStatistics.matchesPosition(c,routes,flowDefaults,ui.routePositions,'all',ui.routeExcludedPositions)
    )&&(!term||[c.id,c.name,c.text,c.upgradedText,c.role,...c.partnerCardIds.map(id=>state.data.cards.find(x=>x.id===id)?.name||''),...c.termIds.map(id=>state.data.terms?.find(t=>t.id===id)?.name||'')].join(' ').toLowerCase().includes(term));
  });
    const collator=new Intl.Collator('zh-CN',{numeric:true}),updated=new Map(Object.entries(state.cardUpdatedAt||{}));if(ui.sort==='updated')for(const r of state.history)for(const p of r.patches)if(p.collection==='cards')updated.set(p.uid,Math.max(updated.get(p.uid)||0,r.seq));
    const costValue=card=>card.cost==='X'?Infinity:Number(card.cost);
    list=list.slice().sort((a,b)=>{
      let result=0;
      if(ui.sort==='natural')result=RARITIES.indexOf(a.rarity)-RARITIES.indexOf(b.rarity)||costValue(a)-costValue(b)||TYPES.indexOf(a.type)-TYPES.indexOf(b.type);
      else if(ui.sort==='name')result=collator.compare(a.name,b.name);
      else if(ui.sort==='cost')result=costValue(a)-costValue(b);
      else if(ui.sort==='rarity')result=RARITIES.indexOf(a.rarity)-RARITIES.indexOf(b.rarity);
      else if(ui.sort==='type')result=TYPES.indexOf(a.type)-TYPES.indexOf(b.type);
      else if(ui.sort==='turns')result=(Number.isFinite(a.turns)?a.turns:Infinity)-(Number.isFinite(b.turns)?b.turns:Infinity);
      else if(ui.sort==='updated')result=(updated.get(b.uid)||0)-(updated.get(a.uid)||0);
      return (result||collator.compare(a.id,b.id))*(ui.descending?-1:1);
    });return list;}
  function filteredTerms(){const q=ui.search.trim().toLowerCase();return state.data.terms.filter(t=>JunnaFilters.matches(ui.kind,t.kind)&&(!q||[t.id,t.name,t.definition,t.template].join(' ').toLowerCase().includes(q))).sort((a,b)=>a.kind.localeCompare(b.kind)||a.id.localeCompare(b.id,undefined,{numeric:true}));}
  function energyText(text,card=null){return JunnaEnergyIcons.render(escape(text),assetURL('energy-junna'),card?.descriptionEnergyDisplay);}
  function knowledgeCopies(card){
    if(!card)return 1;
    const group=groupsFor(card).find(group=>group.mainCardId===card.id&&group.grantedCardIds.length===0);
    return group?JunnaWorkbenchDomain.groupCardCount(group,card.id):1;
  }
  function richText(text,baseText,upgraded=ui.upgraded,card=null){
    const references=JunnaCardReferences.referencesFor(card,state.data.cards),protectedText=JunnaCardReferences.protect(text,references),base=baseText?JunnaCardReferences.protect(baseText,references).text:null;
    let value=escape(protectedText.text);if(upgraded&&base){const original=[...base.matchAll(/\d+/g)].map(m=>m[0]);let i=0;value=value.replace(/\d+/g,n=>n===original[i++]?n:'<span class="text-green">'+n+'</span>');}
    const energy=JunnaEnergyIcons.protect(value,assetURL('energy-junna'),card?.descriptionEnergyDisplay);value=energy.text;
    for(const word of ['立即加速到完成','伤害加成','箭矢','额外触发','知识','重放','消耗','保留','虚弱','易伤','格挡'])value=value.split(word).join('<span class="text-gold">'+word+'</span>');
    value=value.replace(/(?:<span class="text-green">)?\d+(?:<\/span>)?(?:回合|张牌|张|次|点|费)/g,unit=>'<span class="text-unit">'+unit+'</span>').replace(/。(?=\S)/g,'。\n');
    value=JunnaEnergyIcons.restore(value,energy.values);
    for(const item of protectedText.values){const targetText=item.upgraded?item.card.upgradedText:item.card.text;value=value.split(item.token).join('<span class="text-gold card-reference" data-card-reference="'+escape(item.card.uid)+'" title="'+escape(item.text+'\n'+targetText)+'">'+escape(item.text)+'</span>');}
    return value;
  }
  function cardHTML(card,upgraded=ui.upgraded,lazy=false){const type=card.type==='攻击'?'attack':card.type==='能力'?'power':'skill',arrow=card.type==='箭矢',kind=arrow?'stage':type,rarity=card.rarity==='远古'?'ancient':card.rarity==='稀有'?'rare':card.rarity==='罕见'?'uncommon':'common',ancient=rarity==='ancient',cost=upgraded?card.upgradedCost:card.cost,text=upgraded?card.upgradedText:card.text;
    const layer=(name,id)=>'<img class="native-layer '+name+'" src="'+assetURL(id)+'" alt="" draggable="false" decoding="async"'+(lazy?' loading="lazy"':'')+'>';let layers='';
    if(ancient){layers+=layer('native-ancient-portrait',card.art)+layer('native-ancient-border','ancient-card-border')+layer('native-ancient-text','ancient-text-'+type)+layer('native-ancient-highlight','card-highlight-ancient')+(arrow?layer('native-arrow-badge','portrait-stage-junna'):'')+layer('native-banner','banner-ancient')+layer('native-ancient-flame','ancient-flame-0');}
    else{layers+=layer('native-shadow','frame-'+type)+(arrow?'<svg class="native-layer native-arrow-portrait" viewBox="0 0 1456 1080" preserveAspectRatio="none" aria-hidden="true"><image '+(lazy?'data-image-src':'href')+'="'+assetURL(card.art)+'" width="1456" height="1080" preserveAspectRatio="xMidYMid slice" clip-path="url(#junna-arrow-window)"/></svg>':layer('native-portrait',card.art))+layer('native-frame','frame-'+type)+layer('native-class-frame','frame-'+type)+(arrow?layer('native-arrow-portrait-border','portrait-stage-junna'):layer('native-portrait-border','portrait-'+type+'-'+rarity))+layer('native-banner','banner-'+rarity);}
    return '<div class="game-card '+(ancient?'ancient-card ':'')+(upgraded?'is-upgraded':'')+'" data-type="'+kind+'" data-rarity="'+rarity+'" data-frame="'+type+'" data-hover-card="'+escape(card.uid)+'">'+layers+'<div class="native-title"><span>'+escape(card.name)+(upgraded?'+':'')+'</span></div><div class="native-type" style="--plaque:url(\''+assetURL('plaque-'+rarity)+'\')">'+escape(card.type)+'</div><div class="native-description"><div>'+richText(text,upgraded?card.text:null,upgraded,card)+'</div></div><div class="native-energy '+(upgraded&&cost!==card.cost?'cost-upgraded':'')+'">'+layer('native-energy-art','energy-junna')+'<span>'+escape(cost)+'</span></div>'+(arrow?'<div class="native-arrow-turns '+(upgraded&&card.upgradedTurns!==card.turns?'turns-upgraded':'')+'">'+escape(upgraded?card.upgradedTurns:card.turns)+'</div>':'')+'</div>';
  }
  const fittedCards=new WeakMap();let fontGeneration=0;
  function fitCard(card){
    for(const image of card.querySelectorAll('[data-image-src]')){image.setAttribute('href',image.dataset.imageSrc);image.removeAttribute('data-image-src');}
    const width=card.clientWidth;if(!width)return;
    const title=card.querySelector('.native-title span'),body=card.querySelector('.native-description>div');
    const key=width+':'+fontGeneration+':'+title.textContent+':'+body.textContent;
    if(fittedCards.get(card)===key)return;
    const scale=width/300;title.style.whiteSpace=title.textContent.length>10?'normal':'nowrap';
    for(const [node,max,min]of [[title,26,11],[body,21,12]]){
      const overflows=()=>node.scrollHeight>node.parentElement.clientHeight+1||node.scrollWidth>node.parentElement.clientWidth+1;
      node.style.fontSize=max*scale+'px';if(!overflows())continue;
      let low=min,high=max-1;
      while(low<high){const middle=Math.ceil((low+high)/2);node.style.fontSize=middle*scale+'px';if(overflows())high=middle-1;else low=middle;}
      node.style.fontSize=low*scale+'px';
    }
    fittedCards.set(card,key);
  }
  const cardFitObserver=typeof IntersectionObserver==='function'?new IntersectionObserver(entries=>{for(const entry of entries)if(entry.isIntersecting)fitCard(entry.target);},{rootMargin:'300px'}):null;
  function fitCards(invalidate=false){
    if(invalidate)fontGeneration++;
    cancelAnimationFrame(fitTimer);fitTimer=requestAnimationFrame(()=>{
      cardFitObserver?.disconnect();
      const cards=[...document.querySelectorAll('.game-card')],visible=cards.filter(card=>{const bounds=card.getBoundingClientRect();return bounds.width&&bounds.bottom>=-300&&bounds.top<=innerHeight+300&&bounds.right>=0&&bounds.left<=innerWidth;});
      if(cardFitObserver){for(const card of cards)cardFitObserver.observe(card);for(const card of visible)fitCard(card);}
      else for(const card of cards)fitCard(card);
    });
  }
  function renderSidebar(){
    if(ui.tab==='initial'){$('sidebar').innerHTML='';return;}
    if(['cards','reserve','trash'].includes(ui.tab)){ui.archetype=JunnaFilters.prune(ui.archetype,state.data.archetypes.map(a=>a.id));if(typeof ui.term==='string'&&ui.term!=='all'&&!state.data.terms.some(t=>t.id===ui.term&&t.kind==='关键词')){const term=state.data.terms.find(t=>t.id===ui.term);if(term&&!ui.search)ui.search=term.name;}ui.term=JunnaFilters.prune(ui.term,state.data.terms.filter(t=>t.kind==='关键词').map(t=>t.id));}
    const count=(key,value)=>state.data[key].filter(e=>value(e)).length;let html='';
    if(ui.tab==='powers')html=powerView.sidebar();
    else if(ui.tab==='relics')html=relicView.sidebar();
    else if(ui.tab==='potions')html=potionView.sidebar();
    else if(ui.tab==='fits')html=fitPoolView.sidebar();
    else if(ui.tab==='events')html=eventView.sidebar();
    else if(ui.tab==='models')html=modelView.sidebar();
    else if(ui.tab==='flows')html=flowView.sidebar();
    else if(ui.tab==='overview')html='<div class="sidebar-section"><p class="sidebar-title">统计范围</p>'+[['cards','卡池'],['reserve','备用'],['trash','垃圾桶']].map(([scope,label])=>'<button type="button" class="side-item '+(ui.overviewScope===scope?'active':'')+'" data-overview-scope="'+scope+'" aria-pressed="'+(ui.overviewScope===scope)+'"><span>'+label+'</span><span>'+(ui.overviewAll?JunnaPoolReference.poolFaces(state.data,scope).length:count('cards',c=>cardTab(c)===scope&&c.pool))+'</span></button>').join('')+'<label class="check overview-check"><input type="checkbox" data-overview-all'+(ui.overviewAll?' checked':'')+'>全部牌面</label></div><fieldset class="sidebar-section overview-filters"><legend class="sidebar-title">分组要素</legend>'+OVERVIEW_DIMENSIONS.map(d=>'<label class="check overview-check"><input type="checkbox" data-overview-dimension="'+d.key+'"'+(overviewDimensions().some(selected=>selected.key===d.key)?' checked':'')+'>'+escape(d.label)+'</label>').join('')+'</fieldset>';
    else if(ui.tab==='mechanisms'){html='<div class="sidebar-section"><p class="sidebar-title">机制</p>'+state.data.mechanisms.map((m,i)=>'<button class="side-item '+(JunnaFilters.state(ui.mechanismFilter,m.uid)==='include'?'active':'')+'" data-mechanism-filter="'+m.uid+'" '+JunnaFilters.attributes(ui.mechanismFilter,m.uid,m.name)+'><span>'+escape(m.name)+'</span><span>'+String(i+1).padStart(2,'0')+'</span></button>').join('')+'<button class="side-item '+(ui.mechanismFilter==='all'?'active':'')+'" data-mechanism-filter="all" '+JunnaFilters.attributes(ui.mechanismFilter,'all','全部机制')+'><span>全部机制</span><span>'+state.data.mechanisms.length+'</span></button></div><div class="sidebar-bottom"><button class="button" data-action="meta">规则数据</button></div>';}
    else if(ui.tab==='styles')html='<div class="sidebar-section"><p class="sidebar-title">条目目录</p><button class="side-item" data-style-target="style-directory"><span>全部条目</span><span>'+state.data.styles.length+'</span></button>'+styleGroups().map((g,i)=>'<button class="side-item" data-style-target="style-category-'+i+'"><span>'+escape(g.name)+'</span><span>'+g.entries.length+'</span></button>').join('')+'</div>';
    else{html='<label class="field"><span class="field-label">搜索</span><input id="filter-search" type="search" value="'+escape(ui.search)+'" placeholder="'+(['cards','reserve','trash'].includes(ui.tab)?'卡名、牌面、词条':'名称、编号、约定文本')+'" autocomplete="off"></label>';
      if(ui.tab==='terms')html+='<div class="sidebar-section"><p class="sidebar-title">条目</p><button class="side-item '+(ui.kind==='all'?'active':'')+'" data-kind="all" '+JunnaFilters.attributes(ui.kind,'all','全部条目')+'><span>全部条目</span><span>'+state.data.terms.length+'</span></button>'+[...new Set([...KINDS,...state.data.terms.map(t=>t.kind)])].map(kind=>'<button class="side-item '+(JunnaFilters.state(ui.kind,kind)==='include'?'active':'')+'" data-kind="'+escape(kind)+'" '+JunnaFilters.attributes(ui.kind,kind,kind)+'><span>'+escape(kind)+'</span><span>'+count('terms',t=>t.kind===kind)+'</span></button>').join('')+'</div>';
      else{
        const filter=(label,key,values)=>'<label class="field"><span class="field-label">'+label+'</span><select data-filter="'+key+'">'+options(values,ui[key],'全部')+'</select></label>';
        const filterButtons=(label,key,values)=>'<fieldset class="field filter-group" data-filter-group="'+key+'"><legend class="field-label">'+label+'</legend><div class="filter-options">'+[{value:'all',label:'全部'},...values].map(option=>{const value=typeof option==='object'?option.value:option,name=typeof option==='object'?option.label:option;return '<button type="button" class="filter-option" data-filter="'+key+'" data-filter-value="'+escape(value)+'" '+JunnaFilters.attributes(ui[key],String(value),name)+'>'+escape(name)+'</button>';}).join('')+'</div></fieldset>';
        html+=filterButtons('卡池','cardPool',CARD_POOLS)+filterButtons('入池','pool',[{value:'pool',label:'入池牌'},{value:'granted',label:'不入池'}])+filterButtons('类型','type',TYPES)+filterButtons('稀有度','rarity',RARITIES)+filterButtons('费用','cost',['0','1','2','3+','X'])+filterButtons('流派','archetype',state.data.archetypes.map(a=>({value:a.id,label:a.name})))+flowView.positionFilters()+filterButtons('关键词','term',state.data.terms.filter(t=>t.kind==='关键词').map(t=>({value:t.id,label:t.name})))+'<div class="sidebar-bottom"><button class="button" data-action="clear-filters">重置筛选</button><button class="button" data-action="archetypes">流派管理</button></div>';
      }
    }if(['cards','reserve','trash'].includes(ui.tab))html='<fieldset class="field filter-group"><legend class="field-label">分类</legend><div class="filter-options">'+[['cards','卡池'],['reserve','备用'],['trash','垃圾桶']].map(([category,label])=>'<button type="button" class="filter-option" data-card-category="'+category+'" aria-pressed="'+(ui.tab===category)+'">'+label+'</button>').join('')+'</div></fieldset>'+html;$('sidebar').innerHTML=html;
  }
  function cardLinks(card){const link=(id,count=1)=>{const c=find('cards',id);return c?'<button type="button" class="text-button" data-open-card="'+escape(c.uid)+'">'+escape(c.name)+''+(count>1?' ×'+count:'')+(c.trashedAt?' · 已删除':'')+' ↗</button>':'';};return groupsFor(card).flatMap(g=>g.mainCardId===card.id?g.grantedCardIds.map(id=>link(id,g.grantedCardCounts[id])):[link(g.mainCardId)]).join('、');}
  function knowledgeEntries(group){return [group.mainCardId,...group.grantedCardIds].flatMap(id=>{const card=find('cards',id),copies=JunnaWorkbenchDomain.groupCardCount(group,id);return card&&(!card.trashedAt||ui.tab==='trash')?Array.from({length:copies},(_,copy)=>({card,count:1,copy,copies,key:card.uid+'::'+copy})):[];});}
  function knowledgeButton(card,slot,origin=card.uid){
    const source=find('cards',origin)||card,groups=groupsFor(source);if(!groups.length)return '';
    const entries=knowledgeEntries(groups[0]),entry=entries.find(item=>item.key===slot)||entries.find(item=>item.card.uid===card.uid);
    const copies=knowledgeCopies(card),label=copies>1?'知识×'+copies:'知识';
    const position=copies===1&&entry?.copies>1?'<span class="knowledge-copy-position">'+(entry.copy+1)+'/'+entry.copies+'</span>':'';
    const size=copies===1?'<span class="knowledge-size">'+(groups.length>1?groups.length+'组':entries.length+'张')+'</span>':'';
    return '<button type="button" class="knowledge-open" data-knowledge-card="'+escape(card.uid)+'" data-knowledge-slot="'+escape(entry?.key||'')+'" data-knowledge-origin="'+escape(origin)+'" aria-label="查看 '+escape(card.name)+' 的知识牌组'+(copies>1?'，'+label:'')+'"><svg viewBox="0 0 20 20" aria-hidden="true"><rect x="3" y="5" width="10" height="12" rx="1" transform="rotate(-12 8 11)"/><rect x="8" y="3" width="9" height="12" rx="1"/></svg><span>'+label+'</span>'+position+size+'</button>';
  }
  function cardStackHTML(card,slot,lazy=false,origin=card){const group=groupsFor(origin)[0],entries=group?knowledgeEntries(group):[],selected=slot||entries.find(entry=>entry.card.uid===card.uid)?.key,others=entries.filter(entry=>entry.key!==selected).slice(0,2);return '<div class="card-stack'+(others.length?' has-knowledge':'')+'">'+others.map((entry,i)=>'<div class="card-companion" style="--stack-index:'+i+'" aria-hidden="true">'+cardHTML(entry.card,ui.upgraded,lazy)+'</div>').join('')+'<button class="card-button" data-edit="cards" data-uid="'+card.uid+'" title="'+escape(card.name)+'" aria-label="'+(card.trashedAt?'查看 ':'编辑 ')+escape(card.name)+'">'+cardHTML(card,ui.upgraded,lazy)+'</button></div>';}
  function cardEntryHTML(origin,trash,index=0){
    const group=groupsFor(origin)[0],entries=group?knowledgeEntries(group):[];
    const selected=ui.selected?.collection==='cards'&&(ui.selected.cardOrigin||ui.selected.uid)===origin.uid;
    const slot=selected?knowledgeFaces.get(origin.uid):null;
    const entry=entries.find(item=>item.key===slot)||entries.find(item=>item.card.uid===origin.uid),face=entry?.card||origin;
    const identity=(face.uid===origin.uid?'':origin.name+' · ')+face.id;
    const status=face.trashedAt?'已删除':face.pool?'入池':'不入池';
    return '<article class="card-entry '+(selected?'selected':'')+'" data-card-origin="'+escape(origin.uid)+'" data-card-face="'+escape(face.uid)+'" data-card-slot="'+escape(entry?.key||'')+'">'+cardStackHTML(face,entry?.key,index>=4,origin)+'<div class="card-caption"><span>'+escape(identity)+' · '+escape(face.rarity)+'</span><span class="'+(face.partnerCardIds.length?'partner-marker':'')+'">'+status+'</span></div>'+knowledgeButton(face,entry?.key,origin.uid)+(trash&&face.trashedAt?'<button class="button trash-restore" data-restore-card="'+face.uid+'">恢复</button>':'')+'</article>';
  }
  function cycleKnowledgeEntry(entry,direction,focus=false){if(!entry.classList.contains('selected'))return;const origin=find('cards',entry.dataset.cardOrigin),group=origin&&groupsFor(origin)[0],entries=group?knowledgeEntries(group):[];if(entries.length<2)return;const index=Math.max(0,entries.findIndex(item=>item.key===entry.dataset.cardSlot)),next=entries[(index+direction+entries.length)%entries.length];openEditor('cards',next.card.uid,origin.uid,next.key);if(focus)$('work-area').querySelector('[data-card-origin="'+CSS.escape(origin.uid)+'"] .card-button')?.focus({preventScroll:true});}
  function knowledgeWheel(event,cycle){if(event.ctrlKey||(!event.deltaY&&!event.deltaX))return;event.preventDefault();const now=performance.now();if(now-knowledgeWheelAt<180)return;knowledgeWheelAt=now;cycle((event.deltaY||event.deltaX)>0?1:-1);}
  function cycleKnowledgePreview(direction,focus=false){const card=current(),group=ui.selected?.collection==='cards'&&card&&groupsFor(find('cards',ui.selected.cardOrigin)||card)[0],entries=group?knowledgeEntries(group):[];if(entries.length<2)return;let index=entries.findIndex(entry=>entry.key===ui.selected.cardSlot);if(index<0)index=entries.findIndex(entry=>entry.card.uid===card.uid);const next=entries[(index+direction+entries.length)%entries.length];openEditor('cards',next.card.uid,ui.selected.cardOrigin||card.uid,next.key);if(focus)$('preview').focus({preventScroll:true});}
  function renderKnowledge(focus=false){
    if(!knowledgeView)return;const group=find('partnerGroups',knowledgeView.group),entries=group?knowledgeEntries(group):[];if(!entries.length){$('knowledge-dialog').close();return;}
    let index=entries.findIndex(entry=>entry.key===knowledgeView.slot);if(index<0)index=Math.max(0,entries.findIndex(entry=>entry.card.uid===knowledgeView.card));knowledgeView.slot=entries[index].key;knowledgeView.card=entries[index].card.uid;
    const card=entries[index].card,source=find('cards',knowledgeView.source),groups=source?groupsFor(source):[group];
    $('knowledge-total').textContent=entries.reduce((sum,entry)=>sum+entry.count,0)+'张';
    $('knowledge-group-switch').hidden=groups.length<2;$('knowledge-group-switch').innerHTML=groups.map(g=>'<option value="'+escape(g.uid)+'"'+(g.uid===group.uid?' selected':'')+'>'+escape(g.title||find('cards',g.mainCardId)?.name||g.id)+'</option>').join('');
    $('knowledge-upgrade').textContent=knowledgeView.upgraded?'升级牌面':'基础牌面';$('knowledge-upgrade').setAttribute('aria-pressed',String(knowledgeView.upgraded));
    $('knowledge-edit').textContent=(card.trashedAt?'查看 ':'编辑 ')+card.name;
    $('knowledge-fan').innerHTML=entries.map((entry,i)=>'<button type="button" class="knowledge-card'+(i===index?' is-focused':'')+(entry.card.trashedAt?' is-trashed':'')+'" style="--fan-angle:'+Math.max(-10,Math.min(10,(i-index)*5))+'deg;--fan-layer:'+(entries.length-Math.abs(i-index))+'" data-knowledge-focus="'+escape(entry.key)+'" data-knowledge-model="'+escape(entry.card.uid)+'" aria-pressed="'+(i===index)+'" aria-label="'+escape(entry.card.name)+(entry.copies>1?'，第'+(entry.copy+1)+'张，共'+entry.copies+'张':'')+'，'+escape(knowledgeView.upgraded?entry.card.upgradedText:entry.card.text)+'">'+cardHTML(entry.card,knowledgeView.upgraded)+'<span class="knowledge-card-count">'+(entry.copies>1?(entry.copy+1)+' / '+entry.copies:'×1')+(entry.card.trashedAt?' · 已删除':'')+'</span></button>').join('');
    fitCards();if(focus){const button=$('knowledge-fan').querySelector('[data-knowledge-focus="'+CSS.escape(knowledgeView.slot)+'"]');button?.focus({preventScroll:true});button?.scrollIntoView({block:'nearest',inline:'nearest'});}
  }
  function openKnowledge(key,slot,origin=key){const card=find('cards',key),source=find('cards',origin)||card,group=source&&groupsFor(source)[0];if(!group)return;flushEdit();knowledgeView={source:source.uid,card:card.uid,slot,origin,group:group.uid,upgraded:ui.upgraded};renderKnowledge();if(!$('knowledge-dialog').open)$('knowledge-dialog').showModal();fitCards();}
  function openLinkedCard(key){flushEdit();const card=find('cards',key);if(!card)return;selectTab(cardTab(card));ui.search='';ui.cardPool=ui.type=ui.rarity=ui.pool=ui.cost=ui.archetype=ui.routePosition=ui.term=ui.breakthrough='all';ui.routePositions=[];ui.routeExcludedPositions=[];changeTab(ui.tab);openEditor('cards',card.uid);renderAll();document.querySelector('.card-button[data-uid="'+CSS.escape(card.uid)+'"]')?.closest('.card-entry')?.scrollIntoView({block:'nearest',inline:'nearest'});}
  function styleGroups(){
    const names=[...new Set([...STYLE_CATEGORIES,...state.data.styles.map(s=>s.category||'未分类')])];
    return names.map(name=>({name,entries:state.data.styles.filter(s=>(s.category||'未分类')===name)})).filter(g=>g.entries.length);
  }
  function renderStyles(){
    const groups=styleGroups(),opened=new Set([...document.querySelectorAll('.style-category[open]')].map(node=>node.dataset.styleCategory));
    if(!groups.length)return '<p class="empty">暂无条目</p>';
    const jump=(name,target)=>'<button type="button" data-style-target="'+escape(target)+'">'+escape(name)+'</button>';
    const directory='<nav class="style-directory" id="style-directory" aria-labelledby="style-directory-title" tabindex="-1"><h2 id="style-directory-title">条目目录</h2><div class="style-directory-groups">'+groups.map((g,i)=>'<div><h3>'+jump(g.name,'style-category-'+i)+'<span>'+g.entries.length+'</span></h3><ul>'+g.entries.map(s=>'<li>'+jump(s.name,'style-entry-'+s.uid)+'</li>').join('')+'</ul></div>').join('')+'</div></nav>';
    return directory+groups.map((g,i)=>'<details class="style-category" id="style-category-'+i+'" data-style-category="'+escape(g.name)+'"'+(opened.has(g.name)||g.entries.some(s=>ui.selected?.uid===s.uid)?' open':'')+'><summary><h2>'+escape(g.name)+'</h2><span>'+g.entries.length+' 条</span></summary>'+g.entries.map(s=>'<section id="style-entry-'+escape(s.uid)+'" tabindex="-1" class="mechanism-sheet style-sheet '+(ui.selected?.uid===s.uid?'sheet-active':'')+'"><div class="sheet-body"><h3>'+escape(s.name)+'</h3><p class="sheet-definition style-content">'+escape(s.content)+'</p></div><button class="button" data-edit="styles" data-uid="'+escape(s.uid)+'">编辑</button></section>').join('')+'</details>').join('');
  }
  function jumpStyle(target){
    const node=document.getElementById(target);if(!node)return;
    const group=node.closest('.style-category');if(group)group.open=true;
    const focus=node.matches('details')?node.querySelector('summary'):node;
    focus.focus({preventScroll:true});node.scrollIntoView({block:'start'});
  }
  const CARD_SORTS=[{value:'natural',label:'自然排序'},{value:'id',label:'编号'},{value:'name',label:'名称'},{value:'cost',label:'费用'},{value:'rarity',label:'稀有度'},{value:'type',label:'类型'},{value:'turns',label:'回合数'},{value:'updated',label:'最近编辑'}];
  function sortControls(){
    const select='<select class="mobile-sort-select" data-sort-select aria-label="排序">'+CARD_SORTS.map(option=>'<option value="'+option.value+'"'+(ui.sort===option.value?' selected':'')+'>'+option.label+'</option>').join('')+'</select>';
    return '<div class="sort-controls" role="group" aria-label="排序"><span class="sort-label">排序</span>'+select+CARD_SORTS.map(option=>'<button type="button" class="filter-option sort-option" data-sort-key="'+option.value+'" aria-pressed="'+(ui.sort===option.value)+'">'+option.label+'</button>').join('')+'<button type="button" class="filter-option sort-option" data-sort-direction aria-pressed="'+ui.descending+'" aria-label="排序方向：'+(ui.descending?'降序':'升序')+'"><span aria-hidden="true">'+(ui.descending?'↓':'↑')+'</span><span class="sort-direction-label">'+(ui.descending?'降序':'升序')+'</span></button></div>';
  }
  function changeSort(event){
    const select=event.type==='change'?event.target.closest('[data-sort-select]'):null;
    const button=event.target.closest('[data-sort-key],[data-sort-direction]');if(!select&&!button)return false;
    const key=select?select.value:button.dataset.sortKey;if(key&&!CARD_SORTS.some(option=>option.value===key))return false;
    flushEdit();if(key)ui.sort=key;else ui.descending=!ui.descending;
    const selector=select?'[data-sort-select]':key?'[data-sort-key="'+key+'"]':'[data-sort-direction]';
    renderWorkspace();saveUI();mobileLayout.scrollToTop();$('page-actions').querySelector(selector)?.focus({preventScroll:true});return true;
  }
  const TRASH_PAGE_SIZE=24;
  let trashPage=0,trashPageFilters='';
  function trashPageFilterKey(){return JSON.stringify(['search','type','rarity','cardPool','pool','cost','archetype','term','routePositions','routeExcludedPositions','sort','descending','upgraded'].map(key=>ui[key]));}
  function trashPageCards(list){
    const filters=trashPageFilterKey();if(trashPageFilters!==filters){trashPage=0;trashPageFilters=filters;}
    trashPage=Math.max(0,Math.min(trashPage,Math.ceil(list.length/TRASH_PAGE_SIZE)-1));
    return list.slice(trashPage*TRASH_PAGE_SIZE,(trashPage+1)*TRASH_PAGE_SIZE);
  }
  function trashPaginationHTML(total){
    const pages=Math.ceil(total/TRASH_PAGE_SIZE);if(pages<=1)return '';
    return '<nav class="trash-pagination" aria-label="垃圾桶分页" tabindex="-1"><button type="button" class="button" data-trash-page="-1"'+(trashPage===0?' disabled':'')+'>上一页</button><span>'+(trashPage+1)+' / '+pages+'</span><button type="button" class="button" data-trash-page="1"'+(trashPage===pages-1?' disabled':'')+'>下一页</button></nav>';
  }
  function renderWorkspace(){
    if(ui.tab==='initial'){initialView.render();updateChrome();return;}
    if(ui.tab==='powers'){powerView.workspace();updateChrome();return;}
    if(ui.tab==='relics'){relicView.workspace();$('sidebar').innerHTML=relicView.sidebar();updateChrome();return;}
    if(ui.tab==='potions'){potionView.workspace();updateChrome();return;}
    if(ui.tab==='fits'){fitPoolView.workspace();updateChrome();return;}
    if(ui.tab==='events'){eventView.workspace();updateChrome();return;}
    if(ui.tab==='models'){modelView.workspace();updateChrome();return;}
    if(ui.tab==='flows'){flowView.workspace();updateChrome();return;}
    if(ui.tab==='overview'){const stats=overviewStats();$('page-title').textContent='总览';$('result-count').textContent=stats.cards.length+' 张牌面'+(ui.overviewAll?' · '+stats.pool.length+' 张入池 · '+stats.nonPool.length+' 张不入池':' · 入池牌');$('work-area').setAttribute('aria-labelledby','tab-overview');$('page-actions').innerHTML='';$('work-area').innerHTML=renderOverview();updateChrome();return;}
    let html='';const title={mechanisms:'机制',terms:'效果与扳机',cards:'卡池',reserve:'卡池',styles:'风格约定',trash:'卡池'}[ui.tab];$('page-title').textContent=title;$('work-area').setAttribute('aria-labelledby','tab-'+(['reserve','trash'].includes(ui.tab)?'cards':ui.tab));$('page-actions').innerHTML=(['cards','reserve','trash'].includes(ui.tab)?sortControls()+'<button class="button '+(ui.upgraded?'active':'')+'" data-action="upgrade" aria-pressed="'+ui.upgraded+'">'+(ui.upgraded?'升级牌面':'基础牌面')+'</button>':'')+(ui.tab==='trash'?'':'<button class="button primary" data-action="add">新增'+(ui.tab==='mechanisms'?'机制':ui.tab==='terms'||ui.tab==='styles'?'条目':'卡牌')+'</button>');
    if(ui.tab==='mechanisms'){const list=state.data.mechanisms.filter(m=>JunnaFilters.matches(ui.mechanismFilter,m.uid));$('result-count').textContent=state.data.mechanisms.length+' 个机制';html=list.map((m,i)=>'<section class="mechanism-sheet '+(ui.selected?.uid===m.uid?'sheet-active':'')+'"><span class="sheet-number">'+String(state.data.mechanisms.indexOf(m)+1).padStart(2,'0')+'</span><div class="sheet-body"><h2>'+escape(m.name)+'</h2><span class="sheet-level">'+escape(m.level)+'</span><p class="sheet-definition">'+escape(m.definition)+'</p><ul class="sheet-rules">'+m.rules.slice(0,3).map(r=>'<li>'+escape(r)+'</li>').join('')+'</ul>'+(m.rules.length>3?'<details><summary>'+m.rules.length+' 条规则</summary><ul class="sheet-rules">'+m.rules.slice(3).map(r=>'<li>'+escape(r)+'</li>').join('')+'</ul></details>':'')+'<div class="sheet-template">'+m.templates.map(escape).join('<br>')+'</div></div><button class="button" data-edit="mechanisms" data-uid="'+m.uid+'">编辑</button></section>').join('');}
    else if(ui.tab==='styles'){$('result-count').textContent=state.data.styles.length+' 条 · '+styleGroups().length+' 类';html=renderStyles();}
    else if(ui.tab==='terms'){const list=filteredTerms();$('result-count').textContent=list.length+' / '+state.data.terms.length+' 个条目';html='<table class="term-table"><thead><tr><th style="width:24%">名称</th><th style="width:12%">类别</th><th style="width:56%">卡面约定</th><th style="width:8%">卡牌</th></tr></thead><tbody>'+list.map(t=>'<tr tabindex="0" data-term="'+t.uid+'" class="'+(ui.selected?.uid===t.uid?'selected':'')+'"><td><span class="term-name">'+escape(t.name)+'</span><span class="term-id">'+escape(t.id)+'</span></td><td>'+escape(t.kind)+'</td><td class="term-template">'+escape(t.template)+'</td><td class="term-count">'+state.data.cards.filter(c=>cardTab(c)==='cards'&&c.termIds.includes(t.id)).length+'</td></tr>').join('')+'</tbody></table>';if(!list.length)html='<p class="empty">没有匹配的条目</p>';}
    else{const list=filteredCards(),trash=ui.tab==='trash',visible=trash?trashPageCards(list):list,pagination=trash?trashPaginationHTML(list.length):'';$('result-count').textContent=trash?list.length+' 张卡牌':ui.tab==='reserve'?list.length+' 张牌面 · 共 '+state.data.cards.filter(c=>cardTab(c)==='reserve').length+' 张':list.length+' 张牌面 · '+list.filter(c=>c.pool).length+' 张入池 · 共 '+state.data.cards.filter(c=>cardTab(c)==='cards').length+' 张';html=pagination+'<div class="card-grid">'+visible.map((c,index)=>cardEntryHTML(c,trash,index)).join('')+'</div>'+pagination;if(!list.length)html='<p class="empty">'+(trash&&!state.data.cards.some(c=>cardTab(c)==='trash')?'垃圾桶为空':'没有匹配的卡牌')+'</p>';}
    $('work-area').innerHTML=html;fitCards();updateChrome();
  }
  function renderAll(){ui.archetype=JunnaFilters.prune(ui.archetype,state.data.archetypes.map(route=>route.id));ui.flowRoute=JunnaFilters.prune(ui.flowRoute,state.data.archetypes.map(route=>route.uid));if(ui.flowDrill&&!state.data.archetypes.some(route=>route.uid===ui.flowDrill.route))ui.flowDrill=null;document.querySelectorAll('[data-tab]').forEach(b=>b.setAttribute('aria-selected',String(b.dataset.tab===(['reserve','trash'].includes(ui.tab)?'cards':ui.tab))));$('body-layout').classList.toggle('is-initial',ui.tab==='initial');$('body-layout').classList.toggle('is-overview',ui.tab==='overview');$('body-layout').classList.toggle('is-flow',ui.tab==='flows');$('body-layout').classList.toggle('is-models',ui.tab==='models');$('body-layout').classList.toggle('is-powers',ui.tab==='powers');$('body-layout').classList.toggle('is-relics',['relics','potions'].includes(ui.tab));$('body-layout').classList.toggle('is-fit-pool',['fits','events'].includes(ui.tab));if(!['fits','events'].includes(ui.tab))$('body-layout').classList.remove('has-fit-selection');renderSidebar();renderWorkspace();renderInspector();saveUI();}

  function openEditor(collection,key,origin=key,slot){flushEdit();const e=find(collection,key);if(collection!=='root'&&!e)return;ui.selected={collection,uid:e?.uid||key};if(collection==='cards'){ui.selected.cardOrigin=find('cards',origin)?.uid||e.uid;const group=groupsFor(find('cards',ui.selected.cardOrigin)||e)[0],entries=group?knowledgeEntries(group):[],entry=entries.find(item=>item.key===slot)||entries.find(item=>item.card.uid===e.uid);if(entry){ui.selected.cardSlot=entry.key;knowledgeFaces.set(ui.selected.cardOrigin,entry.key);}if(ui.tab==='trash'){const index=filteredCards().findIndex(card=>card.uid===ui.selected.cardOrigin);if(index>=0){trashPage=Math.floor(index/TRASH_PAGE_SIZE);trashPageFilters=trashPageFilterKey();}}}renderInspector();renderWorkspace();saveUI();}
  function closeEditor(){flushEdit();ui.selected=null;renderInspector();renderWorkspace();saveUI();}
  function editorSection(key,title,content,count){return '<details class="form-details editor-section" data-editor-section="'+key+'"><summary>'+escape(title)+(count===undefined?'':'<span class="editor-section-count">'+count+'</span>')+'</summary>'+content+'</details>';}
  function mergePickedIds(existing,visible,selected){const shown=new Set(visible);return [...new Set([...existing.filter(id=>!shown.has(id)),...selected])];}
  function cardTagPicker(title,key,entries,selected){
    return '<fieldset class="card-tag-picker"><legend class="field-label">'+title+'</legend><div class="position-options">'+entries.map(entry=>'<label class="position-tag"><input type="checkbox" data-pick="'+key+'" value="'+escape(entry.id)+'"'+(selected.includes(entry.id)?' checked':'')+'><span>'+escape(entry.name)+'</span></label>').join('')+'</div></fieldset>';
  }
  function checkedPicker(title,key,entries,selected){return '<div class="field"><span class="field-label">'+title+' · '+selected.length+'</span><input class="filter-search" type="search" data-tag-search="'+key+'" placeholder="搜索'+title+'" aria-label="搜索'+title+'"><div class="check-list" data-check-list="'+key+'">'+entries.map(e=>'<label class="check" data-search-text="'+escape((e.name+' '+e.id).toLowerCase())+'"><input type="checkbox" data-pick="'+key+'" value="'+escape(e.id)+'"'+(selected.includes(e.id)?' checked':'')+'><span>'+escape(e.name)+' <span class="term-id">'+escape(e.id)+'</span></span></label>').join('')+'</div></div>';}
  function groupsFor(card){return state.data.partnerGroups.filter(g=>g.mainCardId===card.id);}
  function grantOptions(card,search=''){
    const group=groupsFor(card)[0],query=search.trim().toLowerCase();
    return '<option value="">选择卡牌</option>'+state.data.cards.filter(candidate=>!candidate.trashedAt&&candidate.id!==card.id&&!group?.grantedCardIds.includes(candidate.id)&&(!query||(candidate.name+' '+candidate.id).toLowerCase().includes(query))).map(candidate=>'<option value="'+escape(candidate.id)+'">'+escape(candidate.name)+' · '+escape(candidate.id)+'</option>').join('');
  }
  function relatedHTML(card){
    const mainGroup=groupsFor(card)[0];let html='<div class="section-caption"><span>关联牌</span></div>';
    for(const id of [card.id,...(mainGroup?.grantedCardIds||[])]){
      const c=state.data.cards.find(c=>c.id===id),self=id===card.id,count=JunnaWorkbenchDomain.groupCardCount(mainGroup||{mainCardId:card.id},id);
      const name=self?'<span>'+escape(c.name)+' · 自身</span>':'<button type="button" class="text-button" data-open-card="'+c.uid+'">'+escape(c.name)+(c.trashedAt?' · 已删除':'')+' ↗</button>';
      const remove=self?'':'<button type="button" class="icon-btn" data-remove-grant="'+escape(id)+'" aria-label="移除关联 '+escape(c.name)+'">×</button>';
      html+='<div class="related-entry"><div class="related-heading">'+name+'<input type="number" min="1" step="1" value="'+count+'" data-grant-count="'+escape(id)+'" aria-label="'+escape(c.name)+(self?'自身数量':'发放数量')+'">'+remove+'</div><div class="related-meta">'+escape(c.cost)+'费 · '+escape(c.type)+' · '+(c.pool?'入池':'不入池')+'</div><p>'+energyText(c.text,c)+'</p></div>';
    }
    html+='<div class="inline-actions"><button type="button" class="button" data-action="add-grant">新增同组牌</button></div><div class="field"><span class="field-label">关联已有牌</span><input class="filter-search" type="search" data-grant-search aria-label="搜索关联牌" placeholder="牌名或编号" autocomplete="off"><select data-add-grant aria-label="关联已有牌">'+grantOptions(card)+'</select></div>';
    return html;
  }
  function arrowFocusEditor(card){
    const focus=JunnaArrowFocus.normalize(card.arrowFocus),clip='arrow-focus-'+card.uid;
    return '<section class="arrow-focus-editor"><span class="field-label">箭头聚焦</span><div class="arrow-focus-images"><button type="button" class="arrow-focus-source" data-arrow-focus-pick aria-label="设置箭头聚焦"><img data-arrow-focus-source src="'+assetURL(card.art)+'" alt="'+escape(card.name)+'"><span data-arrow-focus-marker style="left:'+focus.x*100+'%;top:'+focus.y*100+'%"></span></button><svg class="arrow-focus-result" viewBox="0 0 240 416" role="img" aria-label="箭头预览"><defs><clipPath id="'+clip+'"><polygon points="'+JunnaArrowFocus.windowPoints+'"/></clipPath></defs><g clip-path="url(#'+clip+')"><image data-arrow-focus-image href="'+assetURL(card.art)+'" preserveAspectRatio="none"/></g><image href="'+assetURL('floating-arrow-frame')+'" width="240" height="416"/><text x="120" y="81" text-anchor="middle" fill="#fff6e2" font-size="65">'+escape((ui.upgraded?card.upgradedTurns:card.turns)??'')+'</text></svg></div><div class="two-fields">'+field('横向（%）','arrowFocus.x',Math.round(focus.x*1000)/10,'type="number" min="0" max="100" step="0.1"')+field('纵向（%）','arrowFocus.y',Math.round(focus.y*1000)/10,'type="number" min="0" max="100" step="0.1"')+'</div><button type="button" class="button" data-action="center-arrow-focus">居中</button></section>';
  }
  function refreshArrowFocus(){
    const card=current(),source=$('editor-form').querySelector('[data-arrow-focus-source]'),image=$('editor-form').querySelector('[data-arrow-focus-image]'),marker=$('editor-form').querySelector('[data-arrow-focus-marker]');
    if(!card||!source||!image)return;
    const focus=JunnaArrowFocus.normalize(card.arrowFocus);marker.style.left=focus.x*100+'%';marker.style.top=focus.y*100+'%';
    if(source.naturalWidth)for(const [key,value]of Object.entries(JunnaArrowFocus.layout(source.naturalWidth,source.naturalHeight,focus)))image.setAttribute(key,value);
  }
  function renderInspector(){
    if(ui.tab==='potions'&&!ui.selected){$('inspector').hidden=true;$('preview').innerHTML='';$('editor-form').innerHTML='';$('inspector-footer').hidden=true;$('close-inspector').hidden=true;mobileLayout.update();return;}
    if(ui.tab==='initial'){$('inspector').hidden=true;$('editor-form').innerHTML='';return;}
    mobileLayout.update();
    if(ui.tab==='powers'&&ui.selected?.collection!=='root'){powerView.inspector();return;}
    if(ui.tab==='relics'&&ui.selected?.collection!=='root'){relicView.inspector();return;}
    if(ui.tab==='models'){modelView.inspector();return;}
    if(ui.tab==='flows'&&!ui.selected){flowView.inspector();return;}
    if(ui.tab==='overview'){renderOverviewInspector();return;}
    if(['fits','events'].includes(ui.tab)&&ui.selected?.collection!=='fitPool'){$('inspector').hidden=true;$('preview').innerHTML='';$('editor-form').innerHTML='';return;}
    const selected=ui.selected,root=selected?.collection==='root',e=current(),trash=selected?.collection==='cards'&&Boolean(e?.trashedAt);
    const sameCardEditor=selected?.collection==='cards'&&$('editor-form').dataset.card===selected.uid,openSections=sameCardEditor?[...$('editor-form').querySelectorAll('[data-editor-section][open]')].map(section=>section.dataset.editorSection):[];
    $('inspector').hidden=false;$('preview').innerHTML='';$('preview').removeAttribute('tabindex');$('preview').removeAttribute('aria-label');$('editor-form').innerHTML='';$('close-inspector').hidden=!selected;$('inspector-footer').hidden=!selected||root;
    if(!selected||(!root&&!e)){$('inspector-title').textContent='编辑';$('close-inspector').hidden=true;$('inspector-footer').hidden=true;return;}
    $('duplicate-entity').hidden=root||trash;$('delete-entity').hidden=root||trash;$('restore-entity').hidden=!trash;$('reserve-entity').hidden=root||trash||selected.collection!=='cards';$('reserve-entity').textContent=e?.reservedAt?'移回卡池':'移入备用';
    const groupContext=!root&&!trash&&selected.collection==='cards'?JunnaWorkbenchDomain.cardGroup(state.data,e.uid,selected.cardOrigin):null,isMain=Boolean(groupContext&&e.pool);
    $('card-group-actions').hidden=!groupContext;$('reserve-group').textContent=groupContext?.reserveAction==='pool'?'移回一组':'备用一组';
    $('set-group-main').textContent=isMain?'当前主牌':'设置为主牌';$('set-group-main').disabled=!groupContext||isMain;$('set-group-main').dataset.groupUid=groupContext?.group.uid||'';
    $('inspector-title').textContent=trash?'已删除卡牌':root?(selected.uid==='title'?'制作台名称':'规则数据'):'编辑'+LABELS[selected.collection];
    let html='';
    if(root){html=selected.uid==='title'?field('名称','root.title',state.data.title,'class="name-input" data-on="change"'):area('规则数据','rawMeta',JSON.stringify(state.data.meta,null,2),22,'class="code-field"')+'<button type="button" class="button primary" data-action="apply-meta">保存规则数据</button>';}
    else if(selected.collection==='cards'){
      $('preview').innerHTML=cardHTML(e)+knowledgeButton(e,selected.cardSlot,selected.cardOrigin||e.uid);
      if(groupsFor(find('cards',ui.selected.cardOrigin)||e).length){$('preview').tabIndex=0;$('preview').setAttribute('aria-label','知识牌预览');}
      html=field('牌名','name',e.name,'class="name-input" data-on="change"')+'<div class="two-fields">'+selectField('类型','type',TYPES,e.type)+selectField('稀有度','rarity',RARITIES,e.rarity)+'</div><div class="two-fields">'+field('费用','cost',e.cost,'inputmode="text"')+field('升级费用','upgradedCost',e.upgradedCost,'inputmode="text"')+'</div>'+(e.type==='箭矢'?'<div class="two-fields">'+field('回合数','turns',e.turns,'inputmode="text"')+field('升级回合数','upgradedTurns',e.upgradedTurns,'inputmode="text"')+'</div>':'')+field('目标','target',e.target||'自己')+area('基础牌面','text',e.text,4)+area('升级牌面','upgradedText',e.upgradedText,4)+editorSection('classification','分类与定位',selectField('卡池','cardPool',CARD_POOLS,e.cardPool||'角色')+selectField('入池','pool',[{value:'true',label:'入池牌'},{value:'false',label:'不入池'}],String(e.pool))+cardTagPicker('流派','archetypeIds',state.data.archetypes,e.archetypeIds)+cardTagPicker('关键词','termIds',state.data.terms.filter(term=>term.kind==='关键词'),e.termIds)+flowView.fields(e))+editorSection('related','关联牌',relatedHTML(e),1+(groupsFor(e)[0]?.grantedCardIds.length||0))+'<details class="form-details" data-editor-section="art"><summary>插画与出处</summary><div class="inline-actions"><button type="button" class="button" data-action="upload-art">选择图片</button><button type="button" class="button" data-action="reset-art">默认图片</button></div>'+arrowFocusEditor(e)+field('原词','naming.original',e.naming.original||'')+field('来源','naming.work',e.naming.work||'')+field('来源链接','naming.url',e.naming.url||'','type="url"')+field('采用方式','naming.adaptation',e.naming.adaptation||'')+'</details>'+(e.keywords.includes('知识')?editorSection('knowledge-background','知识背景',area('内容','knowledgeBackground',JunnaCardHoverTips.background(e),3)):'')+editorSection('notes','备注',area('备注','notes',e.notes||'',2));
    }else if(selected.collection==='terms'){
      const kinds=[...new Set([...KINDS,...state.data.terms.map(t=>t.kind)])];
      html=field('名称','name',e.name,'class="name-input" data-on="change"')+'<div class="two-fields">'+field('编号','id',e.id,'data-on="change"')+selectField('类别','kind',kinds,e.kind)+'</div>'+area('卡面约定','template',e.template,4)+area('规则定义','definition',e.definition,6)+field('单位','unit',e.unit||'')+field('适用体系','systems',(e.systems||[]).join('，'))+area('备注','notes',e.notes||'',3)+'<div class="inline-actions"><button type="button" class="button" data-action="copy-template">复制约定文本</button><button type="button" class="button" data-action="cards-by-term">关联卡牌</button></div>';
      if(e.upgradedTemplate!==undefined)html+=area('升级约定','upgradedTemplate',e.upgradedTemplate,3);
      if(e.sources)html+=field('来源','sources',Array.isArray(e.sources)?e.sources.join('，'):e.sources);
    }else if(selected.collection==='fitPool')html=JunnaPotions.isPotion(e)?potionView.editor(e):JunnaEvents.isEvent(e)?eventView.editor(e):fitPoolView.editor(e);
    else if(selected.collection==='styles')html=field('名称','name',e.name,'class="name-input" data-on="change"')+selectField('分类','category',[...new Set([...STYLE_CATEGORIES,...styleGroups().map(g=>g.name)])],e.category||'未分类')+area('内容','content',e.content,8);
    else if(selected.collection==='mechanisms')html=field('名称','name',e.name,'class="name-input" data-on="change"')+'<div class="two-fields">'+field('编号','id',e.id,'data-on="change"')+field('层级','level',e.level||'')+'</div>'+area('机制定义','definition',e.definition,5)+area('规则','rules',e.rules.join('\n'),13)+area('描述约定','templates',e.templates.join('\n'),6)+area('备注','notes',e.notes||'',4);
    else if(selected.collection==='archetypes')html=field('名称','name',e.name,'class="name-input" data-on="change"')+field('编号','id',e.id,'data-on="change"')+area('结构与配合','definition',e.definition,7)+field('主体系','system',e.system||'');
    if(!root&&selected.collection!=='cards'){const raw=clone(e);delete raw.uid;html+='<details class="form-details"><summary>结构数据</summary><label class="field"><textarea class="code-field" data-raw-entity rows="14">'+escape(JSON.stringify(raw,null,2))+'</textarea></label><button type="button" class="button" data-action="apply-raw">应用结构数据</button></details>';}
    $('editor-form').innerHTML=html;$('editor-form').dataset.card=selected.collection==='cards'?selected.uid:'';for(const section of $('editor-form').querySelectorAll('[data-editor-section]'))section.open=openSections.includes(section.dataset.editorSection)||(!sameCardEditor&&section.dataset.editorSection==='classification');if(trash)for(const input of $('editor-form').querySelectorAll('input,textarea,select,button'))input.disabled=true;fitCards();refreshArrowFocus();
  }
  function nextId(prefix,list){let n=1;while(list.some(e=>e.id===prefix+String(n).padStart(3,'0')))n++;return prefix+String(n).padStart(3,'0');}
  function newCard(pool=true){return {uid:uid(),id:nextId('U_CARD_',state.data.cards),name:pool?'新卡牌':'新知识牌',cost:1,upgradedCost:1,type:'技能',rarity:'普通',cardPool:'角色',turns:null,upgradedTurns:null,text:'',upgradedText:'',system:'stage',pool,target:'自己',keywords:[],termIds:[],archetypeIds:['SYS_stage'],partnerCardIds:[],effectIds:[],ruleIds:[],breakthroughIds:[],naming:{},art:'defaultPortrait',notes:''};}
  function renameId(collection,entity,next){const old=entity.id;entity.id=next;if(collection==='cards')for(const fit of state.data.fitPool)fit.cardIds=fit.cardIds.map(id=>id===old?next:id);
    if(collection==='cards'){
      for(const group of state.data.partnerGroups){if(group.mainCardId===old)group.mainCardId=next;group.grantedCardIds=group.grantedCardIds.map(id=>id===old?next:id);if(Object.hasOwn(group.grantedCardCounts||{},old)){group.grantedCardCounts[next]=group.grantedCardCounts[old];delete group.grantedCardCounts[old];}}
      for(const t of state.data.terms)if(t.cardId===old)t.cardId=next;
      for(const item of state.data.meta.startingDeck||[]){if(item.cardId===old)item.cardId=next;if(item.fromPartner===old)item.fromPartner=next;}
    }else if(collection==='terms'){for(const c of state.data.cards)for(const key of ['termIds','effectIds','ruleIds'])if(c[key])c[key]=c[key].map(id=>id===old?next:id);}
    else if(collection==='archetypes'){for(const c of state.data.cards)c.archetypeIds=c.archetypeIds.map(id=>id===old?next:id);}
  }
  function renameCard(entity,next){const old=entity.name;if(old&&old!==next)for(const card of state.data.cards){const references=JunnaCardReferences.referencesFor(card,state.data.cards);if(!references.some(target=>target.uid===entity.uid))continue;for(const key of ['text','upgradedText'])card[key]=JunnaCardReferences.renameInText(card[key],references,entity.uid,next);}entity.name=next;}
  function applyField(input){
    if(!previewMode&&(!mcpStatus.ready||mcpStatus.conflict||mcpStatus.resolving)){renderInspector();toast(mcpStatus.conflict?'请先处理版本冲突':mcpStatus.resolving?'MCP 保存中':'MCP 未连接');return;}
    const key=input.dataset.field;if(!key||key==='rawMeta')return;let value=input.value;const e=current();if(!e&&ui.selected?.collection!=='root')return;
    input.setCustomValidity?.('');
    if(['name','id'].includes(key)&&!value.trim()){input.setCustomValidity?.('请输入'+(key==='name'?'名称':'编号'));return;}
    if(key==='id'&&state.data[ui.selected.collection].some(other=>other.uid!==e.uid&&other.id===value)){input.setCustomValidity?.('编号已存在');return;}
    if(['cost','upgradedCost','turns','upgradedTurns'].includes(key)){const n=value.trim().toUpperCase();if(n!=='X'&&!/^\d+$/.test(n)){input.setCustomValidity?.('请输入非负整数或X');return;}value=n==='X'?'X':Number(n);}
    if(key.startsWith('arrowFocus.')){if(!value.trim()||!Number.isFinite(Number(value))||Number(value)<0||Number(value)>100){input.setCustomValidity('请输入0到100');return;}value=Number(value)/100;}
    if(['rules','templates'].includes(key))value=value.split(/\r?\n/).map(s=>s.trim()).filter(Boolean);
    if(['systems','sources','keywords'].includes(key))value=value.split(/[，,\n]/).map(s=>s.trim()).filter(Boolean);
    beginEdit();
    if(key==='root.title')state.data.title=String(value||'少女歌剧工作台');
    else if(key==='id')renameId(ui.selected.collection,e,value);
    else if(key==='name'&&ui.selected.collection==='cards')renameCard(e,value);
    else if(key.startsWith('arrowFocus.'))e.arrowFocus={...JunnaArrowFocus.normalize(e.arrowFocus),[key.slice(11)]:value};
    else if(key.startsWith('potion.')&&JunnaPotions.isPotion(e))JunnaPotions.setField(e,key,value);
    else if(key.startsWith('event.')&&JunnaEvents.isEvent(e))JunnaEvents.setField(e,key,value);
    else if(key.startsWith('naming.'))e.naming[key.slice(7)]=value;
    else if(key==='knowledgeBackground')JunnaCardHoverTips.setBackground(e,value);
    else if(key==='pool'){
      e.pool=value==='true';
    }else e[key]=value;
    if(ui.selected.collection==='fitPool'&&JunnaEvents.isEvent(e))JunnaEvents.sync(e);
    if(ui.selected.collection==='relics')relicView.syncFit(e,key);
    if(key==='type'){e.turns=value==='箭矢'?(e.turns??1):null;e.upgradedTurns=value==='箭矢'?(e.upgradedTurns??e.turns):null;}
    clearTimeout(editTimer);editTimer=setTimeout(flushEdit,650);$('save-state').textContent='编辑中';updateDiskState();
    if(ui.selected.collection==='cards'){$('preview').innerHTML=cardHTML(e);fitCards();}else if(ui.selected.collection==='fitPool')$('preview').innerHTML=e.art?'<img class="fit-inspector-image" src="'+assetURL(e.art)+'" alt="'+escape(e.name)+'">':'';
    refreshArrowFocus();updateChrome();if(['type','pool','cardPool'].includes(key)){flushEdit();renderInspector();renderSidebar();}
  }
  function addEntity(){const collection=ui.tab==='mechanisms'?'mechanisms':ui.tab==='terms'?'terms':ui.tab==='styles'?'styles':['fits','events','potions'].includes(ui.tab)?'fitPool':'cards';let e;
    if(collection==='cards'){e=newCard();if(ui.tab==='reserve')e.reservedAt=new Date().toISOString();}else if(collection==='fitPool'&&ui.tab==='potions')e=JunnaPotions.newEntry({uid:uid(),id:nextId('FIT_POTION_',state.data.fitPool),potionId:nextId('JP',state.data.fitPool.map(entry=>entry.potionDesign).filter(Boolean))});else if(collection==='fitPool'&&ui.tab==='events')e=JunnaEvents.newEntry({uid:uid(),id:nextId('FIT_EVENT_',state.data.fitPool),eventId:nextId('JE',state.data.fitPool.map(entry=>entry.eventDesign).filter(Boolean))});else if(collection==='fitPool')e=JunnaFitPool.normalizeEntry({uid:uid(),id:nextId('FIT_',state.data.fitPool),name:'新契合',theme:JunnaFilters.read(ui.fitTheme).included[0]||'未分类'});else if(collection==='styles')e={uid:uid(),id:nextId('STYLE_',state.data.styles),name:'新条目',content:''};else if(collection==='terms')e={uid:uid(),id:nextId('U_TERM_',state.data.terms),kind:JunnaFilters.read(ui.kind).included[0]||'效果',name:'新条目',definition:'',template:'',systems:[],unit:'',notes:''};else e={uid:uid(),id:nextId('U_ME_',state.data.mechanisms),name:'新机制',level:'',definition:'',rules:[],templates:[],notes:'',system:''};
    transact('新增'+(collection==='fitPool'&&JunnaEvents.isEvent(e)?'事件':LABELS[collection]),d=>{d[collection].push(e);ui.selected={collection,uid:e.uid};},'新增');if(collection==='fitPool'){ui.search='';ui.fitStatus='all';}if(collection==='cards'){ui.search='';ui.cardPool=ui.type=ui.rarity=ui.pool=ui.cost=ui.archetype=ui.routePosition=ui.term=ui.breakthrough='all';ui.routePositions=[];ui.routeExcludedPositions=[];}else if(collection==='mechanisms')ui.mechanismFilter='all';renderAll();$('editor-form').querySelector('[data-field="name"]')?.focus();}
  function duplicateEntity(){const e=current();if(!e)return;const collection=ui.selected.collection,copy={...clone(e),uid:uid(),id:nextId(collection==='cards'?'U_CARD_':collection==='terms'?'U_TERM_':collection==='styles'?'STYLE_':collection==='fitPool'?'FIT_':'U_ME_',state.data[collection]),name:e.name+'（副本）'};
    if(collection==='fitPool'&&JunnaEvents.isEvent(copy)){copy.eventDesign={...copy.eventDesign,uid:uid(),id:nextId('JE',state.data.fitPool.map(entry=>entry.eventDesign).filter(Boolean)),status:'draft'};copy.draftStatus='草稿';delete copy.implementation;JunnaEvents.sync(copy);}
    transact('复制'+LABELS[collection]+' · '+e.name,d=>{d[collection].push(copy);if(collection==='cards'){const group=d.partnerGroups.find(g=>g.mainCardId===e.id);if(group)d.partnerGroups.push({...clone(group),uid:uid(),id:'GROUP_'+copy.id,mainCardId:copy.id});}ui.selected={collection,uid:copy.uid};},'新增');}
  function deleteEntity(){const e=current();if(!e)return;const collection=ui.selected.collection;if(collection==='cards'){if(e.trashedAt)return;transact('移入垃圾桶 · '+e.name,()=>{e.trashedAt=new Date().toISOString();ui.selected=null;},'删除');toast('已移入垃圾桶');return;}transact('删除'+LABELS[collection]+' · '+e.name,d=>{d[collection]=d[collection].filter(x=>x.uid!==e.uid);if(collection==='terms')for(const c of d.cards)for(const key of ['termIds','effectIds','ruleIds'])if(c[key])c[key]=c[key].filter(id=>id!==e.id);ui.selected=null;},'删除');toast('已删除');}
  function restoreCard(key){const card=find('cards',key);if(!card?.trashedAt)return;transact('恢复卡牌 · '+card.name,()=>{delete card.trashedAt;ui.selected=null;},'恢复');toast('已恢复');}
  function moveCardReserve(key,reserved){const card=find('cards',key);if(!card||card.trashedAt)return;transact((reserved?'移入备用':'移回卡池')+' · '+card.name,d=>{JunnaWorkbenchDomain.moveCards(d,key,reserved?'reserve':'pool');ui.selected=null;});}
  function setKnowledgeMain(groupUid){
    const card=current();if(ui.selected?.collection!=='cards'||!card||card.trashedAt)return;
    transact('设置为主牌 · '+card.name,d=>{JunnaWorkbenchDomain.setGroupMain(d,groupUid,card.uid);ui.selected.cardOrigin=card.uid;ui.selected.cardSlot=card.uid+'::0';});
  }
  function moveCardGroup(action){
    const card=current(),selected=ui.selected;if(selected?.collection!=='cards'||!card||card.trashedAt)return;
    const group=JunnaWorkbenchDomain.cardGroup(state.data,card.uid,selected.cardOrigin);if(!group)return;
    action=action||group.reserveAction;
    const main=state.data.cards.find(entry=>entry.id===group.group.mainCardId),label={trash:'删除一组',reserve:'备用一组',pool:'移回一组'}[action];let count=0;
    const result=transact(label+' · '+(main?.name||card.name),d=>{count=JunnaWorkbenchDomain.moveCards(d,card.uid,action,{group:true,originUid:selected.cardOrigin}).length;ui.selected=null;},action==='trash'?'删除':'编辑');
    if(result)toast(action==='trash'?'已将'+count+'张牌移入垃圾桶':action==='reserve'?'已将'+count+'张牌移入备用':'已将'+count+'张牌移回卡池');
  }
  function changeKnowledgeCount(id,count){
    const card=current();if(!card||!Number.isSafeInteger(count)||count<1)return;
    transact('修改知识数量 · '+card.name,d=>{
      let group=d.partnerGroups.find(g=>g.mainCardId===card.id);
      if(id===card.id){
        if(!group){
          if(count===1)return;
          group={uid:uid(),title:card.name,mainCardId:card.id,grantedCardIds:[],grantedCardCounts:{}};
          d.partnerGroups.push(group);
        }
        if(count>1)group.mainCardCount=count;else delete group.mainCardCount;
      }else if(group?.grantedCardIds.includes(id))group.grantedCardCounts[id]=count;
    });
  }
  function addGrant(id){const main=current(),target=state.data.cards.find(c=>c.id===id);if(!main||!target||target.id===main.id||target.trashedAt)return;
    transact('关联知识牌 · '+target.name,d=>{let g=d.partnerGroups.find(g=>g.mainCardId===main.id);if(!g){g={uid:uid(),title:main.name,mainCardId:main.id,grantedCardIds:[],grantedCardCounts:{}};d.partnerGroups.push(g);}g.grantedCardIds=[...new Set([...g.grantedCardIds,id])];g.grantedCardCounts[id]=g.grantedCardCounts[id]||1;},'编辑');}
  function newGrant(){const main=current();if(!main)return;const c=newCard(false);c.system='partner';c.archetypeIds=['SYS_partner'];if(main.reservedAt)c.reservedAt=main.reservedAt;transact('新增同组牌 · '+main.name,d=>{d.cards.push(c);let g=d.partnerGroups.find(g=>g.mainCardId===main.id);if(!g){g={uid:uid(),title:main.name,mainCardId:main.id,grantedCardIds:[],grantedCardCounts:{}};d.partnerGroups.push(g);}g.grantedCardIds.push(c.id);g.grantedCardCounts[c.id]=1;ui.selected={collection:'cards',uid:c.uid};},'新增');}
  function changeTab(tab){flushEdit();clearTimeout(searchTimer);selectTab(tab);trashPage=0;if(ui.selected&&ui.selected.collection!==tab)ui.selected=null;saveUI(true);renderAll();mobileLayout.scrollToTop();}
  function showArchetypes(){flushEdit();renderArchetypes();$('manage-dialog').showModal();}
  function renderArchetypes(){$('manage-title').textContent='流派管理';$('manage-body').innerHTML='<div class="inline-actions"><button class="button primary" data-action="add-archetype">新增流派</button></div>'+state.data.archetypes.map(a=>'<section class="manage-entry" data-archetype="'+a.uid+'"><header><input data-archetype-field="name" value="'+escape(a.name)+'" aria-label="流派名称"><button class="icon-btn" data-delete-archetype="'+a.uid+'" aria-label="删除 '+escape(a.name)+'">×</button></header><textarea data-archetype-field="definition" rows="2" aria-label="流派结构">'+escape(a.definition)+'</textarea><div class="related-meta">'+state.data.cards.filter(c=>cardTab(c)==='cards'&&c.archetypeIds.includes(a.id)).length+' 张卡牌 <button class="text-button" data-edit-archetype="'+a.uid+'">编辑编号</button></div></section>').join('');}

  function changedFields(p){if(p.collection==='root')return [{key:p.uid,before:p.before,after:p.after}];const old=p.before||{},next=p.after||{};return [...new Set([...Object.keys(old),...Object.keys(next)])].filter(k=>k!=='uid'&&JSON.stringify(old[k])!==JSON.stringify(next[k])).map(key=>({key,before:old[key],after:next[key]}));}
  const printable=x=>x===undefined||x===null?'—':typeof x==='string'?x:typeof x==='boolean'?(x?'是':'否'):JSON.stringify(x,null,2);
  async function loadHistoryPage(reset=false){
    if(!mcp)return;
    if(historyLoading){if(reset)historyReloadPending=true;return;}
    if(reset)historyOffset=0;
    historyLoading=true;renderHistory();
    try{
      const search=$('history-search').value.trim(),kind=$('history-kind').value;
      const result=await mcp.call('workbench_history',{offset:historyOffset,limit:50,includePatches:true,search,...(kind==='all'?{}:{collection:kind})});
      const records=new Map(state.history.map(record=>[record.id,record]));
      for(const record of result.records)records.set(record.id,record);
      state.history=[...records.values()].sort((a,b)=>a.seq-b.seq);
      historyQueryTotal=result.total;if(!search&&kind==='all')state.historyTotal=result.total;
      historyOffset+=result.records.length;updateChrome();
    }catch(error){toast(error.message);}
    finally{historyLoading=false;if($('history-dialog').open)renderHistory();if(historyReloadPending){historyReloadPending=false;loadHistoryPage(true);}}
  }
  function renderHistory(){
    const q=$('history-search').value.trim().toLowerCase(),kind=$('history-kind').value,records=state.history.slice().reverse().filter(r=>(kind==='all'||r.patches.some(p=>p.collection===kind))&&(!q||JSON.stringify([r.label,r.kind,r.patches]).toLowerCase().includes(q)));
    $('history-total').textContent=Math.max(state.historyTotal||0,state.history.length)+' 条';$('history-body').innerHTML=records.length?records.map(r=>{const rows=r.patches.flatMap(p=>changedFields(p).map(f=>'<div class="history-diff"><span class="diff-key">'+escape(FIELD_LABELS[f.key]||f.key)+'</span><del>'+escape(printable(f.before))+'</del><ins>'+escape(printable(f.after))+'</ins></div>'));return '<article class="history-record"><header><div><h3>'+escape(r.label)+'</h3><time datetime="'+escape(r.at)+'">'+new Date(r.at).toLocaleString('zh-CN',{hour12:false,timeZone:'Asia/Shanghai'})+' · '+escape(r.kind)+' · #'+r.seq+'</time></div><div class="history-actions"><button class="button" data-restore="'+r.id+'" data-side="before">恢复修改前</button><button class="button" data-restore="'+r.id+'" data-side="after">恢复修改后</button></div></header>'+rows.slice(0,3).join('')+(rows.length>3?'<details><summary class="text-button">'+rows.length+' 项修改</summary>'+rows.slice(3).join('')+'</details>':'')+'</article>';}).join(''):'<p class="empty">'+(historyLoading?'载入中':'暂无记录')+'</p>';
    if(mcp&&(historyLoading||historyOffset<historyQueryTotal&&records.length<historyQueryTotal))$('history-body').insertAdjacentHTML('beforeend','<button class="button" data-more-history'+(historyLoading?' disabled':'')+'>'+(historyLoading?'载入中':'更多')+'</button>');
  }
  function checkHistoryEndpoint(record,side){if(!mcp)return;for(const p of record.patches){const value=p.collection==='root'?(p.uid==='meta'&&!Object.hasOwn(p[side]||{},'powers')&&!Object.hasOwn(p[side]||{},'relics')?rootEntity(state.data,p.uid):state.data[p.uid]):find(p.collection,p.uid)||null;const matches=p.collection!=='root'&&p.before&&p.after&&value?changedFields(p).every(({key})=>Object.hasOwn(value,key)===Object.hasOwn(p[side],key)&&JSON.stringify(value[key])===JSON.stringify(p[side][key])):JSON.stringify(value)===JSON.stringify(p[side]);if(!matches)throw Error('修改的字段已有后续变化，请在历史中合并恢复');}}
  function undo(){if(!previewMode&&(!mcpStatus.ready||mcpStatus.conflict||mcpStatus.resolving))return;flushEdit();const id=state.undoStack.pop(),r=state.history.find(x=>x.id===id);if(!r)return;const before=clone(state.data);try{checkHistoryEndpoint(r,'after');applyPatches(r.patches,'before',true);state.redoStack.push(id);if(!record(before,'撤销 · '+r.label,'撤销',false,id))persist();renderAll();if($('history-dialog').open)renderHistory();}catch(error){state.data=before;state.undoStack.push(id);toast(error.message);}}
  function redo(){if(!previewMode&&(!mcpStatus.ready||mcpStatus.conflict||mcpStatus.resolving))return;flushEdit();const id=state.redoStack.pop(),r=state.history.find(x=>x.id===id);if(!r)return;const before=clone(state.data);try{checkHistoryEndpoint(r,'before');applyPatches(r.patches,'after',true);state.undoStack.push(id);if(!record(before,'重做 · '+r.label,'重做',false,id))persist();renderAll();if($('history-dialog').open)renderHistory();}catch(error){state.data=before;state.redoStack.push(id);toast(error.message);}}
  function restoreRecord(id,side){const r=state.history.find(r=>r.id===id);if(!r)return;transact('恢复'+(side==='before'?'修改前':'修改后')+' · '+r.label,()=>applyPatches(r.patches,side),'恢复');renderHistory();toast('已恢复');}
  function modernize(text){return String(text??'')
    .replace(/额外触发一个以(?:该敌人|目标敌人)为目标的箭矢的效果/g,'额外触发目标的一个箭矢的效果')
    .replace(/(?:将)?以(?:该敌人|目标敌人)为目标的(一个|所有)?箭矢的回合数减少(\d+|X|N)/g,(_,a,n)=>'目标敌人的'+(a==='一个'?'一个':'')+'箭矢加速'+n+'回合')
    .replace(/(?:将)?以自己为目标的(一个|所有)?箭矢的回合数减少(\d+|X|N)/g,(_,a,n)=>'以自己为目标的'+(a==='一个'?'一个':'')+'箭矢加速'+n+'回合')
    .replace(/(?:将)?所有箭矢的回合数减少(\d+|X|N)/g,'箭矢加速$1回合')
    .replace(/(?:将)?(一个|其他)箭矢的回合数减少(\d+|X|N)/g,'$1箭矢加速$2回合')
    .replace(/将以该敌人为目标的所有箭矢的回合数变为0/g,'目标敌人的箭矢立即加速到完成')
    .replace(/将所有箭矢的回合数变为0/g,'箭矢立即加速到完成')
    .replace(/将一个箭矢的回合数变为0/g,'使一个箭矢立即加速到完成')
    .replace(/本回合，你打出的箭矢牌的(?:初始)?回合数变为0/g,'本回合，你打出的箭矢牌立即加速到完成')
    .replace(/使所有箭矢立即触发效果/g,'箭矢立即加速到完成')
    .replace(/使一个箭矢立即触发效果/g,'使一个箭矢立即加速到完成')
    .replace(/复制一个以该敌人为目标且回合数大于0的箭矢(?:，保留其目标和回合数)?/g,'复制目标的一个箭矢')
    .replace(/复制目标的一个回合数大于0的箭矢/g,'复制目标的一个箭矢')
    .replace(/你的箭矢牌获得重放/g,'箭矢牌获得重放')
    .replace(/攻击成长|伤害成长/g,'伤害加成');}
  function applyWording(data,rules){const format=text=>rules.reduce((value,[pattern,replacement])=>value.replace(new RegExp(pattern,'g'),replacement),String(text??''));for(const c of data.cards){c.text=format(c.text);c.upgradedText=format(c.upgradedText);}for(const t of data.terms){for(const key of ['template','upgradedTemplate',...(t.cardId?['definition']:[])])if(t[key]!==undefined)t[key]=format(t[key]);}for(const m of data.mechanisms)m.templates=m.templates.map(format);if(data.meta?.stageDeviceWording?.extraTrigger)data.meta.stageDeviceWording.extraTrigger=format(data.meta.stageDeviceWording.extraTrigger);}
  function applySeedUpdate(data,update){
    const entry=update.collection==='root'?data[update.id]:data[update.collection]?.find(e=>e.id===update.id||e.uid===update.uid);
    if(!entry)return;
    if(update.expected&&Object.entries(update.expected).some(([key,value])=>JSON.stringify(entry[key])!==JSON.stringify(value)))return;
    if(update.expectedAbsent?.some(key=>Object.prototype.hasOwnProperty.call(entry,key)))return;
    Object.assign(entry,clone(update.values));
  }
  function applyTerminology(data,changes){
    const replacements=Array.isArray(changes)?changes:[changes];
    const replace=value=>typeof value==='string'?replacements.reduce((text,{from,to})=>text.split(from).join(to),value):Array.isArray(value)?value.map(replace):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([key,item])=>[key,replace(item)])):value;
    for(const [collection,fields]of Object.entries({mechanisms:['name','definition','rules','templates','notes'],terms:['name','definition','template','upgradedTemplate','notes'],archetypes:['name','definition'],cards:['type','text','upgradedText','role','direction','notes','design','keywords'],styles:['name','content']}))
      for(const item of data[collection]||[])for(const key of fields)if(item[key]!==undefined)item[key]=replace(item[key]);
    for(const key of ['systemNames','stageDeviceWording','stageDeviceSharing','stageDeviceReplay','partnerDefinition','damageBonusDefinition','ruleConventions','rewardRules'])if(data.meta?.[key]!==undefined)data.meta[key]=replace(data.meta[key]);
  }
  function legacyData(raw){
    const pack=Array.isArray(raw)?{cards:raw,partnerGroups:[]}:raw;if(!Array.isArray(pack.cards))throw Error('文件中缺少卡牌');const d=clone(state.data);
    d.cards=pack.cards.map(c=>{const original=seed.data.cards.find(x=>x.id===c.id);const next={...(original?clone(original):newCard()),...clone(c),uid:original?.uid||c.uid||'card_'+c.id};next.text=modernize(next.text);next.upgradedText=modernize(next.upgradedText??next.text);next.termIds=c.termIds||original?.termIds||[...(c.effectIds||[]),...(c.ruleIds||[])];next.archetypeIds=c.archetypeIds||original?.archetypeIds||['SYS_'+(c.system||'stage')];next.effectIds=(next.effectIds||[]).flatMap(id=>id==='EF38'&&next.text.includes('立即加速到完成')?['EF32']:['EF17','EF35','EF38','EF67'].includes(id)?[]:[id]);return next;});
    d.partnerGroups=(pack.partnerGroups||[]).map(g=>({...clone(g),uid:g.uid||'group_'+g.mainCardId}));if(pack.startingDeck)d.meta.startingDeck=clone(pack.startingDeck);return normalizeData(d);
  }
  function exportBundle(){flushEdit();return {...clone(state),ui:clone(ui),exportedAt:new Date().toISOString()};}
  function download(text,name,type){const url=URL.createObjectURL(new Blob([text],{type})),a=document.createElement('a');a.href=url;a.download=name;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),30000);}
  function mergeBundleHistory(bundle){for(const [id,value]of Object.entries(bundle.assets||{}))if(/^data:image\/(png|jpeg|webp|gif);base64,/.test(value))state.assets[id]=value;const ids=new Set(state.history.map(r=>r.id));for(const r of bundle.history||[])if(r.id&&!ids.has(r.id)&&Array.isArray(r.patches)){state.history.push({...clone(r),originSequence:r.seq,seq:++state.revision,originWorkspace:bundle.workspaceId});ids.add(r.id);}}
  async function importBundle(raw,label='导入制作台'){
    flushEdit();const bundle=raw.schemaVersion&&raw.data?.cards?raw:null,next=bundle?normalizeData(bundle.data):legacyData(raw.cards?raw:raw.data?.cards?raw.data:raw);
    for(const batch of seed.seedBatches||[])if(!bundle?.appliedSeedBatches?.includes(batch.id)){if(batch.terminology)applyTerminology(next,batch.terminology);if(batch.wordingRules)applyWording(next,batch.wordingRules);if(batch.remove)for(const [collection,ids]of Object.entries(batch.remove))next[collection]=next[collection].filter(e=>!ids.includes(e.id));if(bundle&&batch.collection==='styles'){const ids=new Set(next.styles.map(s=>s.id));for(const entry of seed.data.styles.filter(s=>batch.ids.includes(s.id)))if(!ids.has(entry.id))next.styles.push(clone(entry));}if(batch.applyUpdatesOnImport&&batch.updates)for(const update of batch.updates)applySeedUpdate(next,update);}
    for(const batch of seed.seedBatches||[])if(!bundle?.appliedSeedBatches?.includes(batch.id)&&batch.entries){const ids=new Set(next[batch.collection].map(e=>e.id));for(const entry of batch.entries)if(!ids.has(entry.id)){next[batch.collection].push(clone(entry));ids.add(entry.id);}}
    if(bundle){if(mcp){for(const [id,value]of Object.entries(bundle.assets||{}))if(!Object.hasOwn(state.assets,id))state.assets[id]=value;}else mergeBundleHistory(bundle);}
    const r=transact(label,()=>{state.data=next;ui.selected=null;ui.search='';ui.cardPool=ui.type=ui.rarity=ui.pool=ui.cost=ui.archetype=ui.routePosition=ui.term=ui.breakthrough='all';ui.routePositions=[];ui.routeExcludedPositions=[];},'导入');if(!r)persist();renderAll();toast('已导入');
  }
  async function completeExport(){
    await saveLocalFile();
    const bundle=exportBundle();
    if(mcp){
      const result=await mcp.call('workbench_get_state',{includeAssets:true,includeHistory:true});
      bundle.assets={...bundle.assets,...result.bundle.assets};
      bundle.history=[...new Map([...result.bundle.history,...bundle.history].map(record=>[record.id,record])).values()].sort((a,b)=>a.seq-b.seq);
    }
    return bundle;
  }
  async function saveHTML(){
    const value=await completeExport();
    value.originWorkspace=workspaceId;
    value.workspaceId='junna-workbench-copy-'+uid();
    const copy=document.documentElement.cloneNode(true);
    copy.querySelector('#workbench-data').textContent=safeJSON(value);
    copy.querySelector('#workbench-data').removeAttribute('data-src');
    copy.querySelector('#workbench-connection')?.remove();
    for(const node of copy.querySelectorAll('[data-workbench-pwa]'))node.remove();
    copy.querySelector('#card-extra-hover-tips')?.remove();
    for(const link of copy.querySelectorAll('link[data-workbench-runtime]')){
      const style=document.createElement('style');
      style.textContent=await(await fetch(link.href)).text();
      link.replaceWith(style);
    }
    for(const script of copy.querySelectorAll('script[data-workbench-runtime]')){
      script.textContent=await(await fetch(script.src)).text();
      script.removeAttribute('src');
    }
    const assets=JSON.parse(copy.querySelector('#workbench-assets').textContent),assetURLs={...assets};
    await Promise.all(Object.entries(assets).map(async([id,url])=>{
      if(url.startsWith('/assets/'))assets[id]=value.assets[id]?.startsWith('data:')?value.assets[id]:await imageDataURL(await(await fetch(url)).blob());
    }));
    copy.querySelector('#workbench-assets').textContent=safeJSON(assets);
    for(const id of ['sidebar','work-area','preview','editor-form','history-body','manage-body'])copy.querySelector('#'+id).replaceChildren();
    for(const dialog of copy.querySelectorAll('dialog'))dialog.removeAttribute('open');
    copy.querySelector('#toast').hidden=true;
    copy.removeAttribute('style');
    let html=copy.outerHTML;
    for(const [id,url]of Object.entries(assetURLs))if(url.startsWith('/assets/'))html=html.split(url).join(assets[id]);
    download('<!doctype html>\n'+html,state.data.title+'_已编辑.html','text/html;charset=utf-8');
    toast('已保存 HTML');
  }
  function imageDataURL(blob){return new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=()=>reject(reader.error);reader.readAsDataURL(blob);});}
  async function setCardArt(file){
    const entry=current();
    if(!['cards','fitPool','powers'].includes(ui.selected?.collection)||!entry)return;
    if(!/^image\/(png|jpeg|webp|gif)$/.test(file.type)){toast('请选择PNG、JPEG、WebP或GIF图片');return;}
    if(file.size>16*1024*1024){toast('图片超过16MB');return;}
    const targetUid=entry.uid,targetCollection=ui.selected.collection,id='art_'+uid();
    try{
      state.assets[id]=connection?.assetURLs?(await mcp.uploadAsset(file,id)).url:await imageDataURL(file);
      transact('替换插画 · '+entry.name,data=>{
        const card=(targetCollection==='powers'?data.meta.powers:data[targetCollection]).find(card=>card.uid===targetUid);
        if(card)card.art=id;
      });
    }catch(error){toast(error.message);}
  }
  function rawApply(){const e=current();if(!e)return;try{const value=JSON.parse($('editor-form').querySelector('[data-raw-entity]').value);if(!value||typeof value!=='object'||Array.isArray(value))throw Error('请输入对象格式');if(value.id&&state.data[ui.selected.collection].some(x=>x.uid!==e.uid&&x.id===value.id))throw Error('编号已存在');transact('结构数据 · '+e.name,d=>{if(value.id!==e.id)renameId(ui.selected.collection,e,value.id);if(ui.selected.collection==='cards'&&value.name!==e.name)renameCard(e,value.name);const list=d[ui.selected.collection],at=list.findIndex(x=>x.uid===e.uid);list[at]={...value,uid:e.uid};});}catch(error){toast(error.message);}}
  function routeAction(action){
    if(action.startsWith('event-')&&eventView.action(action))return;
    if(action==='center-arrow-focus'){const card=current();if(ui.selected?.collection==='cards'&&card)transact('箭头聚焦 · '+card.name,()=>{card.arrowFocus={x:.5,y:.5};});}
    else if(action==='add')addEntity();else if(action==='upgrade'){flushEdit();ui.upgraded=!ui.upgraded;renderWorkspace();renderInspector();saveUI();}
    else if(action==='clear-filters'){flushEdit();ui.search='';ui.cardPool=ui.type=ui.rarity=ui.cost=ui.archetype=ui.routePosition=ui.term=ui.breakthrough='all';ui.routePositions=[];ui.routeExcludedPositions=[];ui.pool=ui.tab==='cards'?'pool':'all';renderAll();}
    else if(action==='archetypes')showArchetypes();else if(action==='meta')openEditor('root','meta');else if(action==='add-grant')newGrant();
    else if(action==='upload-art'){flushEdit();$('art-file').click();}else if(action==='reset-art'){const e=current();if(e)transact('恢复默认插画 · '+e.name,()=>{e.art=ui.selected.collection==='fitPool'?'':'defaultPortrait';});}
    else if(action==='copy-template'){const text=current()?.template||'';if(navigator.clipboard?.writeText)navigator.clipboard.writeText(text).then(()=>toast('已复制')).catch(()=>toast('复制失败'));else toast('请从文本框复制');}
    else if(action==='cards-by-term'){const e=current();if(e){flushEdit();selectTab('cards');ui.term=e.kind==='关键词'?e.id:'all';ui.search=e.kind==='关键词'?'':e.name;ui.selected=null;renderAll();}}
    else if(action==='apply-raw')rawApply();else if(action==='apply-meta'){try{const meta=JSON.parse($('editor-form').querySelector('[data-field="rawMeta"]').value);if(!meta||Array.isArray(meta)||typeof meta!=='object')throw Error('规则数据应为对象');transact('修改规则数据',d=>{d.meta=meta;});}catch(error){toast(error.message);}}
    else if(action==='add-archetype'){const a={uid:uid(),id:nextId('U_AR_',state.data.archetypes),name:'新流派',definition:'',system:''};transact('新增流派',d=>d.archetypes.push(a),'新增');renderArchetypes();}
  }
  $('work-area').addEventListener('click',event=>{const button=event.target.closest('[data-trash-page]');if(!button||button.disabled||ui.tab!=='trash')return;flushEdit();trashPage+=Number(button.dataset.trashPage);renderWorkspace();mobileLayout.scrollToTop();const focus=$('work-area').querySelector('[data-trash-page="'+button.dataset.trashPage+'"]:not(:disabled)')||$('work-area').querySelector('.trash-pagination');focus?.focus({preventScroll:true});});
  document.querySelector('.tabs').addEventListener('click',event=>{const b=event.target.closest('[data-tab]');if(b)changeTab(b.dataset.tab);});
  $('page-actions').addEventListener('click',event=>{if(changeSort(event))return;if(ui.tab==='models'&&modelView.workClick(event))return;const b=event.target.closest('[data-action]');if(b)routeAction(b.dataset.action);});
  $('page-actions').addEventListener('change',changeSort);
  $('sidebar').addEventListener('input',event=>{if(event.target.id==='filter-search'){ui.search=event.target.value;clearTimeout(searchTimer);if(!event.isComposing)searchTimer=setTimeout(()=>{renderWorkspace();saveUI();},120);}});
  $('sidebar').addEventListener('click',event=>{const category=event.target.closest('[data-card-category]');if(category){changeTab(category.dataset.cardCategory);return;}const style=event.target.closest('[data-style-target]');if(style){jumpStyle(style.dataset.styleTarget);return;}if(ui.tab==='models'&&modelView.sidebarClick(event))return;if(ui.tab==='flows'&&flowView.sidebarClick(event))return;const scope=event.target.closest('[data-overview-scope]');if(!scope)return;flushEdit();ui.overviewScope=scope.dataset.overviewScope;ui.selected=null;renderAll();});
  $('sidebar').addEventListener('change',event=>{if(flowView.positionChange(event))return;if(ui.tab==='flows'&&flowView.sidebarChange(event))return;const input=event.target;if(input.matches('[data-overview-all],[data-overview-dimension]')){const dimension=input.dataset.overviewDimension,selector=dimension?'[data-overview-dimension="'+dimension+'"]':'[data-overview-all]',scroll=$('sidebar').scrollTop;if(dimension){const selected=new Set(overviewDimensions().map(d=>d.key));input.checked?selected.add(dimension):selected.delete(dimension);ui.overviewBy=OVERVIEW_DIMENSIONS.filter(d=>selected.has(d.key)).map(d=>d.key);}else ui.overviewAll=input.checked;renderAll();$('sidebar').scrollTop=scroll;$('sidebar').querySelector(selector)?.focus({preventScroll:true});return;}const key=input.dataset.filter;if(key){ui[key]=input.type==='checkbox'?input.checked:input.value;renderWorkspace();saveUI();}});
  $('sidebar').addEventListener('click',event=>{if(ui.tab==='powers'&&powerView.sidebarClick(event))return;if(ui.tab==='fits'&&fitPoolView.sidebarClick(event))return;if(flowView.positionClick(event))return;const filter=event.target.closest('button[data-filter-value]');if(filter){const key=filter.dataset.filter,value=filter.dataset.filterValue;ui[key]=JunnaFilters.cycle(ui[key],value);const sidebar=$('sidebar'),scroll=sidebar.scrollTop;renderSidebar();renderWorkspace();saveUI();sidebar.scrollTop=scroll;sidebar.querySelector('[data-filter="'+key+'"][data-filter-value="'+CSS.escape(value)+'"]')?.focus({preventScroll:true});return;}const editor=event.target.closest('[data-edit]');if(editor){openEditor(editor.dataset.edit,editor.dataset.uid);renderSidebar();return;}const action=event.target.closest('[data-action]');if(action){routeAction(action.dataset.action);return;}const kind=event.target.closest('[data-kind]');if(kind){ui.kind=JunnaFilters.cycle(ui.kind,kind.dataset.kind);renderSidebar();renderWorkspace();saveUI();}const mechanism=event.target.closest('[data-mechanism-filter]');if(mechanism){ui.mechanismFilter=JunnaFilters.cycle(ui.mechanismFilter,mechanism.dataset.mechanismFilter);renderSidebar();renderWorkspace();saveUI();}});
  $('work-area').addEventListener('click',event=>{const style=event.target.closest('[data-style-target]');if(style){jumpStyle(style.dataset.styleTarget);return;}if(ui.tab==='models'&&modelView.workClick(event))return;if(ui.tab==='flows'&&flowView.workClick(event))return;const knowledge=event.target.closest('[data-knowledge-card]');if(knowledge){openKnowledge(knowledge.dataset.knowledgeCard,knowledge.dataset.knowledgeSlot,knowledge.dataset.knowledgeOrigin);return;}const linked=event.target.closest('[data-open-card]');if(linked){openLinkedCard(linked.dataset.openCard);return;}const restore=event.target.closest('[data-restore-card]');if(restore){restoreCard(restore.dataset.restoreCard);return;}const editor=event.target.closest('[data-edit]');if(editor){const entry=editor.closest('.card-entry');openEditor(editor.dataset.edit,editor.dataset.uid,entry?.dataset.cardOrigin||editor.dataset.uid,entry?.dataset.cardSlot);return;}const term=event.target.closest('[data-term]');if(term)openEditor('terms',term.dataset.term);});
  $('preview').addEventListener('click',event=>{const button=event.target.closest('[data-knowledge-card]');if(button)openKnowledge(button.dataset.knowledgeCard,button.dataset.knowledgeSlot,button.dataset.knowledgeOrigin);});
  $('work-area').addEventListener('wheel',event=>{const entry=event.target.closest('.card-entry.selected'),origin=entry&&find('cards',entry.dataset.cardOrigin);if(origin&&groupsFor(origin).length)knowledgeWheel(event,direction=>cycleKnowledgeEntry(entry,direction));},{passive:false});
  $('work-area').addEventListener('keydown',event=>{const entry=event.target.closest('.card-entry.selected'),origin=entry&&find('cards',entry.dataset.cardOrigin);if(!origin||!groupsFor(origin).length||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key))return;event.preventDefault();cycleKnowledgeEntry(entry,['ArrowRight','ArrowDown'].includes(event.key)?1:-1,true);});
  $('preview').addEventListener('wheel',event=>{const card=current();if(ui.selected?.collection==='cards'&&card&&groupsFor(card).length)knowledgeWheel(event,direction=>cycleKnowledgePreview(direction));},{passive:false});
  $('preview').addEventListener('keydown',event=>{if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key))return;event.preventDefault();cycleKnowledgePreview(['ArrowRight','ArrowDown'].includes(event.key)?1:-1,true);});
  $('knowledge-fan').addEventListener('click',event=>{const button=event.target.closest('[data-knowledge-focus]');if(button){knowledgeView.slot=button.dataset.knowledgeFocus;renderKnowledge(true);}});
  $('knowledge-fan').addEventListener('keydown',event=>{if(!knowledgeView||!event.target.closest('.knowledge-card.is-focused')||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(event.key))return;const entries=knowledgeEntries(find('partnerGroups',knowledgeView.group)),index=entries.findIndex(entry=>entry.key===knowledgeView.slot),next=event.key==='Home'?0:event.key==='End'?entries.length-1:(index+(['ArrowRight','ArrowDown'].includes(event.key)?1:-1)+entries.length)%entries.length;event.preventDefault();knowledgeView.slot=entries[next].key;renderKnowledge(true);});
  $('knowledge-fan').addEventListener('wheel',event=>{if(!knowledgeView||!event.target.closest('.knowledge-card.is-focused'))return;knowledgeWheel(event,direction=>{const entries=knowledgeEntries(find('partnerGroups',knowledgeView.group)),index=entries.findIndex(entry=>entry.key===knowledgeView.slot);knowledgeView.slot=entries[(index+direction+entries.length)%entries.length].key;renderKnowledge(true);});},{passive:false});
  $('knowledge-upgrade').addEventListener('click',()=>{knowledgeView.upgraded=!knowledgeView.upgraded;renderKnowledge();});
  $('knowledge-group-switch').addEventListener('change',event=>{knowledgeView.group=event.target.value;renderKnowledge();});
  $('knowledge-edit').addEventListener('click',()=>{const {card,origin,slot}=knowledgeView;ui.upgraded=knowledgeView.upgraded;$('knowledge-dialog').close();openEditor('cards',card,origin,slot);});
  $('knowledge-dialog').addEventListener('click',event=>{if(event.target!==$('knowledge-dialog'))return;const rect=event.target.getBoundingClientRect();if(event.clientX<rect.left||event.clientX>rect.right||event.clientY<rect.top||event.clientY>rect.bottom)event.target.close();});
  $('work-area').addEventListener('keydown',event=>{if(event.key==='Enter'&&event.target.matches('[data-term]'))openEditor('terms',event.target.dataset.term);});
  $('close-inspector').addEventListener('click',closeEditor);$('duplicate-entity').addEventListener('click',duplicateEntity);$('delete-entity').addEventListener('click',deleteEntity);$('reserve-entity').addEventListener('click',()=>moveCardReserve(ui.selected?.uid,!current()?.reservedAt));$('delete-group').addEventListener('click',()=>moveCardGroup('trash'));$('reserve-group').addEventListener('click',()=>moveCardGroup());$('restore-entity').addEventListener('click',()=>restoreCard(ui.selected?.uid));$('edit-title').addEventListener('click',()=>openEditor('root','title'));
  $('editor-form').addEventListener('load',event=>{if(event.target.matches('[data-arrow-focus-source]'))refreshArrowFocus();},true);
  $('editor-form').addEventListener('submit',event=>event.preventDefault());
  $('editor-form').addEventListener('toggle',event=>{if(event.target.open){const raw=event.target.querySelector('[data-raw-entity]'),entity=current();if(raw&&entity){const value=clone(entity);delete value.uid;raw.value=JSON.stringify(value,null,2);}}},true);
  $('editor-form').addEventListener('input',event=>{const input=event.target;if(input.hasAttribute('data-grant-search')){const card=current(),select=$('editor-form').querySelector('[data-add-grant]');if(card&&select)select.innerHTML=grantOptions(card,input.value);return;}if(input.dataset.tagSearch){const q=input.value.toLowerCase();for(const label of $('editor-form').querySelectorAll('[data-check-list="'+input.dataset.tagSearch+'"] [data-search-text]'))label.hidden=!label.dataset.searchText.includes(q);return;}if(input.dataset.field&&input.tagName!=='SELECT'&&input.dataset.on!=='change')applyField(input);});
  $('editor-form').addEventListener('change',event=>{if(ui.tab==='models'&&modelView.editorChange(event))return;if(flowView.editorChange(event))return;const input=event.target;if(input.dataset.pick){const e=current(),key=input.dataset.pick;if(!previewMode&&(!mcpStatus.ready||mcpStatus.conflict)){renderInspector();return;}beginEdit();const picks=[...$('editor-form').querySelectorAll('[data-pick="'+key+'"]')];e[key]=mergePickedIds(e[key]||[],picks.map(n=>n.value),picks.filter(n=>n.checked).map(n=>n.value));if(key==='archetypeIds'){const primary=state.data.archetypes.find(route=>e.archetypeIds.includes(route.id)&&route.system===e.system)||state.data.archetypes.find(route=>e.archetypeIds.includes(route.id));e.system=primary?.system||'utility';}const label=input.closest('.field')?.querySelector('.field-label');if(label)label.textContent=({termIds:'词条',archetypeIds:'流派',cardIds:'实装卡牌'}[key]||key)+' · '+e[key].length;const count=input.closest('[data-editor-section]')?.querySelector('.editor-section-count');if(count)count.textContent=e[key].length;flushEdit();if(key==='archetypeIds')renderInspector();return;}if(input.dataset.grantCount){const n=Number(input.value);if(!Number.isSafeInteger(n)||n<1){renderInspector();return;}changeKnowledgeCount(input.dataset.grantCount,n);return;}if(input.hasAttribute('data-add-grant')){if(input.value)addGrant(input.value);return;}if(input.dataset.field&&(input.tagName==='SELECT'||input.dataset.on==='change')){applyField(input);flushEdit();}});
  $('editor-form').addEventListener('focusout',event=>{if(event.target.dataset.field){if(!event.target.checkValidity())event.target.reportValidity();flushEdit();}});
  $('editor-form').addEventListener('click',event=>{if(flowView.editorClick(event))return;const pick=event.target.closest('[data-arrow-focus-pick]');if(pick){if(event.detail===0){$('editor-form').querySelector('[data-field="arrowFocus.x"]')?.focus();return;}const card=current(),rect=pick.querySelector('img').getBoundingClientRect();if(card&&rect.width&&rect.height)transact('箭头聚焦 · '+card.name,()=>{card.arrowFocus={x:Math.round(Math.min(1,Math.max(0,(event.clientX-rect.left)/rect.width))*1000)/1000,y:Math.round(Math.min(1,Math.max(0,(event.clientY-rect.top)/rect.height))*1000)/1000};});return;}const action=event.target.closest('[data-action]');if(action){routeAction(action.dataset.action);return;}const card=event.target.closest('[data-open-card]');if(card){openLinkedCard(card.dataset.openCard);return;}const remove=event.target.closest('[data-remove-grant]');if(remove){const c=current();transact('移除知识关联 · '+c.name,d=>{for(const g of d.partnerGroups.filter(g=>g.mainCardId===c.id))g.grantedCardIds=g.grantedCardIds.filter(id=>id!==remove.dataset.removeGrant);});}});
  $('set-group-main').addEventListener('click',()=>setKnowledgeMain($('set-group-main').dataset.groupUid));
  $('manage-body').addEventListener('change',event=>{const key=event.target.dataset.archetypeField,entry=event.target.closest('[data-archetype]');if(key&&entry){const a=find('archetypes',entry.dataset.archetype);transact('修改流派 · '+a.name,()=>{a[key]=event.target.value;});}});
  $('manage-body').addEventListener('click',event=>{const action=event.target.closest('[data-action]');if(action){routeAction(action.dataset.action);return;}const remove=event.target.closest('[data-delete-archetype]');if(remove){const a=find('archetypes',remove.dataset.deleteArchetype);transact('删除流派 · '+a.name,d=>{d.archetypes=d.archetypes.filter(x=>x.uid!==a.uid);},'删除');renderArchetypes();}const edit=event.target.closest('[data-edit-archetype]');if(edit){$('manage-dialog').close();openEditor('archetypes',edit.dataset.editArchetype);}});
  $('history-button').addEventListener('click',()=>{flushEdit();renderHistory();$('history-dialog').showModal();if(mcp)loadHistoryPage(true);});
  $('history-search').addEventListener('input',()=>{renderHistory();clearTimeout(historyFilterTimer);if(mcp)historyFilterTimer=setTimeout(()=>loadHistoryPage(true),250);});
  $('history-kind').addEventListener('change',()=>{renderHistory();if(mcp)loadHistoryPage(true);});
  $('history-body').addEventListener('click',event=>{if(event.target.closest('[data-more-history]'))return loadHistoryPage();const b=event.target.closest('[data-restore]');if(b)restoreRecord(b.dataset.restore,b.dataset.side);});
  document.querySelectorAll('[data-close-dialog]').forEach(b=>b.addEventListener('click',()=>$(b.dataset.closeDialog).close()));
  $('undo').addEventListener('click',undo);$('redo').addEventListener('click',redo);
  $('import-button').addEventListener('click',()=>{flushEdit();$('import-file').click();});
  $('import-file').addEventListener('change',async event=>{const file=event.target.files[0];if(!file)return;try{const text=(await file.text()).replace(/^\uFEFF/,'');let raw;if(/^\s*</.test(text)){const doc=new DOMParser().parseFromString(text,'text/html'),embedded=doc.querySelector('#workbench-data')||doc.querySelector('#seed-data');if(!embedded)throw Error('HTML中没有可导入的数据');raw=JSON.parse(embedded.textContent);}else raw=JSON.parse(text);await importBundle(raw,'导入 · '+file.name);}catch(error){toast('导入失败：'+error.message);}event.target.value='';});
  $('art-file').addEventListener('change',event=>{const file=event.target.files[0];if(file)setCardArt(file);event.target.value='';});
  $('export-json').addEventListener('click',async()=>{try{download(JSON.stringify(await completeExport(),null,2),state.data.title+'_数据与历史.json','application/json;charset=utf-8');toast('已导出 JSON');}catch(error){toast(error.message);}});$('export-html').addEventListener('click',()=>saveHTML().catch(error=>toast(error.message)));
  $('file-state').addEventListener('click',saveLocalFile);$('bind-local-file').addEventListener('click',bindLocalFile);$('read-local-file').addEventListener('click',readLocalFile);$('disconnect-local-file').addEventListener('click',disconnectLocalFile);
  document.addEventListener('keydown',event=>{if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='s'){event.preventDefault();saveLocalFile();return;}const editing=['INPUT','TEXTAREA','SELECT'].includes(document.activeElement.tagName);if(!editing&&(event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='z'){event.preventDefault();event.shiftKey?redo():undo();}else if(!editing&&(event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='y'){event.preventDefault();redo();}else if(event.key==='Escape'&&!document.querySelector('dialog[open]'))closeEditor();});
  window.addEventListener('hashchange',()=>{flushEdit();readHash();renderAll();});window.addEventListener('resize',fitCards);document.fonts.ready.then(fitCards);document.fonts.addEventListener('loadingdone',fitCards);
  async function syncIfIdle(){if(document.visibilityState!=='visible'||navigator.onLine===false||mcpConnecting||pending||mcpStatus.busy||mcpStatus.conflict)return;if(mcpStatus.ready&&(document.activeElement?.matches('input,textarea,select')||document.querySelector('dialog[open]')))return;if(mcpStatus.pending)await mcp?.flush();else await mcp?.pull();}
  document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='hidden'){flushEdit();syncMirror();}else syncIfIdle();});window.addEventListener('online',syncIfIdle);window.addEventListener('pagehide',()=>{flushEdit();syncMirror();});
  window.addEventListener('beforeunload',event=>{if((mcp||diskHandle)&&(pending||diskDirty||diskWriting)){flushEdit();event.preventDefault();event.returnValue='';}});
  let saved=null,mirror=null;try{db=await openDB();saved=await readDB();}catch(_){db=null;}
  try{mirror=JSON.parse(localStorage.getItem(mirrorKey)||'null');}catch(_){}
  const candidates=[seed,saved,mirror].filter(x=>x?.data?.cards).sort((a,b)=>(b.revision||0)-(a.revision||0));state=clone(candidates[0]);
  state.workspaceId=workspaceId;state.assets={...(seed.assets||{}),...(saved?.assets||{}),...(mirror?.assets||{}),...(state.assets||{})};state.history=[...new Map([...(seed.history||[]),...(saved?.history||[]),...(mirror?.history||[]),...(state.history||[])].map(r=>[r.id,r])).values()].sort((a,b)=>a.seq-b.seq);state.undoStack=state.undoStack||[];state.redoStack=state.redoStack||[];state.revision=Math.max(state.revision||0,...state.history.map(r=>r.seq||0));
  try{state.data=normalizeData(state.data);}catch(error){state=clone(seed);toast('已载入内置数据：'+error.message);}
  state.appliedSeedBatches=Array.isArray(state.appliedSeedBatches)?state.appliedSeedBatches:[];
  state.seedBatches=clone(seed.seedBatches||[]);
  for(const batch of previewMode?(seed.seedBatches||[]):[])if(!state.appliedSeedBatches.includes(batch.id)){
    const before=clone(state.data);
    if(batch.remove)for(const [collection,ids]of Object.entries(batch.remove)){state.data[collection]=state.data[collection].filter(e=>!ids.includes(e.id));if(collection==='archetypes'&&ids.includes(ui.archetype))ui.archetype='all';}
    else if(batch.terminology)applyTerminology(state.data,batch.terminology);
    else if(batch.wordingRules)applyWording(state.data,batch.wordingRules);
    if(Array.isArray(batch.updates)){for(const update of batch.updates)applySeedUpdate(state.data,update);}
    else if(!batch.wordingRules&&!batch.remove&&!batch.terminology){const ids=new Set(state.data[batch.collection].map(e=>e.id));for(const entry of [...seed.data[batch.collection].filter(e=>batch.ids.includes(e.id)),...(batch.entries||[])])if(!ids.has(entry.id)){state.data[batch.collection].push(clone(entry));ids.add(entry.id);}}
    state.appliedSeedBatches.push(batch.id);record(before,batch.label||'补充突破条目',batch.updates||batch.wordingRules||batch.remove||batch.terminology?'编辑':'导入');
  }
  if(connection&&oldDraftKey)try{const key=workspaceId+':mcp-draft:'+oldDraftKey;recoveredDraft=db?await requestValue(db.transaction('documents','readonly').objectStore('documents').get(key)):JSON.parse(localStorage.getItem(key)||'null');}catch(_){}
  restoreRecoveredDraft();
  window.addEventListener('workbench:before-switch',event=>{
    if(document.body.classList.contains('readonly'))return;
    event.preventDefault();
    const selector=$('mod-select');selector.disabled=true;
    (async()=>{try{
      if(ui.tab==='initial'&&$('initial-form')?.querySelector('[role="status"]')?.textContent==='未保存')throw Error('请先保存初始配置');
      flushEdit();await saveQueue;if(mcp){await mcp.flush();const status=mcp.status();if(status.pending||status.busy||status.conflict||status.error)throw Error('当前修改尚未保存');}
      location.assign(event.detail.target);
    }catch(error){toast(error.message);selector.disabled=false;}})();
  });
  readHash();document.documentElement.style.setProperty('--ancient-mask','url("'+assetURL('ancient-portrait-mask-large')+'")');document.querySelectorAll('[data-asset]').forEach(img=>img.src=assetURL(img.dataset.asset));renderAll();await restoreLocalFile();persist();
  if(connection)setInterval(syncIfIdle,5000);
  const handoff=new URLSearchParams(location.hash.slice(1)).get('handoff');
  if(handoff&&window.opener){window.addEventListener('message',async event=>{if(event.source!==window.opener||event.data?.type!=='junna-workbench-transfer'||event.data.nonce!==handoff)return;try{await importBundle(event.data.data,'导入原编辑器当前修改');history.replaceState(null,'','#tab=cards');selectTab('cards');renderAll();}catch(error){toast('接收失败：'+error.message);}});window.opener.postMessage({type:'junna-workbench-ready',nonce:handoff},'*');}
  window.__JUNNA_WORKBENCH__={getState:()=>clone(state),getUI:()=>clone(ui),flush:async()=>{flushEdit();await saveQueue;await diskQueue;return !lastSaveError&&(previewMode||mcpStatus.ready&&!pending&&!mcpStatus.pending&&!mcpStatus.busy&&!mcpStatus.conflict);},exportBundle,importBundle,saveLocalFile,readLocalFile,disconnectLocalFile,getFileState:()=>({name:diskHandle?.name,granted:diskGranted,conflict:diskConflict,dirty:diskDirty,error:diskError?.message}),openEditor,changeTab,undo,redo,restoreRecord,transact,filteredCards,cardHTML,normalizeData,ready:true};
})();
