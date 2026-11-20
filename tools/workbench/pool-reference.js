/* Extend the incumbent overview with source ranges beside live pool counts.
 * Reference basis: STS2_Card_Reserch/docs/职业卡池设计要求_按五职业规律.md
 * sections 1, 2 and 6; 体系驱动卡池设计流程.md, stage 4.
 * These are absolute complete-pool references, not quotas scaled to draft size.
 */
const JunnaPoolReference = (() => {
  'use strict';
  const costs = [
    {name:'0费',min:9,max:20,match:value=>value===0},
    {name:'1费',min:43,max:54,match:value=>value===1},
    {name:'2费',min:14,max:19,match:value=>value===2},
    {name:'3费',min:3,max:10,match:value=>value===3},
    {name:'4费及以上',min:0,max:2,match:value=>typeof value==='number'&&value>=4},
    {name:'X费',min:1,max:2,match:value=>value==='X'}
  ];
  const rarities = [
    {name:'基础',min:3,max:4},
    {name:'普通',min:20,max:20},
    {name:'罕见',min:36,max:36},
    {name:'稀有',min:26,max:26},
    {name:'远古',min:2,max:2}
  ];
  const types = [{name:'攻击',min:27,max:37},{name:'技能',min:30,max:42},{name:'能力',min:18,max:20}];
  const active = card => !card.trashedAt&&!card.reservedAt;
  const domain=typeof module!=='undefined'&&module.exports?require('./workbench-domain.js'):JunnaWorkbenchDomain;
  const baseType = card => card.type==='飞矢'?card.arrowSubtype:card.type;
  function range(row) {
    if(row.min===undefined||row.max===undefined)return '—';
    return (row.min===row.max?row.min:row.min+'–'+row.max)+(row.unit||'张');
  }
  function difference(row) {
    if(row.min===undefined||row.max===undefined)return '—';
    const unit=row.unit||'张';
    return row.current<row.min?'少'+(row.min-row.current)+unit:row.current>row.max?'多'+(row.current-row.max)+unit:'范围内';
  }
  function compute(data,{includeAll=false,workspaceId}={}) {
    const cards=data.cards.filter(card=>active(card)&&(includeAll||domain.isCharacterReferenceCard(card,workspaceId)));
    const rewards=cards.filter(card=>domain.isRewardCard(card,workspaceId)),routes=data.archetypes||[];
    const large=routes.filter(route=>route.level!=='小体系'),small=routes.filter(route=>route.level==='小体系');
    const breakthroughIds=new Set((data.terms||[]).filter(term=>term.kind==='突破').map(term=>term.id));
    const count=match=>cards.filter(match).length;
    const costRows=costs.map(row=>({...row,current:count(card=>row.match(card.cost))}));
    const typeRows=types.map(row=>({...row,current:count(card=>baseType(card)===row.name)}));
    const rarityRows=rarities.map(row=>({...row,current:count(card=>card.rarity===row.name)}));
    for(const name of ['事件','衍生','状态','诅咒','任务']){const current=count(card=>card.rarity===name);if(current)rarityRows.push({name,current});}
    for(const name of ['状态','诅咒','任务']){const current=count(card=>baseType(card)===name);if(current)typeRows.push({name,current});}
    const unknownCosts=cards.length-costRows.reduce((sum,row)=>sum+row.current,0);
    const unknownTypes=cards.length-typeRows.reduce((sum,row)=>sum+row.current,0);
    return {
      total:cards.length,
      costs:costRows,unknownCosts,
      rarities:rarityRows,
      types:typeRows,unknownTypes,
      structure:[
        {name:'总牌数',min:87,max:88,current:cards.length},
        {name:'大体系',min:2,max:4,current:large.length,unit:'个'},
        {name:'小体系',min:2,max:3,current:small.length,unit:'个'},
        {name:'体系外牌',min:15,max:21,current:count(card=>!routes.some(route=>(card.archetypeIds||[]).includes(route.id)))}
      ],
      routes:routes.map(route=>{
        const members=cards.filter(card=>(card.archetypeIds||[]).includes(route.id)),isSmall=route.level==='小体系';
        return {
          name:route.name,level:route.level,min:isSmall?7:12,max:isSmall?11:20,
          current:(includeAll?cards:rewards).filter(card=>(card.archetypeIds||[]).includes(route.id)).length,
          primary:route.level==='主流派',
          breakthroughs:members.filter(card=>card.role==='突破限制牌'||card.design?.classification==='规则突破'||(card.termIds||[]).some(id=>breakthroughIds.has(id))).length,
          breakthroughMin:isSmall?0:1,breakthroughMax:isSmall?2:3
        };
      })
    };
  }
  return {compute,range,difference};
})();
if(typeof module!=='undefined'&&module.exports)module.exports=JunnaPoolReference;
