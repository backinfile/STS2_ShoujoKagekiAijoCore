'use strict';
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {Readable}=require('node:stream');
const {createGzip}=require('node:zlib');
const here=__dirname,config=JSON.parse(fs.readFileSync(path.join(here,'relay.config.json'),'utf8'));
const ensureConfig=()=>JSON.parse(fs.readFileSync(config.backendConfig,'utf8'));
const allowed=new Set(['workbench_list_mods','workbench_status','workbench_query','workbench_get_entity','workbench_get_state','workbench_get_assets','workbench_history','workbench_commit','workbench_restore']);
const server=http.createServer(async(req,res)=>{
  const reply=(status,value)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});res.end(JSON.stringify(value));};
  res.setHeader('Cache-Control','no-store');res.setHeader('Referrer-Policy','no-referrer');
  if(req.headers.origin&&req.headers.origin!==config.origin)return reply(403,{error:'来源无效'});
  const match=/^\/mods\/([a-z0-9_-]+)(\/.*)$/.exec(req.url);let modId='junna',route=req.url;
  if(match){modId=match[1];route=match[2];}
  try{require('./mods.cjs').getMod(modId);}catch{return reply(404,{error:'Mod 不存在'});}
  const relative=modId==='junna'?'':path.join('mods',modId),modPath=modId==='junna'?'':'/mods/'+modId;
  if(req.method==='GET'&&route==='/'){res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});return fs.createReadStream(path.join(here,relative,'editor.html')).pipe(res);}
  if(['GET','HEAD'].includes(req.method)&&route==='/app.webmanifest'){res.writeHead(200,{'Content-Type':'application/manifest+json; charset=utf-8'});return req.method==='HEAD'?res.end():fs.createReadStream(path.join(here,relative,'app.webmanifest')).pipe(res);}
  if(req.method!=='POST'||!['/mcp','/upload'].includes(route))return reply(404,{error:'路径无效'});
  const expected=Buffer.from('Bearer '+config.secret),auth=Buffer.from(String(req.headers.authorization||''));
  if(auth.length!==expected.length||!crypto.timingSafeEqual(auth,expected))return reply(401,{error:'连接无效'});
  try{
    let size=0;const chunks=[];for await(const chunk of req){size+=chunk.length;if(size>128*1024*1024)return reply(413,{error:'提交过大'});chunks.push(chunk);}
    if(route==='/upload'){
      const local=ensureConfig(),url='http://127.0.0.1:'+local.port;
      const upstream=await fetch(url+modPath+'/upload',{method:'POST',headers:{Authorization:'Bearer '+local.token,Origin:url,'Content-Type':req.headers['content-type'],'X-Asset-Id':req.headers['x-asset-id'],'X-Request-Id':req.headers['x-request-id'],'X-Client-Id':req.headers['x-client-id']},body:Buffer.concat(chunks),signal:AbortSignal.timeout(90000)});
      return reply(upstream.status,await upstream.json());
    }
    const body=JSON.parse(Buffer.concat(chunks).toString('utf8'));
    if(!['initialize','ping','tools/list','tools/call','notifications/initialized','notifications/cancelled'].includes(body.method))return reply(403,{error:'方法无效'});
    if(body.method==='tools/call'&&!allowed.has(body.params?.name))return reply(403,{error:'工具无效'});
    const local=ensureConfig(),url='http://127.0.0.1:'+local.port;
    const upstream=await fetch(url+modPath+'/mcp',{method:'POST',headers:{Authorization:'Bearer '+local.token,Origin:url,'Content-Type':'application/json',Accept:'application/json, text/event-stream','MCP-Protocol-Version':'2025-03-26'},body:JSON.stringify(body),signal:AbortSignal.timeout(90000)});
    if(!upstream.body){res.writeHead(upstream.status);return res.end();}
    if(body.method==='tools/list'){
      const value=await upstream.json();if(value.result?.tools)value.result.tools=value.result.tools.filter(tool=>allowed.has(tool.name));return reply(upstream.status,value);
    }
    if(body.method==='tools/call'&&body.params.name==='workbench_status'){
      const value=await upstream.json(),status=value.result?.structuredContent;if(status){status.url=config.origin+config.prefix+modPath+'/';value.result.content=[{type:'text',text:JSON.stringify(status)}];}return reply(upstream.status,value);
    }
    const compressed=/\bgzip\b/.test(req.headers['accept-encoding']||'');
    res.writeHead(upstream.status,{'Content-Type':upstream.headers.get('content-type')||'application/json',...(compressed?{'Content-Encoding':'gzip',Vary:'Accept-Encoding'}:{})});
    const stream=Readable.fromWeb(upstream.body).on('error',()=>res.destroy());
    if(compressed)stream.pipe(createGzip({level:3})).on('error',()=>res.destroy()).pipe(res);else stream.pipe(res);
  }catch(error){if(!res.headersSent)reply(502,{error:'工作台连接中断'});else res.destroy();}
});
server.listen(config.relayPort,'127.0.0.1',()=>{fs.writeFileSync(path.join(path.dirname(config.backendConfig),'relay.runtime.json'),JSON.stringify({pid:process.pid,port:config.relayPort,startedAt:new Date().toISOString()})+'\n');console.log('Shoujo relay ready');});

module.exports=server;
