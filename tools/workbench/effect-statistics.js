/* Per-effect counts read base descriptions and current card relationships.
 * A face counts once per effect; different effects can share that face.
 * Named references are resolved from live names/IDs, including knowledge peers.
 */
const JunnaEffectStatistics = (() => {
  'use strict';
  const knowledge=card=>(card.keywords||[]).includes('知识')||/^知识[。．]/.test(card.text||'');
  const arrow=card=>card.type==='箭矢';
  const high=card=>typeof card.cost==='number'&&card.cost>=2;
  const x=card=>card.cost==='X';
  const clauses=card=>String(card.text||'').split(/[。；;\n]/).map(value=>value.replace(/\s/g,'')).filter(Boolean);
  function action(clause){
    const text=clause.replace(/^本回合[，,]/,''),event=text.match(/^(.*?)(?:时|后)[，,]?(.+)$/);
    return event&&/每当|每次|每回合|打出|完成|进入手牌|在手牌中|抽到|受到|回合开始|回合结束/.test(event[1])?event[2]:text;
  }
  const any=(card,test)=>clauses(card).some(clause=>test(clause,action(clause)));
  const handArrowPlay=card=>any(card,(s,a)=>/将[^。]*手牌[^。]*作为5回合的箭矢打出/.test(a));
  const gainEnergy=text=>/获得[^，,。]*(?:点)?能量(?=$|[，,。]|并|且|可)/.test(text);
  const bonusGain=text=>/获得[^，,。]*伤害加成/.test(text);
  const discount=text=>/(?:耗能|费用)(?:减少|降低|减|[-−])|(?:耗能|费用)变为0|(?:耗能|费用)为0/.test(text);
  const highEvent=text=>/打出[^。]*(?:耗能|费用)(?:不低于|至少为?|大于等于)(?:2|3|4|5|6|7|8|9)/.test(text);
  const knowledgeEvent=text=>/打出[^。]*知识牌|每打出\d+张知识牌/.test(text);
  const extraHits=text=>/额外造成(?:\d+|X|N)次伤害|伤害次数(?:增加|增多)/.test(text);
  function context(data,route,defaults,profile){
    const aliases=card=>[card.name,card.id?.length>=3?card.id:null].filter(Boolean);
    const referenced=(text,predicate)=>data.cards.some(card=>predicate(card)&&aliases(card).some(name=>text.includes(name)));
    const target=(text,kind,source)=>{
      const predicate=kind==='箭矢'?arrow:knowledge;
      if(text.includes(kind)||referenced(text,predicate))return true;
      if(referenced(text,card=>card.id!==source.id))return false;
      return predicate(source)&&/此牌|自身|其|它|获得|耗能|费用|保留|重放/.test(text);
    };
    const peer=(text,card)=>{
      const ids=new Set(card.partnerCardIds||[]);
      for(const group of data.partnerGroups||[]){const groupIds=[group.mainCardId,...group.grantedCardIds];if(groupIds.includes(card.id))for(const id of groupIds)if(id!==card.id)ids.add(id);}
      return /关联(?:的)?(?:知识)?牌/.test(text)||referenced(text,entry=>ids.has(entry.id));
    };
    return {target,peer,referenced,role:card=>profile(card,route,defaults).role};
  }
  const matchers={
    'role.both':(c,ctx)=>ctx.role(c)==='both',
    'arrow.play':arrow,
    'arrow.hand-as-five-turn-play':handArrowPlay,
    'arrow.enemy-accelerate':c=>any(c,(s,a)=>/所有目标为敌人的箭矢(?:获得[^。]*然后)?加速(?:\d+|[xXN])回合/.test(a)),
    'arrow.self-accelerate':c=>any(c,(s,a)=>/所有目标为自己的箭矢加速(?:\d+|[xXN])回合/.test(a)),
    'arrow.x-play':(c,ctx)=>x(c)&&any(c,(s,a)=>/^打出X张/.test(s)&&ctx.target(a,'箭矢',c)),
    'arrow.play-energy':c=>any(c,(s,a)=>/打出[^。]*箭矢牌(?:时|后)/.test(s)&&gainEnergy(a)),
    'arrow.play-draw':c=>any(c,(s,a)=>/打出[^。]*箭矢牌(?:时|后)/.test(s)&&/抽(?:取)?[^。]*牌/.test(a)),
    'arrow.play-block':c=>any(c,(s,a)=>/打出[^。]*箭矢牌(?:时|后)/.test(s)&&/获得[^。]*格挡/.test(a)),
    'arrow.play-accelerate':c=>any(c,(s,a)=>/打出[^。]*箭矢牌(?:时|后)/.test(s)&&/加速/.test(a)),
    'arrow.complete-return':c=>any(c,(s,a)=>/箭矢[^。]*完成(?:时|后)/.test(s)&&/(?:放回|放入|加入)[^。]*手牌/.test(a)),
    'arrow.complete-block':c=>any(c,(s,a)=>/箭矢[^。]*完成(?:时|后)/.test(s)&&/获得[^。]*格挡/.test(a)),
    'arrow.complete-buff':c=>any(c,(s,a)=>/箭矢[^。]*完成(?:时|后)/.test(s)&&/伤害加成|重放|升级/.test(a)),
    'arrow.extra-bonus':c=>any(c,(s,a)=>/箭矢[^。]*额外触发(?:时|后)/.test(s)&&bonusGain(a)),
    'arrow.count-damage':c=>any(c,s=>/每有[^。]*箭矢|箭矢(?:的)?数量/.test(s)&&/伤害/.test(s)),
    'arrow.count-energy':c=>any(c,s=>/每有[^。]*箭矢|箭矢(?:的)?数量/.test(s)&&gainEnergy(s)),
    'arrow.present-event':c=>/(?:在场|布置)期间/.test(c.text||'')&&/每|打出|受到/.test(c.text||''),
    'arrow.present-block':c=>any(c,s=>/有箭矢在场时|有布置中?的箭矢时/.test(s)&&/获得[^。]*格挡/.test(s)),
    'arrow.present-reduction':c=>/有布置的箭矢时/.test(c.text||'')&&/受到的伤害降低/.test(c.text||''),
    'arrow.damage-block':c=>/箭矢造成伤害(?:时|后)/.test(c.text||'')&&/获得[^。]*格挡/.test(c.text||''),
    'arrow.attack-damage-play':(c,ctx)=>any(c,(s,a)=>/造成攻击伤害(?:时|后)/.test(s)&&/打出/.test(a)&&ctx.target(a,'箭矢',c)),
    'arrow.consume-prevent':c=>/消耗一个布置的箭矢/.test(c.text||'')&&/阻止这次伤害/.test(c.text||''),
    'arrow.accelerate':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/加速(?:\d+|X|N|到完成)|立即加速到完成/.test(a)),
    'arrow.extra':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/额外触发(?:目标的)?(?:一个|所有|全部|目标|该|此)?(?:布置的)?箭矢|箭矢[^。]*额外触发(?:\d+|X|N)次/.test(a)),
    'arrow.copy':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/复制|复制品/.test(a)),
    'arrow.convert-in':c=>/变(?:化)?为[^。]*箭矢牌/.test(c.text||'')&&!/变(?:化)?为[^。]*非箭矢牌/.test(c.text||''),
    'arrow.convert-out':c=>/变(?:化)?为[^。]*非箭矢牌/.test(c.text||''),
    'arrow.generate':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/(?:加入[^。]*(?:手牌|抽牌堆|弃牌堆)|(?:^|[，,])生成(?:\d+|一|两|X)张)/.test(a)&&!/复制|复制品|(?:手牌|抽牌堆|弃牌堆|消耗牌堆)中|无论何处|不论何处/.test(a)),
    'arrow.draw':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/抽取|抽牌堆[^。]*(?:加入|放入)[^。]*手牌/.test(a)),
    'arrow.deck-play':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/抽牌堆/.test(a)&&/打出/.test(a)),
    'arrow.anywhere-play':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/无论何处|不论何处/.test(a)&&/打出/.test(a)),
    'arrow.self-play':c=>arrow(c)&&/(?:重新)?打出(?:自己|此牌)/.test(c.text||''),
    'arrow.return':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/弃牌堆|消耗牌堆|无论何处|不论何处/.test(a)&&/(?:放回|放入|加入)[^。]*手牌/.test(a)),
    'arrow.discard-return':c=>any(c,(s,a)=>/弃牌堆/.test(a)&&/(?:放回|放入|加入)[^。]*手牌/.test(a)),
    'arrow.cost':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/(?:耗能|费用)[^。]*(?:变为|减少|增加|不消耗)|不消耗能量|不支付费用/.test(a)),
    'arrow.retain':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/获得保留/.test(a)),
    'arrow.replay':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&/获得重放/.test(a)),
    'arrow.bonus':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'箭矢',c)&&bonusGain(a)),
    'knowledge.play':knowledge,
    'knowledge.generate':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/(?:加入|生成)[^。]*(?:手牌|抽牌堆|弃牌堆|牌组|知识牌)/.test(a)&&!/复制|复制品/.test(a)),
    'knowledge.copy':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/复制|复制品/.test(a)),
    'knowledge.draw':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/抽取|抽牌堆[^。]*(?:放入|加入)[^。]*手牌/.test(a)),
    'knowledge.return':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/弃牌堆|消耗牌堆|无论何处|不论何处/.test(a)&&/(?:放入|加入|放回)[^。]*手牌/.test(a)),
    'knowledge.auto-play-linked':c=>any(c,(s,a)=>/知识牌/.test(s)&&/免费打出[^。]*关联牌/.test(a)),
    'knowledge.play-energy':c=>any(c,(s,a)=>knowledgeEvent(s)&&gainEnergy(a)),
    'knowledge.play-block':c=>any(c,(s,a)=>knowledgeEvent(s)&&/获得[^。]*格挡/.test(a)),
    'knowledge.play-bonus':c=>any(c,(s,a)=>knowledgeEvent(s)&&bonusGain(a)),
    'knowledge.play-accelerate':c=>any(c,(s,a)=>knowledgeEvent(s)&&/加速/.test(a)),
    'knowledge.play-hits':(c,ctx)=>any(c,s=>(knowledgeEvent(s)||/本场战斗打出过/.test(s)&&ctx.peer(s,c))&&extraHits(s)),
    'knowledge.enter-buff':c=>any(c,(s,a)=>/知识牌[^。]*进入手牌|抽到[^。]*知识牌/.test(s)&&/伤害加成|重放|升级/.test(a)),
    'knowledge.draw-linked':c=>any(c,(s,a)=>/抽到[^。]*知识牌/.test(s)&&/抽取[^。]*关联/.test(a)),
    'knowledge.link-discount':(c,ctx)=>any(c,(s,a)=>ctx.peer(a,c)&&discount(a)),
    'knowledge.link-bonus':(c,ctx)=>any(c,(s,a)=>ctx.peer(a,c)&&bonusGain(a)),
    'knowledge.link-hits':(c,ctx)=>any(c,(s,a)=>ctx.peer(a,c)&&extraHits(a)),
    'knowledge.link-reuse':(c,ctx)=>any(c,(s,a)=>ctx.peer(a,c)&&/抽取|(?:放入|加入|放回)[^。]*手牌|(?:免费)?打出(?:你)?手牌中的|免费打出[^。]*关联牌|(?:无论|不论)何处[，,]?打出/.test(a)),
    'knowledge.link-accelerate':(c,ctx)=>any(c,(s,a)=>ctx.peer(a,c)&&/加速/.test(a)),
    'knowledge.link-replay':(c,ctx)=>any(c,(s,a)=>ctx.peer(a,c)&&/获得重放/.test(a)),
    'knowledge.hand-condition':(c,ctx)=>/手牌中有(?:其他)?知识牌/.test(c.text||'')||any(c,s=>ctx.peer(s,c)&&/在手牌中时/.test(s)),
    'knowledge.count-block':c=>/每(?:打出|使用)过一种知识牌/.test(c.text||'')&&/获得[^。]*格挡/.test(c.text||''),
    'knowledge.hand-damage':c=>/手牌中的所有知识牌/.test(c.text||'')&&/每放入\d+张[^。]*造成\d+点伤害/.test(c.text||''),
    'knowledge.count-damage':c=>/每使用过一种知识牌/.test(c.text||'')&&/伤害增加/.test(c.text||''),
    'knowledge.owned-damage':c=>/每有(?:一|1)张知识牌[，,]伤害增加/.test(c.text||''),
    'knowledge.owned-block':c=>/每有(?:一|1)张知识牌[，,]格挡增加/.test(c.text||''),
    'knowledge.upgrade-linked':(c,ctx)=>any(c,(s,a)=>/升级(?:时|后)/.test(s)&&ctx.peer(s,c)&&/(?:加入|放入)[^。]*牌组/.test(a)),
    'knowledge.postcombat-upgrade':c=>/战斗结束时/.test(c.text||'')&&/永久升级[^。]*知识牌/.test(c.text||''),
    'knowledge.borrow-linked':c=>/知识牌可以作为其关联牌打出/.test(c.text||''),
    'knowledge.count-cost':c=>/每使用过一种知识牌/.test(c.text||'')&&/耗能减少/.test(c.text||''),
    'knowledge.unique-finish':c=>/使用过\d+种知识牌后才能打出/.test(c.text||'')&&/消灭所有敌人/.test(c.text||''),
    'knowledge.upgrade-energy':c=>/升级所有知识牌/.test(c.text||'')&&/每升级\d+张[^。]*获得\d+点能量/.test(c.text||''),
    'knowledge.upgrade':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/(?:^|[，,])升级|将[^。]*升级/.test(a)&&!/可(?:多次|无限)升级/.test(a)),
    'knowledge.transform':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/变(?:化)?为(?!\d|X)/.test(a)),
    'knowledge.destination':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/(?:放入|放回|放到|洗入)[^。]*(?:手牌|牌堆)/.test(a)),
    'knowledge.retain':(c,ctx)=>any(c,(s,a)=>ctx.target(s,'知识',c)&&/获得保留/.test(a)),
    'energy.high':high,
    'energy.gain':c=>any(c,(s,a)=>gainEnergy(a)),
    'energy.retain':c=>any(c,(s,a)=>/可保留[^。]*能量|保留手牌和能量|能量并可保留/.test(a)),
    'energy.increase':c=>any(c,(s,a)=>/(?:耗能|费用)增加|增加[^。]*(?:耗能|费用)/.test(a)),
    'energy.high-draw':c=>any(c,(s,a)=>highEvent(s)&&/抽(?:取)?[^。]*牌/.test(a)),
    'energy.zero-draw':c=>any(c,(s,a)=>/抽取(?:\d+|一|两|X)张0费牌/.test(a)),
    'energy.high-gain':c=>any(c,(s,a)=>highEvent(s)&&gainEnergy(a)),
    'energy.highest-cost-gain':c=>/手牌中最高耗能/.test(c.text||'')&&any(c,(s,a)=>gainEnergy(a)),
    'energy.hand-cost-gain':c=>/手牌总耗能/.test(c.text||'')&&any(c,(s,a)=>gainEnergy(a)),
    'energy.gain-damage':c=>any(c,(s,a)=>/获得能量(?:时|后)/.test(s)&&/造成\d+点伤害/.test(a)),
    'energy.high-block':c=>any(c,(s,a)=>highEvent(s)&&/获得[^。]*格挡/.test(a)),
    'energy.high-return':c=>any(c,(s,a)=>highEvent(s)&&/(?:放回|放入|加入)[^。]*手牌/.test(a)),
    'energy.remaining':c=>/剩余[^。]*能量/.test(c.text||''),
    'energy.spent':c=>/(?:每消耗|每支付|每花费|实际支付|投入)[^。]*能量/.test(c.text||''),
    'energy.paid-replay':c=>/足够的能量/.test(c.text||'')&&/再打出一次/.test(c.text||''),
    'energy.x-damage':c=>x(c)&&/造成[^。]*伤害/.test(c.text||''),
    'energy.x-block':c=>x(c)&&/获得[^。]*格挡/.test(c.text||''),
    'energy.x-accelerate':c=>x(c)&&/加速|回合数减少X/.test(c.text||''),
    'energy.x-extra':c=>x(c)&&/额外触发/.test(c.text||''),
    'energy.x':x,
    'energy.reduce':c=>any(c,(s,a)=>/(?:耗能|费用)(?:减少|降低|减|[-−])/.test(a)),
    'energy.zero':c=>any(c,(s,a)=>/(?:耗能|费用)(?:变为0|为0)|不消耗能量/.test(a)),
    'energy.exchange':c=>/交换[^。]*(?:耗能|费用)/.test(c.text||''),
    'energy.auto-play':(c,ctx)=>handArrowPlay(c)||any(c,(s,a)=>/免费打出|打出手牌中的/.test(a)&&(ctx.referenced(s,high)||/耗能不低于2|高费/.test(s))),
    'energy.retain-hand':c=>high(c)&&((c.keywords||[]).includes('保留')||/(?:^|。)保留(?:。|$)/.test(c.text||''))||/保留(?:所有|全部)?手牌|保留(?:所有|全部)?(?:耗能|费用)?不低于[2-9](?:费)?的牌/.test(c.text||''),
    'energy.tutor':(c,ctx)=>any(c,(s,a)=>/抽取|(?:加入|放入|放回)[^。]*手牌/.test(a)&&(/高费|耗能不低于2/.test(s)||ctx.referenced(s,high))),
    'bonus.gain':c=>any(c,(s,a)=>bonusGain(a)),
    'bonus.transfer':c=>any(c,(s,a)=>/伤害加成[^。]*(?:转移|赋予)|(?:转移|赋予)[^。]*伤害加成/.test(a)),
    'bonus.multiply':c=>/伤害加成[^。]*(?:\d+|X|N)倍|(?:\d+|X|N)倍[^。]*伤害加成/.test(c.text||''),
    'bonus.hits':c=>any(c,s=>/伤害加成/.test(s)&&extraHits(s)),
    'bonus.aoe':c=>/伤害加成[^。]*所有敌人/.test(c.text||''),
    'bonus.multihit':(c,ctx)=>[...String(c.text||'').matchAll(/造成(?:\d+|X|N)点伤害(\d+|X|N)次/g)].some(match=>match[1]==='X'||match[1]==='N'||Number(match[1])>1)||/造成\d+点伤害/.test(c.text||'')&&(any(c,(s,a)=>extraHits(a)&&!ctx.referenced(a,other=>other.id!==c.id))||/每放入\d+张[^。]*造成\d+点伤害/.test(c.text||'')),
    'bonus.ongoing':c=>c.type==='能力'&&any(c,(s,a)=>/回合(?:开始|结束)|获得能量(?:时|后)/.test(s)&&/造成\d+点伤害/.test(a)),
    'bonus.extra-hits':(c,ctx)=>any(c,(s,a)=>extraHits(a)&&!/伤害加成/.test(s)&&(/选择|使其|卡牌|所有[^。]*牌/.test(a)||ctx.referenced(a,other=>other.id!==c.id))),
    'bonus.rules':c=>any(c,s=>/伤害加成/.test(s)&&/继承|上限|失去|保留|新生成/.test(s))
  };
  function entries(route,data,defaults,profile){
    const ctx=context(data,route,defaults,profile),cards=[...new Map(route.members.map(card=>[card.uid||card.id,card])).values()];
    return ['trigger','payoff','both','tool'].flatMap(role=>(route.effectRoles?.[role]?.effects||[]).map((effect,index)=>{
      const item=typeof effect==='string'?{id:role+'-'+index,text:effect}:effect,matcher=matchers[item.matcher||item.id];
      return {...item,role,known:Boolean(matcher),members:matcher?cards.filter(card=>matcher(card,ctx)):[]};
    }));
  }
  return {entries,handArrowPlay,matcherIds:Object.keys(matchers)};
})();
if(typeof module!=='undefined'&&module.exports)module.exports=JunnaEffectStatistics;
