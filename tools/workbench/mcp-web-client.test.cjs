'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
const {WorkbenchStore}=require('./mcp-store.cjs');
const web=require('./mcp-web-client.js');

function fixture(t,{withSnapshot=false}={}){
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'junna-web-race-')),file=path.join(dir,'state.json');
  const seed={schemaVersion:1,workspaceId:'race-test',revision:1,history:[],assets:{},data:{title:'测试',meta:{},cards:[{uid:'a',id:'A',name:'原名',text:'原文',cost:1,type:'技能'}],terms:[],mechanisms:[],partnerGroups:[],archetypes:[],styles:[],fitPool:[]}};
  fs.writeFileSync(file,JSON.stringify(seed));t.after(()=>fs.rmSync(dir,{recursive:true,force:true}));
  const store=new WorkbenchStore({file,seed,workspaceId:seed.workspaceId});
  const initialSnapshot=withSnapshot?store.state({includeAssets:true}):null;
  let hold=null,bundle=initialSnapshot?.bundle;const requests=[];
  const originalFetch=global.fetch;
  global.fetch=async(_url,options)=>{
    const request=JSON.parse(options.body);
    if(request.method==='initialize')return {ok:true,json:async()=>({result:{}})};
    const {name,arguments:args}=request.params;
    requests.push({name,args});
    try{
      let value;
      if(name==='workbench_get_state')value=store.state(args);
      else if(name==='workbench_get_entity')value=store.get(args);
      else if(name==='workbench_commit')value=await store.commit(args);
      else if(name==='workbench_get_assets')value={assets:{}};
      else throw Error(name);
      if(name==='workbench_get_state'&&hold){const gate=hold;hold=null;gate.captured();await gate.wait;}
      return {ok:true,json:async()=>({result:{structuredContent:value}})};
    }catch(error){return {ok:true,json:async()=>({result:{isError:true,structuredContent:{code:error.code,message:error.message,...error.details}}})};}
  };
  t.after(()=>{global.fetch=originalFetch;});
  const controller=web.create({connection:{baseURL:'http://127.0.0.1:1',token:'test'},initialSnapshot,clientId:'web',uuid:randomUUID,getAssets:()=>bundle.assets,onSnapshot:value=>{bundle=value;},onCommit:()=>{},onStatus:()=>{},onDraft:()=>{}});
  async function remote(collection,uid,updates){const current=store.get({collection,uid});return store.commit({requestId:randomUUID(),clientId:'other',label:'其他条目修改',changes:[{collection,uid,expectedVersion:current.version,before:current.entity,after:{...current.entity,...updates}}]});}
  async function edit(updates,base){const before=base||structuredClone(bundle.data.cards[0]),after={...before,...updates};bundle.data.cards[0]=after;controller.enqueue({id:randomUUID(),kind:'编辑',label:'卡牌编辑',patches:[{collection:'cards',uid:'a',before,after}]});await controller.flush();}
  async function delayedPull(){let release,captured;const wait=new Promise(resolve=>{release=resolve;}),started=new Promise(resolve=>{captured=resolve;});hold={wait,captured};const pending=controller.pull();await started;return {release,done:pending};}
  return {store,controller,remote,edit,delayedPull,requests,get bundle(){return bundle;}};
}

test('web saves send changed fields and merge concurrent MCP edits',async t=>{
  const f=fixture(t);await f.controller.start();
  await f.remote('cards','a',{notes:'MCP 修改的备注'});
  await f.edit({text:'网页修改的牌效'});
  assert.equal(f.controller.status().conflict,null);
  const saved=f.store.get({collection:'cards',uid:'a'}).entity;
  assert.equal(saved.notes,'MCP 修改的备注');assert.equal(saved.text,'网页修改的牌效');
  const change=f.requests.find(request=>request.name==='workbench_commit').args.changes[0];
  assert.deepEqual(change.updates,{text:'网页修改的牌效'});
  assert.deepEqual(change.expectedFields,{text:'原文'});
  assert.equal(Object.hasOwn(change,'before'),false);assert.equal(Object.hasOwn(change,'after'),false);
});

test('static image URLs are never submitted as new uploaded assets',async t=>{
  const f=fixture(t);await f.controller.start();
  f.bundle.assets['static-frame']='/assets/frame.png';
  await f.edit({text:'正常保存'});
  assert.equal(f.controller.status().conflict,null);
  assert.deepEqual(f.requests.find(request=>request.name==='workbench_commit').args.assets,{});
});

