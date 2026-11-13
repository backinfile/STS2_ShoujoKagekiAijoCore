(function(root){
  'use strict';
  const systems={stage:'箭矢',partner:'知识',growth:'伤害加成',energy:'能量',utility:'通用'};
  function create({data,ui,dom,escape,assetURL,energyText,field,area,flush,render}){
    const entries=()=>data().meta.powers||[];
    const selected=()=>ui().selected?.collection==='powers'?entries().find(e=>e.uid===ui().selected.uid):null;
    const sourceCards=entry=>(entry.cardIds||[]).map(id=>data().cards.find(c=>c.id===id)).filter(Boolean);
    function filtered(){const state=ui(),q=state.search.trim().toLowerCase();return entries().filter(e=>JunnaFilters.matches(state.powerSystem,e.system)&&JunnaFilters.matches(state.powerOrigin,e.origin)&&(!q||[e.name,e.id,e.text,e.upgradedText,...sourceCards(e).map(c=>c.name)].join(' ').toLowerCase().includes(q)));}
    function links(entry){return sourceCards(entry).map(c=>'<button type="button" class="text-button" data-open-card="'+escape(c.uid)+'">'+escape(c.name)+' ↗</button>').join('');}
    function sidebar(){const all=entries(),state=ui(),button=(key,value,label,count)=>'<button type="button" class="side-item '+(JunnaFilters.state(state[key],value)==='include'?'active':'')+'" data-power-filter="'+key+'" data-value="'+value+'" '+JunnaFilters.attributes(state[key],value,label)+'><span>'+escape(label)+'</span><span>'+count+'</span></button>';return '<label class="field"><span class="field-label">搜索</span><input id="filter-search" type="search" value="'+escape(state.search)+'" placeholder="名称、效果、来源卡牌" autocomplete="off"></label><div class="sidebar-section"><p class="sidebar-title">来源</p>'+button('powerOrigin','all','全部',all.length)+button('powerOrigin','junna','纯那',all.filter(e=>e.origin==='junna').length)+button('powerOrigin','base','本体',all.filter(e=>e.origin==='base').length)+'</div><div class="sidebar-section"><p class="sidebar-title">体系</p>'+button('powerSystem','all','全部',all.length)+Object.entries(systems).filter(([key])=>all.some(e=>e.system===key)).map(([key,name])=>button('powerSystem',key,name,all.filter(e=>e.system===key).length)).join('')+'</div>';}
    function workspace(){
      const list=filtered(),state=ui();dom('body-layout').classList.toggle('has-power-selection',Boolean(selected())||state.selected?.collection==='root');
      dom('page-title').textContent='Power';dom('result-count').textContent=list.length+' / '+entries().length+' 项';
      dom('page-actions').innerHTML='<button type="button" class="button '+(state.upgraded?'active':'')+'" data-action="upgrade" aria-pressed="'+state.upgraded+'">'+(state.upgraded?'升级效果':'基础效果')+'</button>';
      dom('work-area').setAttribute('aria-labelledby','tab-powers');
      dom('tab-powers').scrollIntoView({block:'nearest',inline:'nearest'});
      dom('work-area').innerHTML=list.length?'<div class="power-list">'+list.map(e=>'<article class="power-entry '+(selected()?.uid===e.uid?'selected':'')+'" data-power-entry="'+escape(e.uid)+'"><button type="button" class="power-open" data-edit="powers" data-uid="'+escape(e.uid)+'" aria-label="编辑Power '+escape(e.name)+'">'+(e.art?'<img class="power-icon" src="'+assetURL(e.art)+'" alt="" width="128" height="128" loading="lazy" decoding="async">':'')+'<span class="power-heading"><span class="power-name">'+escape(e.name)+'</span><span class="power-status">'+escape(e.type)+' · '+escape(e.duration)+(e.origin==='base'?' · 本体':'')+'</span></span></button><p class="power-effect">'+energyText(state.upgraded?e.upgradedText||e.text:e.text)+'</p><div class="power-card-links">'+links(e)+'</div></article>').join('')+'</div>':'<p class="empty">没有匹配的Power</p>';
    }
    function inspector(){
      const entry=selected();dom('inspector').hidden=!entry;dom('inspector-footer').hidden=true;dom('preview').innerHTML='';dom('preview').removeAttribute('tabindex');dom('preview').removeAttribute('aria-label');dom('editor-form').innerHTML='';dom('editor-form').dataset.card='';dom('close-inspector').hidden=!entry;
      if(!entry)return;
      dom('inspector-title').textContent='编辑Power';dom('preview').innerHTML='<div class="power-icon-preview"><img src="'+assetURL(entry.art)+'" alt="'+escape(entry.name)+'" width="128" height="128"><img src="'+assetURL(entry.art)+'" alt="32像素预览" width="32" height="32"></div>';
      dom('editor-form').innerHTML=field('名称','name',entry.name,'class="name-input" data-on="change"')+'<div class="power-id">'+escape(entry.id)+'</div><div class="two-fields">'+field('类型','type',entry.type)+field('持续时间','duration',entry.duration)+'</div>'+area('基础效果','text',entry.text,4)+area('升级效果','upgradedText',entry.upgradedText||entry.text,4)+'<div class="power-card-links">'+links(entry)+'</div><details class="form-details"><summary>图标与契合</summary><div class="inline-actions"><button type="button" class="button" data-action="upload-art">选择图标</button></div>'+area('契合','iconDesign',entry.iconDesign,5)+field('出处','source',entry.source||'')+field('来源链接','sourceUrl',entry.sourceUrl||'','type="url"')+'</details>';
    }
    function sidebarClick(event){const button=event.target.closest('[data-power-filter]');if(!button)return false;flush();const key=button.dataset.powerFilter;ui()[key]=JunnaFilters.cycle(ui()[key],button.dataset.value);render();return true;}
    return {sidebar,workspace,inspector,sidebarClick,filtered};
  }
  root.JunnaPowers={create};
})(globalThis);
