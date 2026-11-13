'use strict';
const fs=require('node:fs');
const path=require('node:path');
const {spawn}=require('node:child_process');
const {Client}=require('@modelcontextprotocol/sdk/client/index.js');
const {StreamableHTTPClientTransport}=require('@modelcontextprotocol/sdk/client/streamableHttp.js');
const {StdioServerTransport}=require('@modelcontextprotocol/sdk/server/stdio.js');
const {createProtocolServer,definitions}=require('./mcp-tools.cjs');
const {ensureConfig}=require('./mcp-config.cjs');
async function ensureDaemon(){
  const config=ensureConfig();
  if(config.remoteURL){const remote=new URL(config.remoteURL);if(remote.protocol!=='https:'||!/^([a-f0-9]{64})$/.test(config.remoteToken||''))throw Error('远端 MCP 配置无效');return {baseURL:remote.href.replace(/\/$/,''),headers:{Authorization:'Bearer '+config.remoteToken}};}
  const baseURL='http://127.0.0.1:'+config.port,headers={Authorization:'Bearer '+config.token};
  const check=async()=>{try{const r=await fetch(baseURL+'/health',{headers,signal:AbortSignal.timeout(1500)});if(!r.ok)return false;const health=await r.json();if(health.connected&&!['junna-workbench-mcp','shoujo-workbench-mcp'].includes(health.serverType))throw Error('旧保存服务仍在运行，请先执行 start-mcp.ps1');return ['junna-workbench-mcp','shoujo-workbench-mcp'].includes(health.serverType);}catch(error){if(error.message.includes('旧保存服务'))throw error;return false;}};
  if(!await check()){const out=fs.openSync(path.join(__dirname,'mcp.stdout.log'),'a'),err=fs.openSync(path.join(__dirname,'mcp.stderr.log'),'a');try{const child=spawn(process.execPath,[path.join(__dirname,'mcp-daemon.cjs')],{cwd:__dirname,detached:true,windowsHide:true,stdio:['ignore',out,err]});child.unref();}finally{fs.closeSync(out);fs.closeSync(err);}let ready=false;for(let i=0;i<60;i++){await new Promise(resolve=>setTimeout(resolve,100));if(await check()){ready=true;break;}}if(!ready)throw Error('本地 MCP 启动失败，请查看 mcp.stderr.log');}
  return {baseURL,headers};
}
async function connectClient(modId){const {baseURL,headers}=await ensureDaemon(),client=new Client({name:'junna-local-client',version:'1.0.0'});await client.connect(new StreamableHTTPClientTransport(new URL(baseURL+(modId?'/mods/'+require('./mods.cjs').getMod(modId).id:'')+'/mcp'),{requestInit:{headers}}));return client;}
async function startStdio(){const client=await connectClient(),handlers=Object.fromEntries(definitions.map(def=>[def.name,async args=>{const result=await client.callTool({name:def.name,arguments:args});if(result.isError){const v=result.structuredContent||JSON.parse(result.content[0].text);const error=Error(v.message);error.code=v.code;error.details=v;throw error;}return result.structuredContent;} ]));const server=createProtocolServer(handlers);await server.connect(new StdioServerTransport());process.stdin.on('end',()=>client.close());}
if(require.main===module){(process.argv.includes('--start')?ensureDaemon().then(({baseURL})=>process.stdout.write(baseURL+'\n')):startStdio()).catch(error=>{process.stderr.write(error.message+'\n');process.exitCode=1;});}
module.exports={ensureDaemon,connectClient};
