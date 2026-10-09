"""Qualify supplied validation input and explicit refault of private C023 writes.

Requires the complete immutable MoveWriteFrameFaultV1 parent. No private CPU
import, silicon-frame qualification, or package publication is performed.
"""
import argparse, contextlib, hashlib, importlib.util, io, itertools, json, os, re, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
REPO = Path(__file__).resolve().parents[1]
HELPER = REPO/'scripts/test-copper68k-move-write-frame-fault.py'
spec = importlib.util.spec_from_file_location('write_fault', HELPER)
m = importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
sha,load,save,require,inventory=m.sha,m.load,m.save,m.require,m.inventory
FIXTURE='Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteValidationTests.cs'
CPU='Copper68k/M68kAdvancedTimingInterpreter.Rte020.cs'
PARENT_PROOF='93ec2beed12410b94ce6cf448e1b5bc67e010474edf5ed7b20b2b7a765ae123e'
VARIANTS=['candidate','ignored-input','replay-phase','fault-pc']
FILTER='FullyQualifiedName~SyntheticM68020MoveWriteValidationTests'
ENV={'COPPER68K_RUN_020_MOVE_WRITE_VALIDATION':'1','COPPER68K_RUN_020_MOVE_WRITE_FRAME_FAULT':'1','COPPER68K_RUN_020_MOVE_WRITE_FRAME':'1'}
NS=m.NS


def sources(parent,variant):
    prior=load(parent/'candidate/execution.json')['sources']
    require(len(prior)==238 and FIXTURE not in prior,'Incomplete validation parent')
    data={n:(parent/'candidate/source'/n).read_bytes() for n in prior}
    require(all(hashlib.sha256(v).hexdigest()==prior[n] for n,v in data.items()),'Changed validation parent sources')
    data[FIXTURE]=(REPO/FIXTURE).read_bytes()
    text=data[CPU].decode('utf-8').replace('\r\n','\n')
    old='ExecuteRte020(context, (ssw & 0x100) == 0 ? input : null);'
    if variant=='ignored-input':text=m.m.p.once(text,old,'ExecuteRte020(context, null);')
    elif variant=='replay-phase':text=m.m.p.once(text,old,'if ((ssw & 0x100) != 0) context.Phase = 0; '+old)
    elif variant=='fault-pc':text=m.m.p.once(text,'words[0] = sr; Long(2, context.InstructionPc);','words[0] = sr; Long(2, unchecked(context.InstructionPc + 2));')
    if variant!='candidate':data[CPU]=text.encode('utf-8')
    return data


def command(root,variant):
    selection=FILTER+('|'+m.FILTER+'|'+m.m.FILTER if variant=='candidate' else '')
    return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selection,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]


def expected(model,width,current,count,variant):
    rows={}
    for returned,location,ccr,software,mode,request in itertools.product(['user','user-M','ISP','MSP'],range(2),[0,31],[False,True],['buffer','refault'],range(4)):
        bad=variant=='fault-pc' or variant=='ignored-input' and mode=='buffer' or variant=='replay-phase' and mode=='refault' and request>0
        for byte in range(4 if request==1 else 2):
            key=f'{model}/MOVE/write-frame-fault/width={width}/current={current}/returned={returned}/location={location}/count={count}/ccr={ccr}/software={software}/mode={mode}/request={request}/byte={byte}'
            rows[key]={'mismatching' if bad else 'passing':1}
    require(len(rows)==640,'Wrong validation inventory')
    return rows


