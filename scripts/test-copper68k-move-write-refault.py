"""Audit returned-bank refaults of cold private C023 pending stores.

Requires the complete immutable MoveWriteChangedInputV2 parent. No private CPU
import, hardware-frame qualification, or package publication is performed.
"""
import argparse, contextlib, hashlib, importlib.util, io, itertools, json, os, re, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
REPO = Path(__file__).resolve().parents[1]
HELPER = REPO/'scripts/test-copper68k-move-write-changed-input.py'
spec = importlib.util.spec_from_file_location('write_validation', HELPER)
m = importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
sha,load,save,require,inventory=m.sha,m.load,m.save,m.require,m.inventory
FIXTURE='Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteRefaultTests.cs'
CPU='Copper68k/M68kAdvancedTimingInterpreter.FinalWrite020.cs'
PARENT_PROOF='4f6e928d90ad460567c23817ee3680e87579d65ed6b725b17904cc7a41c62ceb'
MODELS=['68EC020','68020','68030','A1200']
VARIANTS=['candidate','recomputed-flags','lost-fc','wrong-destination','lost-pipe']
FILTER='FullyQualifiedName~SyntheticM68020MoveWriteRefaultTests'
ENV={'COPPER68K_RUN_020_MOVE_WRITE_REFAULT':'1',**m.ENV}
NS=m.NS


def once(text,old,new):
    require(text.count(old)==1,'Nonunique refault mutation')
    return text.replace(old,new)


def sources(parent,variant):
    prior=load(parent/'candidate/execution.json')['sources']
    require(len(prior)==241 and FIXTURE not in prior,'Incomplete refault parent')
    data={n:(parent/'candidate/source'/n).read_bytes() for n in prior}
    require(all(hashlib.sha256(v).hexdigest()==prior[n] for n,v in data.items()),'Changed refault parent sources')
    data[FIXTURE]=(REPO/FIXTURE).read_bytes()
    text=data[CPU].decode('utf-8').replace('\r\n','\n')
    if variant=='recomputed-flags':text=once(text,'if (!_resumingFinalMoveWrite020) SetMoveFlags(value, size);','SetMoveFlags(value, size);')
    elif variant=='lost-fc':text=once(text,'_restoredWriteFunctionCode020 = (ushort)(ssw & 7);','_restoredWriteFunctionCode020 = null;')
    elif variant=='wrong-destination':text=once(text,'WriteFinalMoveDestination020(address, value, size);','WriteFinalMoveDestination020(State.A[1], value, size);')
    elif variant=='lost-pipe':text=once(text,'_instructionPipe = pipe;','_instructionPipe.Reset(pc);')
    if variant!='candidate':data[CPU]=text.encode('utf-8')
    return data


def command(root,variant):
    selection=FILTER+('|'+m.FILTER+'|'+m.m.FILTER+'|'+m.m.m.FILTER+'|'+m.m.m.m.FILTER+'|'+m.m.m.m.m.FILTER if variant=='candidate' else '')
    return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selection,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]


def expected(model,width,current,mode,variant):
    rows={}
    for returned,location,ccr,fc,count,lane in itertools.product(['user','user-M','ISP','MSP'],range(2),range(32),[1,5],range(4),range(width)):
        bad=variant=='wrong-destination' or variant=='recomputed-flags' and ccr&15!=8 or variant=='lost-fc' and fc!=(5 if returned in ['ISP','MSP'] else 1) or variant=='lost-pipe' and count>0
        key=f'{model}/MOVE/write-refault/width={width}/current={current}/returned={returned}/location={location}/ccr={ccr}/fc={fc}/count={count}/mode={mode}/byte={lane}'
        rows[key]={'mismatching' if bad else 'passing':1}
    require(len(rows)==2048*width,'Wrong refault inventory')
    return rows


