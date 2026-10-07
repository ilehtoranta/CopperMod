from pathlib import Path
import hashlib,json,subprocess,itertools,re,xml.etree.ElementTree as ET,sys
b=Path(__file__).parent;root=Path(sys.argv[1]);mode=sys.argv[2];assert mode in ['Clean','AliasOrder','ByteStackStride'];repo=Path(sys.argv[3]);evidenceRoot=Path(sys.argv[4])
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def check(x,msg):
 if not x:raise ValueError(msg)
i=load(root/'inputs.json');base=load(root/'base-inputs.json');check(i['testExit']==(0 if mode=='Clean' else 1),'Wrong execution exit');check(i['helperSha256']==sha(b/'run-copper68k-cmpm-consolidation.ps1'),'Wrong producer');check(i['baseInputsSha256']==sha(root/'base-inputs.json'),'Changed base inputs')
source=root/'source';actual={str(p.relative_to(source)).replace('\\','/'):sha(p) for p in source.rglob('*') if p.suffix in ['.cs','.csproj'] and 'bin' not in p.parts and 'obj' not in p.parts};check(actual=={x['path'].replace('\\','/'):x['sha256'] for x in i['sources']},'Incomplete/changed source inventory');expected={x['path']:x['sha256'] for x in base['sourceIds']};cpu='Copper68k/M68kAdvancedTimingInterpreter.cs';check(set(actual)==set(expected),'Wrong source inventory selection');check([p for p in expected if actual[p]!=expected[p]]==([] if mode=='Clean' else [cpu]),'Wrong mutation source scope')
for p,h in base['normalAssemblies'].items():check(sha(repo/p)==h,'Changed protected assembly')
normalFiles=['Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll'];check(set(base['normalAssemblies'])=={p for p in normalFiles if (repo/p).exists()},'Incomplete protected assembly identities')
currentFiles=[p for p in subprocess.check_output(['git','ls-files','Copper68k','Copper68k.Tests'],cwd=repo,text=True).splitlines() if Path(p).suffix in ['.cs','.csproj']]
helpers=['scripts/test-copper68k-cmpm-consolidation.ps1','scripts/run-copper68k-cmpm-consolidation.ps1','scripts/verify-copper68k-cmpm-consolidation.py','scripts/prove-copper68k-cmpm-consolidation-integrity.py']
check(len(base['repositoryInputs'])==len(set(currentFiles+helpers)) and {x['path'] for x in base['repositoryInputs']}==set(currentFiles+helpers),'Incomplete repository input identities')
for row in base['repositoryInputs']:check(sha(repo/row['path'])==row['sha256'],'Changed repository source/helper')
pin='1e1ab44489b68982a1bbc981c45cddf01fec09c5';legacy='Copper68k.Tests/M68020CmpmTests.cs';check(base['historicalPin']==pin and base['mode']==mode,'Wrong historical/model pin');fixture=subprocess.check_output(['git','show',pin+':'+legacy],cwd=repo).decode('utf-8-sig');check((source/legacy).read_text(encoding='utf-8-sig')==fixture.replace('\r\n','\n'),'Changed historical regression fixture');check(base['historicalSha256']==hashlib.sha256(fixture.encode()).hexdigest(),'Wrong historical fixture identity')
baseline={p:sha(repo/p) for p in currentFiles};baseline[legacy]=hashlib.sha256(fixture.replace('\r\n','\n').encode()).hexdigest();check(expected==baseline,'Wrong baseline source identity')
if mode!='Clean':
 clean=evidenceRoot/'Clean';t=(clean/'source'/cpu).read_text(encoding='utf-8-sig');start=t.index('        private void ExecuteCmpmPostIncrement(');end=t.index('        private void ExecuteWordBranch(',start);block=t[start:end]
 if mode=='AliasOrder':
  old='var source = ReadSized(State.A[sourceRegister], size);';check(block.count(old)==1,'Missing unique alias target');block=block.replace(old,'var capturedDestination = State.A[destinationRegister]; // intentional old-base alias sampling\n            '+old);old='var destination = ReadSized(State.A[destinationRegister], size);';check(block.count(old)==1,'Missing destination target');block=block.replace(old,'var destination = ReadSized(capturedDestination, size);')
 else:
  for reg in ['sourceRegister','destinationRegister']:
   old=f'size == M68kOperandSize.Byte && {reg} == 7 ? 2u : (uint)size';check(block.count(old)==1,'Missing byte stride target');block=block.replace(old,f'size == M68kOperandSize.Byte && {reg} == 7 ? 1u : (uint)size')
 check((source/cpu).read_text(encoding='utf-8-sig')==t[:start]+block+t[end:],'Wrong intentional mutation')
