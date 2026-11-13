(() => {
  'use strict';
  const config=JSON.parse(document.getElementById('workbench-mods')?.textContent||'null');
  if(!config)return;
  const select=document.getElementById('mod-select');
  select.value=config.current;
  select.addEventListener('change',()=>{
    const id=select.value;if(id===config.current)return;
    const mod=config.mods.find(mod=>mod.id===id);if(!mod)return;
    const target=new URL(mod.href,location.href);
    const event=new CustomEvent('workbench:before-switch',{cancelable:true,detail:{target:target.href}});
    if(!window.dispatchEvent(event)){select.value=config.current;return;}
    location.assign(target.href);
  });
})();
