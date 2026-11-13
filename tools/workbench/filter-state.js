(function(root){
  'use strict';
  function read(value){
    const unique=values=>[...new Set((Array.isArray(values)?values:[]).filter(item=>typeof item==='string'&&item!=='all'))];
    if(typeof value==='string')return {included:value&&value!=='all'?[value]:[],excluded:[]};
    const excluded=unique(value?.excluded);
    return {included:unique(value?.included).filter(item=>!excluded.includes(item)),excluded};
  }
  function state(value,option){
    const selection=read(value);
    if(option==='all')return selection.included.length||selection.excluded.length?'none':'include';
    if(selection.excluded.includes(option))return 'exclude';
    return selection.included.includes(option)?'include':'none';
  }
  function cycle(value,option){
    if(option==='all')return 'all';
    const selection=read(value),previous=state(value,option);
    selection.included=selection.included.filter(item=>item!==option);
    selection.excluded=selection.excluded.filter(item=>item!==option);
    if(previous==='none')selection.included.push(option);
    if(previous==='include')selection.excluded.push(option);
    return selection.included.length||selection.excluded.length?selection:'all';
  }
  function matches(value,actual,predicate=(candidate,option)=>String(candidate)===option){
    const selection=read(value),values=Array.isArray(actual)?actual:[actual];
    const has=option=>values.some(candidate=>predicate(candidate,option));
    return (!selection.included.length||selection.included.some(has))&&!selection.excluded.some(has);
  }
  function prune(value,allowed){
    const selection=read(value),valid=new Set(allowed);
    if(typeof value==='string')return value==='all'||valid.has(value)?value:'all';
    selection.included=selection.included.filter(item=>valid.has(item));
    selection.excluded=selection.excluded.filter(item=>valid.has(item));
    return selection.included.length||selection.excluded.length?selection:'all';
  }
  function encode(value){return typeof value==='object'?JSON.stringify(read(value)):String(value);}
  function decode(value){
    if(!String(value).startsWith('{'))return value;
    try{return read(JSON.parse(value));}catch(_){return 'all';}
  }
  function attributes(value,option,label){
    const current=state(value,option),pressed={none:'false',include:'true',exclude:'mixed'}[current];
    const status={none:'未筛选',include:'筛选',exclude:'排除'}[current];
    const escaped=String(label).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    return 'data-filter-state="'+current+'" aria-pressed="'+pressed+'" aria-label="'+escaped+' · '+status+'"';
  }
  const api={read,state,cycle,matches,prune,encode,decode,attributes};
  if(typeof module==='object'&&module.exports)module.exports=api;
  else root.JunnaFilters=api;
})(typeof globalThis==='object'?globalThis:this);
