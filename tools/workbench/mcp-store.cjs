'use strict';
const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const domain=require('./workbench-domain.js');
const {ENTITY_COLLECTIONS,entityList,rootEntity,assignRoot}=domain;
const fitPool=require('./fit-pool-view.js');
const clone=value=>JSON.parse(JSON.stringify(value));
const stable=value=>JSON.stringify(value&&typeof value==='object'?Array.isArray(value)?value.map(v=>JSON.parse(stable(v))):Object.fromEntries(Object.keys(value).sort().map(k=>[k,JSON.parse(stable(value[k]))])):value);
const equal=(a,b)=>stable(a)===stable(b);
const hash=value=>crypto.createHash('sha256').update(typeof value==='string'?value:stable(value)).digest('hex');
const keyOf=(collection,uid)=>collection+'/'+uid;
class WorkbenchError extends Error{
  constructor(code,message,details={}){super(message);this.code=code;this.details=details;}
}
function assert(ok,message,code='INVALID_ARGUMENT'){if(!ok)throw new WorkbenchError(code,message);}
function entity(data,collection,uid){return collection==='root'?data[uid]??null:entityList(data,collection).find(e=>e.uid===uid)??null;}
function validTarget(collection,uid){assert(ENTITY_COLLECTIONS.includes(collection)||collection==='root'&&['title','meta'].includes(uid),'条目类型无效');assert(typeof uid==='string'&&uid.length>0&&uid.length<256,'内部编号无效');}
function conflictingFields(current,change){
  assert(change.collection!=='root'&&!Object.hasOwn(change,'after'),'字段校验用于指定字段更新');
  assert(change.updates&&typeof change.updates==='object'&&!Array.isArray(change.updates),'缺少修改字段');
  const expected=Object.hasOwn(change,'expectedFields')?change.expectedFields:{};
  assert(expected&&typeof expected==='object'&&!Array.isArray(expected),'缺少字段原值');
  const absent=new Set(change.expectedAbsent||[]);
  const fields=new Set([...Object.keys(change.updates),...(change.unset||[])]);
  for(const field of fields)assert(Object.hasOwn(expected,field)||absent.has(field),'缺少字段原值：'+field);
  if(current===null)return ['$entry'];
  return [...new Set([...fields,...Object.keys(expected),...absent])].filter(field=>{
    const exists=Object.hasOwn(current,field);
    const matches=Object.hasOwn(expected,field)?exists&&equal(current[field],expected[field]):!exists;
    const alreadyApplied=Object.hasOwn(change.updates,field)?exists&&equal(current[field],change.updates[field]):(change.unset||[]).includes(field)&&!exists;
    return !matches&&!alreadyApplied;
  });
}
function version(bundle,collection,uid){const value=collection==='root'?rootEntity(bundle.data,uid):entity(bundle.data,collection,uid);return String(bundle.entityVersions?.[keyOf(collection,uid)]||0)+':'+(value===null?'missing':hash(value));}
function versions(bundle){const out={};for(const key of Object.keys(bundle.entityVersions||{})){const slash=key.indexOf('/');out[key]=version(bundle,key.slice(0,slash),key.slice(slash+1));}for(const collection of ENTITY_COLLECTIONS)for(const e of entityList(bundle.data,collection))out[keyOf(collection,e.uid)]=version(bundle,collection,e.uid);for(const key of ['title','meta'])out[keyOf('root',key)]=version(bundle,'root',key);return out;}
function diff(before,after){const patches=[];for(const collection of ENTITY_COLLECTIONS){const b=new Map(entityList(before,collection).map((e,i)=>[e.uid,{e,i}])),a=new Map(entityList(after,collection).map((e,i)=>[e.uid,{e,i}]));for(const uid of new Set([...b.keys(),...a.keys()])){const old=b.get(uid),next=a.get(uid);if(!equal(old?.e??null,next?.e??null))patches.push({collection,uid,before:old?.e??null,after:next?.e??null,beforeIndex:old?.i??-1,afterIndex:next?.i??-1});}}for(const uid of ['title','meta'])if(!equal(rootEntity(before,uid),rootEntity(after,uid)))patches.push({collection:'root',uid,before:rootEntity(before,uid),after:rootEntity(after,uid)});return patches;}
class WorkbenchStore{
  constructor({file,seed,workspaceId,assetDirectory}){
    this.file=path.resolve(file);
    this.workspaceId=workspaceId;
    this.normalize=domain.create({seed,uid:crypto.randomUUID,JunnaFitPool:fitPool});
    this.queue=Promise.resolve();
    if(/\.(sqlite|db)$/.test(file))this.storage=new (require('./mcp-sqlite.cjs').SqliteStorage)(this.file,{assetDirectory});
  }
  read({copy=true}={}){
    if(this.storage&&copy)return this.storage.read({full:true});
    const stat=this.storage?null:fs.statSync(this.file);
    const signature=this.storage?this.storage.signature():[stat.mtimeMs,stat.ctimeMs,stat.size,stat.ino].join(':');
    if(this.cache?.signature!==signature){
      const bundle=this.storage?this.storage.read():JSON.parse(fs.readFileSync(this.file,'utf8').replace(/^\uFEFF/,''));
      assert(bundle.workspaceId===this.workspaceId&&bundle.schemaVersion===1,'制作台数据不匹配','INVALID_WORKSPACE');
      bundle.data=this.normalize(bundle.data);bundle.history=bundle.history||[];bundle.assets=bundle.assets||{};bundle.entityVersions=bundle.entityVersions||{};bundle.mcpRequests=bundle.mcpRequests||{};
      bundle.revision=Math.max(bundle.revision||0,...bundle.history.map(r=>r.seq||0));
      this.cache={signature,bundle};
    }
    return copy?clone(this.cache.bundle):this.cache.bundle;
  }
  state({includeAssets=false,includeHistory=false,knownRevision,incremental=false,assetURLs=false}={}){
    const b=this.read({copy:false});
    if(knownRevision===b.revision)return {unchanged:true,revision:b.revision};
    if(incremental&&this.storage&&Number.isInteger(knownRevision)&&knownRevision>=0&&knownRevision<b.revision){
      const records=this.storage.statement('SELECT value FROM history WHERE seq>? ORDER BY seq LIMIT 201').all(knownRevision);
      if(records.length<=200&&records.length&&JSON.parse(records[0].value).seq===knownRevision+1){
        const targets=new Map();
        for(const row of records)for(const patch of JSON.parse(row.value).patches)targets.set(keyOf(patch.collection,patch.uid),patch);
        const changes=[...targets.values()].map(p=>({collection:p.collection,uid:p.uid,after:p.collection==='root'?rootEntity(b.data,p.uid):entity(b.data,p.collection,p.uid),afterIndex:p.collection==='root'?-1:entityList(b.data,p.collection).findIndex(e=>e.uid===p.uid)}));
        return clone({delta:true,changes,revision:b.revision,versions:Object.fromEntries(changes.map(p=>[keyOf(p.collection,p.uid),version(b,p.collection,p.uid)])),assetIds:Object.keys(b.assets),assets:b.assets,historyTotal:b.historyTotal,cardUpdatedAt:b.cardUpdatedAt});
      }
    }
    const {assets,history,mcpRequests,entityVersions,...bundle}=b;
    if(!this.cache.cardUpdatedAt){
      this.cache.cardUpdatedAt={...b.cardUpdatedAt};
      for(const record of history)for(const change of record.patches){
        if(change.collection==='cards')this.cache.cardUpdatedAt[change.uid]=Math.max(this.cache.cardUpdatedAt[change.uid]||0,record.seq);
      }
    }
    this.cache.versions=this.cache.versions||versions(b);
    return clone({bundle:{...bundle,seedBatches:[],appliedSeedBatches:[],assets:assetURLs?assets:includeAssets?this.assets(Object.keys(assets),true):{},history:includeHistory&&this.storage?this.storage.records():includeHistory?history:[],historyTotal:b.historyTotal??history.length,cardUpdatedAt:this.cache.cardUpdatedAt,undoStack:[],redoStack:[]},versions:this.cache.versions,assetIds:Object.keys(assets),revision:b.revision});
  }
  assets(ids,inline=false){const b=this.read({copy:false});return Object.fromEntries(ids.filter(id=>Object.hasOwn(b.assets,id)).map(id=>[id,this.storage?this.storage.asset(id,inline):b.assets[id]]));}
  get({collection,uid,id}){assert(ENTITY_COLLECTIONS.includes(collection)||collection==='root','条目类型无效');const b=this.read({copy:false});if(!uid&&id&&collection!=='root')uid=entityList(b.data,collection).find(e=>e.id===id)?.uid;assert(typeof uid==='string','条目不存在','NOT_FOUND');validTarget(collection,uid);return {collection,uid,entity:clone(entity(b.data,collection,uid)),version:version(b,collection,uid),revision:b.revision};}
  query({collection='cards',search='',limit=50,offset=0,fields,includeArchived=false}={}){assert(ENTITY_COLLECTIONS.includes(collection),'条目类型无效');const b=this.read({copy:false});let list=entityList(b.data,collection).filter(e=>(includeArchived||!e.trashedAt&&!e.reservedAt)&&(!search||[e.id,e.name,e.text,e.theme,e.content].join(' ').toLowerCase().includes(search.toLowerCase())));const total=list.length;list=list.slice(Math.max(0,offset),Math.max(0,offset)+Math.min(300,Math.max(1,limit))).map(e=>({entity:fields?Object.fromEntries([...new Set(['uid','id',...fields])].filter(k=>Object.hasOwn(e,k)).map(k=>[k,e[k]])):e,version:version(b,collection,e.uid)}));return clone({collection,total,entries:list,revision:b.revision});}
  history({limit=30,offset=0,search='',collection,includePatches=false}={}){
    const bundle=this.read({copy:false}),query=search.toLowerCase();
    const pageSize=Math.min(100,Math.max(1,limit)),pageOffset=Math.max(0,offset);
    let records,total;
    if(this.storage){
      const where=` WHERE (?='' OR instr(lower(value),?)>0)
        AND (?='' OR EXISTS(SELECT 1 FROM json_each(history.value,'$.patches')
          WHERE json_extract(json_each.value,'$.collection')=?))`;
      const args=[query,query,collection||'',collection||''];
      total=this.storage.statement('SELECT count(*) AS total FROM history'+where).get(...args).total;
      records=this.storage.statement('SELECT value FROM history'+where+' ORDER BY seq DESC LIMIT ? OFFSET ?')
        .all(...args,pageSize,pageOffset).map(row=>JSON.parse(row.value));
    }else{
      const matching=bundle.history.filter(record=>(!collection||record.patches.some(patch=>patch.collection===collection))
        &&(!query||JSON.stringify([record.label,record.id,record.kind,record.patches]).toLowerCase().includes(query)))
        .sort((a,b)=>b.seq-a.seq);
      total=matching.length;
      records=matching.slice(pageOffset,pageOffset+pageSize);
    }
    if(!includePatches)records=records.map(({patches,...record})=>({...record,changed:patches.map(patch=>({collection:patch.collection,uid:patch.uid}))}));
    return clone({total,records,revision:bundle.revision});
  }
  commit(input){const operation=this.queue.catch(()=>{}).then(()=>this.locked(()=>this.apply(input)));this.queue=operation;return operation;}
  async locked(run){if(this.storage){try{return this.storage.transaction(run);}finally{this.cache=null;}}const lock=this.file+'.mcp-lock';let handle;try{handle=fs.openSync(lock,'wx');fs.writeFileSync(handle,JSON.stringify({pid:process.pid}));}catch(error){if(error.code==='EEXIST'){let owner;try{owner=JSON.parse(fs.readFileSync(lock,'utf8'));process.kill(owner.pid,0);}catch(e){if(e.code==='ESRCH'){fs.unlinkSync(lock);return this.locked(run);}}throw new WorkbenchError('STORE_BUSY','另一服务正在提交，请重试');}throw error;}try{return await run();}finally{fs.closeSync(handle);fs.unlinkSync(lock);}}
  apply(input){
    assert(input&&typeof input==='object','缺少提交');const {requestId,clientId,label,kind='编辑',reference=null,assets={}}=input;
    assert(typeof requestId==='string'&&requestId.length>=8&&requestId.length<200,'缺少提交编号');assert(typeof clientId==='string'&&clientId.length>0&&clientId.length<200,'缺少客户端编号');assert(typeof label==='string'&&label.length>0&&label.length<500,'缺少记录名称');
    const source=this.read({copy:false}),requestHash=hash(input),receipt=this.storage?this.storage.receipt(requestId):source.mcpRequests[requestId];
    if(receipt){assert(receipt.hash===requestHash,'提交编号已用于另一内容','REQUEST_ID_REUSED');return {...clone(receipt.result),replayed:true};}
    const originalBytes=this.storage?null:fs.readFileSync(this.file);
    assert(this.read({copy:false})===source,'内容文件在提交期间已修改','FILE_CHANGED');
    const b={...source,assets:{...source.assets},history:source.history.slice(),entityVersions:{...source.entityVersions},mcpRequests:{...source.mcpRequests}};
    let changes=input.changes;
    if(input.restore){const r=this.storage?this.storage.record(input.restore.recordId):b.history.find(r=>r.id===input.restore.recordId);assert(r,'记录不存在','NOT_FOUND');const side=input.restore.side;assert(['before','after'].includes(side),'恢复方向无效');changes=r.patches.map(p=>({collection:p.collection,uid:p.uid,before:p[side==='before'?'after':'before'],after:p[side],afterIndex:p[side+'Index'],expectedVersion:input.restore.expectedVersions?.[keyOf(p.collection,p.uid)]}));}
    assert(Array.isArray(changes)&&changes.length<=2000,'提交条目格式无效');assert(Object.keys(assets).length<=100,'图片数量过多');
    const next=clone(b.data),targets=new Set(),conflicts=[];
    for(const p of changes){validTarget(p.collection,p.uid);const key=keyOf(p.collection,p.uid);assert(!targets.has(key),'同一次提交包含重复条目');targets.add(key);const current=entity(b.data,p.collection,p.uid),actual=version(b,p.collection,p.uid);assert(typeof p.expectedVersion==='string','缺少条目版本','VERSION_REQUIRED');
      const guarded=Object.hasOwn(p,'expectedFields')||Object.hasOwn(p,'expectedAbsent');
      const fields=guarded?conflictingFields(current,p):[];
      if(guarded?fields.length>0:p.expectedVersion!==actual||(Object.hasOwn(p,'before')&&!equal(p.before,p.collection==='root'&&p.uid==='meta'&&!Object.hasOwn(p.before||{},'powers')&&!Object.hasOwn(p.before||{},'relics')?rootEntity(b.data,'meta'):current))){conflicts.push({collection:p.collection,uid:p.uid,expectedVersion:p.expectedVersion,currentVersion:actual,current,...(guarded?{fields}:{})});continue;}
      let after;if(Object.hasOwn(p,'after')){assert(Object.hasOwn(p,'before'),'整体替换需要读取时的内容');after=p.after;}else {assert(current!==null,'条目不存在','NOT_FOUND');assert(p.updates&&typeof p.updates==='object'&&!Array.isArray(p.updates),'缺少修改字段');assert(p.collection!=='root','根字段请使用整体替换');after={...current,...p.updates};for(const field of p.unset||[]){assert(!['uid','id','__proto__','constructor','prototype'].includes(field),'删除字段无效');delete after[field];}}
      if(p.collection==='root'){assert(after!==null,'根字段需要保留');assignRoot(next,p.uid,after);}else{assert(after===null||after&&typeof after==='object'&&!Array.isArray(after)&&after.uid===p.uid,'内部编号需要保持一致');const list=entityList(next,p.collection,true),at=list.findIndex(e=>e.uid===p.uid);if(at>=0)list.splice(at,1);if(after!==null)list.splice(at>=0?at:Math.min(Number.isInteger(p.afterIndex)&&p.afterIndex>=0?p.afterIndex:list.length,list.length),0,clone(after));}
    }
    if(conflicts.length)throw new WorkbenchError('VERSION_CONFLICT','内容已被其他客户端修改',{conflicts,revision:b.revision});
    const normalized=this.normalize(next),patches=diff(b.data,normalized),addedAssets={};for(const [id,value]of Object.entries(assets)){assert(/^[a-zA-Z0-9_-]{1,200}$/.test(id)&&typeof value==='string'&&/^data:image\/(png|jpeg|webp|gif);base64,[a-zA-Z0-9+/=]+$/.test(value)&&value.length<=24*1024*1024,'图片格式无效');assert(!Object.hasOwn(b.assets,id)||(this.storage?this.storage.asset(id,true):b.assets[id])===value,'图片编号已存在','ASSET_CONFLICT');if(!Object.hasOwn(b.assets,id))addedAssets[id]=value;}
    b.data=normalized;Object.assign(b.assets,addedAssets);let record=null;
    if(patches.length||Object.keys(addedAssets).length){b.revision++;record={id:requestId,seq:b.revision,at:new Date().toISOString(),label,kind,clientId,patches,reference};for(const p of patches){const key=keyOf(p.collection,p.uid);b.entityVersions[key]=(b.entityVersions[key]||0)+1;p.afterVersion=version(b,p.collection,p.uid);}b.history.push(record);}
    const result={saved:true,revision:b.revision,record,versions:Object.fromEntries([...new Map([...changes,...patches].map(p=>[keyOf(p.collection,p.uid),p])).values()].map(p=>[keyOf(p.collection,p.uid),version(b,p.collection,p.uid)])),assetIds:Object.keys(addedAssets)};b.mcpRequests[requestId]={hash:requestHash,result};
    // Receipts are durable so retrying a lost response never applies an edit twice.
    if(this.storage){this.storage.save(b,patches,result,requestId,requestHash,addedAssets);return result;}
    const temp=this.file+'.pending-'+crypto.randomUUID();try{fs.writeFileSync(temp,JSON.stringify(b)+'\n',{flag:'wx'});assert(crypto.createHash('sha256').update(fs.readFileSync(this.file)).digest('hex')===crypto.createHash('sha256').update(originalBytes).digest('hex'),'内容文件在提交期间已修改','FILE_CHANGED');fs.renameSync(temp,this.file);}finally{if(fs.existsSync(temp))fs.unlinkSync(temp);}return result;
  }
}
module.exports={WorkbenchStore,WorkbenchError,version,versions,diff,equal,keyOf};
