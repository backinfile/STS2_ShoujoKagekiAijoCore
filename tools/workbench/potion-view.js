/* THESIS: Give potions a direct place beside relics.
 * OWN-WORLD: Reuse the workbench's compact sidebar and relic rows.
 * STORY: Find a rarity, inspect a potion, edit its effect and image.
 * FIRST VIEWPORT: Rarity filters at left; icon, name and effect in each row.
 * FORM: A focused extension of the existing item editor. */
(function(root){
  'use strict';
  const fits=typeof module==='object'&&module.exports?require('./fit-pool-view.js'):root.JunnaFitPool;
  const filters=typeof module==='object'&&module.exports?require('./filter-state.js'):root.JunnaFilters;
  const rarities=['普通','罕见','稀有'],values=['Common','Uncommon','Rare'];
  const isPotion=entry=>fits.isPotionEntry(entry);
  function filterEntries(entries,ui){
    const query=String(ui.search||'').trim().toLowerCase();
    return entries.filter(isPotion).filter(entry=>filters.matches(ui.rarity,entry.potionDesign.rarity)&&(!query||[entry.name,entry.text,entry.potionDesign.character].join(' ').toLowerCase().includes(query))).sort((a,b)=>rarities.indexOf(a.potionDesign.rarity)-rarities.indexOf(b.potionDesign.rarity));
  }
  function newEntry({uid,id,potionId}){
    return fits.normalizeEntry({uid,id,name:'新药水',theme:'职业药水',draftStatus:'草稿',cardIds:[],potionDesign:{id:potionId,rarity:'普通',rarityValue:'Common',character:'星见纯那',characterId:'ShoujoKagekiJunna',status:'draft',nameStatus:'待定',artStatus:'待定'}});
  }
  function setField(entry,key,value){
    if(key==='potion.rarity'){
      const index=rarities.indexOf(value);if(index<0)return false;
      entry.potionDesign.rarity=value;entry.potionDesign.rarityValue=values[index];return true;
    }
    if(key==='potion.character'){entry.potionDesign.character=String(value);return true;}
    return false;
  }
  function create({data,ui:getUI,dom,escape,assetURL,energyText,field,area,selectField}){
    const entries=()=>data().fitPool.filter(isPotion);
    function sidebar(){
      const ui=getUI(),list=entries();
      return '<label class="field"><span class="field-label">搜索</span><input id="filter-search" type="search" value="'+escape(ui.search)+'" placeholder="名称、效果" autocomplete="off"></label><fieldset class="field filter-group"><legend class="field-label">稀有度</legend><div class="filter-options">'+['all',...rarities].map(value=>'<button type="button" class="filter-option" data-filter="rarity" data-filter-value="'+value+'" '+filters.attributes(ui.rarity,value,value==='all'?'全部':value)+'>'+(value==='all'?'全部':value)+'</button>').join('')+'</div></fieldset><div class="sidebar-section"><p class="sidebar-title">药水 · '+list.length+'</p>'+list.map(entry=>'<button type="button" class="side-item '+(ui.selected?.uid===entry.uid?'active':'')+'" data-edit="fitPool" data-uid="'+escape(entry.uid)+'"><span>'+escape(entry.name)+'</span><span>'+escape(entry.potionDesign.rarity)+'</span></button>').join('')+'</div>';
    }
    function workspace(){
      const ui=getUI(),all=entries(),list=filterEntries(all,ui);
      dom('body-layout').classList.toggle('has-relic-selection',Boolean(ui.selected?.collection==='fitPool'&&all.some(entry=>entry.uid===ui.selected.uid)));
      dom('page-title').textContent='药水';dom('result-count').textContent=list.length+' / '+all.length+' 瓶';
      dom('work-area').setAttribute('aria-labelledby','tab-potions');
      dom('page-actions').innerHTML='<button type="button" class="button primary" data-action="add">新增药水</button>';
      dom('work-area').innerHTML=list.length?'<div class="relic-list">'+list.map(entry=>'<article class="relic-entry '+(ui.selected?.uid===entry.uid?'selected':'')+'" data-potion-entry="'+escape(entry.uid)+'"><button type="button" class="relic-open" data-edit="fitPool" data-uid="'+escape(entry.uid)+'" aria-label="编辑药水 '+escape(entry.name)+'">'+(entry.art?'<img class="relic-icon" src="'+assetURL(entry.art)+'" alt="" width="128" height="128">':'')+'<span class="relic-heading"><span class="relic-name">'+escape(entry.name)+'</span><span class="relic-rarity">'+escape(entry.potionDesign.rarity)+(entry.potionDesign.character?' · '+escape(entry.potionDesign.character):'')+'</span></span></button><div class="relic-body"><p class="relic-effect">'+energyText(entry.text)+'</p></div></article>').join('')+'</div>':'<p class="empty">没有匹配的药水</p>';
    }
    function editor(entry){
      dom('inspector-title').textContent='编辑药水';
      dom('preview').innerHTML=entry.art?'<img class="relic-inspector-icon" src="'+assetURL(entry.art)+'" alt="'+escape(entry.name)+'" width="128" height="128">':'';
      return field('名称','name',entry.name,'class="name-input" data-on="change"')+'<div class="two-fields">'+selectField('稀有度','potion.rarity',rarities,entry.potionDesign.rarity)+field('角色','potion.character',entry.potionDesign.character||'')+'</div>'+area('效果','text',entry.text,4)+'<div class="inline-actions"><button type="button" class="button" data-action="upload-art">选择图片</button>'+(entry.art?'<button type="button" class="button" data-action="reset-art">移除图片</button>':'')+'</div>'+area('备注','notes',entry.notes||'',4);
    }
    return{sidebar,workspace,editor};
  }
  const api={isPotion,filterEntries,newEntry,setField,create};
  if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaPotions=api;
})(typeof globalThis==='object'?globalThis:this);
