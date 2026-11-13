(function(root){
  'use strict';
  function create({data,ui,dom,escape,assetURL,energyText,field,area}){
    const entries=()=>data().meta.relics||[];
    const selected=()=>ui().selected?.collection==='relics'?entries().find(e=>e.uid===ui().selected.uid):null;
    function link(entry,label){return '<button type="button" class="text-button" data-edit="relics" data-uid="'+escape(entry.uid)+'">'+escape(label)+' ↗</button>';}
    function relationship(entry){const from=entries().find(e=>e.id===entry.upgradeFrom),to=entries().find(e=>e.id===entry.upgradeTo);return [from?link(from,'升级自 '+from.name):'',to?link(to,'升级为 '+to.name):''].filter(Boolean).join('');}
    function sidebar(){return '<div class="sidebar-section"><p class="sidebar-title">遗物</p>'+entries().map(e=>'<button type="button" class="side-item '+(selected()?.uid===e.uid?'active':'')+'" data-edit="relics" data-uid="'+escape(e.uid)+'"><span>'+escape(e.name)+'</span><span>'+escape(e.rarity)+'</span></button>').join('')+'</div>';}
    function workspace(){
      const all=entries();
      dom('body-layout').classList.toggle('has-relic-selection',Boolean(selected())||ui().selected?.collection==='root');
      dom('page-title').textContent='遗物';dom('result-count').textContent=all.length+' 件遗物';
      dom('work-area').setAttribute('aria-labelledby','tab-relics');dom('page-actions').innerHTML='';
      dom('work-area').innerHTML=all.length?'<div class="relic-list">'+all.map(e=>'<article class="relic-entry '+(selected()?.uid===e.uid?'selected':'')+'"><button type="button" class="relic-open" data-edit="relics" data-uid="'+escape(e.uid)+'" aria-label="编辑遗物 '+escape(e.name)+'">'+(e.art?'<img class="relic-icon" src="'+assetURL(e.art)+'" alt="" width="128" height="128">':'')+'<span class="relic-heading"><span class="relic-name">'+escape(e.name)+'</span><span class="relic-rarity">'+escape(e.rarity)+'</span></span></button><div class="relic-body"><p class="relic-effect">'+energyText(e.text)+'</p><div class="relic-relation">'+relationship(e)+'</div></div></article>').join('')+'</div>':'<p class="empty">暂无遗物</p>';
    }
    function inspector(){
      const entry=selected();dom('inspector').hidden=!entry;dom('inspector-footer').hidden=true;
      dom('preview').innerHTML='';dom('preview').removeAttribute('tabindex');dom('preview').removeAttribute('aria-label');
      dom('editor-form').innerHTML='';dom('editor-form').dataset.card='';dom('close-inspector').hidden=!entry;
      if(!entry)return;
      dom('inspector-title').textContent='编辑遗物';
      dom('preview').innerHTML=entry.art?'<img class="relic-inspector-icon" src="'+assetURL(entry.art)+'" alt="'+escape(entry.name)+'" width="128" height="128">':'';
      dom('editor-form').innerHTML=field('名称','name',entry.name,'class="name-input" data-on="change"')+field('稀有度','rarity',entry.rarity)+area('效果','text',entry.text,4)+field('出处','source',entry.source)+field('来源链接','sourceUrl',entry.sourceUrl,'type="url"');
    }
    function syncFit(entry,key){if(['name','text','art','source','sourceUrl'].includes(key))for(const fit of data().fitPool)if(fit.relicIds?.includes(entry.id))fit[key]=entry[key];}
    return {sidebar,workspace,inspector,syncFit};
  }
  root.JunnaRelics={create};
})(globalThis);
