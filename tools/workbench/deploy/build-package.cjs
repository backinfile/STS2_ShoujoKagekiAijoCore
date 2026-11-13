'use strict';
const fs=require('node:fs'),path=require('node:path'),{execFileSync}=require('node:child_process');
const [snapshotFile,destination,connectionFile]=process.argv.slice(2);if(!snapshotFile||!destination||!connectionFile)throw Error('Usage: node build-package.cjs <Junna MCP snapshot> <destination> <private connection config>');
const source=path.resolve(__dirname,'..'),out=path.resolve(destination),snapshot=path.resolve(snapshotFile),configFile=path.resolve(connectionFile);
const config=JSON.parse(fs.readFileSync(configFile,'utf8').replace(/^\uFEFF/,''));
if(new URL(config.origin).protocol!=='https:'||!/^[a-f0-9]{64}$/.test(config.secret)||!/^\/(?:[A-Za-z0-9_-]+\/)*[A-Za-z0-9_-]+$/.test(config.prefix))throw Error('Cloud connection configuration is invalid');
fs.mkdirSync(out,{recursive:true});
for(const name of ['filter-state.js','mcp-daemon.cjs','mcp-config.cjs','mcp-store.cjs','mcp-sqlite.cjs','mcp-tools.cjs','mods.cjs','native-store.cjs','native-game.json','workspace-registry.cjs','workbench-domain.js','fit-pool-view.js','seed.json','package.json','package-lock.json'])fs.copyFileSync(path.join(source,name),path.join(out,name));
for(const name of ['relay.cjs','run-server.cjs','publish-readonly.cjs','static-runtime.cjs'])fs.writeFileSync(path.join(out,name),fs.readFileSync(path.join(__dirname,name),'utf8').replaceAll("require('../","require('./"));
for(const mod of require('../mods.cjs').mods){
  const relative=mod.id==='junna'?'':path.join('mods',mod.id),pub=path.join(out,'public',relative),editor=path.join(out,relative);
  if(mod.readOnly){
    fs.mkdirSync(pub,{recursive:true});fs.mkdirSync(editor,{recursive:true});
    const {buildNativePage}=require('../native-page.cjs');
    fs.writeFileSync(path.join(pub,'index.html'),buildNativePage({directory:pub}));
    fs.copyFileSync(path.join(source,'native-game.json'),path.join(pub,'snapshot.json'));
    fs.writeFileSync(path.join(editor,'editor.html'),buildNativePage({directory:pub,editorPrefix:config.prefix}));
    fs.copyFileSync(path.join(pub,'index.html'),path.join(editor,'public-template.html'));
    fs.cpSync(path.join(pub,'assets'),path.join(out,'public/assets'),{recursive:true});continue;
  }
  const input=mod.id==='junna'?snapshot:path.join(out,mod.id+'-seed.json');if(mod.id!=='junna')fs.writeFileSync(input,JSON.stringify(require('../mods.cjs').emptyBundle(mod.id)));
  const env={...process.env,SHOUJO_WORKBENCH_MOD:mod.id,SHOUJO_WORKBENCH_SOURCE:source};
  execFileSync(process.execPath,[path.join(source,'build-readonly.cjs'),input,pub],{stdio:'inherit',env});
  execFileSync(process.execPath,[path.join(__dirname,'build-editor.cjs'),input,pub,editor,configFile],{stdio:'inherit',env});
  fs.copyFileSync(path.join(pub,'index.html'),path.join(editor,'public-template.html'));
  if(relative)fs.cpSync(path.join(pub,'assets'),path.join(out,'public/assets'),{recursive:true});
  if(mod.id!=='junna')fs.unlinkSync(input);
}
fs.writeFileSync(path.join(out,'relay.config.json'),JSON.stringify({origin:config.origin,secret:config.secret,prefix:config.prefix,relayPort:config.serverPort||54828,backendConfig:config.backendConfig||'/var/lib/junna-workbench/backend.json',stateDirectory:config.stateDirectory||'/var/lib/junna-workbench',webDirectory:config.webDirectory||'/var/www/junna'},null,2)+'\n');
console.log(JSON.stringify({files:fs.readdirSync(out).length,workspaces:require('../mods.cjs').mods.length}));
