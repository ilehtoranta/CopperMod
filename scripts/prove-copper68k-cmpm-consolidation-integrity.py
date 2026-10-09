from pathlib import Path
import json,hashlib,shutil,subprocess,sys,xml.etree.ElementTree as ET
root=Path(sys.argv[1]);repo=Path(sys.argv[2]);mode=sys.argv[3];assert mode in ['create','verify'];scripts=Path(__file__).parent
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def save(p,obj):p.write_text(json.dumps(obj,indent=2),encoding='utf-8')
definitions=[('OmittedFixture','Clean','Incomplete/changed source inventory'),('WrongProducer','Clean','Wrong producer'),('EmptySelection','Clean','Wrong/empty execution selection'),('WrongWeight','Clean','Missing/extra keys or weights'),('WrongHistoricalPin','Clean','Wrong historical/model pin'),('ChangedHistoricalFixture','Clean','Changed historical regression fixture'),('WrongFailureReason','AliasOrder','Unrelated failure identifier/reason'),('WrongBaselineCpu','Clean','Wrong baseline source identity')]
if mode=='create':
 controls=root/'integrity';assert not controls.exists();rows=[]
 for name,selection,expected in definitions:
  parent=root/selection;r=controls/name;r.mkdir(parents=True);shutil.copytree(parent/'source',r/'source',ignore=shutil.ignore_patterns('bin','obj'))
  for p in parent.iterdir():
   if p.name in ['inputs.json','base-inputs.json','audit.trx'] or p.name.endswith('-arithmetic-extend.json'):shutil.copy2(p,r/p.name)
  if name=='OmittedFixture':(r/'source/Copper68k.Tests/Synthetic/SyntheticExtendDecimalTests.cs').unlink()
  if name=='WrongProducer':
   p=r/'inputs.json';d=load(p);d['helperSha256']='0'*64;save(p,d)
  if name=='EmptySelection':
   p=r/'audit.trx';tree=ET.parse(p);ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};results=tree.find('.//t:Results',ns)
   for child in list(results):results.remove(child)
   tree.write(p,encoding='utf-8',xml_declaration=True)
  if name=='WrongWeight':
   p=r/'68020-arithmetic-extend.json';d=load(p);d['combinations'][next(iter(d['combinations']))]['passing']+=1;save(p,d)
  if name in ['WrongHistoricalPin','ChangedHistoricalFixture']:
   p=r/'base-inputs.json';d=load(p)
   if name=='WrongHistoricalPin':d['historicalPin']='0'*40
   else:
    fixture=r/'source/Copper68k.Tests/M68020CmpmTests.cs';fixture.write_text(fixture.read_text(encoding='utf-8-sig')+'\n// altered historical witness\n',encoding='utf-8');h=sha(fixture)
    for item in d['sourceIds']:
     if item['path'].replace('\\','/')=='Copper68k.Tests/M68020CmpmTests.cs':item['sha256']=h
    inputs=load(r/'inputs.json')
    for item in inputs['sources']:
     if item['path'].replace('\\','/')=='Copper68k.Tests/M68020CmpmTests.cs':item['sha256']=h
    save(r/'inputs.json',inputs)
   save(p,d);inputs=load(r/'inputs.json');inputs['baseInputsSha256']=sha(p);save(r/'inputs.json',inputs)
  if name=='WrongFailureReason':
   p=r/'A1200-arithmetic-extend.json';d=load(p);d['failures'][0]['reason']='unrelated failure';save(p,d)
  if name=='WrongBaselineCpu':
   cpu=r/'source/Copper68k/M68kAdvancedTimingInterpreter.cs';cpu.write_text(cpu.read_text(encoding='utf-8-sig')+'\n// unrelated copied CPU source\n',encoding='utf-8');h=sha(cpu);base=load(r/'base-inputs.json');inputs=load(r/'inputs.json')
   for collection in [base['sourceIds'],inputs['sources']]:
    for item in collection:
     if item['path'].replace('\\','/')=='Copper68k/M68kAdvancedTimingInterpreter.cs':item['sha256']=h
   save(r/'base-inputs.json',base);inputs['baseInputsSha256']=sha(r/'base-inputs.json');save(r/'inputs.json',inputs)
  result=subprocess.run([sys.executable,str(scripts/'verify-copper68k-cmpm-consolidation.py'),str(r),selection,str(repo),str(root)],capture_output=True,text=True)
  (r/'rejection.log').write_text(result.stdout+result.stderr,encoding='utf-8')
  if result.returncode==0 or expected not in result.stderr:raise ValueError('Wrong integrity rejection: '+name+' '+result.stderr)
  rows.append(dict(name=name,mode=selection,exit=result.returncode,reason=expected,rejectionSha256=sha(r/'rejection.log')));print(name+': rejected precisely',flush=True)
 save(root/'integrity.json',dict(schema=1,controls=rows,helperSha256=sha(Path(__file__)),cleanVerificationSha256=sha(root/'Clean/verification.json')))
