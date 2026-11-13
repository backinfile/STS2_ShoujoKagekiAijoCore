'use strict';
const {mods,getMod,defaultMod}=require('./mods.cjs');
const safe=value=>JSON.stringify(value).replace(/</g,'\\u003c');
function modPage(html,{modId=defaultMod,editorPrefix}={}){
  getMod(modId);
  const prefix=editorPrefix||'';
  const entries=mods.map(mod=>({...mod,href:prefix+(mod.id===defaultMod?'/':'/mods/'+mod.id+'/')}));
  return html.replace('<!--MOD_SWITCHER-->','<label class="mod-switcher"><span>Mod</span><select id="mod-select" aria-label="Mod">'+entries.map(mod=>'<option value="'+mod.id+'"'+(mod.id===modId?' selected':'')+'>'+mod.name+'</option>').join('')+'</select></label><script id="workbench-mods" type="application/json">'+safe({current:modId,mods:entries})+'</script>');
}
module.exports={modPage};
