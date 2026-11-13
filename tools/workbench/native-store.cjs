'use strict';
const fs=require('node:fs'),crypto=require('node:crypto');
const {WorkbenchError}=require('./mcp-store.cjs');
const clone=value=>JSON.parse(JSON.stringify(value));
const readonly=()=>{throw new WorkbenchError('READ_ONLY','游戏本体资料不可修改');};
function createNativeStore(file){
  const bundle=JSON.parse(fs.readFileSync(file,'utf8'));const digest=crypto.createHash('sha256').update(JSON.stringify(bundle)).digest('hex'),version=bundle.revision+':'+digest;
  const freeze=value=>{if(value&&typeof value==='object'){for(const child of Object.values(value))freeze(child);Object.freeze(value);}return value;};freeze(bundle);
  const collections={cards:bundle.data.cards,relics:bundle.data.meta.relics,ancients:bundle.data.ancients};
  function list(collection){if(!Object.hasOwn(collections,collection))throw new WorkbenchError('INVALID_ARGUMENT','本体条目类型无效');return collections[collection];}
  const versions=Object.fromEntries(Object.entries(collections).flatMap(([collection,entries])=>entries.map(entry=>[collection+'/'+entry.uid,version])));
  return {workspaceId:bundle.workspaceId,read:({copy=true}={})=>copy?clone(bundle):bundle,storage:{close(){}},
    state:({knownRevision}={})=>knownRevision===bundle.revision?{unchanged:true,revision:bundle.revision}:clone({bundle:{...bundle,versions},versions,assetIds:[],revision:bundle.revision}),
    get:({collection,uid,id})=>{const entry=list(collection).find(entry=>uid?entry.uid===uid:entry.id===id);if(!entry)throw new WorkbenchError('NOT_FOUND','条目不存在');return clone({collection,uid:entry.uid,entity:entry,version,revision:bundle.revision});},
    query:({collection='cards',search='',limit=50,offset=0,fields}={})=>{const matching=list(collection).filter(entry=>JSON.stringify(entry).toLowerCase().includes(String(search).toLowerCase()));return clone({collection,total:matching.length,entries:matching.slice(Math.max(0,offset),Math.max(0,offset)+Math.min(300,Math.max(1,limit))).map(entry=>({entity:fields?Object.fromEntries([...new Set(['uid','id',...fields])].filter(key=>Object.hasOwn(entry,key)).map(key=>[key,entry[key]])):entry,version})),revision:bundle.revision});},
    history:()=>({total:0,records:[],revision:bundle.revision}),assets:()=>({}),commit:readonly};
}
module.exports={createNativeStore};
