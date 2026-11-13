"""Import the selected installed game into the immutable workbench reference."""
import argparse, hashlib, importlib, json, re, sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-dir', type=Path, required=True)
    parser.add_argument('--decompiled', type=Path, required=True)
    parser.add_argument('--runtime-export',type=Path,required=True)
    parser.add_argument('--parser-dir', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).with_name('native-game.json'))
    args = parser.parse_args()
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'telemetry'))
    from pck_reader import pck_index, pck_read
    sys.path.insert(0, str(args.parser_dir))
    import card_parser, relic_parser, event_parser
    from description_resolver import resolve_description
    release = json.loads((args.game_dir / 'release_info.json').read_text(encoding='utf-8-sig'))
    rules = json.loads(Path(__file__).with_name('native-rules.json').read_text(encoding='utf-8'))
    if release['version'] != rules['gameVersion']:
        raise ValueError('Review native-rules.json against this game version first')
    pck = args.game_dir / 'SlayTheSpire2.pck'
    index = pck_index(pck)
    loc = {name: json.loads(pck_read(pck, index['localization/zhs/' + name + '.json']).decode('utf-8-sig')) for name in ['cards','relics','ancients','epochs','characters','enchantments','card_keywords']}
    runtime=json.loads(args.runtime_export.read_text(encoding='utf-8-sig'))
    assembly_hash=hashlib.sha256((args.game_dir/'data_sts2_windows_x86_64/sts2.dll').read_bytes()).hexdigest()
    if runtime.get('gameVersion')!=release['version'] or runtime.get('locale')!='zhs' or runtime.get('assemblySHA256')!=assembly_hash or runtime.get('errors'):
        raise ValueError('Native runtime export does not match this game version, locale or assembly, or contains errors')
    native_cards={entry['id']:entry for entry in runtime['cards']}
    native_relics={entry['id']:entry for entry in runtime['relics']}
    source = args.decompiled
    card_parser.CARDS_DIR = source / 'MegaCrit.Sts2.Core.Models.Cards'
    card_parser.POOLS_DIR = source / 'MegaCrit.Sts2.Core.Models.CardPools'
    relic_parser.RELICS_DIR = source / 'MegaCrit.Sts2.Core.Models.Relics'
    relic_parser.RELIC_POOLS_DIR = source / 'MegaCrit.Sts2.Core.Models.RelicPools'
    def plain(text):
        text=re.sub(r'\[(?!/?img\])[^\]]+\]','',str(text or ''))
        def icon(match):
            number=match[1]
            count=len(re.findall(r'\[img\]',match[2]))
            kind='能量' if 'energy_icon' in match[2] else '星星'
            return (number if number is not None else str(count))+'点'+kind
        text=re.sub(r'(\d+|X)?((?:\[img\][^\[]*(?:energy_icon|star_icon)\.png\[/img\])+)',icon,text)
        return re.sub(r'\[/?[^\]]+\]','',text).strip()
    def identifier(name):
        return card_parser.class_name_to_id(name)
    pools = {}
    for file in sorted(card_parser.POOLS_DIR.glob('*.cs')):
        if file.stem.startswith(('Mock','Deprecated')): continue
        for name in re.findall(r'ModelDb\.Card<(\w+)>',file.read_text(encoding='utf-8')):
            pools[name] = file.stem.removesuffix('CardPool').lower()
    owners = {'ironclad':'铁甲战士','silent':'静默猎手','defect':'故障机器人','necrobinder':'亡灵契约师','regent':'储君','fool':'愚人','colorless':'无色','curse':'诅咒','status':'状态','event':'事件','token':'衍生','quest':'任务','deprived':'特殊'}
    types={'Attack':'攻击','Skill':'技能','Power':'能力','Status':'状态','Curse':'诅咒','Quest':'任务'}
    rarities={'Basic':'基础','Common':'普通','Uncommon':'罕见','Rare':'稀有','Ancient':'先古','Event':'事件','Token':'衍生','Status':'状态','Curse':'诅咒','Quest':'任务','Starter':'初始','Shop':'商店','Special':'特殊','Unknown':'特殊'}
    cards=[]
    source_files=[]
    for file in sorted(card_parser.CARDS_DIR.glob('*.cs')):
        if file.stem.startswith(('Mock','Deprecated')): continue
        card=card_parser.parse_single_card(file,loc['cards'],pools)
        if not card: continue
        source_files.append(file)
        name=loc['cards'].get(card['id']+'.title',loc['cards'].get(card['id']+'.title.dingy',card['name']))
        values=dict(card.get('vars') or {})
        upgrades=card.get('upgrade') or {}
        for key,change in upgrades.items():
            if key=='cost' or not isinstance(change,str) or not re.fullmatch(r'[+-]\d+',change):continue
            for var in values:
                if var.lower().removesuffix('power')==key.removesuffix('power') and isinstance(values[var],(int,float)):
                    values[var]+=int(change)
        text=plain(card['description'])
        upgraded=plain(resolve_description(card['description_raw'],values,is_upgraded=True))
        keyword_names={'Exhaust':'消耗','Innate':'固有','Ethereal':'虚无','Retain':'保留','Unplayable':'不能被打出','Sly':'机巧','Eternal':'永恒'}
        keyword_names={k:plain(loc['card_keywords'].get(k.upper()+'.title',v))for k,v in keyword_names.items()}
        keywords=[keyword_names.get(k,k)for k in card.get('keywords') or []]
        upgraded_keywords=keywords.copy()
        for k,label in keyword_names.items():
            if upgrades.get('remove_'+k.lower()) and label in upgraded_keywords:upgraded_keywords.remove(label)
            if upgrades.get('add_'+k.lower()) and label not in upgraded_keywords:upgraded_keywords.append(label)
        # Keep native keyword metadata separately instead of rewriting localized card text.
        owner=card['color']
        cards.append({'uid':'native-card-'+card['id'],'id':card['id'],'name':name,'type':types.get(card['type'],card['type']),'rarity':rarities.get(card['rarity'],card['rarity']),'owner':owners.get(owner,owner),'ownerId':owner,'cost':'X' if card['is_x_cost'] else card['cost'],'starCost':'X' if card['is_x_star_cost'] else card['star_cost'],'upgradedCost':'X' if card['is_x_cost'] else upgrades.get('cost',card['cost']),'text':text,'upgradedText':upgraded,'keywords':keywords,'upgradedKeywords':upgraded_keywords,'relatedCardIds':card.get('spawns_cards') or [],'sourceFile':'Models/Cards/'+file.name,'descriptionTemplate':card['description_raw']})
    # Text, costs, titles, keywords and upgrades come from native model formatting.
    for card in cards:
        actual=native_cards[card['id']]
        for field in ['name','cost','upgradedCost','starCost','upgradedStarCost']:
            card[field]=actual[field]
        for field in ['text','upgradedText']:card[field]=plain(actual[field])
        card['keywords']=[plain(loc['card_keywords'][key.upper()+'.title']) for key in actual['keywords']]
        card['upgradedKeywords']=[plain(loc['card_keywords'][key.upper()+'.title']) for key in actual['upgradedKeywords']]
    relic_pools=relic_parser.parse_relic_pools()
    for file in relic_parser.RELIC_POOLS_DIR.glob('*.cs'):
        if file.stem.startswith(('Mock','Deprecated')):continue
        for name in re.findall(r'ModelDb\.Relic<(\w+)>',file.read_text(encoding='utf-8')):relic_pools[name]=file.stem.removesuffix('RelicPool').lower()
    relics=[]
    for file in sorted(relic_parser.RELICS_DIR.glob('*.cs')):
        relic=relic_parser.parse_single_relic(file,loc['relics'],relic_pools)
        if not relic or not relic['description']:continue
        source_files.append(file)
        relics.append({'uid':'native-relic-'+relic['id'],'id':relic['id'],'name':relic['name'],'rarity':rarities.get(relic['rarity'],relic['rarity']),'owner':owners.get(relic['pool'],'通用'),'ownerId':relic['pool'],'text':plain(relic['description']),'flavor':plain(relic['flavor']),'sourceFile':'Models/Relics/'+file.name})
    for relic in relics:
        actual=native_relics[relic['id']]
        relic['name']=actual['name'];relic['text']=plain(actual['text'])
    relic_ids={r['id']for r in relics}
    ancients=[]
    for file in sorted((source/'MegaCrit.Sts2.Core.Models.Events').glob('*.cs')):
        content=file.read_text(encoding='utf-8')
        if ': AncientEventModel' not in content or file.stem.startswith('Deprecated'):continue
        key=identifier(file.stem)
        rule=rules['ancients'][key]
        source_files.append(file)
        relic_list=event_parser.extract_ancient_relics(content)
        if any(id not in relic_ids for id in relic_list):raise ValueError('Unresolved Ancient relic: '+key)
        # Preserve pool membership directly from the reviewed source properties.
        groups=[]
        for match in re.finditer(r'private (?:List|IEnumerable)<EventOption> ((?:Option)?Pool\d|PositiveOptions|CurseOptions|BaseOptionPool|OptionPool)\s*(?:=>[^;]*;|\{[\s\S]*?\n\t\})',content):
            ids=list(dict.fromkeys(identifier(n)for n in re.findall(r'RelicOption<(\w+)>|ModelDb\.Relic<(\w+)>',match[0]) for n in n if n))
            if ids:groups.append({'name':match[1],'relicIds':ids})
        conditions=rule.get('relicConditions',{})
        # Use the native enchantment names in the player-facing conditions.
        substitutions={'Swift':loc['enchantments'].get('SWIFT.title','Swift'),'Instinct':loc['enchantments'].get('INSTINCT.title','Instinct'),'Goopy':loc['enchantments'].get('GOOPY.title','Goopy')}
        def wording(s):
            s=re.sub(r'\{([A-Z_]+)\}',lambda m:loc['relics'][m[1]+'.title'],s)
            s=s.replace('“黏液”','“'+plain(substitutions['Goopy'])+'”')
            for a,b in substitutions.items():s=s.replace(a,plain(b))
            return s
        ancients.append({'uid':'native-ancient-'+key,'id':key,'name':plain(loc['ancients'].get(key+'.title',file.stem)),'epithet':plain(loc['ancients'].get(key+'.epithet','')),'acts':rule['acts'],'unlock':wording(rule['unlock']),'selectionRules':[wording(s)for s in rule['selectionRules']],'relicIds':relic_list,'relicConditions':{k:wording(v)for k,v in conditions.items()},'optionPools':rule['optionPools'],'sourceFile':'Models/Events/'+file.name})
    ancients.sort(key=lambda ancient:(min(ancient['acts']),ancient['id']))
    for ancient in ancients:
        for relic in relics:
            if relic['id'] in ancient['relicIds']:
                relic.setdefault('ancientIds',[]).append(ancient['id'])
                if relic['id']in ancient['relicConditions']:relic.setdefault('conditions',[]).append(ancient['relicConditions'][relic['id']])
    facts=['MegaCrit.Sts2.Core.Models/ModelDb.cs','MegaCrit.Sts2.Core.Models/ActModel.cs','MegaCrit.Sts2.Core.Models/AncientEventModel.cs','MegaCrit.Sts2.Core.Runs/RunManager.cs','MegaCrit.Sts2.Core.Unlocks/UnlockState.cs']
    source_files.extend(source/f for f in facts)
    source_files.extend((source/'MegaCrit.Sts2.Core.Models.Acts').glob('*.cs'))
    sources={str(f.relative_to(source)).replace('MegaCrit.Sts2.Core.',''):hashlib.sha256(f.read_bytes()).hexdigest()for f in source_files}
    bundle={'schemaVersion':1,'workspaceId':'sts2-native-reference-v1','revision':113000,'data':{'title':'游戏本体','cards':cards,'meta':{'relics':relics,'nativeSource':{'gameVersion':release['version'],'commit':release['commit'],'locale':'zhs','assemblySHA256':assembly_hash,'textSource':'Native CardModel and RelicModel formatting','runtimeExportSHA256':hashlib.sha256(args.runtime_export.read_bytes()).hexdigest(),'sourceSHA256':hashlib.sha256(json.dumps(sources,sort_keys=True).encode()).hexdigest()},'ancientRules':rules['commonRules']},'ancients':ancients},'assets':{},'history':[],'undoStack':[],'redoStack':[]}
    args.output.write_text(json.dumps(bundle,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    args.output.with_name('native-provenance.json').write_text(json.dumps({'version':release['version'],'locale':'zhs','files':sources},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'cards':len(cards),'relics':len(relics),'ancients':len(ancients),'version':release['version']},ensure_ascii=False))

if __name__=='__main__':main()