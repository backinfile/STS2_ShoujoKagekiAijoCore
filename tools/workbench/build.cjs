'use strict';
// Build a read-only snapshot; the deployment builder also produces authenticated editors.
const path=require('node:path'),{execFileSync}=require('node:child_process');
const args=process.argv.slice(2),value=flag=>args[args.indexOf(flag)+1];
if(!args.includes('--snapshot'))throw Error('Usage: npm run build -- --snapshot <MCP snapshot.json> [--output <directory>] [--mod core|karen|junna]');
const mod=require('./mods.cjs').getMod(args.includes('--mod')?value('--mod'):'junna');
execFileSync(process.execPath,[path.join(__dirname,'build-readonly.cjs'),path.resolve(value('--snapshot')),path.resolve(args.includes('--output')?value('--output'):'out/workbench')],{stdio:'inherit',env:{...process.env,SHOUJO_WORKBENCH_MOD:mod.id}});
