"""Audit secondary exception-entry faults of the private C021/C023 continuation.

Requires the complete immutable MoveWriteValidationV1 parent. No private CPU
import, hardware-frame qualification, or package publication is performed.
"""
import argparse, contextlib, hashlib, importlib.util, io, itertools, json, os, re, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
REPO = Path(__file__).resolve().parents[1]
HELPER = REPO/'scripts/test-copper68k-move-write-validation.py'
spec = importlib.util.spec_from_file_location('write_validation', HELPER)
m = importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
sha,load,save,require,inventory=m.sha,m.load,m.save,m.require,m.inventory
FIXTURE='Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteEntryFaultTests.cs'
CPU='Copper68k/M68kAdvancedTimingInterpreter.Rte020.cs'
PARENT_PROOF='3a2d2b3c8f82f4205224819a3ba82ba2d6066958881b9649d6153f88366cbf64'
MODELS=['68EC020','68020','68030','A1200']
VARIANTS=['candidate','no-halt','stack-advance','wrong-saved-sr']
FILTER='FullyQualifiedName~SyntheticM68020MoveWriteEntryFaultTests'
ENV={'COPPER68K_RUN_020_MOVE_WRITE_ENTRY_FAULT':'1',**m.ENV}
NS=m.NS


def once(text,old,new):
    require(text.count(old)==1,'Nonunique entry-fault mutation')
    return text.replace(old,new)


def sources(parent,variant):
    prior=load(parent/'candidate/execution.json')['sources']
    require(len(prior)==239 and FIXTURE not in prior,'Incomplete entry-fault parent')
    data={n:(parent/'candidate/source'/n).read_bytes() for n in prior}
    require(all(hashlib.sha256(v).hexdigest()==prior[n] for n,v in data.items()),'Changed entry-fault parent sources')
    data[FIXTURE]=(REPO/FIXTURE).read_bytes()
    text=data[CPU].decode('utf-8').replace('\r\n','\n')
    old='catch (RteReadFault020) { State.Halted = true; _instructionPipe.Reset(); }'
    if variant=='no-halt':text=once(text,old,old.replace('State.Halted = true','State.Halted = false'))
    elif variant=='stack-advance':text=once(text,old,old.replace('State.Halted = true','State.SetActiveStackPointer(unchecked(State.A[7] + 2)); State.Halted = true'))
    elif variant=='wrong-saved-sr':text=once(text,'words[0] = sr; Long(2, context.InstructionPc);','words[0] = (ushort)(sr ^ 1); Long(2, context.InstructionPc);')
    if variant!='candidate':data[CPU]=text.encode('utf-8')
    return data


def command(root,variant):
    selection=FILTER+('|'+m.FILTER+'|'+m.m.FILTER+'|'+m.m.m.FILTER if variant=='candidate' else '')
    return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selection,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]


def expected(model,kind,bank,variant):
    rows={}
    for location,ccr,request in itertools.product(range(2),range(32) if kind=='ccr' else [31],range(4)):
        for byte,entry in itertools.product(range(4 if request==1 else 2),[0] if kind=='ccr' else range(47)):
            for entry_byte in range(4 if entry==46 else 2):
                bad=variant in ['no-halt','stack-advance'] or variant=='wrong-saved-sr' and entry==46
                key=f'{model}/RTE/write-validation-entry/{kind}/bank={bank}/location={location}/ccr={ccr}/request={request}/byte={byte}/entry={entry}/entry-byte={entry_byte}'
                rows[key]={'mismatching' if bad else 'passing':1}
    require(len(rows)==(1280 if kind=='ccr' else 1920),'Wrong entry-fault inventory')
    return rows


