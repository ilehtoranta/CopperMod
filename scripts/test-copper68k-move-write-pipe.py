"""Audit private pending-write saved pipes with complete retained transfer evidence.

No production CPU import, physical pipeline qualification or architectural
promotion of the known native A7/trace/refault disagreements occurs.
"""
import argparse,contextlib,hashlib,importlib.util,io,itertools,json,os,re,subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
REPO=Path(__file__).resolve().parents[1]
HELPER=REPO/'scripts/test-copper68k-memory-read-write.py'
FIXTURE='Copper68k.Tests/Synthetic/SyntheticM68020MoveWritePipeTests.cs'
spec=importlib.util.spec_from_file_location('read_write',HELPER);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
sha,load,save,require,inventory=m.sha,m.load,m.save,m.require,m.inventory
PARENT_PROOF='d6f501a8605e78626bffc7ce26c04379d6fd2401a2e4fbf4c80508e152db2ecb'
CPU='Copper68k/M68kAdvancedTimingInterpreter.FinalWrite020.cs'
FILTER='FullyQualifiedName~SyntheticM68020MoveWritePipeTests'
VARIANTS=['candidate','discard-pipe','swap-pipe','refault-count','dfclear-pipe']
MODELS=m.MODELS
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def once(text,old,new):
 require(text.count(old)==1,'Missing/ambiguous private pipe mutation target');return text.replace(old,new)
def expected(model,width,lane,count,edit,variant):
 bad=variant=='discard-pipe' and count>0 or variant=='swap-pipe' and count==3 or variant=='refault-count' and lane=='refault' and count>0 or variant=='dfclear-pipe' and lane=='software' and count>0
 status='mismatching' if bad else 'passing'
 return {f'{model}/MOVE/write-pipe/width={width}/lane={lane}/count={count}/edit={edit}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}':{status:1} for bank,source,post,alias,vi,ccr in itertools.product(['user','user-M','ISP','MSP'],[0,7],[False,True],[False,True],range(4),[0,31])}
def sources(parent,variant):
 data={name:(parent/'candidate/source'/name).read_bytes() for name in inventory(parent/'candidate/source')};require(len(data)==235,'Missing complete private pipe parent sources');data[FIXTURE]=(REPO/FIXTURE).read_bytes()
 t=data[CPU].decode('utf-8').replace('\r\n','\n')
 if variant=='discard-pipe':t=once(t,'CompleteTiming(M68kInstructionTimingKey.Rte); _instructionPipe = pipe;','CompleteTiming(M68kInstructionTimingKey.Rte); _instructionPipe.Reset(pc);')
 elif variant=='swap-pipe':
  marker='        // Read the full private state before committing stack, SR or the write.'
  swapped='''        if (count == 3)
        {
            var copy = pipe;
            _ = copy.TryConsumeKnownHead(out var first, out _);
            _ = copy.TryConsumeKnownHead(out var second, out _);
            _ = copy.TryConsumeKnownHead(out var third, out _);
            pipe.Reset(pc);
            pipe.Append(pc, first, default);
            pipe.Append(unchecked(pc + 2), third, default);
            pipe.Append(unchecked(pc + 4), second, default);
        }
'''
  t=once(t,marker,swapped+marker)
 elif variant=='refault-count':t=once(t,'words[11] = (ushort)(1 | (fault.Pipe.Count << 8));','words[11] = 1;')
 elif variant=='dfclear-pipe':t=once(t,'if ((ssw & 0x100) != 0) WriteFinalMoveDestination020(address, value, size);','if ((ssw & 0x100) != 0) WriteFinalMoveDestination020(address, value, size); else _instructionPipe.Reset(pc);')
 if variant!='candidate':data[CPU]=t.encode('utf-8')
 return data
def command(root,variant):
 selection=FILTER+('|' + m.command(root,'candidate')[m.command(root,'candidate').index('--filter')+1] if variant=='candidate' else '')
 return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selection,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]
