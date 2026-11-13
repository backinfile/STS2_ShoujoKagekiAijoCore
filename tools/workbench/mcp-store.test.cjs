'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
const {WorkbenchStore}=require('./mcp-store.cjs');
const {workspaceId}=require('./mcp-config.cjs');
function fixture(t){const dir=fs.mkdtempSync(path.join(os.tmpdir(),'junna-mcp-')),file=path.join(dir,'workbench.json'),seed={schemaVersion:1,workspaceId,revision:1,history:[],assets:{},data:{title:'测试',meta:{},cards:[{uid:'card-a',id:'A',name:'甲',text:'测试',cost:1,type:'箭矢',turns:'X',upgradedTurns:'X',extraHoverTips:[{id:'background',description:'背景'}]},{uid:'card-b',id:'B',name:'乙',text:'另一张',cost:1,type:'技能'}],terms:[],mechanisms:[],archetypes:[],partnerGroups:[],styles:[{uid:'style-a',id:'S',name:'风格',content:'约定'}],fitPool:[{uid:'fit-a',id:'F',name:'契合',theme:'星',text:'原文',cardIds:['A']}]}};fs.writeFileSync(file,JSON.stringify(seed));t.after(()=>fs.rmSync(dir,{recursive:true,force:true}));return {store:new WorkbenchStore({file,seed,workspaceId}),file,seed};}
function input(changes,extra={}){return {requestId:randomUUID(),clientId:'test-client',label:'测试编辑',changes,...extra};}
const patch=(e,updates)=>({collection:'cards',uid:e.uid,expectedVersion:e.version,updates});

function guardedPatch(entry,updates){
  const fields=Object.keys(updates);
  return {...patch(entry,updates),expectedFields:Object.fromEntries(fields.filter(field=>Object.hasOwn(entry.entity,field)).map(field=>[field,entry.entity[field]])),expectedAbsent:fields.filter(field=>!Object.hasOwn(entry.entity,field))};
}

test('unchanged snapshots reuse the parsed file and detect external replacements',t=>{
  const {store,file}=fixture(t);
  const originalRead=fs.readFileSync;
  let reads=0;
  fs.readFileSync=function(target,...args){if(target===file)reads++;return originalRead.call(fs,target,...args);};
  t.after(()=>{fs.readFileSync=originalRead;});
  store.state({knownRevision:1});
  store.state({knownRevision:1});
  assert.equal(reads,1);
  const changed=JSON.parse(originalRead(file,'utf8'));
  changed.data.cards[0].name='外部更新后的名称';changed.revision=2;
  fs.writeFileSync(file,JSON.stringify(changed));
  assert.equal(store.get({collection:'cards',id:'A'}).entity.name,'外部更新后的名称');
});

test('returned snapshots and queries cannot mutate the cached workspace',t=>{
  const {store}=fixture(t);
  store.state().bundle.data.cards[0].name='未提交修改';
  store.query({collection:'cards'}).entries[0].entity.name='另一个未提交修改';
  store.read().data.cards[0].name='直接读取修改';
  assert.equal(store.get({collection:'cards',id:'A'}).entity.name,'甲');
});

test('live snapshots omit offline seed batches while exports retain them',t=>{
  const {store,file}=fixture(t),bundle=JSON.parse(fs.readFileSync(file,'utf8'));
  bundle.seedBatches=[{id:'old-migration',updates:[{text:'已迁移内容'}]}];bundle.appliedSeedBatches=['old-migration'];
  fs.writeFileSync(file,JSON.stringify(bundle));
  const snapshot=store.state().bundle;
  assert.deepEqual(snapshot.seedBatches,[]);assert.deepEqual(snapshot.appliedSeedBatches,[]);
  assert.equal(Object.hasOwn(snapshot,'entityVersions'),false);
  assert.deepEqual(store.read().seedBatches,bundle.seedBatches);
});

test('guarded changes merge independent fields of the same card',async t=>{
  const {store}=fixture(t),old=store.get({collection:'cards',id:'A'});
  await store.commit(input([patch(old,{name:'另一端的名称'})]));
  await store.commit(input([guardedPatch(old,{cost:2,notes:'网页备注'})]));
  const card=store.get({collection:'cards',id:'A'}).entity;
  assert.equal(card.name,'另一端的名称');assert.equal(card.cost,2);assert.equal(card.notes,'网页备注');
});