def verify(root,variant,data,retained,parent_names):
    e=load(root/'execution.json');ids={n:hashlib.sha256(v).hexdigest() for n,v in data.items()}
    require(e['sources']==ids==inventory(root/'source'),'Changed complete refault source inventory')
    require(e['command']==command(root,variant) and e['settings']==ENV and e['exit']==(0 if variant=='candidate' else 1),'Wrong refault command, settings or exit')
    outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(e['outputs']=={n:sha(root/n) for n in outputs},'Changed refault outputs')
    tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',NS)
    names={f'{route}WriteRefaultContinuation':'Passed' if variant=='candidate' else 'Failed' for route in ['Scalar','Batch']}
    if variant=='candidate':names.update(parent_names)
    require(len(tests)==len(names) and {x.get('testName').split('.')[-1]:x.get('outcome') for x in tests}==names,'Wrong or empty refault selection')
    counter=tree.find('.//t:Counters',NS);failed=sum(v=='Failed' for v in names.values())
    require(all(int(counter.get(k,-1))==v for k,v in dict(total=len(names),executed=len(names),passed=len(names)-failed,failed=failed,notExecuted=0).items()),'Wrong refault counters')
    require(tree.find('.//t:ResultSummary',NS).get('outcome')==('Completed' if variant=='candidate' else 'Failed'),'Wrong refault outcome')
    definitions={x.get('id'):x for x in tree.findall('.//t:UnitTest',NS)}
    require(all(Path(definitions[x.get('testId')].get('storage')).resolve()==(root/'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll').resolve() for x in tests),'Wrong loaded refault test assembly')
    stdout='\n'.join(x.text or '' for x in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',NS));reports=[];totals=dict(passing=0,mismatching=0,unsupported=0,untested=0)
    def summary(d):
        matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
        require(len(matches)==1 and json.loads(matches[0][0])==d['counts'] and int(matches[0][1])==d['logicalCases'] and int(matches[0][2])==len(d['combinations']),'Changed-input report/TRX disagreement')
    for model,width,current,mode,route in itertools.product(MODELS,[1,2,4],['ISP','MSP'],['mapped','software'],['scalar','batch']):
        group=f'move-write-refault-{width}-{current}-{mode}-{route}';path=root/f'{model}-{group}.json';d=load(path);wanted=expected(model,width,current,mode,variant)
        counts={s:sum(v.get(s,0) for v in wanted.values()) for s in totals}
        require(d['schema']==1 and d['model']==model and d['group']==group and d['logicalCases']==len(wanted) and d['xunitBatches']==1 and d['counts']==counts and d['combinations']==wanted,'Wrong refault combinations: '+path.name)
        bad={n for n,v in wanted.items() if 'passing' not in v}
        require(len(d['failures'])==len(bad) and {x['id'] for x in d['failures']}==bad and all(wanted[x['id']]=={x['status']:1} and x['reason'] for x in d['failures']),'Wrong complete refault failure witnesses')
        summary(d);reports.append(dict(path=path.name,sha256=sha(path)))
        for s in totals:totals[s]+=counts[s]
    if variant=='candidate':
        require(len(retained)==1392,'Incomplete refault retention')
        for name,wanted in retained.items():
            d=load(root/name);require(d==wanted,'Changed retained refault-parent report: '+name);summary(d);reports.append(dict(path=name,sha256=sha(root/name)))
    require({x.name for x in root.glob('*.json')}=={x['path'] for x in reports}|{'execution.json'},'Missing or foreign refault reports')
    return dict(variant=variant,counts=totals,retentionCases=667392 if variant=='candidate' else 0,executions=len(names),reports=reports,executionSha256=sha(root/'execution.json'))


def audit(parent,output,validate):
    require(sha(parent/'proof.json')==PARENT_PROOF,'Wrong refault parent proof')
    with contextlib.redirect_stdout(io.StringIO()):m.audit(parent.parent/'MoveWriteEntryV1',parent,True)
    linkage=dict(producers={f.relative_to(REPO).as_posix():sha(f) for f in [Path(__file__),HELPER,REPO/FIXTURE]},parentProofSha256=PARENT_PROOF,variants=VARIANTS,settings=ENV)
    if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
    require(load(output/'inputs.json')==linkage,'Changed refault producers or selection')
    retained={f.name:load(f) for f in (parent/'candidate').glob('*-move-write-*.json')}
    tree=ET.parse(parent/'candidate/audit.trx');parent_names={x.get('testName').split('.')[-1]:x.get('outcome') for x in tree.findall('.//t:UnitTestResult',NS)}
    require(len(parent_names)==12 and set(parent_names.values())=={'Passed'},'Incomplete refault parent roster')
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
    proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=242,productionImport=False,publication=False,hardwareQualified=False,architecturalPromotion=False,roadmapComplete=False)
    if validate:require(load(output/'proof.json')==proof,'Changed complete refault proof')
    else:save(output/'proof.json',proof)
    print('Complete private write-refault audit',sha(output/'proof.json'),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--qualified-parent-directory',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--validate-only',action='store_true')
    args=parser.parse_args();audit(args.qualified_parent_directory.resolve(),args.output.resolve(),args.validate_only)
