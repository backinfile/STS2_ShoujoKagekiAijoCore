(function(root){
  'use strict';
  const copy=value=>JSON.parse(JSON.stringify(value));
  const fields=['startingHp','startingGold','startingDeck','startingRelics'];
  function read(data){const m=data.meta||{};return {startingHp:m.startingHp??70,startingGold:m.startingGold??99,startingDeck:copy(m.startingDeck||[]),startingRelics:copy(m.startingRelics??[])};}
  const active=entry=>!entry.trashedAt&&!entry.reservedAt;
  function validate(value,data){
    const integer=(v,min,max,name)=>{if(!Number.isInteger(v)||v<min||v>max)throw Error(name+'须为'+min+'～'+max+'的整数');};
    integer(value.startingHp,1,9999,'初始生命');integer(value.startingGold,0,99999,'初始金币');
    if(!Array.isArray(value.startingDeck)||!value.startingDeck.length)throw Error('请添加初始牌');
    const ids=new Set();
    for(const row of value.startingDeck){if(!data.cards.some(c=>active(c)&&c.id===row.cardId))throw Error('初始牌已停用或不存在：'+row.cardId);if(ids.has(row.cardId))throw Error('初始牌重复，请合并数量');ids.add(row.cardId);integer(row.count,1,99,'卡牌数量');}
    if(!Array.isArray(value.startingRelics))throw Error('初始遗物格式无效');
    const relics=data.meta?.relics||[];
    if(new Set(value.startingRelics).size!==value.startingRelics.length)throw Error('初始遗物重复');
    for(const id of value.startingRelics)if(!relics.some(r=>active(r)&&r.id===id))throw Error('初始遗物已停用或不存在：'+id);
    return copy(value);
  }
  function create({data,dom,escape,transact,toast}){
    let draft=null,base=null;
    const readonly=()=>document.body.classList.contains('readonly');
    function capture(){const form=dom('initial-form');if(!form)return;const before=read(data());if(!draft){base=before;draft=copy(before);}draft.startingHp=Number(form.elements.startingHp.value);draft.startingGold=Number(form.elements.startingGold.value);draft.startingDeck=[...form.querySelectorAll('[data-start-card]')].map(row=>({cardId:row.querySelector('select').value,count:Number(row.querySelector('input').value)}));draft.startingRelics=[...form.querySelectorAll('[data-start-relic] select')].map(input=>input.value);}
    function render(){
      const d=data(),value=draft||read(d),locked=readonly(),cards=d.cards.filter(active),relics=(d.meta?.relics||[]).filter(active),count=value.startingDeck.reduce((n,c)=>n+c.count,0);
      const options=(entries,id)=>entries.map(e=>'<option value="'+escape(e.id)+'"'+(e.id===id?' selected':'')+'>'+escape(e.name)+' · '+escape(e.id)+'</option>').join('')+(entries.some(e=>e.id===id)?'':'<option selected value="'+escape(id)+'">'+escape(id)+'</option>');
      const number=(name,label,min,max)=>'<label class="field"><span class="field-label">'+label+'</span>'+(locked?'<strong>'+escape(value[name])+'</strong>':'<input name="'+name+'" type="number" min="'+min+'" max="'+max+'" step="1" required value="'+escape(value[name])+'">')+'</label>';
      dom('page-title').textContent='初始';dom('result-count').textContent=count+' 张牌 · '+value.startingRelics.length+' 件遗物';dom('page-actions').innerHTML='';dom('work-area').setAttribute('aria-labelledby','tab-initial');
      dom('work-area').innerHTML='<form id="initial-form" class="initial-config"><section><h2>基本</h2><div class="initial-numbers">'+number('startingHp','生命',1,9999)+number('startingGold','金币',0,99999)+'</div></section><section><h2>牌组 <span>'+count+'</span></h2><div class="initial-rows">'+value.startingDeck.map((row,i)=>'<div class="initial-row" data-start-card="'+i+'">'+(locked?'<span>'+escape(cards.find(c=>c.id===row.cardId)?.name||row.cardId)+'</span><span>'+row.count+'</span>':'<select aria-label="第'+(i+1)+'种初始牌">'+options(cards,row.cardId)+'</select><input aria-label="第'+(i+1)+'种卡牌数量" type="number" min="1" max="99" step="1" required value="'+escape(row.count)+'"><button type="button" class="icon-btn" data-initial-remove-card="'+i+'" aria-label="移除第'+(i+1)+'种初始牌">×</button>')+'</div>').join('')+'</div>'+(!locked?'<button type="button" class="button" data-initial-add="card">添加卡牌</button>':'')+'</section><section><h2>遗物 <span>'+value.startingRelics.length+'</span></h2><div class="initial-rows">'+value.startingRelics.map((id,i)=>'<div class="initial-row initial-relic-row" data-start-relic="'+i+'">'+(locked?'<span>'+escape(relics.find(r=>r.id===id)?.name||id)+'</span>':'<select aria-label="第'+(i+1)+'件初始遗物">'+options(relics,id)+'</select><button type="button" class="icon-btn" data-initial-remove-relic="'+i+'" aria-label="移除第'+(i+1)+'件初始遗物">×</button>')+'</div>').join('')+'</div>'+(!locked?'<button type="button" class="button" data-initial-add="relic">添加遗物</button>':'')+'</section>'+(!locked?'<div class="initial-actions"><button type="submit" class="button primary">保存</button><button type="button" class="button" data-initial-reset>重置</button><span role="status">'+(draft?'未保存':'')+'</span></div>':'')+'</form>';
      if(locked)return;
      const form=dom('initial-form');form.addEventListener('input',()=>{capture();form.querySelector('[role="status"]').textContent='未保存';});
      form.addEventListener('click',event=>{
        const button=event.target.closest('button');if(!button||button.type==='submit')return;
        if(button.hasAttribute('data-initial-reset')){draft=base=null;render();return;}
        capture();
        if(button.hasAttribute('data-initial-remove-card'))draft.startingDeck.splice(Number(button.dataset.initialRemoveCard),1);
        if(button.hasAttribute('data-initial-remove-relic'))draft.startingRelics.splice(Number(button.dataset.initialRemoveRelic),1);
        if(button.dataset.initialAdd==='card'){const next=cards.find(c=>!draft.startingDeck.some(r=>r.cardId===c.id));if(next)draft.startingDeck.push({cardId:next.id,count:1});}
        if(button.dataset.initialAdd==='relic'){const next=relics.find(r=>!draft.startingRelics.includes(r.id));if(next)draft.startingRelics.push(next.id);}
        render();
      });
      form.addEventListener('submit',event=>{event.preventDefault();capture();try{const next=validate(draft,data());if(JSON.stringify(base)!==JSON.stringify(read(data())))throw Error('初始配置已有更新，请重置后再修改');const record=transact('修改初始配置',d=>{for(const key of fields)d.meta[key]=copy(next[key]);});if(record){draft=base=null;render();}}catch(error){toast(error.message);}});
    }
    return {render};
  }
  const api={read,validate,create};if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaInitialConfig=api;
})(typeof globalThis!=='undefined'?globalThis:this);