def verify(root,variant,data,retained,parent_names):
 record=load(root/'execution.json');ids={n:hashlib.sha256(v).hexdigest() for n,v in data.items()}
 require(record['sources']==ids==inventory(root/'source'),'Changed complete private pipe sources')
 require(record['command']==command(root,variant) and record['exit']==(0 if variant=='candidate' else 1),'Wrong private pipe command/exit')
 outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
 require(set(record['outputs'])==set(outputs) and record['outputs']=={p:sha(root/p) for p in outputs},'Changed private pipe outputs')
 tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',NS);names={x:'Passed' if variant=='candidate' else 'Failed' for x in ['ScalarPendingWritePipes','BatchPendingWritePipes']}
 if variant=='candidate':names.update(parent_names)
 require(len(tests)==len(names) and {t.get('testName').split('.')[-1]:t.get('outcome') for t in tests}==names,'Wrong private pipe outcomes/roster')
 counters=tree.find('.//t:Counters',NS);failed=sum(x=='Failed' for x in names.values())
 require(int(counters.get('total'))==len(names) and int(counters.get('failed'))==failed and int(counters.get('passed'))==len(names)-failed,'Wrong private pipe counters')
 stdout='\n'.join(t.text or '' for t in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',NS));totals=dict(passing=0,mismatching=0,unsupported=0,untested=0);rows=[]
 def summary(d):
  matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
  require(len(matches)==1 and json.loads(matches[0][0])==d['counts'] and int(matches[0][1])==d['logicalCases'] and int(matches[0][2])==len(d['combinations']),'Private pipe TRX/report disagreement')
 for model,width,lane,count,mode in itertools.product(MODELS,[1,2,4],['write','software','refault'],range(4),['scalar','batch']):
  for edit in range(-1,count):
   p=root/f'{model}-move-write-pipe-{width}-{lane}-count{count}-edit{edit}-{mode}.json';d=load(p);wanted=expected(model,width,lane,count,edit,variant);counts={s:sum(row.get(s,0) for row in wanted.values()) for s in totals}
   require(d['schema']==1 and d['model']==model and d['group']==f'move-write-pipe-{width}-{lane}-count{count}-edit{edit}-{mode}' and d['xunitBatches']==1 and d['logicalCases']==256 and d['combinations']==wanted and d['counts']==counts,'Wrong private pipe keys, weights or results')
   bad={key for key,row in wanted.items() if 'passing' not in row}
   require(len(d['failures'])==len(bad) and {f['id'] for f in d['failures']}==bad and all(f['status']=='mismatching' and f['reason'] for f in d['failures']),'Wrong complete private pipe failure witnesses')
   if variant=='refault-count':require(all(f['reason']=='Pending-write pipe fault changed completed effects or frame state' for f in d['failures']),'Wrong private pipe refault cause')
   summary(d)
   for s in totals:totals[s]+=counts[s]
   rows.append(dict(path=p.name,sha256=sha(p)))
 if variant=='candidate':
  require(len(retained)==360,'Missing private pipe retention inventory')
  for name,wanted in retained.items():
   p=root/name;d=load(p);require(d==wanted,'Changed private pipe retained report: '+name);summary(d);rows.append(dict(path=name,sha256=sha(p)))
 require({p.name for p in root.glob('*.json')}=={row['path'] for row in rows}|{'execution.json'},'Missing/foreign private pipe reports')
 return dict(variant=variant,counts=totals,retentionCases=1433600 if variant=='candidate' else 0,executions=len(names),reports=rows,executionSha256=sha(root/'execution.json'))
def audit(parent,output,validate):
 require(sha(parent/'proof.json')==PARENT_PROOF,'Wrong qualified private pipe parent')
 with contextlib.redirect_stdout(io.StringIO()):m.audit(parent.parent/'MoveWriteHandlersV2',parent,True)
 linkage=dict(producers={p.relative_to(REPO).as_posix():sha(p) for p in [Path(__file__),HELPER,REPO/FIXTURE]},parentProofSha256=PARENT_PROOF,variants=VARIANTS)
 if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
 require(load(output/'inputs.json')==linkage,'Wrong private pipe producer/parent/selection')
 retained={p.name:load(p) for p in (parent/'candidate').glob('*.json') if p.name.startswith(tuple(x+'-' for x in MODELS))}
 tree=ET.parse(parent/'candidate/audit.trx');parent_names={t.get('testName').split('.')[-1]:t.get('outcome') for t in tree.findall('.//t:UnitTestResult',NS)};require(len(parent_names)==18 and set(parent_names.values())=={'Passed'},'Incomplete private pipe parent roster')
 entries=[]
 for variant in VARIANTS:
  root=output/variant;data=sources(parent,variant)
  if not validate:
   (root/'source').mkdir(parents=True)
   for name,value in data.items():
    p=root/'source'/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(value)
   env=dict(os.environ,COPPER68K_RUN_020_MOVE_WRITE_PIPE='1',COPPER68K_RUN_020_MEMORY_READ_WRITE='1',COPPER68K_RUN_020_MOVE_WRITE_HANDLERS='1',COPPER68K_RUN_020_MOVE_WRITE_DISCOVERY='1',COPPER68K_RUN_020_OPERAND_READ_DISCOVERY='1',COPPER68K_RUN_020_MEMORY_DESTINATION_RECOVERY='1',COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
   with (root/'execution.log').open('w',encoding='utf-8') as log:result=subprocess.run(command(root,variant),env=env,stdout=log,stderr=subprocess.STDOUT)
   outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
   save(root/'execution.json',dict(command=command(root,variant),exit=result.returncode,sources={n:hashlib.sha256(v).hexdigest() for n,v in data.items()},outputs={p:sha(root/p) for p in outputs}))
  entry=verify(root,variant,data,retained,parent_names);entries.append(entry);print(variant,entry['counts'],'retention',entry['retentionCases'],flush=True)
 proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=236,productionImport=False,publication=False,hardwareQualified=False,architecturalPromotion=False,roadmapComplete=False)
 if validate:require(load(output/'proof.json')==proof,'Changed complete private pipe proof')
 else:save(output/'proof.json',proof)
 print('Complete private pending-write pipe audit',sha(output/'proof.json'),flush=True)
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--qualified-parent-directory',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--validate-only',action='store_true');a=p.parse_args();audit(a.qualified_parent_directory.resolve(),a.output.resolve(),a.validate_only)
