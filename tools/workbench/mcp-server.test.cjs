'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const http=require('node:http');
const {randomUUID,randomBytes}=require('node:crypto');
const {Client}=require('@modelcontextprotocol/sdk/client/index.js');
const {StreamableHTTPClientTransport}=require('@modelcontextprotocol/sdk/client/streamableHttp.js');
const {StdioClientTransport}=require('@modelcontextprotocol/sdk/client/stdio.js');
const {createDaemon}=require('./mcp-daemon.cjs');
const {workspaceId}=require('./mcp-config.cjs');
const web=require('./mcp-web-client.js');
async function service(t,sqlite=false){const dir=fs.mkdtempSync(path.join(os.tmpdir(),'junna-mcp-http-')),file=path.join(dir,sqlite?'state.sqlite':'state.json'),seed={workspaceId,schemaVersion:1,revision:1,history:[],assets:{},data:{title:'测试',meta:{},cards:[{uid:'a',id:'A',name:'原名',text:'原文',type:'技能',cost:1}],terms:[],mechanisms:[],partnerGroups:[],archetypes:[],styles:[],fitPool:[]}};if(sqlite){const db=new (require('./mcp-sqlite.cjs').SqliteStorage)(file);db.import(seed);db.close();}else fs.writeFileSync(file,JSON.stringify(seed));const probe=http.createServer();await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));const port=probe.address().port;await new Promise(resolve=>probe.close(resolve));const token=randomBytes(32).toString('hex'),baseURL='http://127.0.0.1:'+port,{server,store}=createDaemon({file,seed,port,token,pageFile:path.join(__dirname,'shell.html')});await new Promise(resolve=>server.listen(port,'127.0.0.1',resolve));t.after(async()=>{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));store.storage?.close();fs.rmSync(dir,{recursive:true,force:true});});return {baseURL,token,store};}
async function client(t,s){const c=new Client({name:'test',version:'1.0.0'});await c.connect(new StreamableHTTPClientTransport(new URL(s.baseURL+'/mcp'),{requestInit:{headers:{Authorization:'Bearer '+s.token}}}));t.after(()=>c.close());return c;}

test('binary uploads return durable HTTP image URLs and replay safely',async t=>{
  const serviceInfo=await service(t,true);
  const requestId=randomUUID(),bytes=Buffer.from('uploaded image');
  const options={method:'POST',headers:{Authorization:'Bearer '+serviceInfo.token,'Content-Type':'image/png','X-Asset-Id':'art_test','X-Request-Id':requestId,'X-Client-Id':'web'},body:bytes};
  const response=await fetch(serviceInfo.baseURL+'/upload',options);
  assert.equal(response.status,200);
  const result=await response.json();
  assert.match(result.url,/^\/assets\/[a-f0-9]+\.png$/);
  assert.equal((await(await fetch(serviceInfo.baseURL+'/upload',options)).json()).replayed,true);
  assert.equal(serviceInfo.store.assets(['art_test'],true).art_test,'data:image/png;base64,'+bytes.toString('base64'));
  assert.equal(serviceInfo.store.history().total,1);
});
test('real MCP clients share versions and return structured conflicts; legacy PUT is disabled',async t=>{const s=await service(t),a=await client(t,s),b=await client(t,s);assert((await a.listTools()).tools.some(x=>x.name==='workbench_commit'));const get=async c=>(await c.callTool({name:'workbench_get_entity',arguments:{collection:'cards',id:'A'}})).structuredContent,first=await get(a),second=await get(b);const edit=(e,name)=>({requestId:randomUUID(),clientId:name,label:'修改名称',changes:[{collection:'cards',uid:e.uid,expectedVersion:e.version,updates:{name}}]});assert(!(await a.callTool({name:'workbench_commit',arguments:edit(first,'甲')})).isError);const conflict=await b.callTool({name:'workbench_commit',arguments:edit(second,'乙')});assert(conflict.isError);assert.equal(conflict.structuredContent.code,'VERSION_CONFLICT');assert.equal(conflict.structuredContent.conflicts[0].current.name,'甲');const legacy=await fetch(s.baseURL+'/file',{method:'PUT',headers:{Authorization:'Bearer '+s.token},body:'{}'});assert.equal(legacy.status,410);assert.equal((await legacy.json()).code,'MCP_REQUIRED');assert.equal((await fetch(s.baseURL+'/mcp',{method:'POST',headers:{Authorization:'Bearer '+s.token,Origin:'https://example.com'},body:'{}'})).status,403);});
test('web queue submits via MCP, preserves conflicting drafts and merges separate fields',async t=>{const s=await service(t);function browser(id){let bundle;const drafts=[],controller=web.create({connection:s,clientId:id,uuid:randomUUID,getAssets:()=>bundle.assets,onSnapshot:b=>{bundle=b;},onCommit:()=>{},onStatus:()=>{},onDraft:d=>drafts.push(d)});return {controller,drafts,get bundle(){return bundle;}};}const a=browser('web-a'),b=browser('web-b');await a.controller.start();await b.controller.start();function edit(web,updates){const before=structuredClone(web.bundle.data.cards[0]),after={...before,...updates};web.bundle.data.cards[0]=after;web.controller.enqueue({id:randomUUID(),kind:'编辑',label:'网页编辑',patches:[{collection:'cards',uid:'a',before,after}]});return web.controller.flush();}await edit(a,{name:'服务器名字',text:'服务器文本'});await edit(b,{text:'本地新文本'});assert.equal(b.controller.status().conflict.code,'VERSION_CONFLICT');assert.equal(b.controller.draft().outbox[0].record.patches[0].after.text,'本地新文本');const proposals=await b.controller.proposals();assert.equal(proposals[0].before.name,'服务器名字');assert.equal(proposals[0].after.name,'服务器名字');assert.equal(proposals[0].after.text,'服务器文本');await b.controller.replace(proposals.map(({collection,uid,expectedVersion,before,after})=>({collection,uid,expectedVersion,before,after:{...after,text:'本地新文本'}})));assert.equal(b.controller.status().pending,0);assert.equal(s.store.get({collection:'cards',id:'A'}).entity.text,'本地新文本');assert.equal(s.store.get({collection:'cards',id:'A'}).entity.name,'服务器名字');});
