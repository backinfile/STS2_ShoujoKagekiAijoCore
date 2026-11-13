'use strict';
const mods=Object.freeze([
  {id:'sts2',name:'游戏本体',modId:'SlayTheSpire2',workspaceId:'sts2-native-reference-v1',readOnly:true},
  {id:'core',name:'Core',modId:'ShoujoKagekiAijoCore',workspaceId:'shoujo-core-workbench-v1'},
  {id:'karen',name:'华恋',modId:'ShoujoKagekiAijoKaren',workspaceId:'shoujo-karen-workbench-v1'},
  {id:'junna',name:'纯那',modId:'ShoujoKagekiJunna',workspaceId:'junna-mod-workbench-20261101-v1'}
]);
const defaultMod='junna';
function getMod(id=defaultMod){const mod=mods.find(mod=>mod.id===id);if(!mod){const error=Error('Mod 不存在');error.code='NOT_FOUND';throw error;}return mod;}
function emptyBundle(id){const mod=getMod(id);return {schemaVersion:1,workspaceId:mod.workspaceId,revision:0,data:{title:mod.name+' Mod 设计工作台',meta:{systems:[{id:'stage',name:mod.name}],powers:[],relics:[],startingDeck:[],startingRelics:[]},cards:[],terms:[],mechanisms:[],partnerGroups:[],archetypes:[],styles:[],fitPool:[]},assets:{},history:[],entityVersions:{},mcpRequests:{},ui:{tab:'cards'},undoStack:[],redoStack:[]};}
module.exports={mods,defaultMod,getMod,emptyBundle};
