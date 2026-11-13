(function(root){
  'use strict';
  const clone=value=>JSON.parse(JSON.stringify(value));
  const same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
  function create({connection,initialSnapshot,clientId,uuid,getAssets,onSnapshot,onCommit,onStatus,onDraft}){
    let versions={},knownAssets=new Set(),outbox=[],revision=-1,ready=false,error=null,conflict=null,initialized=false,busy=null,rpcId=0;
    let snapshotRequest=0,editGeneration=0,resolving=false,snapshotBundle=null,uploading=0,syncing=null;
    if(initialSnapshot){versions=clone(initialSnapshot.versions);snapshotBundle=clone(initialSnapshot.bundle);revision=snapshotBundle.revision;knownAssets=new Set(Object.keys(snapshotBundle.assets||{}));}
    const invalidateSnapshots=()=>{editGeneration++;};
    const status=()=>({ready,error,conflict,busy:!!busy||resolving||uploading>0,resolving,pending:outbox.length,revision});
    const draft=()=>({clientId,versions:clone(versions),outbox:clone(outbox)});
    const changed=()=>{onStatus(status());onDraft(draft());};
    const uploadedAssets=assets=>Object.fromEntries(Object.entries(assets||{}).filter(([,value])=>typeof value==='string'&&value.startsWith('data:image/')));
    async function rpc(method,params){
      const controller=new AbortController();
      const timeout=setTimeout(()=>controller.abort(),connection.timeoutMs||20000);
      try{
        const response=await fetch(connection.baseURL+'/mcp',{method:'POST',headers:{Authorization:'Bearer '+connection.token,'Content-Type':'application/json',Accept:'application/json, text/event-stream','MCP-Protocol-Version':'2025-03-26'},body:JSON.stringify({jsonrpc:'2.0',id:++rpcId,method,params}),signal:controller.signal});
        if(!response.ok)throw Error('本地 MCP 未连接');
        const message=await response.json();
        if(message.error)throw Error(message.error.message);
        return message.result;
      }finally{
        clearTimeout(timeout);
      }
    }
    async function call(name,args={}){if(!initialized){await rpc('initialize',{protocolVersion:'2025-03-26',capabilities:{},clientInfo:{name:'junna-workbench-web',version:'1.0.0'}});initialized=true;}const result=await rpc('tools/call',{name,arguments:args}),value=result.structuredContent||JSON.parse(result.content[0].text);if(result.isError){const e=Error(value.message);Object.assign(e,value);throw e;}return value;}
    function snapshot(options={}){
      if(syncing?.generation===editGeneration)return syncing.promise;
      const operation={generation:editGeneration};
      operation.promise=readSnapshot(options).finally(()=>{if(syncing===operation)syncing=null;});
      syncing=operation;return operation.promise;
    }
    async function readSnapshot({initial=false}={}){
      const request=++snapshotRequest,generation=editGeneration;
      const stale=()=>request!==snapshotRequest||generation!==editGeneration;
      try{
        const result=await call('workbench_get_state',{includeHistory:false,includeAssets:initial,assetURLs:!!connection.assetURLs,incremental:!!snapshotBundle,...(snapshotBundle?{knownRevision:revision}:{})});
        if(stale())return false;
        if(result.unchanged){if(initial&&snapshotBundle){onSnapshot(clone(snapshotBundle));return true;}return false;}
        if(result.delta){
          if(!snapshotBundle)throw Error('缺少同步基线');
          for(const change of result.changes.filter(change=>change.collection==='root')){
            if(change.uid==='meta'){const {powers,relics}=snapshotBundle.data.meta;snapshotBundle.data.meta={...clone(change.after),powers,relics};}
            else snapshotBundle.data[change.uid]=clone(change.after);
          }
          for(const collection of new Set(result.changes.filter(change=>change.collection!=='root').map(change=>change.collection))){
            const changes=result.changes.filter(change=>change.collection===collection);
            const list=['powers','relics'].includes(collection)?snapshotBundle.data.meta[collection]:snapshotBundle.data[collection];
            const removed=new Set(changes.map(change=>change.uid));
            const remaining=list.filter(entry=>!removed.has(entry.uid));
            for(const change of changes.filter(change=>change.after).sort((a,b)=>a.afterIndex-b.afterIndex))remaining.splice(Math.max(0,change.afterIndex),0,clone(change.after));
            list.splice(0,list.length,...remaining);
          }
          Object.assign(versions,result.versions);
          knownAssets=new Set(result.assetIds);revision=result.revision;
          snapshotBundle={...snapshotBundle,revision,assets:{...snapshotBundle.assets,...result.assets},historyTotal:result.historyTotal,cardUpdatedAt:result.cardUpdatedAt};
          onSnapshot(clone(snapshotBundle),result.changes);return true;
        }
        const missing=result.assetIds.filter(id=>!knownAssets.has(id));
        if(!initial&&missing.length){const assets={};for(let i=0;i<missing.length;i+=100)Object.assign(assets,(await call('workbench_get_assets',{ids:missing.slice(i,i+100),asURLs:!!connection.assetURLs})).assets);Object.assign(result.bundle.assets,assets);}
        if(stale())return false;
        versions=result.versions;knownAssets=new Set(result.assetIds);revision=result.revision;snapshotBundle=clone(result.bundle);onSnapshot(result.bundle);return true;
      }catch(e){if(stale())return false;throw e;}
    }
    async function start(recovered){if(recovered?.outbox?.length){if(!snapshotBundle)versions=clone(recovered.versions);outbox=clone(recovered.outbox);for(const item of outbox){item.assets=uploadedAssets(item.assets);if(item.payload)item.payload.assets=uploadedAssets(item.payload.assets);}}try{await snapshot({initial:true});ready=true;error=null;for(const entry of outbox)for(const [id]of Object.entries(entry.assets||{}))knownAssets.delete(id);changed();if(outbox.length)flush();return true;}catch(e){error=e;ready=false;changed();return false;}}
    function enqueue(record){invalidateSnapshots();const assets=Object.fromEntries(Object.entries(uploadedAssets(getAssets())).filter(([id])=>!knownAssets.has(id)));outbox.push({record:clone(record),assets});changed();flush();}
    function changePayload(p){
      const change={collection:p.collection,uid:p.uid,expectedVersion:p.expectedVersion||versions[p.collection+'/'+p.uid]||'0:missing'};
      if(p.collection==='root'||!p.before||!p.after)return {...change,before:clone(p.before),after:clone(p.after),...(Number.isInteger(p.afterIndex)?{afterIndex:p.afterIndex}:{})};
      const fields=[...new Set([...Object.keys(p.before),...Object.keys(p.after)])].filter(field=>!same(p.before[field],p.after[field])||Object.hasOwn(p.before,field)!==Object.hasOwn(p.after,field));
      return {...change,updates:Object.fromEntries(fields.filter(field=>Object.hasOwn(p.after,field)).map(field=>[field,p.after[field]])),unset:fields.filter(field=>!Object.hasOwn(p.after,field)),expectedFields:Object.fromEntries(fields.filter(field=>Object.hasOwn(p.before,field)).map(field=>[field,p.before[field]])),expectedAbsent:fields.filter(field=>!Object.hasOwn(p.before,field))};
    }
    async function drain(){while(outbox.length&&!conflict){const item=outbox[0];if(!item.payload)item.payload={requestId:item.record.id,clientId,label:item.record.label,kind:item.record.kind,reference:item.record.reference||null,changes:item.record.patches.map(changePayload),assets:item.assets};changed();try{const result=await call('workbench_commit',item.payload);invalidateSnapshots();Object.assign(versions,result.versions);for(const id of result.assetIds)knownAssets.add(id);outbox.shift();ready=true;error=null;onCommit(item.record.id,result);changed();}catch(e){error=e;if(e.code&&e.code!=='STORE_BUSY'){conflict=e;ready=true;}else ready=false;changed();break;}}}
    function flush(){if(busy)return busy;if(conflict)return Promise.resolve(false);busy=drain().finally(()=>{busy=null;changed();});changed();return busy;}
    async function pull(){if(busy||outbox.length||conflict)return false;try{const updated=await snapshot();ready=true;error=null;changed();return updated;}catch(e){ready=false;error=e;changed();return false;}}
    async function proposals(){const map=new Map();for(const {record}of outbox)for(const p of record.patches){const key=p.collection+'/'+p.uid,old=map.get(key);map.set(key,{...p,before:old?old.before:p.before});}const list=[];for(const p of map.values()){const current=await call('workbench_get_entity',{collection:p.collection,uid:p.uid});let merged=clone(current.entity);if(current.entity===null||p.after===null||p.before===null||typeof p.after!=='object'||p.collection==='root')merged=same(current.entity,p.before)?clone(p.after):clone(current.entity);else{for(const field of new Set([...Object.keys(p.before),...Object.keys(p.after)]))if(!same(p.before[field],p.after[field])&&same(current.entity?.[field],p.before[field])){if(Object.hasOwn(p.after,field))merged[field]=clone(p.after[field]);else delete merged[field];}}const fields=current.entity&&p.collection!=='root'&&p.before&&p.after?[...new Set([...Object.keys(p.before),...Object.keys(p.after)])].filter(field=>!same(p.before[field],p.after[field])||Object.hasOwn(p.before,field)!==Object.hasOwn(p.after,field)):null;list.push({...p,fields,local:p.after,before:current.entity,after:merged,expectedVersion:current.version});}return list;}
    async function replace(changes){invalidateSnapshots();const backup=draft();onDraft(backup,true);resolving=true;changed();try{await snapshot({initial:true});outbox=[];conflict=null;error=null;if(changes.length){const record={id:uuid(),label:'合并版本冲突',kind:'编辑',patches:changes};outbox.push({record,assets:Object.assign({},...backup.outbox.map(e=>e.assets||{}))});}changed();await flush();if(!outbox.length)await pull();}catch(e){error=e;throw e;}finally{resolving=false;changed();}}
    async function uploadAsset(file,assetId){
      const requestId=uuid(),controller=new AbortController();
      const timeout=setTimeout(()=>controller.abort(),connection.timeoutMs||60000);
      uploading++;changed();
      try{
        const response=await fetch(connection.baseURL+'/upload',{method:'POST',headers:{Authorization:'Bearer '+connection.token,'Content-Type':file.type,'X-Asset-Id':assetId,'X-Request-Id':requestId,'X-Client-Id':clientId},body:file,signal:controller.signal});
        const result=await response.json();
        if(!response.ok)throw Error(result.message||'图片上传失败');
        return result;
      }finally{clearTimeout(timeout);uploading--;changed();}
    }
    return {start,enqueue,flush,pull,proposals,replace,status,draft,call,invalidateSnapshots,uploadAsset};
  }
  const api={create};if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaWorkbenchMcp=api;
})(typeof globalThis==='object'?globalThis:this);
