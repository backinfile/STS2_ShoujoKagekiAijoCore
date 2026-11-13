'use strict';
const fs=require('node:fs');
const path=require('node:path');
const {Server}=require('@modelcontextprotocol/sdk/server/index.js');
const {ListToolsRequestSchema,CallToolRequestSchema}=require('@modelcontextprotocol/sdk/types.js');
const {ENTITY_COLLECTIONS:COLLECTIONS,entityList}=require('./workbench-domain.js');
const {WorkbenchError}=require('./mcp-store.cjs');
const string={type:'string'},boolean={type:'boolean'},integer={type:'integer'};
const object=(properties,required=[])=>({type:'object',properties,required,additionalProperties:false});
const collection={type:'string',enum:[...COLLECTIONS,'ancients','root']};
const change=object({collection,uid:string,expectedVersion:string,before:{},after:{},updates:{type:'object'},unset:{type:'array',items:string},expectedFields:{type:'object'},expectedAbsent:{type:'array',items:string},afterIndex:integer},['collection','uid','expectedVersion']);
const definitions=[
  ['workbench_list_mods','列出少女歌剧工作台的 Mod 工作区。',object({}),true],
  ['workbench_status','查看工作台地址、连接状态和条目数量。',object({}),true],
  ['workbench_query','查找条目，返回每条内容及其版本。编辑前使用 get_entity 读取完整内容。',object({collection,search:string,limit:integer,offset:integer,fields:{type:'array',items:string},includeArchived:boolean}),true],
  ['workbench_get_entity','按 uid 或 id 读取完整条目和版本；新条目 uid 返回 missing 版本。',object({collection,uid:string,id:string},['collection']),true],
  ['workbench_get_state','读取工作台；incremental 按 knownRevision 返回增量，assetURLs 返回资源链接。',object({includeAssets:boolean,includeHistory:boolean,knownRevision:integer,incremental:boolean,assetURLs:boolean}),true],
  ['workbench_get_assets','读取指定图片，asURLs 返回资源链接。',object({ids:{type:'array',items:string},asURLs:boolean},['ids']),true],
  ['workbench_history','查询修改记录，可读取 before/after 用于版本校验后的恢复。',object({search:string,collection,limit:integer,offset:integer,includePatches:boolean}),true],
  ['workbench_commit','原子提交多个条目。每条提供读取时的 expectedVersion。已有条目优先用 updates/unset，expectedFields 提供所改字段的原值，原先缺失的字段列入 expectedAbsent；不同字段自动合并，相同字段的不同修改返回 VERSION_CONFLICT。创建、删除、整体替换使用 before/after。冲突整批拒绝并返回当前内容及冲突字段，合并后使用新 requestId。',object({requestId:string,clientId:string,label:string,kind:string,reference:{type:['string','null']},changes:{type:'array',items:change},assets:{type:'object',additionalProperties:string}},['requestId','clientId','label','changes']),false],
  ['workbench_restore','撤销或恢复一条历史记录。提供每个受影响条目的当前版本；内容与该历史端点不一致时保留现状并返回冲突。',object({requestId:string,clientId:string,recordId:string,side:{type:'string',enum:['before','after']},expectedVersions:{type:'object',additionalProperties:string}},['requestId','clientId','recordId','side','expectedVersions']),false],
  ['workbench_import_asset','从本地图片导入不可变资产，可同时替换一张卡牌或契合条目的配图。替换时提供条目版本。',object({requestId:string,clientId:string,label:string,assetId:string,sourcePath:string,collection,uid:string,expectedVersion:string},['requestId','clientId','label','assetId','sourcePath']),false],
  ['workbench_export','导出当前数据或游戏用知识背景提示。返回数据；outputPath 可保存副本。',object({format:{type:'string',enum:['bundle','knowledgeTips']},outputPath:string}),false]
].map(([name,description,inputSchema,readOnlyHint])=>({name,description,inputSchema:{...inputSchema,properties:{...inputSchema.properties,modId:{type:'string',enum:require('./mods.cjs').mods.map(mod=>mod.id)}}},annotations:{readOnlyHint,destructiveHint:false,idempotentHint:true,openWorldHint:false}}));
function createToolHandlers(store,{url}){
  return {
    workbench_list_mods:async()=>({mods:require('./mods.cjs').mods,currentMod:'junna'}),
    workbench_status:async()=>{const b=store.read({copy:false});return {connected:true,serverType:'shoujo-workbench-mcp',url,workspaceId:b.workspaceId,revision:b.revision,counts:Object.fromEntries((b.data.ancients?['cards','relics','ancients']:COLLECTIONS).map(k=>[k,k==='ancients'?b.data.ancients.length:entityList(b.data,k).length]))};},
    workbench_query:async args=>store.query(args),
    workbench_get_entity:async args=>store.get(args),
    workbench_get_state:async args=>store.state(args),
    workbench_get_assets:async({ids,asURLs=false})=>{if(!Array.isArray(ids)||ids.length>100)throw new WorkbenchError('INVALID_ARGUMENT','图片数量无效');return {assets:store.assets(ids,!asURLs)};},
    workbench_history:async args=>store.history(args),
    workbench_commit:async args=>store.commit(args),
    workbench_restore:async({recordId,side,expectedVersions,...args})=>store.commit({...args,label:(side==='before'?'撤销':'重做')+' · '+recordId,kind:side==='before'?'撤销':'重做',reference:recordId,restore:{recordId,side,expectedVersions}}),
    workbench_import_asset:async({sourcePath,assetId,collection,uid,expectedVersion,...args})=>{const ext=path.extname(sourcePath).toLowerCase(),mime={'.png':'png','.jpg':'jpeg','.jpeg':'jpeg','.webp':'webp','.gif':'gif'}[ext];if(!mime||fs.statSync(sourcePath).size>16*1024*1024)throw new WorkbenchError('INVALID_ARGUMENT','请选择 16MB 以内的 PNG、JPEG、WebP 或 GIF');const value='data:image/'+mime+';base64,'+fs.readFileSync(sourcePath).toString('base64');return store.commit({...args,changes:uid?[{collection,uid,expectedVersion,updates:{art:assetId}}]:[],assets:{[assetId]:value}});},
    workbench_export:async({format='bundle',outputPath}={})=>{const b=store.read();let value;if(format==='knowledgeTips')value={schemaVersion:1,locale:'zh-CN',tipId:'junna_knowledge_background',cards:Object.fromEntries(b.data.cards.filter(c=>c.extraHoverTips?.some(t=>t.id==='junna_knowledge_background')).map(c=>[c.id,{name:c.name,extraHoverTips:c.extraHoverTips}]))};else{const {mcpRequests,...bundle}=b;value=bundle;}if(outputPath){fs.writeFileSync(path.resolve(outputPath),JSON.stringify(value,null,2)+'\n');return {saved:true,path:path.resolve(outputPath),revision:b.revision};}return {data:value,revision:b.revision};}
  };
}
function createProtocolServer(handlers){
  const server=new Server({name:'shoujo-workbench',version:'1.0.0'},{capabilities:{tools:{}},instructions:'少女歌剧工作台内容统一通过此 MCP 编辑。先 list_mods 选择 Mod，再为调用传入 modId；省略时兼容原纯那工作区。先 query/get_entity 读取条目及 version，再 commit 并提供 expectedVersion、唯一 requestId 和 clientId。已有条目优先只提交所改字段，并用 expectedFields/expectedAbsent 校验其原值；同条目不同字段自动合并，相同字段的不同修改整批拒绝。保留冲突草稿，合并后用新 requestId 提交。网页与 MCP 共享数据、版本和历史。'});
  server.setRequestHandler(ListToolsRequestSchema,async()=>({tools:definitions}));
  server.setRequestHandler(CallToolRequestSchema,async request=>{try{const handler=handlers[request.params.name];if(!handler)throw new WorkbenchError('NOT_FOUND','工具不存在');const value=await handler(request.params.arguments||{});return {content:[{type:'text',text:['workbench_get_state','workbench_get_assets','workbench_export'].includes(request.params.name)?JSON.stringify({ok:true,revision:value.revision,unchanged:value.unchanged,saved:value.saved,path:value.path}):JSON.stringify(value)}],structuredContent:value};}catch(error){const value={ok:false,code:error.code||'INTERNAL_ERROR',message:error.message,...error.details};return {isError:true,content:[{type:'text',text:JSON.stringify(value)}],structuredContent:value};}});
  return server;
}
module.exports={definitions,createToolHandlers,createProtocolServer};
