"""Audit changed handler input to cold private C021/C023 continuations.

Requires the complete immutable MoveWriteEntryV1 parent. No private CPU
import, hardware-frame qualification, or package publication is performed.
"""
import argparse, contextlib, hashlib, importlib.util, io, itertools, json, os, re, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
REPO = Path(__file__).resolve().parents[1]
HELPER = REPO/'scripts/test-copper68k-move-write-entry-fault.py'
spec = importlib.util.spec_from_file_location('write_validation', HELPER)
m = importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
sha,load,save,require,inventory=m.sha,m.load,m.save,m.require,m.inventory
FIXTURE='Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteChangedInputTests.cs'
CPU='Copper68k/M68kAdvancedTimingInterpreter.Rte020.cs'
PARENT_PROOF='540bc6b5f896666eb404cf9ccb6c2da1cbf7ad0e6e62dfef2dde29791c3c6478'
MODELS=['68EC020','68020','68030','A1200']
VARIANTS=['candidate','ignored-input','replay-phase','lost-m-bit']
FILTER='FullyQualifiedName~SyntheticM68020MoveWriteChangedInputTests'
ENV={'COPPER68K_RUN_020_MOVE_WRITE_CHANGED_INPUT':'1',**m.ENV}
NS=m.NS


def once(text,old,new):
    require(text.count(old)==1,'Nonunique changed-input mutation')
    return text.replace(old,new)


def sources(parent,variant):
    prior=load(parent/'candidate/execution.json')['sources']
    require(len(prior)==240 and FIXTURE not in prior,'Incomplete changed-input parent')
    data={n:(parent/'candidate/source'/n).read_bytes() for n in prior}
    require(all(hashlib.sha256(v).hexdigest()==prior[n] for n,v in data.items()),'Changed changed-input parent sources')
    data[FIXTURE]=(REPO/FIXTURE).read_bytes()
    text=data[CPU].decode('utf-8').replace('\r\n','\n')
    old='ExecuteRte020(context, (ssw & 0x100) == 0 ? input : null);'
    if variant=='ignored-input':text=once(text,old,'ExecuteRte020(context, null);')
    elif variant=='replay-phase':text=once(text,old,'context.Phase = 0; '+old)
    elif variant=='lost-m-bit':text=once(text,'value = repaired; supplied = null;','value = repaired & 0xffffefff; supplied = null;')
    if variant!='candidate':data[CPU]=text.encode('utf-8')
    return data


def command(root,variant):
    selection=FILTER+('|'+m.FILTER+'|'+m.m.FILTER+'|'+m.m.m.FILTER+'|'+m.m.m.m.FILTER if variant=='candidate' else '')
    return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selection,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]


def expected(model,bank,variant):
    rows={}
    for location,ccr,returned,request in itertools.product(range(2),range(32),['user','user-M','ISP','MSP'],range(4)):
        bad=variant=='ignored-input' or variant=='replay-phase' and request>0 or variant=='lost-m-bit' and request==0 and returned in ['user','ISP']
        status='unsupported' if variant=='replay-phase' and request>=2 else 'mismatching' if bad else 'passing'
        key=f'{model}/RTE/write-changed-input/bank={bank}/location={location}/ccr={ccr}/returned={returned}/request={request}'
        rows[key]={status:1}
    require(len(rows)==1024,'Wrong changed-input inventory')
    return rows