def verify(root,variant,data,retained,parent_names):
    e=load(root/'execution.json');ids={n:hashlib.sha256(v).hexdigest() for n,v in data.items()}
    require(e['sources']==ids==inventory(root/'source'),'Changed complete validation source inventory')
    require(e['command']==command(root,variant) and e['settings']==ENV and e['exit']==(0 if variant=='candidate' else 1),'Wrong validation command, settings or exit')
    outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(e['outputs']=={n:sha(root/n) for n in outputs},'Changed validation outputs')
    tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',NS)
    names={f'{route}WriteValidationContinuation':'Passed' if variant=='candidate' else 'Failed' for route in ['Scalar','Batch']}
    if variant=='candidate':names.update(parent_names)
    require(len(tests)==len(names) and {x.get('testName').split('.')[-1]:x.get('outcome') for x in tests}==names,'Wrong or empty validation selection')
    counter=tree.find('.//t:Counters',NS);failed=sum(v=='Failed' for v in names.values())
    require(all(int(counter.get(k,-1))==v for k,v in dict(total=len(names),executed=len(names),passed=len(names)-failed,failed=failed,notExecuted=0).items()),'Wrong validation counters')
    require(tree.find('.//t:ResultSummary',NS).get('outcome')==('Completed' if variant=='candidate' else 'Failed'),'Wrong validation outcome')
    definitions={x.get('id'):x for x in tree.findall('.//t:UnitTest',NS)}
    require(all(Path(definitions[x.get('testId')].get('storage')).resolve()==(root/'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll').resolve() for x in tests),'Wrong loaded validation test assembly')
    stdout='\n'.join(x.text or '' for x in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',NS));reports=[];totals=dict(passing=0,mismatching=0,unsupported=0,untested=0)
    def summary(d):
        matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
        require(len(matches)==1 and json.loads(matches[0][0])==d['counts'] and int(matches[0][1])==d['logicalCases'] and int(matches[0][2])==len(d['combinations']),'Validation report/TRX disagreement')
    for model,width,current,count,route in itertools.product(m.m.MODELS,[1,2,4],['ISP','MSP'],range(4),['scalar','batch']):
        group=f'move-write-validation-{width}-{current}-count{count}-{route}';path=root/f'{model}-{group}.json';d=load(path);wanted=expected(model,width,current,count,variant)
        counts={s:sum(v.get(s,0) for v in wanted.values()) for s in totals}
        require(d['schema']==1 and d['model']==model and d['group']==group and d['logicalCases']==640 and d['xunitBatches']==1 and d['counts']==counts and d['combinations']==wanted,'Wrong validation combinations: '+path.name)
        bad={n for n,v in wanted.items() if 'mismatching' in v}
        require(len(d['failures'])==len(bad) and {x['id'] for x in d['failures']}==bad and all(x['status']=='mismatching' and x['reason'] for x in d['failures']),'Wrong complete validation failure witnesses')
        summary(d);reports.append(dict(path=path.name,sha256=sha(path)))
        for s in totals:totals[s]+=counts[s]
    if variant=='candidate':
        require(len(retained)==1152,'Incomplete validation retention')
        for name,wanted in retained.items():
            d=load(root/name);require(d==wanted,'Changed retained validation-parent report: '+name);summary(d);reports.append(dict(path=name,sha256=sha(root/name)))
    require({x.name for x in root.glob('*.json')}=={x['path'] for x in reports}|{'execution.json'},'Missing or foreign validation reports')
    return dict(variant=variant,counts=totals,retentionCases=476928 if variant=='candidate' else 0,executions=len(names),reports=reports,executionSha256=sha(root/'execution.json'))


def audit(parent,output,validate):
    require(sha(parent/'proof.json')==PARENT_PROOF,'Wrong validation parent proof')
    with contextlib.redirect_stdout(io.StringIO()):m.audit(parent.parent/'MoveWriteFrameV1',parent,True)
    linkage=dict(producers={f.relative_to(REPO).as_posix():sha(f) for f in [Path(__file__),HELPER,REPO/FIXTURE]},parentProofSha256=PARENT_PROOF,variants=VARIANTS,settings=ENV)
    if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
    require(load(output/'inputs.json')==linkage,'Changed validation producers or selection')
    retained={f.name:load(f) for f in (parent/'candidate').glob('*-move-write-*.json')}
    tree=ET.parse(parent/'candidate/audit.trx');parent_names={x.get('testName').split('.')[-1]:x.get('outcome') for x in tree.findall('.//t:UnitTestResult',NS)}
    require(len(parent_names)==6 and set(parent_names.values())=={'Passed'},'Incomplete validation parent roster')
    entries=[]
    for variant in VARIANTS:
        root=output/variant;data=sources(parent,variant)
        if not validate:
            (root/'source').mkdir(parents=True)
            for n,v in data.items():
                f=root/'source'/n;f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(v)
            env={k:v for k,v in os.environ.items() if not k.startswith('COPPER68K_RUN_')};env.update(ENV);env['COPPER68K_SYNTHETIC_REPORT_DIR']=str(root)
            with (root/'execution.log').open('w',encoding='utf-8') as log:done=subprocess.run(command(root,variant),env=env,stdout=log,stderr=subprocess.STDOUT)
            outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
            save(root/'execution.json',dict(command=command(root,variant),settings=ENV,exit=done.returncode,sources={n:hashlib.sha256(v).hexdigest() for n,v in data.items()},outputs={n:sha(root/n) for n in outputs}))
        entry=verify(root,variant,data,retained,parent_names);entries.append(entry);print(variant,entry['counts'],'retention',entry['retentionCases'],flush=True)
    proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=239,productionImport=False,publication=False,hardwareQualified=False,architecturalPromotion=False,roadmapComplete=False)
    if validate:require(load(output/'proof.json')==proof,'Changed complete validation proof')
    else:save(output/'proof.json',proof)
    print('Complete private write-validation audit',sha(output/'proof.json'),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--qualified-parent-directory',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--validate-only',action='store_true')
    args=parser.parse_args();audit(args.qualified_parent_directory.resolve(),args.output.resolve(),args.validate_only)
