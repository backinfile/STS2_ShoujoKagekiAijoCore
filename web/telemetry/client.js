/* Shared mobile controls, connection recovery and scoped PWA installation. */
'use strict';
(() => {
 window.enhanceFilters=form=>{
  if(form.previousElementSibling?.classList.contains('filter-toggle'))return;
  const button=document.createElement('button');button.type='button';button.className='filter-toggle';button.textContent='筛选';button.setAttribute('aria-controls',form.id);button.setAttribute('aria-expanded','false');
  button.addEventListener('click',()=>{const open=form.classList.toggle('filters-open');button.setAttribute('aria-expanded',String(open));});form.before(button);
 };
 document.querySelectorAll('form.filters').forEach(window.enhanceFilters);
 let hiddenAt=0,lastRefresh=0,prompt;
 const refresh=()=>{if(document.hidden||Date.now()-lastRefresh<1000)return;lastRefresh=Date.now();window.dispatchEvent(new Event('observatory:refresh'));};
 window.addEventListener('offline',()=>{const status=document.getElementById('connection');status.textContent='离线';status.className='connection';});
 window.addEventListener('online',refresh);
 document.addEventListener('visibilitychange',()=>{if(document.hidden)hiddenAt=Date.now();else if(hiddenAt&&Date.now()-hiddenAt>30000)refresh();});
 const install=document.getElementById('install');
 const standalone=()=>matchMedia('(display-mode:standalone)').matches||navigator.standalone;
 window.addEventListener('beforeinstallprompt',event=>{event.preventDefault();prompt=event;if(!standalone())install.hidden=false;});
 window.addEventListener('appinstalled',()=>{prompt=null;install.hidden=true;});
 const ios=/iPad|iPhone|iPod/.test(navigator.userAgent)||(navigator.platform==='MacIntel'&&navigator.maxTouchPoints>1);
 if(ios&&!standalone())install.hidden=false;
 install.addEventListener('click',async()=>{
  if(prompt){const current=prompt;prompt=null;install.hidden=true;await current.prompt();await current.userChoice;return;}
  if(ios){const sheet=document.createElement('dialog');sheet.className='modal';sheet.innerHTML='<div class="modal-top"><h2>安装</h2><button aria-label="关闭">×</button></div><div class="install-help">Safari：分享 → 添加到主屏幕</div>';sheet.querySelector('button').onclick=()=>sheet.close();sheet.addEventListener('close',()=>sheet.remove());document.body.append(sheet);sheet.showModal();}
 });
 if('serviceWorker' in navigator&&window.isSecureContext){
  const register=async()=>{
   try{
    const registration=await navigator.serviceWorker.register('sw.js',{scope:'./',updateViaCache:'none'});
    const offerUpdate=()=>{
     if(!registration.waiting||!navigator.serviceWorker.controller||document.getElementById('appUpdate'))return;
     const button=document.createElement('button');button.id='appUpdate';button.textContent='更新';install.before(button);
     button.onclick=()=>{button.disabled=true;registration.waiting?.postMessage('SKIP_WAITING');};
    };
    offerUpdate();registration.addEventListener('updatefound',()=>{registration.installing?.addEventListener('statechange',offerUpdate);});
    let reloading=false;navigator.serviceWorker.addEventListener('controllerchange',()=>{if(document.getElementById('appUpdate')&&!reloading){reloading=true;location.reload();}});
   }catch{/* Installation can be retried on the next visit. */}
  };
  window.addEventListener('load',()=>{if('requestIdleCallback' in window)requestIdleCallback(register,{timeout:5000});else setTimeout(register,1200);},{once:true});
 }
})();
