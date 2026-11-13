'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {randomUUID}=require('node:crypto');
const {WorkbenchStore}=require('./mcp-store.cjs');
function fixture(t){const dir=fs.mkdtempSync(path.join(os.tmpdir(),'junna-entry-versions-')),file=path.join(dir,'state.json'),seed={schemaVersion:1,workspaceId:'entries-test',revision:1,history:[],assets:{},data:{title:'测试',meta:{rule:'规则',powers:[{uid:'p1',id:'P1',name:'能力甲'},{uid:'p2',id:'P2',name:'能力乙'}],relics:[{uid:'r1',id:'R1',name:'遗物甲'},{uid:'r2',id:'R2',name:'遗物乙'}]},cards:[{uid:'c1',id:'C1',name:'卡牌',type:'技能',cost:1}],terms:[],mechanisms:[],partnerGroups:[],archetypes:[],styles:[],fitPool:[]}};fs.writeFileSync(file,JSON.stringify(seed));t.after(()=>fs.rmSync(dir,{recursive:true,force:true}));return new WorkbenchStore({file,seed,workspaceId:seed.workspaceId});}
const input=changes=>({requestId:randomUUID(),clientId:'test',label:'条目独立版本',changes});
const update=(entry,name)=>({collection:entry.collection,uid:entry.uid,expectedVersion:entry.version,updates:{name}});
for(const [collection,a,b]of [['powers','p1','p2'],['relics','r1','r2']])test(collection+' have independent versions and preserve unrelated entries',async t=>{
  const store=fixture(t),first=store.get({collection,uid:a}),second=store.get({collection,uid:b}),meta=store.get({collection:'root',uid:'meta'}),card=store.get({collection:'cards',uid:'c1'});
  await store.commit(input([update(first,'甲修改')]));
  assert.equal(store.get({collection,uid:b}).version,second.version);
  assert.equal(store.get({collection:'root',uid:'meta'}).version,meta.version);
  assert.equal(store.get({collection:'cards',uid:'c1'}).version,card.version);
  await store.commit(input([update(second,'乙修改'),update(card,'卡牌修改')]));
  assert.equal(store.get({collection,uid:a}).entity.name,'甲修改');
  assert.equal(store.get({collection,uid:b}).entity.name,'乙修改');
  assert.equal(store.get({collection:'cards',uid:'c1'}).entity.name,'卡牌修改');
  await assert.rejects(store.commit(input([update(first,'过期写入')])),{code:'VERSION_CONFLICT'});
});
test('legacy meta changes update only the affected Power entry version',async t=>{
  const store=fixture(t),meta=store.get({collection:'root',uid:'meta'}),first=store.get({collection:'powers',uid:'p1'}),second=store.get({collection:'powers',uid:'p2'}),after=structuredClone(meta.entity);after.powers[0].name='旧客户端修改';
  const result=await store.commit(input([{collection:'root',uid:'meta',expectedVersion:meta.version,before:meta.entity,after}]));
  assert(result.versions['powers/p1']);assert.equal(result.versions['root/meta'],meta.version);
  assert.notEqual(store.get({collection:'powers',uid:'p1'}).version,first.version);
  assert.equal(store.get({collection:'powers',uid:'p2'}).version,second.version);
  assert.equal(store.get({collection:'root',uid:'meta'}).version,meta.version);
});
