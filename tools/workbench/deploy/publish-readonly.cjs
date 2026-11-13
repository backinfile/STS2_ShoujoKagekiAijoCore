'use strict';
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib'),crypto=require('node:crypto');
const {mods,defaultMod}=require('../mods.cjs');
const {createRegistry}=require('../workspace-registry.cjs');
function createPublisher({stateDirectory='/var/lib/junna-workbench',web='/var/www/junna',templateDirectory=__dirname}={}){
  const registry=createRegistry({stateDirectory,assetDirectory:path.join(web,'assets')});let published=null;
  function publish(){
    const key=JSON.stringify(registry.list().map(mod=>[mod.id,mod.revision]));if(key===published)return false;
    const release=path.join(web,'releases','shoujo-'+crypto.randomUUID());fs.mkdirSync(release,{recursive:true});
    fs.symlinkSync(path.join(web,'assets'),path.join(release,'assets'),'junction');
    const exportedAt=new Date().toISOString(),summary={exportedAt,mods:[]};
    for(const mod of mods){
      const relative=mod.id===defaultMod?'':path.join('mods',mod.id),directory=path.join(release,relative);
      const store=registry.get(mod.id),snapshot=store.state({assetURLs:true});
      const data=JSON.stringify({...snapshot.bundle,versions:snapshot.versions,exportedAt}).replace(/</g,'\\u003c');
      const html=fs.readFileSync(path.join(templateDirectory,relative,'public-template.html'),'utf8');
      fs.mkdirSync(directory,{recursive:true});
      for(const [name,content]of [['index.html',html],['snapshot.json',data]]){fs.writeFileSync(path.join(directory,name),content);fs.writeFileSync(path.join(directory,name+'.gz'),zlib.gzipSync(content));}
      const info={id:mod.id,revision:snapshot.revision,cards:snapshot.bundle.data.cards.length,assets:snapshot.assetIds.length};
      fs.writeFileSync(path.join(directory,'manifest.json'),JSON.stringify({...info,exportedAt})+'\n');summary.mods.push(info);
    }
    fs.writeFileSync(path.join(release,'workspaces.json'),JSON.stringify(summary)+'\n');
    const next=path.join(web,'current.pending');if(fs.existsSync(next))fs.unlinkSync(next);fs.symlinkSync(release,next,'junction');fs.renameSync(next,path.join(web,'current'));
    published=key;console.log('Published Shoujo workspaces '+key);return true;
  }
  return {publish,close:()=>registry.close()};
}
function start(){const config=JSON.parse(fs.readFileSync(path.join(__dirname,'relay.config.json'),'utf8'));const publisher=createPublisher({stateDirectory:config.stateDirectory||path.dirname(config.backendConfig),web:config.webDirectory||'/var/www/junna'});const tick=()=>{try{publisher.publish();}catch(error){console.error('Workspaces publish failed:',error.message);}};tick();const timer=setInterval(tick,1000);process.on('SIGTERM',()=>{clearInterval(timer);publisher.close();process.exit(0);});}
if(require.main===module)start();module.exports={createPublisher,start};
