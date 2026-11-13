'use strict';
const fs=require('node:fs');
const {connectClient}=require('./mcp-server.cjs');
async function callTool(name,args={}){const client=await connectClient(args.modId);try{const result=await client.callTool({name,arguments:args});const value=result.structuredContent||JSON.parse(result.content[0].text);if(result.isError){const error=Error(value.message);Object.assign(error,value);throw error;}return value;}finally{await client.close();}}
if(require.main===module){const [name,argsFile]=process.argv.slice(2);callTool(name,argsFile?JSON.parse(fs.readFileSync(argsFile,'utf8')):{}).then(value=>process.stdout.write(JSON.stringify(value,null,2)+'\n')).catch(error=>{process.stderr.write(JSON.stringify({code:error.code,message:error.message,conflicts:error.conflicts})+'\n');process.exitCode=1;});}
module.exports={callTool};
