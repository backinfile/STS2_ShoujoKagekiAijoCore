'use strict';
const fs=require('node:fs'),path=require('node:path');
const {mods,defaultMod,getMod,emptyBundle}=require('./mods.cjs');
const {SqliteStorage}=require('./mcp-sqlite.cjs');
const {WorkbenchStore}=require('./mcp-store.cjs');
function createRegistry({stateDirectory,assetDirectory,legacyFile=path.join(stateDirectory,'state.sqlite')}){
  const stores=new Map();
  try{for(const mod of mods){
    if(mod.readOnly){stores.set(mod.id,require('./native-store.cjs').createNativeStore(path.join(__dirname,'native-game.json')));continue;}
    const file=mod.id===defaultMod?legacyFile:path.join(stateDirectory,'mods',mod.id+'.sqlite');
    // The existing Junna database is authoritative; never initialize it from a build snapshot.
    if(mod.id===defaultMod&&!fs.existsSync(file))throw Error('缺少原纯那主台数据库');
    const storage=new SqliteStorage(file,{assetDirectory});
    try{if(storage.signature()===undefined){if(mod.id===defaultMod)throw Error('原纯那数据库未初始化');storage.import(emptyBundle(mod.id));}}finally{storage.close();}
    const store=new WorkbenchStore({file,assetDirectory,workspaceId:mod.workspaceId,seed:emptyBundle(mod.id)});
    stores.set(mod.id,store);store.read({copy:false});
  }}catch(error){for(const store of stores.values())store.storage.close();throw error;}
  return {get:(id=defaultMod)=>{getMod(id);return stores.get(id);},list:()=>mods.map(mod=>({...mod,revision:stores.get(mod.id).read({copy:false}).revision})),close:()=>{for(const store of stores.values())store.storage.close();}};
}
function createWorkspaceHandlers(registry,{url,modId}){
  const {definitions,createToolHandlers}=require('./mcp-tools.cjs');
  return Object.fromEntries(definitions.map(({name})=>[name,async(args={})=>{
    const selected=modId||args.modId||defaultMod;
    if(modId&&args.modId&&args.modId!==modId){const error=Error('请求 Mod 与当前工作区不一致');error.code='INVALID_WORKSPACE';throw error;}
    if(getMod(selected).readOnly&&!definitions.find(tool=>tool.name===name)?.annotations.readOnlyHint){const error=Error('游戏本体资料不可修改');error.code='READ_ONLY';throw error;}
    if(name==='workbench_list_mods')return {mods:registry.list(),currentMod:selected};
    const {modId:requested,...input}=args;
    const handlers=createToolHandlers(registry.get(selected),{url:url+(selected===defaultMod?'':'/mods/'+selected)});
    const result=await handlers[name](input);
    return name==='workbench_status'?{...result,modId:selected,mods:registry.list()}:result;
  }]));
}
module.exports={createRegistry,createWorkspaceHandlers};
