/* Existing workbench extension: event scenes, choices and their linked cards.
 * Reuses the fitPool entries and MCP transactions so references keep their identity. */
(function(root){
  'use strict';
  const fits=typeof module==='object'&&module.exports?require('./fit-pool-view.js'):root.JunnaFitPool;
  const isEvent=entry=>fits.isEventEntry(entry);
  const design=entry=>entry.eventDesign;
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
  function filterEntries(entries,query){
    const search=String(query||'').trim().toLowerCase();
    return entries.filter(isEvent).filter(entry=>!search||[entry.name,entry.text,JSON.stringify(entry.eventDesign)].join(' ').toLowerCase().includes(search));
  }
  function create({data,ui:getUI,dom,escape,assetURL,field,area,selectField,checkedPicker,flush,transact}){
    const entries=()=>data().fitPool.filter(isEvent);
    const cards=entry=>(entry.cardIds||[]).map(id=>data().cards.find(card=>card.id===id&&!card.trashedAt)).filter(Boolean);
    function sidebar(){const ui=getUI(),list=filterEntries(entries(),ui.search);return '<label class="field"><span class="field-label">搜索</span><input id="filter-search" type="search" value="'+escape(ui.search)+'" placeholder="名称、正文、选项" autocomplete="off"></label><div class="sidebar-section"><p class="sidebar-title">事件 · '+entries().length+'</p>'+list.map(entry=>'<button type="button" class="side-item '+(ui.selected?.uid===entry.uid?'active':'')+'" data-edit="fitPool" data-uid="'+escape(entry.uid)+'"><span>'+escape(entry.name)+'</span></button>').join('')+'</div>';}
    function workspace(){
      const ui=getUI(),list=filterEntries(entries(),ui.search);
      dom('body-layout').classList.toggle('has-fit-selection',ui.selected?.collection==='fitPool');
      dom('page-title').textContent='事件';dom('result-count').textContent=list.length+' / '+entries().length+' 个事件';dom('work-area').setAttribute('aria-labelledby','tab-events');
      dom('page-actions').innerHTML='<button type="button" class="button primary" data-action="add">新增事件</button>';
      dom('work-area').innerHTML=list.length?'<div class="event-list">'+list.map(entry=>{
        const event=design(entry),eligible=event.eligibility||{},url=fits.sourceURL(entry.sourceUrl);
        const conditions=[eligible.actNumber?'第'+eligible.actNumber+'幕':'',eligible.character,eligible.oncePerRun?'每局一次':''].filter(Boolean).join(' · ');
        return '<article class="event-entry'+(ui.selected?.uid===entry.uid?' selected':'')+'"><header class="event-heading"><div><h2>'+escape(entry.name)+'</h2><span class="event-conditions">'+escape(conditions)+'</span></div><button type="button" class="button" data-edit="fitPool" data-uid="'+escape(entry.uid)+'">编辑</button></header><div class="event-scene'+(entry.art?' has-art':'')+'">'+(entry.art?'<img class="event-art" src="'+assetURL(entry.art)+'" alt="'+escape(entry.name)+'" loading="lazy">':'')+'<div class="event-content"><p class="event-narrative">'+escape(event.narrative||'')+'</p><ol class="event-options">'+(event.options||[]).map(option=>'<li><span>'+escape(option.label||'未命名选项')+'</span><p>'+escape(option.effectText||'')+'</p></li>').join('')+'</ol></div></div><div class="fit-card-links">'+cards(entry).map(card=>'<button type="button" class="text-button" data-open-card="'+escape(card.uid)+'">'+escape(card.name)+' ↗</button>').join('')+'</div>'+(url?'<a class="fit-source" href="'+escape(url)+'" target="_blank" rel="noopener noreferrer">'+escape(entry.source||'来源')+' ↗</a>':'')+'</article>';
      }).join('')+'</div>':'<p class="empty">没有匹配的事件</p>';
    }
    function editor(entry){
      const event=design(entry),eligible=event.eligibility||{};
      dom('inspector-title').textContent='编辑事件';dom('preview').innerHTML=entry.art?'<img class="fit-inspector-image" src="'+assetURL(entry.art)+'" alt="'+escape(entry.name)+'">':'';
      return field('名称','name',entry.name,'class="name-input"')+field('角色','event.character',eligible.character||'')+'<div class="two-fields">'+selectField('幕次','event.actNumber',[{value:'',label:'全部'},1,2,3,4],eligible.actNumber||'')+selectField('出现次数','event.oncePerRun',[{value:'true',label:'每局一次'},{value:'false',label:'不限'}],String(Boolean(eligible.oncePerRun)))+'</div>'+area('正文','event.narrative',event.narrative||'',6)+(event.options||[]).map((option,index)=>'<fieldset class="event-option-fields"><legend>选项 '+(index+1)+'</legend>'+field('名称','event.option.'+index+'.label',option.label||'')+area('效果','event.option.'+index+'.effectText',option.effectText||'',2)+area('结果','event.option.'+index+'.resultText',option.resultText||'',3)+'<button type="button" class="button danger" data-action="event-remove-option:'+index+'">删除选项</button></fieldset>').join('')+'<button type="button" class="button" data-action="event-add-option">新增选项</button><div class="inline-actions"><button type="button" class="button" data-action="upload-art">选择图片</button>'+(entry.art?'<button type="button" class="button" data-action="reset-art">移除图片</button>':'')+'</div>'+checkedPicker('关联卡牌','cardIds',data().cards.filter(card=>!card.trashedAt),entry.cardIds||[])+field('出处','source',entry.source||'')+field('来源链接','sourceUrl',entry.sourceUrl||'','type="url"')+area('备注','notes',entry.notes||'',4);
    }
    function action(action){
      if(!/^event-(add-option|remove-option:\d+)$/.test(action))return false;
      flush();const ui=getUI(),entry=entries().find(entry=>entry.uid===ui.selected?.uid);if(!entry)return true;
      transact(action==='event-add-option'?'新增事件选项':'删除事件选项',()=>{
        const options=design(entry).options||=[];
        if(action==='event-add-option'){let index=1;while(options.some(option=>option.id==='option_'+index))index++;options.push({id:'option_'+index,label:'新选项',effectText:'',resultText:'',effects:[],costs:[],endsEvent:true});}
        else options.splice(Number(action.split(':')[1]),1);
        sync(entry);
      });return true;
    }
    return{sidebar,workspace,editor,action};
  }
  const api={isEvent,newEntry,setField,sync,filterEntries,create};
  if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaEvents=api;
})(typeof globalThis==='object'?globalThis:this);