test('old saved drafts discard static URL uploads before retrying',async t=>{
  const f=fixture(t),current=f.store.get({collection:'cards',uid:'a'}),id=randomUUID();
  const record={id,kind:'编辑',label:'旧版草稿',patches:[{collection:'cards',uid:'a',before:current.entity,after:{...current.entity,text:'恢复修改'}}]};
  const assets={frame:'/assets/frame.png'};
  const payload={requestId:id,clientId:'web',label:record.label,kind:record.kind,reference:null,changes:record.patches.map(p=>({...p,expectedVersion:current.version})),assets};
  await f.controller.start({versions:{'cards/a':current.version},outbox:[{record,assets,payload}]});
  await f.controller.flush();
  assert.equal(f.controller.status().conflict,null);
  assert.equal(f.store.get({collection:'cards',uid:'a'}).entity.text,'恢复修改');
  assert.deepEqual(f.requests.find(request=>request.name==='workbench_commit').args.assets,{});
});

test('recovered edits survive a failed startup and retry with the same request',async t=>{
  const f=fixture(t),current=f.store.get({collection:'cards',uid:'a'}),requestId=randomUUID();
  const recovered={versions:{['cards/a']:current.version},outbox:[{record:{id:requestId,kind:'编辑',label:'恢复草稿',patches:[{collection:'cards',uid:'a',before:current.entity,after:{...current.entity,text:'离线草稿'}}]},assets:{}}]};
  const onlineFetch=global.fetch;global.fetch=async()=>{throw Error('offline');};
  assert.equal(await f.controller.start(recovered),false);
  assert.equal(f.controller.draft().outbox.length,1);
  global.fetch=onlineFetch;
  await f.controller.flush();
  assert.equal(f.controller.status().pending,0);
  assert.equal(f.store.get({collection:'cards',uid:'a'}).entity.text,'离线草稿');
  assert.equal(f.store.history().records[0].id,requestId);
});

test('a failed conflict refresh preserves the queued draft',async t=>{
  const f=fixture(t);await f.controller.start();
  await f.remote('cards','a',{text:'另一端修改'});await f.edit({text:'网页草稿'});
  assert.equal(f.controller.status().conflict.code,'VERSION_CONFLICT');
  const before=f.controller.draft();
  const onlineFetch=global.fetch;global.fetch=async()=>{throw Error('offline');};
  await assert.rejects(f.controller.replace([]),/offline/);
  global.fetch=onlineFetch;
  assert.deepEqual(f.controller.draft().outbox,before.outbox);
});

test('the editor connects without AbortSignal.timeout in older mobile browsers',async t=>{
  const originalTimeout=AbortSignal.timeout;
  AbortSignal.timeout=undefined;
  t.after(()=>{AbortSignal.timeout=originalTimeout;});
  const f=fixture(t);
  assert.equal(await f.controller.start(),true,'手机浏览器缺少超时 API 时仍应连接 MCP');
  assert.equal(f.controller.status().ready,true);
  assert.equal(f.bundle.data.cards[0].name,'原名');
});

test('initial synchronization keeps historical patches out of the card snapshot',async t=>{
  const f=fixture(t);
  const saved=await f.remote('cards','a',{name:'最新卡牌'});
  assert.equal(await f.controller.start(),true);
  assert.equal(f.bundle.data.cards[0].name,'最新卡牌');
  assert.deepEqual(f.bundle.history,[]);
  assert.equal(f.bundle.historyTotal,1);
  assert.equal(f.bundle.cardUpdatedAt.a,saved.revision);
});

test('the MCP timeout also covers reading the response body',{timeout:1000},async t=>{
  const originalFetch=global.fetch;
  t.after(()=>{global.fetch=originalFetch;});
  global.fetch=async(_url,options)=>({ok:true,json:()=>new Promise((resolve,reject)=>{
    options.signal.addEventListener('abort',()=>reject(options.signal.reason),{once:true});
  })});
  const controller=web.create({connection:{baseURL:'http://127.0.0.1:1',token:'test',timeoutMs:5},clientId:'timeout-test',uuid:randomUUID,getAssets:()=>({}),onSnapshot:()=>{},onCommit:()=>{},onStatus:()=>{},onDraft:()=>{}});
  assert.equal(await controller.start(),false);
  assert.equal(controller.status().ready,false);
  assert.match(controller.status().error.message,/abort|time/i);
});

test('a delayed sync cannot rewind a card version after its successful save',async t=>{
  const f=fixture(t);await f.controller.start();
  await f.remote('root','meta',{otherEntry:'changed'});
  const sync=await f.delayedPull();
  await f.edit({name:'已保存的卡名'});
  sync.release();await sync.done;
  assert.equal(f.bundle.data.cards[0].name,'已保存的卡名','旧同步响应覆盖了刚保存的卡牌');
  await f.edit({text:'继续编辑'});
  assert.equal(f.controller.status().conflict,null,'自己的成功提交不应导致下一次编辑版本冲突');
  assert.equal(f.store.get({collection:'cards',uid:'a'}).entity.text,'继续编辑');
});