def verify(root,variant,data,retained,parent_names):
    e=load(root/'execution.json');ids={n:hashlib.sha256(v).hexdigest() for n,v in data.items()}
    require(e['sources']==ids==inventory(root/'source'),'Changed complete entry-fault source inventory')
    require(e['command']==command(root,variant) and e['settings']==ENV and e['exit']==(0 if variant=='candidate' else 1),'Wrong entry-fault command, settings or exit')
    outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(e['outputs']=={n:sha(root/n) for n in outputs},'Changed entry-fault outputs')
    tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',NS)
    names={f'{route}WriteValidationEntryFaults':'Passed' if variant=='candidate' else 'Failed' for route in ['Scalar','Batch']}
    if variant=='candidate':names.update(parent_names)
    require(len(tests)==len(names) and {x.get('testName').split('.')[-1]:x.get('outcome') for x in tests}==names,'Wrong or empty entry-fault selection')
    counter=tree.find('.//t:Counters',NS);failed=sum(v=='Failed' for v in names.values())
    require(all(int(counter.get(k,-1))==v for k,v in dict(total=len(names),executed=len(names),passed=len(names)-failed,failed=failed,notExecuted=0).items()),'Wrong entry-fault counters')
    require(tree.find('.//t:ResultSummary',NS).get('outcome')==('Completed' if variant=='candidate' else 'Failed'),'Wrong entry-fault outcome')
    definitions={x.get('id'):x for x in tree.findall('.//t:UnitTest',NS)}
    require(all(Path(definitions[x.get('testId')].get('storage')).resolve()==(root/'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll').resolve() for x in tests),'Wrong loaded entry-fault test assembly')
    stdout='\n'.join(x.text or '' for x in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',NS));reports=[];totals=dict(passing=0,mismatching=0,unsupported=0,untested=0)
    def summary(d):
        matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
        require(len(matches)==1 and json.loads(matches[0][0])==d['counts'] and int(matches[0][1])==d['logicalCases'] and int(matches[0][2])==len(d['combinations']),'Entry-fault report/TRX disagreement')
    for model,kind,bank,route in itertools.product(MODELS,['enumeration','ccr'],['ISP','MSP'],['scalar','batch']):
        group=f'move-write-entry-{kind}-{bank}-{route}';path=root/f'{model}-{group}.json';d=load(path);wanted=expected(model,kind,bank,variant)
        counts={s:sum(v.get(s,0) for v in wanted.values()) for s in totals}
        require(d['schema']==1 and d['model']==model and d['group']==group and d['logicalCases']==len(wanted) and d['xunitBatches']==1 and d['counts']==counts and d['combinations']==wanted,'Wrong entry-fault combinations: '+path.name)
        bad={n for n,v in wanted.items() if 'mismatching' in v}
        require(len(d['failures'])==len(bad) and {x['id'] for x in d['failures']}==bad and all(x['status']=='mismatching' and x['reason'] for x in d['failures']),'Wrong complete entry-fault failure witnesses')
        summary(d);reports.append(dict(path=path.name,sha256=sha(path)))
        for s in totals:totals[s]+=counts[s]
    if variant=='candidate':
        require(len(retained)==1344,'Incomplete entry-fault retention')
        for name,wanted in retained.items():
            d=load(root/name);require(d==wanted,'Changed retained entry-fault-parent report: '+name);summary(d);reports.append(dict(path=name,sha256=sha(root/name)))
    require({x.name for x in root.glob('*.json')}=={x['path'] for x in reports}|{'execution.json'},'Missing or foreign entry-fault reports')
    return dict(variant=variant,counts=totals,retentionCases=599808 if variant=='candidate' else 0,executions=len(names),reports=reports,executionSha256=sha(root/'execution.json'))


def audit(parent,output,validate):
    require(sha(parent/'proof.json')==PARENT_PROOF,'Wrong entry-fault parent proof')
    with contextlib.redirect_stdout(io.StringIO()):m.audit(parent.parent/'MoveWriteFrameFaultV1',parent,True)
    linkage=dict(producers={f.relative_to(REPO).as_posix():sha(f) for f in [Path(__file__),HELPER,REPO/FIXTURE]},parentProofSha256=PARENT_PROOF,variants=VARIANTS,settings=ENV)
    if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
    require(load(output/'inputs.json')==linkage,'Changed entry-fault producers or selection')
    retained={f.name:load(f) for f in (parent/'candidate').glob('*-move-write-*.json')}
    tree=ET.parse(parent/'candidate/audit.trx');parent_names={x.get('testName').split('.')[-1]:x.get('outcome') for x in tree.findall('.//t:UnitTestResult',NS)}
    require(len(parent_names)==8 and set(parent_names.values())=={'Passed'},'Incomplete entry-fault parent roster')
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
    proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=240,productionImport=False,publication=False,hardwareQualified=False,architecturalPromotion=False,roadmapComplete=False)
    if validate:require(load(output/'proof.json')==proof,'Changed complete entry-fault proof')
    else:save(output/'proof.json',proof)
    print('Complete private write-entry audit',sha(output/'proof.json'),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--qualified-parent-directory',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--validate-only',action='store_true')
    args=parser.parse_args();audit(args.qualified_parent_directory.resolve(),args.output.resolve(),args.validate_only)
