'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),crypto=require('node:crypto');
async function verify(configFile){
 const config=JSON.parse(fs.readFileSync(configFile,'utf8')),url='http://127.0.0.1:'+config.relayPort;
 async function call(name,args={},mod='junna'){
  const route=mod==='junna'?'':'/mods/'+mod;
  const response=await fetch(url+route+'/mcp',{method:'POST',headers:{Authorization:'Bearer '+config.secret,'Content-Type':'application/json',Accept:'application/json, text/event-stream'},body:JSON.stringify({jsonrpc:'2.0',id:1,method:'tools/call',params:{name,arguments:args}})});
  assert.equal(response.status,200,mod+' MCP status');return (await response.json()).result;
 }
 const mods=(await call('workbench_list_mods')).structuredContent.mods;assert.equal(mods.length,4);
 const summary=[];
 for(const mod of mods){
  const status=(await call('workbench_status',{},mod.id)).structuredContent;assert.equal(status.modId,mod.id);
  const page=await fetch(url+(mod.id==='junna'?'/':'/mods/'+mod.id+'/'));assert.equal(page.status,200,mod.id+' page');const html=await page.text();assert.match(html,/少女歌剧工作台/);
  const bundle=(await call('workbench_get_state',{},mod.id)).structuredContent.bundle;
  const filename=(config.webDirectory||'/var/www/junna')+'/current'+(mod.id==='junna'?'':'/mods/'+mod.id)+'/snapshot.json';const published=JSON.parse(fs.readFileSync(filename,'utf8'));assert.equal(published.revision,bundle.revision);
  if(mod.readOnly){assert.equal(bundle.data.cards.length,716);assert.equal(bundle.data.meta.relics.length,315);assert.equal(bundle.data.ancients.length,8);for(const ancient of bundle.data.ancients)assert.equal(ancient.optionPools.length,3);
   const rejected=await call('workbench_commit',{requestId:crypto.randomUUID(),clientId:'release-verification',label:'只读检查',changes:[]},mod.id);assert.equal(rejected.structuredContent.code,'READ_ONLY');
   const response=await fetch(url+'/mods/'+mod.id+'/upload',{method:'POST',headers:{Authorization:'Bearer '+config.secret,'Content-Type':'image/png','X-Asset-Id':'readonly-check','X-Request-Id':crypto.randomUUID(),'X-Client-Id':'release-verification'},body:Buffer.from('read-only check')});assert.equal(response.status,403);assert.equal((await response.json()).code,'READ_ONLY');
   assert.ok(!html.includes('workbench-connection'));assert.ok(!html.includes(config.secret));
  }
  summary.push({mod:mod.id,revision:status.revision,cards:bundle.data.cards.length,readOnly:!!mod.readOnly});
 }
 const denied=await fetch(url+'/mcp',{method:'POST',headers:{Authorization:'Bearer invalid','Content-Type':'application/json'},body:'{}'});assert.equal(denied.status,401);
 return {passed:true,workspaces:summary,nativeWritesRejected:true,invalidTokenRejected:true};
}
if(require.main===module||process.argv[1]==='-')verify(process.argv[2]).then(value=>console.log(JSON.stringify(value))).catch(error=>{console.error(error.message);process.exitCode=1;});
module.exports={verify};