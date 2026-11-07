/* The release builder replaces the version with a digest of the shipped assets. */
'use strict';
const VERSION='__ASSET_VERSION__';
const PREFIX='core-observatory:'+self.registration.scope+':';
const CACHE=PREFIX+VERSION;
const local=path=>new URL(path,self.registration.scope).href;
self.addEventListener('install',event=>{
 event.waitUntil(caches.open(CACHE).then(cache=>cache.addAll(['offline.html','style.css','assets/app_icon_192.png'].map(local))));
});
self.addEventListener('message',event=>{if(event.data==='SKIP_WAITING')self.skipWaiting();});
self.addEventListener('activate',event=>{
 event.waitUntil((async()=>{for(const key of await caches.keys())if(key.startsWith(PREFIX)&&key!==CACHE)await caches.delete(key);await self.clients.claim();})());
});
let writes=Promise.resolve();
function remember(request,response){
 if(!response.ok||response.type==='opaque')return Promise.resolve();
 writes=writes.catch(()=>{}).then(async()=>{
  const cache=await caches.open(CACHE);await cache.put(request,response);
  const keys=await cache.keys(),removable=keys.filter(key=>key.url!==local('offline.html'));
  for(const key of removable.slice(0,Math.max(0,keys.length-80)))await cache.delete(key);
 });
 return writes;
}
self.addEventListener('fetch',event=>{
 const request=event.request,url=new URL(request.url),scope=new URL(self.registration.scope);
 if(request.method!=='GET'||url.origin!==scope.origin||!url.pathname.startsWith(scope.pathname))return;
 const path=url.pathname.slice(scope.pathname.length);
 // Live data and authenticated page responses always use the network.
 if(path.startsWith('api/'))return;
 if(request.mode==='navigate'){
  event.respondWith(fetch(request).catch(async()=>await caches.match(local('offline.html'))||Response.error()));return;
 }
 if(!/^(?:app\.js|errors\.js|client\.js|style\.css|errors\.css|catalog\.json|relics\.json|characters\.json|assets\/[a-z0-9_]+\.(?:webp|png))$/.test(path))return;
 event.respondWith((async()=>{
  const cached=await caches.match(request,{cacheName:CACHE});
  if(cached)return cached;
  const response=await fetch(request);event.waitUntil(remember(request,response.clone()).catch(()=>{}));return response;
 })());
});