for(const resolution of ['server','merge'])test('old sync cannot resurrect a conflict after choosing '+resolution,async t=>{
  const f=fixture(t);await f.controller.start();
  await f.remote('root','meta',{otherEntry:'changed'});
  const sync=await f.delayedPull();
  await f.remote('cards','a',{name:'服务器卡名',text:'服务器牌效'});
  await f.edit({text:'本地牌效'});
  assert.equal(f.controller.status().conflict.code,'VERSION_CONFLICT');
  if(resolution==='server')await f.controller.replace([]);
  else {const proposals=await f.controller.proposals();await f.controller.replace(proposals.map(({collection,uid,expectedVersion,before,after,local})=>({collection,uid,expectedVersion,before,after:{...after,text:local.text}})));}
  sync.release();await sync.done;
  assert.equal(f.bundle.data.cards[0].name,'服务器卡名','旧同步响应回退了冲突处理后的条目');
  await f.edit({cost:2});
  assert.equal(f.controller.status().conflict,null,'点击冲突处理后继续编辑又发生冲突');
  const card=f.store.get({collection:'cards',uid:'a'}).entity;
  assert.equal(card.name,'服务器卡名');assert.equal(card.cost,2);
  assert.equal(card.text,resolution==='merge'?'本地牌效':'服务器牌效');
});

test('starting a field edit protects its unsubmitted draft from an earlier sync',async t=>{
  const f=fixture(t);await f.controller.start();await f.remote('root','meta',{otherEntry:'changed'});
  const sync=await f.delayedPull(),base=structuredClone(f.bundle.data.cards[0]);f.controller.invalidateSnapshots();f.bundle.data.cards[0].text='尚未到自动保存时点的输入';
  sync.release();await sync.done;
  assert.equal(f.bundle.data.cards[0].text,'尚未到自动保存时点的输入');
  await f.edit({text:'完成输入'},base);assert.equal(f.controller.status().conflict,null);
});


test('slow polling shares the in-flight snapshot instead of starting another download',async t=>{
  const f=fixture(t);await f.controller.start();
  await f.remote('cards','a',{name:'服务器更新'});
  const before=f.requests.filter(r=>r.name==='workbench_get_state').length;
  const slow=await f.delayedPull(),second=f.controller.pull();
  try{
    assert.equal(f.requests.filter(r=>r.name==='workbench_get_state').length-before,1,'5秒轮询不能重开尚未完成的慢请求');
  }finally{slow.release();await Promise.all([slow.done,second]);}
  assert.equal(f.controller.status().ready,true);assert.equal(f.bundle.data.cards[0].name,'服务器更新');
});

test('first connection uses the downloaded snapshot as an incremental baseline',async t=>{
  const f=fixture(t,{withSnapshot:true});
  await f.remote('cards','a',{name:'下载快照后的新内容'});
  const current=f.store.get({collection:'cards',uid:'a'}),originalFetch=global.fetch;
  let incremental=false;
  global.fetch=async(url,options)=>{
    const request=JSON.parse(options.body),args=request.params?.arguments;
    if(request.params?.name==='workbench_get_state'&&args.incremental&&args.knownRevision===1){
      incremental=true;
      return {ok:true,json:async()=>({result:{structuredContent:{delta:true,changes:[{collection:'cards',uid:'a',after:current.entity,afterIndex:0}],versions:{'cards/a':current.version},assetIds:[],assets:{},historyTotal:1,cardUpdatedAt:{a:current.revision},revision:current.revision}}})};
    }
    return originalFetch(url,options);
  };
  assert.equal(await f.controller.start(),true);
  assert.equal(incremental,true,'首次连接应复用已下载的快照，避免再下载完整内容');
  assert.equal(f.bundle.data.cards[0].name,'下载快照后的新内容');
  assert.equal(f.controller.status().revision,current.revision);
  await f.edit({text:'增量同步后继续编辑'});assert.equal(f.controller.status().conflict,null);
});

test('reconnecting after a failed startup retains the downloaded snapshot baseline',async t=>{
  const f=fixture(t,{withSnapshot:true}),onlineFetch=global.fetch;
  global.fetch=async()=>{throw Error('offline');};assert.equal(await f.controller.start(),false);
  global.fetch=onlineFetch;await f.controller.pull();
  const request=f.requests.find(r=>r.name==='workbench_get_state');
  assert.equal(request.args.knownRevision,1,'重连应沿用本地已下载版本');
  assert.equal(request.args.incremental,true);
  assert.equal(f.controller.status().ready,true);assert.equal(f.bundle.data.cards[0].name,'原名');
});
