'use strict';
const http=require('node:http');
const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const {StreamableHTTPServerTransport}=require('@modelcontextprotocol/sdk/server/streamableHttp.js');
const {WorkbenchStore}=require('./mcp-store.cjs');
const {createToolHandlers,createProtocolServer}=require('./mcp-tools.cjs');
const {ensureConfig,dataPath,workspaceId}=require('./mcp-config.cjs');
function createDaemon({file=dataPath,seed=JSON.parse(fs.readFileSync(path.join(__dirname,'seed.json'),'utf8')),token,port,assetDirectory,registry,pageFile=path.resolve(__dirname,'../纯那Mod设计工作台.html')}={}){
  const store=registry?registry.get():new WorkbenchStore({file,seed,workspaceId,assetDirectory}),url='http://127.0.0.1:'+port,handlers=createToolHandlers(store,{url});
  const server=http.createServer(async(req,res)=>{
    const reply=(status,value)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});res.end(JSON.stringify(value));};
    let modId;const route=new URL(req.url,'http://localhost').pathname;
    const scoped=/^\/mods\/([^/]+)(\/.*)$/.exec(route);
    if(scoped){modId=scoped[1];req.url=scoped[2];}
    let requestStore=store,requestHandlers=handlers;
    try{if(registry){requestStore=registry.get(modId);requestHandlers=require('./workspace-registry.cjs').createWorkspaceHandlers(registry,{url,modId});}else if(scoped)return reply(404,{error:'Mod 不存在'});}catch(error){return reply(404,{error:error.message});}
    const host=req.headers.host,origin=req.headers.origin;
    if(!['127.0.0.1:'+port,'localhost:'+port].includes(host))return reply(403,{error:'主机无效'});
    if(origin&&origin!=='null'&&![url,'http://localhost:'+port].includes(origin))return reply(403,{error:'来源无效'});
    if(origin)res.setHeader('Access-Control-Allow-Origin',origin);
    res.setHeader('Vary','Origin');res.setHeader('Cache-Control','no-store');
    if(req.method==='OPTIONS'){res.setHeader('Access-Control-Allow-Methods','GET, POST, OPTIONS');res.setHeader('Access-Control-Allow-Headers','Authorization, Content-Type, MCP-Protocol-Version, Accept');return res.writeHead(204).end();}
    if(req.method==='GET'&&['/','/index.html'].includes(req.url)){res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});return fs.createReadStream(pageFile).pipe(res);}
    const auth=String(req.headers.authorization||'');if(auth.length!==71||!crypto.timingSafeEqual(Buffer.from(auth),Buffer.from('Bearer '+token)))return reply(401,{error:'本地 MCP 连接无效'});
    if(req.url==='/health'&&req.method==='GET')return reply(200,{connected:true,workspaceId:requestStore.workspaceId,serverType:'shoujo-workbench-mcp',url});
    if(req.url==='/file')return reply(410,{error:'请通过 MCP 读取和提交条目',code:'MCP_REQUIRED'});
    if(req.url==='/upload'&&req.method==='POST'){
      try{
        const mime=String(req.headers['content-type']);
        if(!/^image\/(png|jpeg|webp|gif)$/.test(mime))return reply(400,{message:'图片类型无效'});
        let size=0;const chunks=[];
        for await(const chunk of req){size+=chunk.length;if(size>16*1024*1024)return reply(413,{message:'图片超过16MB'});chunks.push(chunk);}
        const assetId=String(req.headers['x-asset-id']||'');
        const result=await requestStore.commit({requestId:String(req.headers['x-request-id']||''),clientId:String(req.headers['x-client-id']||''),label:'导入图片',changes:[],assets:{[assetId]:'data:'+mime+';base64,'+Buffer.concat(chunks).toString('base64')}});
        return reply(200,{...result,url:requestStore.assets([assetId])[assetId]});
      }catch(error){return reply(error.code==='READ_ONLY'?403:400,{message:error.message,code:error.code});}
    }
    if(req.url!=='/mcp')return reply(404,{error:'路径无效'});
    if(req.method!=='POST')return reply(405,{error:'使用 MCP POST 请求'});
    let protocol,transport;
    try{const chunks=[];let size=0;for await(const chunk of req){size+=chunk.length;if(size>128*1024*1024)return reply(413,{error:'提交超过128MB'});chunks.push(chunk);}const body=JSON.parse(Buffer.concat(chunks).toString('utf8'));protocol=createProtocolServer(requestHandlers);transport=new StreamableHTTPServerTransport({sessionIdGenerator:undefined,enableJsonResponse:true});await protocol.connect(transport);res.on('close',()=>{transport.close().catch(()=>{});protocol.close().catch(()=>{});});await transport.handleRequest(req,res,body);}catch(error){if(!res.headersSent)reply(500,{error:'MCP 请求处理失败'});else res.destroy();}
  });
  return {server,store};
}
function start(){const config=ensureConfig(),{server}=createDaemon(config);server.on('error',error=>{process.stderr.write(error.message+'\n');process.exitCode=1;});server.listen(config.port,'127.0.0.1',()=>{fs.writeFileSync(path.join(__dirname,'local-save.runtime.json'),JSON.stringify({pid:process.pid,port:config.port,serverType:'junna-workbench-mcp',startedAt:new Date().toISOString()},null,2)+'\n');process.stdout.write('纯那工作台 MCP 已启动\n');});return server;}
if(require.main===module)start();
module.exports={createDaemon,start};