integrity=load(root/'integrity.json');assert integrity['helperSha256']==sha(Path(__file__)) and integrity['cleanVerificationSha256']==sha(root/'Clean/verification.json');assert len(integrity['controls'])==len(definitions)
for row,(name,selection,expected) in zip(integrity['controls'],definitions):assert row['name']==name and row['mode']==selection and row['exit']!=0 and row['reason']==expected and row['rejectionSha256']==sha(root/'integrity'/name/'rejection.log')
entries=[]
for selection,expected in [('Clean',(203520,0)),('AliasOrder',(203232,288)),('ByteStackStride',(203340,180))]:
 r=root/selection;v=load(r/'verification.json');i=load(r/'inputs.json');base=load(r/'base-inputs.json');assert (v['passing'],v['mismatching'])==expected and v['executions']==48 and v['retainedTimingExecutions']==36 and len(v['reports'])==8
 assert v['inputsSha256']==sha(r/'inputs.json') and v['trxSha256']==sha(r/'audit.trx') and v['helperSha256']==sha(scripts/'verify-copper68k-cmpm-consolidation.py') and i['helperSha256']==sha(scripts/'run-copper68k-cmpm-consolidation.ps1') and i['baseInputsSha256']==sha(r/'base-inputs.json')
 for row in v['reports']:assert row['sha256']==sha(r/row['path'])
 for file,h in base['normalAssemblies'].items():assert sha(repo/file)==h
 for row in base['repositoryInputs']:assert sha(repo/row['path'])==row['sha256']
 entries.append(dict(mode=selection,passing=expected[0],mismatching=expected[1],verificationSha256=sha(r/'verification.json'),inputsSha256=sha(r/'inputs.json'),trxSha256=sha(r/'audit.trx'),cpuSha256=sha(r/'build/bin/Copper68k/release/Copper68k.dll'),testSha256=sha(r/'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'),replacementAnchors=v['replacementAnchors']))
proof=dict(schema=1,scope='CMPM alias ordering and A7 byte stride semantic replacement; entire shared arithmetic-extend matrix across eight profiles plus pinned original witnesses. Existing 36 timing/width/flags/memory cases are co-executed and retained.',historicalPin='1e1ab44489b68982a1bbc981c45cddf01fec09c5',entries=entries,passingCleanScenarios=203520,aliasMutationMismatches=288,stackMutationMismatches=180,originalAliasWitnessFailures=4,originalStackWitnessFailures=1,retainedTimingExecutions=36,integrityControls=8,integritySha256=sha(root/'integrity.json'),historicalMethods=['AliasedAddressRegisterUsesTheNextOperandAfterSourceIncrement','ByteStackRegisterUsesTwoByteStrideForBothAliasedOperands'],originalAndReplacementDetected=True,mutantFixturesUnchanged=True,productionCpuChanged=False,publication=False,roadmapComplete=False,helperSha256=sha(Path(__file__)))
save(root/'proof.json',proof);print('CMPM consolidation proof SHA-256: '+sha(root/'proof.json'))