def verify(root,variant,data,retained,parent_names):
    e=load(root/'execution.json');ids={n:hashlib.sha256(v).hexdigest() for n,v in data.items()}
    require(e['sources']==ids==inventory(root/'source'),'Changed complete changed-input source inventory')
    require(e['command']==command(root,variant) and e['settings']==ENV and e['exit']==(0 if variant=='candidate' else 1),'Wrong changed-input command, settings or exit')
    outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(e['outputs']=={n:sha(root/n) for n in outputs},'Changed changed-input outputs')
    tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',NS)
    names={f'{route}ChangedWriteValidationInput':'Passed' if variant=='candidate' else 'Failed' for route in ['Scalar','Batch']}
    if variant=='candidate':names.update(parent_names)
    require(len(tests)==len(names) and {x.get('testName').split('.')[-1]:x.get('outcome') for x in tests}==names,'Wrong or empty changed-input selection')
    counter=tree.find('.//t:Counters',NS);failed=sum(v=='Failed' for v in names.values())
    require(all(int(counter.get(k,-1))==v for k,v in dict(total=len(names),executed=len(names),passed=len(names)-failed,failed=failed,notExecuted=0).items()),'Wrong changed-input counters')
    require(tree.find('.//t:ResultSummary',NS).get('outcome')==('Completed' if variant=='candidate' else 'Failed'),'Wrong changed-input outcome')
    definitions={x.get('id'):x for x in tree.findall('.//t:UnitTest',NS)}
    require(all(Path(definitions[x.get('testId')].get('storage')).resolve()==(root/'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll').resolve() for x in tests),'Wrong loaded changed-input test assembly')
    stdout='\n'.join(x.text or '' for x in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',NS));reports=[];totals=dict(passing=0,mismatching=0,unsupported=0,untested=0)
    def summary(d):
        matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
        require(len(matches)==1 and json.loads(matches[0][0])==d['counts'] and int(matches[0][1])==d['logicalCases'] and int(matches[0][2])==len(d['combinations']),'Changed-input report/TRX disagreement')
    for model,bank,route in itertools.product(MODELS,['ISP','MSP'],['scalar','batch']):
        group=f'move-write-changed-input-{bank}-{route}';path=root/f'{model}-{group}.json';d=load(path);wanted=expected(model,bank,variant)
        counts={s:sum(v.get(s,0) for v in wanted.values()) for s in totals}
        require(d['schema']==1 and d['model']==model and d['group']==group and d['logicalCases']==len(wanted) and d['xunitBatches']==1 and d['counts']==counts and d['combinations']==wanted,'Wrong changed-input combinations: '+path.name)
        bad={n for n,v in wanted.items() if 'passing' not in v}
        require(len(d['failures'])==len(bad) and {x['id'] for x in d['failures']}==bad and all(wanted[x['id']]=={x['status']:1} and x['reason'] for x in d['failures']),'Wrong complete changed-input failure witnesses')
        summary(d);reports.append(dict(path=path.name,sha256=sha(path)))
        for s in totals:totals[s]+=counts[s]
    if variant=='candidate':
        require(len(retained)==1376,'Incomplete changed-input retention')
        for name,wanted in retained.items():
            d=load(root/name);require(d==wanted,'Changed retained changed-input-parent report: '+name);summary(d);reports.append(dict(path=name,sha256=sha(root/name)))
    require({x.name for x in root.glob('*.json')}=={x['path'] for x in reports}|{'execution.json'},'Missing or foreign changed-input reports')
    return dict(variant=variant,counts=totals,retentionCases=651008 if variant=='candidate' else 0,executions=len(names),reports=reports,executionSha256=sha(root/'execution.json'))


def audit(parent,output,validate):
    require(sha(parent/'proof.json')==PARENT_PROOF,'Wrong changed-input parent proof')
    with contextlib.redirect_stdout(io.StringIO()):m.audit(parent.parent/'MoveWriteValidationV1',parent,True)
    linkage=dict(producers={f.relative_to(REPO).as_posix():sha(f) for f in [Path(__file__),HELPER,REPO/FIXTURE]},parentProofSha256=PARENT_PROOF,variants=VARIANTS,settings=ENV)
    if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
    require(load(output/'inputs.json')==linkage,'Changed changed-input producers or selection')
    retained={f.name:load(f) for f in (parent/'candidate').glob('*-move-write-*.json')}
    tree=ET.parse(parent/'candidate/audit.trx');parent_names={x.get('testName').split('.')[-1]:x.get('outcome') for x in tree.findall('.//t:UnitTestResult',NS)}
    require(len(parent_names)==10 and set(parent_names.values())=={'Passed'},'Incomplete changed-input parent roster')
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
    proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=241,productionImport=False,publication=False,hardwareQualified=False,architecturalPromotion=False,roadmapComplete=False)
    if validate:require(load(output/'proof.json')==proof,'Changed complete changed-input proof')
    else:save(output/'proof.json',proof)
    print('Complete private write-changed-input audit',sha(output/'proof.json'),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--qualified-parent-directory',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--validate-only',action='store_true')
    args=parser.parse_args();audit(args.qualified_parent_directory.resolve(),args.output.resolve(),args.validate_only)