test('absent-field guards merge new fields after another client edits the entry',async t=>{
  const {store}=fixture(t),old=store.get({collection:'cards',id:'A'});
  await store.commit(input([patch(old,{cost:2})]));
  await store.commit(input([{...patch(old,{notes:'新备注'}),expectedAbsent:['notes']}]));
  const current=store.get({collection:'cards',id:'A'}).entity;
  assert.equal(current.cost,2);assert.equal(current.notes,'新备注');
});

test('guarded same-field conflicts reject the entire batch and name the fields',async t=>{
  const {store,file}=fixture(t),old=store.get({collection:'cards',id:'A'}),other=store.get({collection:'cards',id:'B'});
  await store.commit(input([patch(old,{cost:2})]));
  const before=fs.readFileSync(file,'utf8');
  await assert.rejects(store.commit(input([guardedPatch(old,{cost:3}),guardedPatch(other,{text:'本批修改'})])),error=>error.code==='VERSION_CONFLICT'&&error.details.conflicts[0].fields.includes('cost'));
  assert.equal(fs.readFileSync(file,'utf8'),before);
});

test('already-applied guarded values succeed without another history entry',async t=>{
  const {store}=fixture(t),old=store.get({collection:'cards',id:'A'});
  await store.commit(input([patch(old,{cost:2})]));
  const current=store.get({collection:'cards',id:'A'});
  const result=await store.commit(input([guardedPatch(old,{cost:2})]));
  assert.equal(result.record,null);
  assert.equal(result.versions['cards/'+old.uid],current.version);
  assert.equal(store.history().total,1);
});

test('a guarded update must cover every modified field',async t=>{
  const {store}=fixture(t),old=store.get({collection:'cards',id:'A'});
  const change=guardedPatch(old,{cost:2});change.updates.name='缺少原值';
  await assert.rejects(store.commit(input([change])),{code:'INVALID_ARGUMENT'});
});

