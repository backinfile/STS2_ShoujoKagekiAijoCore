(async()=>{
  'use strict';
  const $=id=>document.getElementById(id),escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const dataElement=$('native-data');let bundle;
  try{const response=await fetch(dataElement.dataset.src,{cache:'no-cache'});if(!response.ok)throw Error();bundle=await response.json();}catch{ $('native-content').innerHTML='<div class="native-empty" role="alert">资料载入失败 <button id="native-retry">重试</button></div>';$('native-retry').onclick=()=>location.reload();return;}
  const data=bundle.data,source=data.meta.nativeSource,collections={cards:data.cards,relics:data.meta.relics,ancients:data.ancients};
  const labels={cards:'卡牌',relics:'遗物',ancients:'先古之民'},params=new URLSearchParams(location.hash.slice(1));
  const state={tab:Object.hasOwn(collections,params.get('tab'))?params.get('tab'):'cards',selected:params.get('id'),search:'',owner:'',rarity:'',upgraded:false,page:0};
  $('native-version').textContent=source.gameVersion+' · 只读';
  function list(){const query=state.search.trim().toLowerCase();return collections[state.tab].filter(entry=>(!query||[entry.name,entry.id,entry.text,entry.upgradedText,entry.unlock,...entry.selectionRules||[]].join(' ').toLowerCase().includes(query))&&(!state.owner||entry.owner===state.owner)&&(!state.rarity||entry.rarity===state.rarity));}
  function options(id,key){const select=$(id),values=[...new Set(collections[state.tab].map(entry=>entry[key]).filter(Boolean))];select.innerHTML='<option value="">'+(key==='owner'?'全部角色':'全部稀有度')+'</option>'+values.map(value=>'<option>'+escape(value)+'</option>').join('');select.value=state[key]||'';select.hidden=state.tab==='ancients';}
  const effect=entry=>state.upgraded&&state.tab==='cards'?entry.upgradedText:entry.text;
  const cost=entry=>(state.upgraded?entry.upgradedCost:entry.cost)+((state.upgraded?entry.upgradedStarCost:entry.starCost)!==null&&(state.upgraded?entry.upgradedStarCost:entry.starCost)!==undefined?' · '+(state.upgraded?entry.upgradedStarCost:entry.starCost)+'星':'');
  function referenceLink(collection,id,label){return '<button class="native-link" data-native-collection="'+collection+'" data-native-id="'+escape(id)+'">'+escape(label)+'</button>';}
  function showDetail(){
    const entry=collections[state.tab].find(entry=>entry.id===state.selected),panel=$('native-detail');panel.hidden=!entry;if(!entry)return;
    let html='<div class="native-detail-heading"><div><h2>'+escape(entry.name)+'</h2><small>'+escape(entry.id)+'</small></div><button class="native-close" id="native-close" aria-label="关闭详情">×</button></div>';
    if(state.tab==='ancients'){
      const offering=id=>{const relic=data.meta.relics.find(relic=>relic.id===id);return '<div>'+referenceLink('relics',id,relic.name)+'<p>'+escape(relic.text)+'</p>'+(entry.relicConditions[id]?'<p class="native-condition">'+escape(entry.relicConditions[id])+'</p>':'')+'</div>';};
      html+='<p class="native-epithet">'+escape(entry.epithet)+'</p><div class="native-ancient-rules"><section><h3>出现规则</h3><p>'+entry.acts.map(n=>'第'+n+'幕').join('、')+'</p><p>'+escape(entry.unlock)+'</p></section><section><h3>抽取规则</h3><ul>'+entry.selectionRules.map(text=>'<li>'+escape(text)+'</li>').join('')+'</ul></section></div><div class="native-pool-grid">'+entry.optionPools.map(pool=>'<section><h3>'+escape(pool.name)+' <small>'+pool.relicIds.length+'</small></h3><div class="native-offerings">'+pool.relicIds.map(offering).join('')+'</div></section>').join('')+'</div><details class="native-common-rules"><summary>通用出现规则</summary><ul>'+data.meta.ancientRules.map(text=>'<li>'+escape(text)+'</li>').join('')+'</ul></details>';
    }else{
      html+='<p class="native-meta">'+[entry.owner,entry.rarity,entry.type,state.tab==='cards'?cost(entry)+'费':''].filter(Boolean).map(escape).join(' · ')+'</p>';
      if(state.tab==='cards')html+='<h3>基础</h3><p class="native-effect">'+escape(entry.text)+'</p>'+(entry.keywords.length?'<p class="native-keywords">'+entry.keywords.map(escape).join(' · ')+'</p>':'')+'<h3>升级</h3><p class="native-effect">'+escape(entry.upgradedText)+'</p>'+(entry.upgradedKeywords.length?'<p class="native-keywords">'+entry.upgradedKeywords.map(escape).join(' · ')+'</p>':'')+(entry.relatedCardIds.length?'<h3>关联卡牌</h3><div class="native-related">'+entry.relatedCardIds.map(id=>{const card=data.cards.find(card=>card.id===id);return card?referenceLink('cards',id,card.name):'';}).join('')+'</div>':'');
      else html+='<p class="native-effect">'+escape(entry.text)+'</p>'+(entry.conditions?.length?'<h3>出现条件</h3><ul>'+entry.conditions.map(text=>'<li>'+escape(text)+'</li>').join('')+'</ul>':'')+(entry.ancientIds?.length?'<h3>先古之民</h3><div class="native-related">'+entry.ancientIds.map(id=>referenceLink('ancients',id,data.ancients.find(a=>a.id===id).name)).join('')+'</div>':'')+(entry.flavor?'<p class="native-flavor">'+escape(entry.flavor)+'</p>':'');
    }
    panel.innerHTML=html;$('native-close').hidden=state.tab==='ancients';$('native-close').onclick=()=>{state.selected=null;render();};
  }
  function render(){
    document.body.dataset.nativeView=state.tab;
    if(state.tab==='ancients'&&!state.selected)state.selected=data.ancients[0]?.id;
    $('native-title').textContent=labels[state.tab];document.title='少女歌剧工作台 · 游戏本体 · '+labels[state.tab];
    for(const tab of document.querySelectorAll('[data-native-tab]')){const active=tab.dataset.nativeTab===state.tab;tab.setAttribute('aria-pressed',String(active));tab.textContent=labels[tab.dataset.nativeTab]+' '+collections[tab.dataset.nativeTab].length;}
    $('native-upgrade').hidden=state.tab!=='cards';$('native-upgrade').setAttribute('aria-pressed',String(state.upgraded));$('native-upgrade').textContent=state.upgraded?'升级牌面':'基础牌面';
    options('native-owner','owner');options('native-rarity','rarity');
    const entries=list(),size=40;state.page=Math.max(0,Math.min(state.page,Math.ceil(entries.length/size)-1));const page=entries.slice(state.page*size,(state.page+1)*size);
    $('native-count').textContent=entries.length+' / '+collections[state.tab].length;
    const columns=state.tab==='ancients'?['名称','幕次','遗物']:state.tab==='cards'?['名称','角色','稀有度','类型','费用','效果']:['名称','角色','稀有度','效果'];
    $('native-content').innerHTML=state.tab==='ancients'?'<nav class="native-ancient-directory" aria-label="先古之民">'+page.map(entry=>'<button data-native-collection="ancients" data-native-id="'+entry.id+'" aria-pressed="'+String(entry.id===state.selected)+'">'+escape(entry.name)+'</button>').join('')+'</nav>':page.length?'<table class="native-table"><thead><tr>'+columns.map(label=>'<th scope="col">'+label+'</th>').join('')+'</tr></thead><tbody>'+page.map(entry=>'<tr'+(entry.id===state.selected?' class="selected"':'')+'><th scope="row">'+referenceLink(state.tab,entry.id,entry.name)+'<small>'+escape(entry.id)+'</small></th>'+(state.tab==='ancients'?'<td>'+entry.acts.map(n=>'第'+n+'幕').join('、')+'</td><td>'+entry.relicIds.length+'</td>':'<td>'+escape(entry.owner)+'</td><td>'+escape(entry.rarity)+'</td>'+(state.tab==='cards'?'<td>'+escape(entry.type)+'</td><td class="native-cost">'+escape(cost(entry))+'</td>':'')+'<td class="native-effect">'+escape(effect(entry))+'</td>')+'</tr>').join('')+'</tbody></table>':'<p class="native-empty">没有匹配的条目</p>';
    $('native-page').textContent=entries.length?(state.page+1)+' / '+Math.ceil(entries.length/size):'0 / 0';$('native-prev').disabled=state.page===0;$('native-next').disabled=(state.page+1)*size>=entries.length;
    showDetail();history.replaceState(null,'','#'+new URLSearchParams({tab:state.tab,...(state.selected?{id:state.selected}:{})}));
  }
  document.addEventListener('click',event=>{
    const tab=event.target.closest('[data-native-tab]');if(tab){Object.assign(state,{tab:tab.dataset.nativeTab,selected:null,search:'',owner:'',rarity:'',page:0});$('native-search').value='';render();return;}
    const link=event.target.closest('[data-native-id]');if(link){if(link.dataset.nativeCollection!==state.tab){Object.assign(state,{tab:link.dataset.nativeCollection,search:'',owner:'',rarity:'',page:0});$('native-search').value='';}state.selected=link.dataset.nativeId;render();$('native-detail').scrollTop=0;if(matchMedia('(max-width:850px)').matches)$('native-detail').scrollIntoView({block:'start',behavior:'auto'});}
  });
  $('native-search').addEventListener('input',event=>{state.search=event.target.value;state.page=0;render();});
  for(const key of ['owner','rarity'])$('native-'+key).addEventListener('change',event=>{state[key]=event.target.value;state.page=0;render();});
  $('native-upgrade').onclick=()=>{state.upgraded=!state.upgraded;render();};
  $('native-prev').onclick=()=>{state.page--;render();$('native-content').scrollTop=0;};$('native-next').onclick=()=>{state.page++;render();$('native-content').scrollTop=0;};
  render();
})();