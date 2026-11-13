const JunnaEnergyIcons=(()=>{
  'use strict';
  // Match the base game's EnergyIconsFormatter; keep editable text and statistics intact.
  const number='(?:<span class="text-green">)?(?:[-−+]?\\d+|[XN](?:[+−-]\\d+)?)(?:<\\/span>)?';
  const adjustment=new RegExp('((?:耗能|费用)(?:增加|减少|降低|提高|加|减|[-−+]))('+number+')(?:点(?:能量)?|能量)?','g');
  const fixedCost=new RegExp('((?:耗能|费用)(?:变为|变成|降为|降低至|为))('+number+')(?:点(?:能量)?|能量)?','g');
  const threshold=new RegExp('((?:耗能|费用)(?:不低于|不高于|大于等于|小于等于|低于|高于|大于|小于|等于))('+number+')(?:点(?:能量)?|能量|费)?','g');
  const retention=new RegExp('((?:可)?保留)('+number+')(?:点)?能量','g');
  const retainedGain=new RegExp('('+number+')(?:点)?能量(?=(?:并|且)(?:可)?保留(?:到|至)下(?:个)?回合)','g');
  const remaining=new RegExp('((?:每)?剩余)('+number+')(?:点)?能量','g');
  const quantity=new RegExp('('+number+')(?:点)?能量','g');
  const fee=new RegExp('('+number+')费','g');
  const escape=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  function protect(html,iconURL,display='icons'){
    if(display==='text')return {text:String(html??''),values:[]};
    const values=[],image='<img class="text-energy-icon" src="'+escape(iconURL)+'" alt="" aria-hidden="true" draggable="false" decoding="async">';
    function icons(amount,numeric=false){
      const plain=String(amount??'').replace(/<[^>]*>/g,''),repeated=!numeric&&/^[1-3]$/.test(plain);
      return '<span class="inline-energy" role="img" aria-label="'+escape(plain?plain+'点能量':'能量')+'"'+(plain?' data-energy-amount="'+escape(plain)+'"':'')+'>'+(repeated?image.repeat(Number(plain)):(amount||'')+image)+'</span>';
    }
    function token(value){const key='\uE002'+String.fromCodePoint(0xF8000+values.length)+'\uE003';values.push({token:key,html:value});return key;}
    let text=String(html??'');
    text=text.replace(adjustment,(_,prefix,amount)=>prefix+token(icons(amount,true)));
    text=text.replace(fixedCost,(_,prefix,amount)=>prefix+token(icons(amount,true)));
    text=text.replace(threshold,(_,prefix,amount)=>prefix+token(icons(amount)));
    text=text.replace(retention,(_,prefix,amount)=>prefix+token(icons(amount,true)));
    text=text.replace(retainedGain,(_,amount)=>token(icons(amount,true)));
    text=text.replace(remaining,(_,prefix,amount)=>prefix+token(icons(amount,true)));
    text=text.replace(quantity,(_,amount)=>token(icons(amount)));
    text=text.replace(fee,(_,amount)=>token(icons(amount)));
    text=text.replace(/能量/g,()=>token(icons()));
    return {text,values};
  }
  function restore(text,values){let html=text;for(const item of values)html=html.split(item.token).join(item.html);return html;}
  function render(html,iconURL,display='icons'){const value=protect(html,iconURL,display);return restore(value.text,value.values);}
  return {protect,restore,render};
})();
if(typeof module!=='undefined'&&module.exports)module.exports=JunnaEnergyIcons;