test('guarded updates report a conflict when the entry was deleted',async t=>{
  const {store}=fixture(t),old=store.get({collection:'cards',id:'A'});
  await store.commit(input([{collection:'cards',uid:old.uid,expectedVersion:old.version,before:old.entity,after:null}]));
  await assert.rejects(store.commit(input([guardedPatch(old,{cost:2})])),error=>error.code==='VERSION_CONFLICT'&&error.details.conflicts[0].current===null);
});
test('two clients editing one entry: stale version preserves the first edit',async t=>{const {store}=fixture(t),a=store.get({collection:'cards',id:'A'}),b=store.get({collection:'cards',id:'A'});await store.commit(input([patch(a,{name:'客户端甲'})]));await assert.rejects(store.commit(input([patch(b,{name:'客户端乙'})])),e=>e.code==='VERSION_CONFLICT'&&e.details.conflicts[0].current.name==='客户端甲');assert.equal(store.get({collection:'cards',id:'A'}).entity.name,'客户端甲');});
test('independent entries can commit concurrently',async t=>{const {store}=fixture(t),a=store.get({collection:'cards',id:'A'}),b=store.get({collection:'cards',id:'B'});await Promise.all([store.commit(input([patch(a,{name:'甲改'})])),store.commit(input([patch(b,{name:'乙改'})]))]);assert.deepEqual(store.query({collection:'cards'}).entries.map(e=>e.entity.name),['甲改','乙改']);});
test('one conflict rejects the entire batch including assets',async t=>{const {store,file}=fixture(t),a=store.get({collection:'cards',id:'A'}),b=store.get({collection:'cards',id:'B'});await store.commit(input([patch(a,{name:'先修改'})]));const before=fs.readFileSync(file,'utf8');await assert.rejects(store.commit(input([patch(a,{name:'过期'}),patch(b,{name:'批次乙'})],{assets:{test:'data:image/png;base64,YQ=='}})),{code:'VERSION_CONFLICT'});assert.equal(fs.readFileSync(file,'utf8'),before);});
test('idempotency survives restart and prevents request-id reuse',async t=>{const {store,file,seed}=fixture(t),a=store.get({collection:'cards',id:'A'}),request=input([patch(a,{name:'只改一次'})]);const first=await store.commit(request),restarted=new WorkbenchStore({file,seed,workspaceId});const retry=await restarted.commit(request);assert.equal(retry.replayed,true);assert.equal(first.revision,retry.revision);assert.equal(restarted.history().total,1);await assert.rejects(restarted.commit({...request,label:'不同内容'}),{code:'REQUEST_ID_REUSED'});});
test('stale before content is rejected even with a newly supplied version',async t=>{const {store}=fixture(t),old=store.get({collection:'cards',id:'A'});await store.commit(input([patch(old,{name:'新名字'})]));const now=store.get({collection:'cards',id:'A'});await assert.rejects(store.commit(input([{collection:'cards',uid:old.uid,expectedVersion:now.version,before:old.entity,after:{...old.entity,text:'旧页面的文本'}}])),{code:'VERSION_CONFLICT'});});
test('undo requires both current versions and matching historical endpoints',async t=>{const {store}=fixture(t),a=store.get({collection:'cards',id:'A'});const result=await store.commit(input([patch(a,{name:'已编辑'})]));const now=store.get({collection:'cards',id:'A'});await store.commit(input([],{restore:{recordId:result.record.id,side:'before',expectedVersions:{['cards/'+a.uid]:now.version}}}));const restored=store.get({collection:'cards',id:'A'});assert.equal(restored.entity.name,a.entity.name);assert.notEqual(restored.version,a.version);await store.commit(input([patch(restored,{notes:'后来修改'})]));const latest=store.get({collection:'cards',id:'A'});await assert.rejects(store.commit(input([],{restore:{recordId:result.record.id,side:'after',expectedVersions:{['cards/'+a.uid]:latest.version}}})),{code:'VERSION_CONFLICT'});});
test('creation and deletion keep tombstone versions; domain data stays intact',async t=>{const {store}=fixture(t),a=store.get({collection:'cards',id:'A'});await store.commit(input([patch(a,{notes:'保留特殊内容'})]));const now=store.get({collection:'cards',id:'A'});assert.equal(now.entity.turns,'X');assert.equal(now.entity.upgradedTurns,'X');assert.deepEqual(now.entity.extraHoverTips,a.entity.extraHoverTips);assert.equal(store.query({collection:'fitPool'}).entries[0].entity.text,'原文');await store.commit(input([{collection:'cards',uid:a.uid,before:now.entity,after:null,expectedVersion:now.version}]));const missing=store.get({collection:'cards',uid:a.uid});assert.match(missing.version,/^2:missing$/);assert.equal(store.state().versions['cards/'+a.uid],missing.version);await assert.rejects(store.commit(input([{collection:'cards',uid:a.uid,before:null,after:a.entity,expectedVersion:'0:missing'}])),{code:'VERSION_CONFLICT'});});
module.exports={fixture,input,patch};

test('temporary star-arrow snapshot restores arrow type without losing countdown or identity',async t=>{
  for(const turns of [2,'X']){
    const {store,file,seed}=fixture(t),old=JSON.parse(fs.readFileSync(file,'utf8'));
    Object.assign(old.data.cards[0],{type:'\u661f\u77e2',turns,upgradedTurns:turns==='X'?'X':1,arrowSubtype:'攻击'});
    fs.writeFileSync(file,JSON.stringify(old));
    const before=store.get({collection:'cards',id:'A'});assert.equal(before.entity.type,'箭矢');
    await store.commit(input([patch(before,{notes:'回切后正常编辑'})]));
    const card=new WorkbenchStore({file,seed,workspaceId}).get({collection:'cards',id:'A'}).entity;
    assert.equal(card.uid,'card-a');assert.equal(card.id,'A');assert.equal(card.type,'箭矢');
    assert.equal(card.turns,turns);assert.equal(card.upgradedTurns,turns==='X'?'X':1);assert.equal(card.arrowSubtype,'攻击');
    assert.deepEqual(card.extraHoverTips,before.entity.extraHoverTips);
  }
});
