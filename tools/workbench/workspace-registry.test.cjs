'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs'),os=require('node:os'),path=require('node:path'),crypto=require('node:crypto');
const {SqliteStorage}=require('./mcp-sqlite.cjs');
const {mods,emptyBundle}=require('./mods.cjs');
const {createRegistry,createWorkspaceHandlers}=require('./workspace-registry.cjs');
const {createDaemon}=require('./mcp-daemon.cjs');
const {Client}=require('@modelcontextprotocol/sdk/client/index.js');
const {StreamableHTTPClientTransport}=require('@modelcontextprotocol/sdk/client/streamableHttp.js');
function setup(t){
  const directory=fs.mkdtempSync(path.join(os.tmpdir(),'shoujo-workspaces-')),file=path.join(directory,'state.sqlite');
  const storage=new SqliteStorage(file);const bundle=emptyBundle('junna');bundle.revision=13;
  bundle.data.cards=[{uid:'shared-card',id:'J01',name:'纯那原卡',type:'技能',cost:1,text:'原文'}];
  bundle.entityVersions={'cards/shared-card':4};storage.import(bundle);storage.close();
  let registry=createRegistry({stateDirectory:directory,assetDirectory:path.join(directory,'assets')});
  t.after(()=>{registry.close();fs.rmSync(directory,{recursive:true,force:true});});
  return {directory,get registry(){return registry;},restart(){registry.close();registry=createRegistry({stateDirectory:directory,assetDirectory:path.join(directory,'assets')});}};
}
test('keeps original Junna data and versions; independently persists Mod edits, receipts and history',async t=>{
  const s=setup(t),before=s.registry.get('junna').get({collection:'cards',id:'J01'});
  assert.match(before.version,/^4:/);assert.equal(s.registry.get().read({copy:false}).revision,13);
  const requestId=crypto.randomUUID(),input={requestId,clientId:'test',label:'各 Mod 创建同号条目',changes:[]};
  for(const modId of ['core','karen']){
    const store=s.registry.get(modId),current=store.get({collection:'cards',uid:'shared-card'});
    const result=await store.commit({...input,changes:[{collection:'cards',uid:'shared-card',expectedVersion:current.version,before:null,after:{uid:'shared-card',id:'C01',name:modId,type:'技能',cost:1,text:modId}}]});
    assert.equal(result.revision,1);assert.equal(store.history().total,1);
  }
  assert.deepEqual(s.registry.get().get({collection:'cards',id:'J01'}),before);
  const current=s.registry.get('karen').get({collection:'cards',uid:'shared-card'});
  await s.registry.get('karen').commit({requestId:crypto.randomUUID(),clientId:'test',label:'华恋修改',changes:[{collection:'cards',uid:current.uid,expectedVersion:current.version,updates:{name:'华恋新名'}}]});
  assert.equal(s.registry.get('core').get({collection:'cards',uid:current.uid}).entity.name,'core');
  s.restart();assert.equal(s.registry.get('karen').get({collection:'cards',uid:current.uid}).entity.name,'华恋新名');assert.equal(s.registry.get('core').history().total,1);
  assert.deepEqual(s.registry.get().get({collection:'cards',id:'J01'}),before);
  assert.throws(()=>s.registry.get('../junna'),{code:'NOT_FOUND'});
});
test('real MCP routes and explicit modId select the same workspace; legacy calls keep Junna',async t=>{
  const s=setup(t),token=crypto.randomBytes(32).toString('hex');
  // Bind an OS-assigned loopback port, then pass the resolved port to host validation.
  const net=require('node:net'),probe=net.createServer();await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));const port=probe.address().port;await new Promise(resolve=>probe.close(resolve));
  const {server}=createDaemon({registry:s.registry,port,token,pageFile:path.join(__dirname,'shell.html')});await new Promise(resolve=>server.listen(port,'127.0.0.1',resolve));
  t.after(async()=>{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));});
  async function connect(route){const client=new Client({name:'test',version:'1.0.0'});await client.connect(new StreamableHTTPClientTransport(new URL('http://127.0.0.1:'+port+route),{requestInit:{headers:{Authorization:'Bearer '+token}}}));t.after(()=>client.close());return client;}
  const legacy=await connect('/mcp'),karen=await connect('/mods/karen/mcp');
  const call=async(client,name,args={})=>(await client.callTool({name,arguments:args})).structuredContent;
  assert.equal((await call(legacy,'workbench_status')).modId,'junna');
  assert.equal((await call(legacy,'workbench_status',{modId:'core'})).modId,'core');
  assert.equal((await call(karen,'workbench_status')).workspaceId,mods.find(mod=>mod.id==='karen').workspaceId);
  const mismatch=await karen.callTool({name:'workbench_status',arguments:{modId:'junna'}});assert.equal(mismatch.structuredContent.code,'INVALID_WORKSPACE');
  assert.equal((await call(legacy,'workbench_list_mods')).mods.length,4);
  const readonlyResult=await legacy.callTool({name:'workbench_commit',arguments:{modId:'sts2',requestId:crypto.randomUUID(),clientId:'test',label:'不可写',changes:[]}});assert.equal(readonlyResult.structuredContent.code,'READ_ONLY');
  const blockedUpload=await fetch('http://127.0.0.1:'+port+'/mods/sts2/upload',{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'image/png','X-Asset-Id':'test-art','X-Request-Id':crypto.randomUUID(),'X-Client-Id':'test'},body:Buffer.from('test image')});assert.equal(blockedUpload.status,403);assert.equal((await blockedUpload.json()).code,'READ_ONLY');
  assert.equal((await fetch('http://127.0.0.1:'+port+'/mods/unknown/mcp',{method:'POST',headers:{Authorization:'Bearer '+token},body:'{}'})).status,404);
  const input={method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'image/png','X-Asset-Id':'test-art','X-Request-Id':crypto.randomUUID(),'X-Client-Id':'test'},body:Buffer.from('test image')};
  const upload=await fetch('http://127.0.0.1:'+port+'/mods/karen/upload',input);assert.equal(upload.status,200);
  assert.ok(s.registry.get('karen').assets(['test-art'])['test-art']);assert.equal(s.registry.get().assets(['test-art'])['test-art'],undefined);
});

