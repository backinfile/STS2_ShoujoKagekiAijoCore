(function(root){
  'use strict';
  const categories=[['relics','遗物池','遗物'],['reserve','备用','备用遗物'],['trash','垃圾桶','遗物垃圾桶']];
  const category=entry=>entry.trashedAt?'trash':entry.reservedAt?'reserve':'relics';
  const rarity=entry=>String(entry.rarity??'').trim()||'未分类';

  function create({data,ui,dom,escape,assetURL,energyText,field,area,filterButtons}){
    const entries=()=>data().meta.relics||[];
    const scopedEntries=()=>entries().filter(entry=>category(entry)===ui().relicCategory);
    const readonly=()=>document.body.classList.contains('readonly');
    const selected=()=>ui().selected?.collection==='relics'?scopedEntries().find(e=>e.uid===ui().selected.uid):null;

    function matchesFilters(entry){
      const query=ui().search.trim().toLowerCase();
      return JunnaFilters.matches(ui().rarity,rarity(entry))&&
        (!query||[entry.name,entry.id,entry.text,entry.source].some(value=>String(value??'').toLowerCase().includes(query)));
    }

    function sidebar(){
      const categoryOptions=categories.map(([key,label])=>
        '<button type="button" class="filter-option" data-relic-category="'+key+'" aria-pressed="'+(ui().relicCategory===key)+'">'+label+'</button>').join('');
      const rarities=[...new Set(entries().map(rarity))];
      return '<fieldset class="field filter-group"><legend class="field-label">分类</legend><div class="filter-options">'+categoryOptions+'</div></fieldset>'+
        '<label class="field"><span class="field-label">搜索</span><input id="filter-search" type="search" value="'+escape(ui().search)+'" placeholder="名称、效果等" autocomplete="off"></label>'+
        '<div class="relic-filters">'+filterButtons('稀有度','rarity',rarities)+'</div>'+
        '<div class="sidebar-bottom"><button type="button" class="button" data-action="clear-filters">重置筛选</button></div>';
    }

    function tile(entry,active,trash,canRestore){
      const icon=entry.art?'<img class="relic-icon" src="'+assetURL(entry.art)+'" alt="" width="40" height="40" loading="lazy">':'';
      const label=escape((trash?'查看':'编辑')+'遗物 '+entry.name);
      const restore=canRestore?'<button type="button" class="button relic-restore" data-restore-relic="'+escape(entry.uid)+'" aria-label="恢复 '+escape(entry.name)+'">恢复</button>':'';
      return '<article class="relic-entry'+(active?' selected':'')+'"><button type="button" class="relic-open" data-edit="relics" data-uid="'+escape(entry.uid)+'" aria-label="'+label+'" aria-controls="inspector" aria-expanded="'+active+'">'+
        '<span class="relic-tile-heading">'+icon+'<span class="relic-heading"><span class="relic-name">'+escape(entry.name)+'</span><span class="relic-rarity">'+escape(rarity(entry))+'</span></span></span>'+
        '<span class="relic-effect">'+energyText(entry.text)+'</span></button>'+restore+'</article>';
    }

    function workspace(){
      const scope=scopedEntries(),list=scope.filter(matchesFilters),uid=selected()?.uid,trash=ui().relicCategory==='trash';
      const canRestore=trash&&!readonly();
      dom('body-layout').classList.toggle('has-relic-selection',Boolean(uid)||ui().selected?.collection==='root');
      dom('page-title').textContent=categories.find(([key])=>key===ui().relicCategory)?.[2]||'遗物';
      dom('result-count').textContent=list.length+(list.length===scope.length?'':' / '+scope.length)+' 件遗物';
      dom('work-area').setAttribute('aria-labelledby','tab-relics');dom('page-actions').innerHTML='';
      const empty=scope.length?'没有匹配的遗物':trash?'垃圾桶为空':'暂无遗物';
      dom('work-area').innerHTML=list.length?
        '<div class="relic-grid">'+list.map(entry=>tile(entry,uid===entry.uid,trash,canRestore)).join('')+'</div>':
        '<p class="empty">'+empty+'</p>';
    }

    function relationship(entry){
      const links=[[entry.upgradeFrom,'升级自'],[entry.upgradeTo,'升级为']].map(([id,action])=>{
        const target=entries().find(e=>e.id===id);
        return target?'<button type="button" class="text-button" data-edit="relics" data-uid="'+escape(target.uid)+'">'+
          action+' '+escape(target.name)+(target.trashedAt?' · 已删除':'')+' ↗</button>':'';
      }).join('');
      return links?'<div class="relic-relation">'+links+'</div>':'';
    }

    function inspector(){
      const entry=selected();dom('inspector').hidden=!entry;dom('inspector-footer').hidden=!entry||readonly();
      dom('preview').innerHTML='';dom('preview').removeAttribute('tabindex');dom('preview').removeAttribute('aria-label');
      dom('editor-form').innerHTML='';dom('editor-form').dataset.card='';dom('close-inspector').hidden=!entry;
      if(!entry)return;
      const trash=Boolean(entry.trashedAt);
      dom('inspector-title').textContent=trash?'已删除遗物':'编辑遗物';
      dom('duplicate-entity').hidden=true;dom('card-group-actions').hidden=true;
      dom('reserve-entity').hidden=trash;dom('reserve-entity').textContent=entry.reservedAt?'移回遗物池':'移入备用';
      dom('delete-entity').hidden=trash;dom('restore-entity').hidden=!trash;
      dom('preview').innerHTML=entry.art?'<img class="relic-inspector-icon" src="'+assetURL(entry.art)+'" alt="'+escape(entry.name)+'" width="88" height="88">':'';
      dom('editor-form').innerHTML=field('名称','name',entry.name,'class="name-input" data-on="change"')+field('稀有度','rarity',entry.rarity)+area('效果','text',entry.text,4)+relationship(entry)+field('出处','source',entry.source)+field('来源链接','sourceUrl',entry.sourceUrl,'type="url"');
      if(trash)for(const input of dom('editor-form').querySelectorAll('input,textarea'))input.disabled=true;
    }
    function syncFit(entry,key){if(['name','text','art','source','sourceUrl'].includes(key))for(const fit of data().fitPool)if(fit.relicIds?.includes(entry.id))fit[key]=entry[key];}
    return {sidebar,workspace,inspector,syncFit,matchesFilters};
  }
  root.JunnaRelics={create,category};
})(globalThis);
