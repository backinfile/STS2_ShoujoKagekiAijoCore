'use strict';
const fs=require('node:fs'),path=require('node:path');
const {fork}=require('node:child_process');
const config=JSON.parse(fs.readFileSync(path.join(__dirname,'relay.config.json'),'utf8'));
const backend=JSON.parse(fs.readFileSync(config.backendConfig,'utf8'));
const registry=require('./workspace-registry.cjs').createRegistry({stateDirectory:config.stateDirectory||path.dirname(config.backendConfig),assetDirectory:path.join(config.webDirectory||'/var/www/junna','assets')});
const {server}=require('./mcp-daemon.cjs').createDaemon({registry,token:backend.token,port:backend.port,pageFile:path.join(__dirname,'editor.html')});
server.listen(backend.port,'127.0.0.1',()=>console.log('Shoujo workbench MCP ready'));
const relay=require('./relay.cjs');
let publisher,stopping=false;
function startPublisher(){publisher=fork(path.join(__dirname,'publish-readonly.cjs'),[],{stdio:'inherit',windowsHide:true});publisher.on('exit',()=>{if(!stopping)setTimeout(startPublisher,1000);});}
startPublisher();
process.on('SIGTERM',()=>{stopping=true;publisher?.kill();relay.close();server.close(()=>{registry.close();process.exit(0);});});
