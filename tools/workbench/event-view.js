/* Existing workbench extension: event scenes, choices and their linked cards.
 * Reuses the fitPool entries and MCP transactions so references keep their identity. */
(function(root){
  'use strict';
  const fits=typeof module==='object'&&module.exports?require('./fit-pool-view.js'):root.JunnaFitPool;
  const isEvent=entry=>fits.isEventEntry(entry);
  const filters=typeof module==='object'&&module.exports?require('./filter-state.js'):root.JunnaFilters;
  const design=entry=>entry.eventDesign;
  const category=entry=>entry.trashedAt?'trash':entry.reservedAt?'reserve':'events';
  const categories=[['events','事件池','事件'],['reserve','备用','备用事件'],['trash','垃圾桶','事件垃圾桶']];
  const act=entry=>String(design(entry).eligibility?.actNumber||'any');
  const character=entry=>String(design(entry).eligibility?.character||'');
  function sync(entry){
    const event=design(entry),eligibility=event.eligibility||{};
    event.name=entry.name;
    const conditions=[eligibility.actNumber?'第'+eligibility.actNumber+'幕':'',eligibility.character?eligibility.character+'专属':''].filter(Boolean).join(' · ');
    entry.text=[conditions,event.narrative||'',(event.options||[]).map(option=>'['+(option.label||'未命名选项')+'] '+(option.effectText||'')).join('\n')].filter(Boolean).join('\n\n');
  }
  function newEntry({uid,id,eventId}){
    return fits.normalizeEntry({uid,id,name:'新事件',theme:'角色专属事件',draftStatus:'草稿',cardIds:[],eventDesign:{id:eventId,uid,status:'draft',name:'新事件',eligibility:{character:'星见纯那',actNumber:2,roomType:'event',oncePerRun:true},narrative:'',options:[]}});
  }
  function setField(entry,key,value){
    const event=design(entry);
    if(key==='event.narrative')event.narrative=String(value);
    else if(key==='event.character')(event.eligibility||={}).character=String(value);
    else if(key==='event.actNumber')(event.eligibility||={}).actNumber=Number(value)||null;
    else if(key==='event.oncePerRun')(event.eligibility||={}).oncePerRun=value==='true';
    else {
      const match=/^event\.option\.(\d+)\.(label|effectText|resultText)$/.exec(key);
      if(!match||!event.options?.[Number(match[1])])return false;
      event.options[Number(match[1])][match[2]]=String(value);
    }
    sync(entry);return true;
  }
  function filterEntries(entries,query,{eventAct='all',eventCharacter='all'}={}){
    const search=String(query||'').trim().toLowerCase();
    return entries.filter(entry=>isEvent(entry)&&filters.matches(eventAct,act(entry))&&
      filters.matches(eventCharacter,'role:'+character(entry))&&
      (!search||[entry.name,entry.id,entry.source,entry.text,JSON.stringify(design(entry))].join(' ').toLowerCase().includes(search)));
  }
  function create({data,ui:getUI,dom,escape,assetURL,field,area,selectField,checkedPicker,filterButtons,flush,transact}){
    const entries=()=>data().fitPool.filter(isEvent);
    const readonly=()=>document.body.classList.contains('readonly');
    const cards=entry=>(entry.cardIds||[]).map(id=>data().cards.find(card=>card.id===id&&!card.trashedAt)).filter(Boolean);

    function sidebar(){
      const ui=getUI(),all=entries();
      const categoryOptions=categories.map(([key,label])=>'<button type="button" class="filter-option" data-event-category="'+key+'" aria-pressed="'+(ui.eventCategory===key)+'">'+label+'</button>').join('');
      const acts=[...new Set(all.map(act))].sort((a,b)=>a.localeCompare(b,undefined,{numeric:true}));
      const characters=[...new Set(all.map(character))];
      const actsHTML=filterButtons('幕次','eventAct',acts.map(value=>({value,label:value==='any'?'不限幕次':'第'+value+'幕'})));
      const charactersHTML=filterButtons('角色','eventCharacter',characters.map(name=>({value:'role:'+name,label:name||'不限角色'})));
      return '<fieldset class="field filter-group"><legend class="field-label">分类</legend><div class="filter-options">'+categoryOptions+'</div></fieldset>'+
        '<label class="field"><span class="field-label">搜索</span><input id="filter-search" type="search" value="'+escape(ui.search)+'" placeholder="名称、正文、选项" autocomplete="off"></label>'+
        '<div class="event-filters">'+actsHTML+charactersHTML+'</div>'+
        '<div class="sidebar-bottom"><button type="button" class="button" data-action="clear-filters">重置筛选</button></div>';
    }

    function tile(entry,active,trash,canRestore){
      const event=design(entry),eligible=event.eligibility||{},options=event.options||[];
      const conditions=[eligible.actNumber?'第'+eligible.actNumber+'幕':'',eligible.character,eligible.oncePerRun?'每局一次':''].filter(Boolean).join(' · ');
      const art=entry.art?'<img class="event-thumbnail" src="'+assetURL(entry.art)+'" alt="" width="56" height="40" loading="lazy" decoding="async">':'';
      const summary=event.narrative||options.map(option=>option.label).filter(Boolean).join(' · ');
      const restore=canRestore?'<button type="button" class="button event-restore" data-restore-event="'+escape(entry.uid)+'" aria-label="恢复 '+escape(entry.name)+'">恢复</button>':'';
      return '<article class="event-entry'+(active?' selected':'')+'"><button type="button" class="event-open" data-edit="fitPool" data-uid="'+escape(entry.uid)+'" aria-label="'+(trash?'查看':'编辑')+'事件 '+escape(entry.name)+'" aria-controls="inspector" aria-expanded="'+active+'">'+
        '<span class="event-tile-heading">'+art+'<span><span class="event-name">'+escape(entry.name)+'</span>'+(conditions?'<span class="event-conditions">'+escape(conditions)+'</span>':'')+'</span></span>'+
        (summary?'<span class="event-summary">'+escape(summary)+'</span>':'')+'</button>'+restore+'</article>';
    }

    function workspace(){
      const ui=getUI(),scope=entries().filter(entry=>category(entry)===ui.eventCategory),list=filterEntries(scope,ui.search,ui);
      const selected=ui.selected?.collection==='fitPool'?scope.find(entry=>entry.uid===ui.selected.uid):null;
      const trash=ui.eventCategory==='trash',canRestore=trash&&!readonly();
      dom('body-layout').classList.toggle('has-fit-selection',Boolean(selected));
      dom('page-title').textContent=categories.find(([key])=>key===ui.eventCategory)?.[2]||'事件';
      dom('result-count').textContent=list.length+(list.length===scope.length?'':' / '+scope.length)+' 个事件';
      dom('work-area').setAttribute('aria-labelledby','tab-events');
      dom('page-actions').innerHTML=trash?'':'<button type="button" class="button primary" data-action="add">新增事件</button>';
      const empty=scope.length?'没有匹配的事件':trash?'垃圾桶为空':'暂无事件';
      dom('work-area').innerHTML=list.length?
        '<div class="event-grid">'+list.map(entry=>tile(entry,selected?.uid===entry.uid,trash,canRestore)).join('')+'</div>':
        '<p class="empty">'+empty+'</p>';
    }

    function editor(entry){
      const event=design(entry),eligible=event.eligibility||{},locked=entry.trashedAt||readonly();
      const related=cards(entry);
      const links=related.length?'<div class="field"><span class="field-label">关联卡牌</span><div class="fit-card-links">'+related.map(card=>
        '<button type="button" class="text-button" data-open-card="'+escape(card.uid)+'">'+escape(card.name)+' ↗</button>').join('')+'</div></div>':'';
      const url=fits.sourceURL(entry.sourceUrl);
      const sourceLink=url?'<a class="field fit-source" href="'+escape(url)+'" target="_blank" rel="noopener noreferrer">'+escape(entry.source||'来源')+' ↗</a>':'';
      dom('inspector-title').textContent=entry.trashedAt?'已删除事件':'编辑事件';
      dom('preview').innerHTML=entry.art?'<img class="fit-inspector-image" src="'+assetURL(entry.art)+'" alt="'+escape(entry.name)+'">':'';
      const options=(event.options||[]).map((option,index)=>
        '<fieldset class="event-option-fields"><legend>选项 '+(index+1)+'</legend>'+
        field('名称','event.option.'+index+'.label',option.label||'')+
        area('效果','event.option.'+index+'.effectText',option.effectText||'',2)+
        area('结果','event.option.'+index+'.resultText',option.resultText||'',3)+
        (locked?'':'<button type="button" class="button danger" data-action="event-remove-option:'+index+'">删除选项</button>')+'</fieldset>').join('');
      const tools=locked?'':'<button type="button" class="button" data-action="event-add-option">新增选项</button>'+
        '<div class="inline-actions"><button type="button" class="button" data-action="upload-art">选择图片</button>'+
        (entry.art?'<button type="button" class="button" data-action="reset-art">移除图片</button>':'')+'</div>'+
        checkedPicker('关联卡牌','cardIds',data().cards.filter(card=>!card.trashedAt),entry.cardIds||[]);
      return field('名称','name',entry.name,'class="name-input"')+
        field('角色','event.character',eligible.character||'')+
        '<div class="two-fields">'+selectField('幕次','event.actNumber',[{value:'',label:'全部'},1,2,3,4],eligible.actNumber||'')+
        selectField('出现次数','event.oncePerRun',[{value:'true',label:'每局一次'},{value:'false',label:'不限'}],String(Boolean(eligible.oncePerRun)))+'</div>'+
        area('正文','event.narrative',event.narrative||'',6)+options+tools+links+
        field('出处','source',entry.source||'')+field('来源链接','sourceUrl',entry.sourceUrl||'','type="url"')+sourceLink+area('备注','notes',entry.notes||'',4);
    }
    function action(action){
      if(!/^event-(add-option|remove-option:\d+)$/.test(action))return false;
      flush();const ui=getUI(),entry=entries().find(entry=>entry.uid===ui.selected?.uid);if(!entry||entry.trashedAt)return true;
      transact(action==='event-add-option'?'新增事件选项':'删除事件选项',()=>{
        const options=design(entry).options||=[];
        if(action==='event-add-option'){let index=1;while(options.some(option=>option.id==='option_'+index))index++;options.push({id:'option_'+index,label:'新选项',effectText:'',resultText:'',effects:[],costs:[],endsEvent:true});}
        else options.splice(Number(action.split(':')[1]),1);
        sync(entry);
      });return true;
    }
    return{sidebar,workspace,editor,action};
  }
  const api={isEvent,category,newEntry,setField,sync,filterEntries,create};
  if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaEvents=api;
})(typeof globalThis==='object'?globalThis:this);
