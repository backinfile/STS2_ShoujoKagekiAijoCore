'use strict';
const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const date=value=>new Date(value*1000).toLocaleString('zh-CN',{hour12:false});
const sourceName={game:'游戏',engine:'引擎',shader:'着色器',managed:'托管异常',stderr:'标准错误'};
const platformName={pc:'PC',android:'安卓'};
let page=1,requestVersion=0,detailVersion=0,listController,detailController;
const restored=new URLSearchParams(location.hash.slice(1));
for(const name of ['source','sort','search','platform','game_target']){const control=$('errorFilters').elements.namedItem(name);if(restored.has(name))control.value=restored.get(name);}
for(const [name,id] of [['build','build'],['mod_version','modVersion'],['core_version','coreVersion'],['source_mod','sourceMod']])if(restored.has(name))$(id).add(new Option(restored.get(name),restored.get(name),true,true));
page=Math.max(1,Math.min(100000,Number(restored.get('page'))||1));
async function api(path,signal){const response=await fetch(path,{cache:'no-store',credentials:'same-origin',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(15000)]):AbortSignal.timeout(15000)});if(!response.ok)throw Error('请求失败（'+response.status+'）');return response.json();}
function options(id,values,selected){$(id).replaceChildren(new Option('全部版本','all'));for(const value of new Set([...values,selected].filter(v=>v&&v!=='all')))$(id).add(new Option(value,value));$(id).value=selected;}
async function load(){
 listController?.abort();listController=new AbortController();detailController?.abort();
 const version=++requestVersion;detailVersion++;$('refresh').disabled=true;$('connection').textContent='更新中';$('connection').className='connection';$('error').hidden=true;$('detail').replaceChildren();
 const query=new URLSearchParams(new FormData($('errorFilters')));query.set('page',page);query.set('compact','1');
 try{
  const data=await api('api/errors?'+query,listController.signal);if(version!==requestVersion)return;
  page=data.page;query.set('page',page);history.replaceState(null,'','#'+query);
  options('build',data.builds,query.get('build'));options('modVersion',data.mod_versions,query.get('mod_version'));options('coreVersion',data.core_versions??[],query.get('core_version'));options('sourceMod',data.source_mods??[],query.get('source_mod'));
  $('total').textContent=data.total+' 类';$('capacity').textContent='错误 '+(data.stored_groups??data.total)+' / '+data.group_limit+' · 报告 '+data.stored_reports+' / '+data.report_limit;
  $('errors').innerHTML=data.items.length?'<table><thead><tr><th>错误</th><th>版本</th><th>次数</th><th>会话</th><th>最近出现</th></tr></thead><tbody>'+data.items.map(item=>'<tr><td><button class="error-row" data-error="'+esc(item.id)+'"><strong>'+esc(item.message)+'</strong><small>'+esc(sourceName[item.source]??item.source)+' · '+esc(platformName[item.platform]??item.platform)+'</small></button></td><td data-label="版本">'+esc(item.build)+'<small>Mod '+esc(item.mod_version)+'</small></td><td data-label="次数">'+esc(item.occurrences)+'</td><td data-label="会话">'+esc(item.sessions)+'</td><td data-label="最近出现">'+esc(date(item.last_seen))+'</td></tr>').join('')+'</tbody></table>':'<div class="empty">暂无报错</div>';
  $('pagination').innerHTML='<button data-page="'+(page-1)+'" '+(page<=1?'disabled':'')+'>上一页</button><span>'+page+' / '+data.pages+'</span><button data-page="'+(page+1)+'" '+(page>=data.pages?'disabled':'')+'>下一页</button>';
  $('connection').textContent='已更新';$('connection').className='connection ok';$('updated').textContent='更新于 '+new Date().toLocaleTimeString('zh-CN',{hour12:false});
 }catch(error){if(version!==requestVersion||error.name==='AbortError')return;$('error').textContent=error.message;$('error').hidden=false;$('connection').textContent='更新失败';$('errors').replaceChildren();$('pagination').replaceChildren();$('total').textContent='';}
 finally{if(version===requestVersion)$('refresh').disabled=false;}
}
async function detail(id){
 detailController?.abort();detailController=new AbortController();const version=++detailVersion;
 const target=matchMedia('(max-width:800px)').matches?$('errorSheetBody'):$('detail');target.textContent='加载中';if(target.id==='errorSheetBody'&&!$('errorSheet').open)$('errorSheet').showModal();
 try{
  const item=await api('api/errors/'+id,detailController.signal);if(version!==detailVersion)return;
  const mods=Array.isArray(item.mods)?item.mods:[];
  target.innerHTML='<h2>'+esc(item.message)+'</h2><dl><dt>Core / API</dt><dd>'+esc(item.core_telemetry?.core_version??'—')+' / '+esc(item.core_telemetry?.api_version??'—')+'</dd><dt>游戏 / Mod</dt><dd>'+esc(item.build)+' / '+esc(item.mod_version)+'</dd><dt>游戏构建</dt><dd>'+esc(item.game_commit??'—')+'</dd><dt>平台</dt><dd>'+esc(platformName[item.platform]??item.platform)+' · '+esc(item.os??'—')+'</dd><dt>来源</dt><dd>'+esc(sourceName[item.source]??item.source)+'</dd><dt>首次出现</dt><dd>'+esc(date(item.first_seen))+'</dd><dt>最近出现</dt><dd>'+esc(date(item.last_seen))+'</dd><dt>累计次数</dt><dd>'+esc(item.occurrences)+'</dd><dt>数据状态</dt><dd>客户端报告</dd></dl><details class="error-mods"><summary>其他 Mod · '+mods.length+(item.mods_truncated?' · 已截断':'')+'</summary><ul>'+mods.map(mod=>'<li><strong>'+esc(mod.name)+'</strong><span>'+esc(mod.version)+'</span><small>'+esc(mod.id)+'</small></li>').join('')+'</ul></details><h3>调用栈</h3><pre>'+esc(item.stack||'—')+'</pre><details><summary>错误指纹</summary><code>'+esc(item.id)+'</code></details>';

 }catch(error){if(version===detailVersion&&error.name!=='AbortError')target.textContent=error.message;}
}
$('errorFilters').addEventListener('submit',event=>{event.preventDefault();page=1;load();});
$('errorFilters').addEventListener('change',event=>{if(event.target.tagName==='SELECT'){page=1;load();}});
$('refresh').addEventListener('click',load);
window.addEventListener('observatory:refresh',load);
$('closeErrorSheet').addEventListener('click',()=>$('errorSheet').close());
$('errorSheet').addEventListener('close',()=>{detailVersion++;detailController?.abort();});
$('pagination').addEventListener('click',event=>{const button=event.target.closest('[data-page]');if(button&&!button.disabled){page=Number(button.dataset.page);load();}});
$('errors').addEventListener('click',event=>{const button=event.target.closest('[data-error]');if(button)detail(button.dataset.error);});
load();