models=['68000','68010','68EC020','68020','68030','68040','68060','A1200'];timing='Copper68k.Tests.M68020CmpmTests.CmpmComparesSelectedWidthPreservesExtendAndDoesNotWriteMemory';alias='Copper68k.Tests.M68020CmpmTests.AliasedAddressRegisterUsesTheNextOperandAfterSourceIncrement';stack='Copper68k.Tests.M68020CmpmTests.ByteStackRegisterUsesTwoByteStrideForBothAliasedOperands';names={f'{timing}(profile: {p}, size: {s}, flags: {f})':'Passed' for p,s,f in itertools.product(range(3),range(3),[20,25,18,27])}
for size in range(3):names[f'{alias}(size: {size})']='Failed' if mode=='AliasOrder' else 'Passed'
names[stack]='Passed' if mode=='Clean' else 'Failed'
for model in models:names[f'Copper68k.Tests.Synthetic.SyntheticExtendDecimalTests.ExtendComparisonAndAliases(modelId: "{model}")']='Failed' if mode!='Clean' and model not in ['68000','68010'] else 'Passed'
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',ns);check(len(tests)==48 and {t.get('testName') for t in tests}==set(names),'Wrong/empty execution selection')
for t in tests:
 check(t.get('outcome')==names[t.get('testName')],'Wrong executed test outcome')
 if t.get('outcome')=='Failed' and (t.get('testName').startswith(alias) or t.get('testName')==stack):
  message=t.find('t:Output/t:ErrorInfo/t:Message',ns).text;exp,act=(16388,16386) if mode=='ByteStackStride' else (16,20);check(re.search(rf'Expected:\s*{exp}\b',message) and re.search(rf'Actual:\s*{act}\b',message),'Wrong historical mutation reason')
stdout='\n'.join(x.text or '' for x in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',ns));rows=[];passing=0;misses=0;anchors=[]
for model in models:
 p=root/f'{model}-arithmetic-extend.json';d=load(p);keys={};failures={}
 for width,family,src,dst,supervisor in itertools.product([1,2,4],['ADDX','SUBX','CMPM'],range(8),range(8),[False,True]):
  for memory in [True] if family=='CMPM' else [False,True]:
   key=f'{model}/{family}/{width}/r{src}-r{dst}/memory={memory}/super={supervisor}';weight=1569 if src==0 and dst==1 and supervisor else 1;failed=model not in ['68000','68010'] and family=='CMPM' and ((mode=='AliasOrder' and src==dst) or (mode=='ByteStackStride' and width==1 and (src==7 or dst==7)))
   keys[key]={'passing':weight} if not failed else {'mismatching':1}
   if failed:
    sr=0x2700 if supervisor else 0x700
    if mode=='AliasOrder':reason=f'SR expected {sr|24:04X}, actual {sr|20:04X}, mask=FFFF'
    elif src==7 and dst==7:reason=f'SR expected {sr|24:04X}, actual {sr|25:04X}, mask=FFFF'
    else:address=0x4700 if supervisor else 0x7800;reason=f'A7 expected {address+2:08X}, actual {address+1:08X}'
    op=(0xb108|(dst<<9)|({1:0,2:1,4:2}[width]<<6)|src);mask=(1<<(width*8))-1;failures[f'{key}/op={op:04X}/s=00000001/d={mask:08X}/ccr=14']=reason
 m=len(failures);counts=dict(passing=25440-m,mismatching=m,unsupported=0,untested=0);check(d['schema']==1 and d['model']==model and d['group']=='arithmetic-extend' and d['logicalCases']==25440 and d['xunitBatches']==1,'Wrong report identity/weights');check(d['counts']==counts,'Wrong status counts');check(d['combinations']==keys,'Missing/extra keys or weights');check(len(d['failures'])==m,'Incomplete failure selection')
 for f in d['failures']:check(f['status']=='mismatching' and f['id'] in failures and f['reason']==failures.pop(f['id']),'Unrelated failure identifier/reason')
 check(not failures,'Missing precise failures');matches=re.findall(re.escape(f'{model}/arithmetic-extend: ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout);check(len(matches)==1 and json.loads(matches[0][0])==counts and int(matches[0][1])==25440 and int(matches[0][2])==1920,'Actual TRX/report disagreement');rows.append(dict(path=p.name,sha256=sha(p),passing=25440-m,mismatching=m));passing+=25440-m;misses+=m
 if model=='A1200' and mode!='Clean':
  anchors=[f'{model}/CMPM/{width}/r{reg}-r{reg}/memory=True/super=True/op={0xb108|(reg<<9)|({1:0,2:1,4:2}[width]<<6)|reg:04X}/s=00000001/d={(1<<(width*8))-1:08X}/ccr=14' for reg,width in ([(0,1),(0,2),(0,4),(7,1)] if mode=='AliasOrder' else [(7,1)])]
  check(all(any(f['id']==a for f in d['failures']) for a in anchors),'Missing direct replacement witnesses')
check(passing+misses==203520 and misses=={'Clean':0,'AliasOrder':288,'ByteStackStride':180}[mode],'Wrong complete consolidation counts')
v=dict(schema=1,mode=mode,passing=passing,mismatching=misses,unsupported=0,untested=0,executions=48,retainedTimingExecutions=36,reports=rows,sources=len(actual),historicalPin=pin,historicalFixtureSha256=base['historicalSha256'],replacementAnchors=anchors,inputsSha256=sha(root/'inputs.json'),trxSha256=sha(root/'audit.trx'),helperSha256=sha(Path(__file__)),productionCpuChanged=False,retirementApplied=False,roadmapComplete=False);(root/'verification.json').write_text(json.dumps(v,indent=2));print(f'{mode}: {passing} passing / {misses} precisely identified synthetic failures; original witnesses and 36 timing cases verified')
