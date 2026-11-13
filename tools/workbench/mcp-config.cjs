'use strict';
const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const configPath=process.env.SHOUJO_WORKBENCH_CONFIG||path.join(__dirname,'local-save.config.json');
const dataPath=path.resolve(__dirname,'../../out/workbench/state.json');
const workspaceId='junna-mod-workbench-20261101-v1';
function ensureConfig(){
  if(!fs.existsSync(configPath))throw Error('请配置工作台 remoteURL 与 remoteToken，或设置 SHOUJO_WORKBENCH_CONFIG');
  const config=JSON.parse(fs.readFileSync(configPath,'utf8').replace(/^\uFEFF/,''));
  if(config.remoteURL){const remote=new URL(config.remoteURL);if(remote.protocol!=='https:'||!/^[a-f0-9]{64}$/.test(config.remoteToken||''))throw Error('远端 MCP 配置无效');return config;}
  if(!Number.isInteger(config.port)||config.port<1024||config.port>65535||!/^[a-f0-9]{64}$/.test(config.token))throw Error('本地 MCP 配置无效');
  return config;
}
module.exports={ensureConfig,dataPath,workspaceId};