test('native reference keeps all three option pools, including shared relics, and rejects every write path',async t=>{
 const s=setup(t),native=s.registry.get('sts2');const bundle=native.read(),data=bundle.data;
 assert.equal(data.meta.nativeSource.gameVersion,'v0.113.0');assert.equal(data.cards.length,716);assert.equal(data.meta.relics.length,315);assert.equal(data.ancients.length,8);
 const relics=new Set(data.meta.relics.map(relic=>relic.id));
 for(const ancient of data.ancients){assert.equal(ancient.optionPools.length,3);for(const pool of ancient.optionPools)for(const id of pool.relicIds)assert.ok(relics.has(id),id);assert.deepEqual([...new Set(ancient.optionPools.flatMap(pool=>pool.relicIds))].sort(),[...ancient.relicIds].sort());}
 const neow=data.ancients.find(ancient=>ancient.id==='NEOW');assert.deepEqual(neow.optionPools[0].relicIds,neow.optionPools[1].relicIds);assert.equal(neow.optionPools[2].relicIds.length,10);
 const bash=data.cards.find(card=>card.id==='BASH');assert.match(bash.upgradedText,/10/);assert.match(bash.upgradedText,/3/);
 const handlers=createWorkspaceHandlers(s.registry,{url:'http://127.0.0.1',modId:'sts2'});
 for(const name of ['workbench_commit','workbench_restore','workbench_import_asset','workbench_export'])await assert.rejects(handlers[name]({}),{code:'READ_ONLY'});
 assert.throws(()=>native.commit({}),{code:'READ_ONLY'});assert.throws(()=>native.read({copy:false}).data.cards.push({}),TypeError);
 assert.deepEqual(native.read(),bundle);
});
