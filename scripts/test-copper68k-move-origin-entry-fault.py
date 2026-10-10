"""Audit actual MOVE origins and secondary C023 entry faults.

Requires the complete immutable MoveWriteRefaultV1 parent. No private CPU
import, hardware-frame qualification, or package publication is performed.
"""
import argparse, contextlib, hashlib, importlib.util, io, itertools, json, os, re, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
REPO = Path(__file__).resolve().parents[1]
HELPER = REPO/'scripts/test-copper68k-move-write-refault.py'
spec = importlib.util.spec_from_file_location('write_validation', HELPER)
m = importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
sha,load,save,require,inventory=m.sha,m.load,m.save,m.require,m.inventory
FIXTURE='Copper68k.Tests/Synthetic/SyntheticM68020MoveOriginEntryFaultTests.cs'
CPU='Copper68k/M68kAdvancedTimingInterpreter.FinalWrite020.cs'
PARENT_PROOF='96abb10dcbe6c0c51f5922a28f2484018b2bbcdaaac9059eaf0416028eb87bd3'
MODELS=['68EC020','68020','68030','A1200']
VARIANTS=['candidate','no-halt','stack-advance','wrong-saved-pc','source-replay']
FILTER='FullyQualifiedName~SyntheticM68020MoveOriginEntryFaultTests'
ENV={'COPPER68K_RUN_020_MOVE_ORIGIN_ENTRY':'1',**m.ENV}
NS=m.NS


def once(text,old,new):
    require(text.count(old)==1,'Nonunique origin-entry mutation')
    return text.replace(old,new)


def sources(parent,variant):
    prior=load(parent/'candidate/execution.json')['sources']
    require(len(prior)==242 and FIXTURE not in prior,'Incomplete origin-entry parent')
    data={n:(parent/'candidate/source'/n).read_bytes() for n in prior}
    require(all(hashlib.sha256(v).hexdigest()==prior[n] for n,v in data.items()),'Changed origin-entry parent sources')
    data[FIXTURE]=(REPO/FIXTURE).read_bytes()
    text=data[CPU].decode('utf-8').replace('\r\n','\n')
    stack='if (!_physicalAddressMap!.IsCpuPhysicalAddressMapped(State.A[7], 2, M68kBusAccessKind.CpuDataWrite))\n                { State.Halted = true; _instructionPipe.Reset(); return; }'
    if variant=='no-halt':text=once(text,stack,stack.replace('State.Halted = true','State.Halted = false'))
    elif variant=='stack-advance':text=once(text,stack,stack.replace('State.Halted = true','State.SetActiveStackPointer(unchecked(State.A[7] + 2)); State.Halted = true'))
    elif variant=='wrong-saved-pc':text=once(text,'words[0] = sr; Long(2, fault.NextPc);','words[0] = sr; Long(2, unchecked(fault.NextPc + 2));')
    elif variant=='source-replay':text=once(text,'var sr = State.StatusRegister;','var sr = State.StatusRegister; _ = ReadSized(State.A[fault.Opcode & 7], FinalMoveSize020(fault.Opcode));')
    if variant!='candidate':data[CPU]=text.encode('utf-8')
    return data


def command(root,variant):
    prior=m.command(root,'candidate');selection=FILTER+('|'+prior[prior.index('--filter')+1] if variant=='candidate' else '')
    return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selection,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]


def cases(kind):
    for width,bank,location in itertools.product([1,2,4],['user','user-M','ISP','MSP'],range(2)):
        if kind=='opcodes':forms=itertools.product(range(8),range(8),[False,True],[2],[31],[0],[0],[0])
        elif kind=='boundaries':forms=itertools.product([0],[1],[True],range(4),range(32),[0],[0],[0])
        else:forms=((source,source if alias else 1,post,2,ccr,byte,entry,entry_byte) for source,alias,post,ccr,byte,entry in itertools.product([0,7],[False,True],[False,True],[0,31],range(width),range(17)) for entry_byte in range(4 if entry==16 else 2))
        for source,destination,post,value,ccr,byte,entry,entry_byte in forms:
            yield width,bank,location,source,destination,post,value,ccr,byte,entry,entry_byte


def groups():
    for kind in ['opcodes','entry','boundaries']:
        if kind=='entry':
            for width,bank in itertools.product([1,2,4],['user','user-M','ISP','MSP']):yield kind,width,bank
        else:yield kind,0,None


def expected(model,kind,variant,selected_width=0,selected_bank=None):
    rows={};opcodes=set()
    for width,bank,location,source,destination,post,value,ccr,byte,entry,entry_byte in cases(kind):
        if selected_width and (width!=selected_width or bank!=selected_bank):continue
        opcode=({1:0x1090,2:0x3090,4:0x2090}[width])|source|(destination<<9)|(8 if post else 0);opcodes.add(opcode)
        bad=variant=='source-replay' or variant in ['no-halt','stack-advance'] and entry!=16 or variant=='wrong-saved-pc' and entry>=14
        key=f'{model}/MOVE/origin-entry/{kind}/opcode={opcode:04X}/width={width}/bank={bank}/location={location}/source={source}/destination={destination}/post={post}/value={value}/ccr={ccr}/operand-byte={byte}/entry={entry}/entry-byte={entry_byte}'
        rows[key]={'mismatching' if bad else 'passing':1}
    require(len(rows)==(1152*selected_width if kind=='entry' else 3072),'Wrong origin-entry inventory')
    if kind=='opcodes':require(len(opcodes)==384 and {0x1290,0x3298,0x2e9f}<=opcodes,'Incomplete simple MOVE word enumeration')
    return rows


def verify(root,variant,data,retained,parent_names):
    e=load(root/'execution.json');ids={n:hashlib.sha256(v).hexdigest() for n,v in data.items()}
    require(e['sources']==ids==inventory(root/'source'),'Changed complete origin-entry source inventory')
    require(e['command']==command(root,variant) and e['settings']==ENV and e['exit']==(0 if variant=='candidate' else 1),'Wrong origin-entry command, settings or exit')
    outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(e['outputs']=={n:sha(root/n) for n in outputs},'Changed origin-entry outputs')
    tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',NS)
    names={f'{route}MoveOriginEntryFaults':'Passed' if variant=='candidate' else 'Failed' for route in ['Scalar','Batch']}
    if variant=='candidate':names.update(parent_names)
    require(len(tests)==len(names) and {x.get('testName').split('.')[-1]:x.get('outcome') for x in tests}==names,'Wrong or empty origin-entry selection')
    counter=tree.find('.//t:Counters',NS);failed=sum(v=='Failed' for v in names.values())
    require(all(int(counter.get(k,-1))==v for k,v in dict(total=len(names),executed=len(names),passed=len(names)-failed,failed=failed,notExecuted=0).items()),'Wrong origin-entry counters')
    require(tree.find('.//t:ResultSummary',NS).get('outcome')==('Completed' if variant=='candidate' else 'Failed'),'Wrong origin-entry outcome')
    definitions={x.get('id'):x for x in tree.findall('.//t:UnitTest',NS)}
    require(all(Path(definitions[x.get('testId')].get('storage')).resolve()==(root/'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll').resolve() for x in tests),'Wrong loaded origin-entry test assembly')
    stdout='\n'.join(x.text or '' for x in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',NS));reports=[];totals=dict(passing=0,mismatching=0,unsupported=0,untested=0)
    def summary(d):
        matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
        require(len(matches)==1 and json.loads(matches[0][0])==d['counts'] and int(matches[0][1])==d['logicalCases'] and int(matches[0][2])==len(d['combinations']),'Changed-input report/TRX disagreement')
    for model,(kind,width,bank),route in itertools.product(MODELS,list(groups()),['scalar','batch']):
        suffix=f'-{width}-{bank}' if kind=='entry' else ''
        group=f'move-origin-entry-{kind}{suffix}-{route}';path=root/f'{model}-{group}.json';d=load(path);wanted=expected(model,kind,variant,width,bank)
        counts={s:sum(v.get(s,0) for v in wanted.values()) for s in totals}
        require(d['schema']==1 and d['model']==model and d['group']==group and d['logicalCases']==len(wanted) and d['xunitBatches']==1 and d['counts']==counts and d['combinations']==wanted,'Wrong origin-entry combinations: '+path.name)
        bad={n for n,v in wanted.items() if 'passing' not in v}
        require(len(d['failures'])==len(bad) and {x['id'] for x in d['failures']}==bad and all(wanted[x['id']]=={x['status']:1} and x['reason'] for x in d['failures']),'Wrong complete origin-entry failure witnesses')
        summary(d);reports.append(dict(path=path.name,sha256=sha(path)))
        for s in totals:totals[s]+=counts[s]
    if variant=='candidate':
        require(len(retained)==1488,'Incomplete origin-entry retention')
        for name,wanted in retained.items():
            d=load(root/name);require(d==wanted,'Changed retained origin-entry-parent report: '+name);summary(d);reports.append(dict(path=name,sha256=sha(root/name)))
    require({x.name for x in root.glob('*.json')}=={x['path'] for x in reports}|{'execution.json'},'Missing or foreign origin-entry reports')
    return dict(variant=variant,counts=totals,retentionCases=1126144 if variant=='candidate' else 0,executions=len(names),reports=reports,executionSha256=sha(root/'execution.json'))


def audit(parent,output,validate):
    require(sha(parent/'proof.json')==PARENT_PROOF,'Wrong origin-entry parent proof')
    with contextlib.redirect_stdout(io.StringIO()):m.audit(parent.parent/'MoveWriteChangedInputV2',parent,True)
    linkage=dict(producers={f.relative_to(REPO).as_posix():sha(f) for f in [Path(__file__),HELPER,REPO/FIXTURE]},parentProofSha256=PARENT_PROOF,variants=VARIANTS,settings=ENV)
    if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
    require(load(output/'inputs.json')==linkage,'Changed origin-entry producers or selection')
    retained={f.name:load(f) for f in (parent/'candidate').glob('*-move-write-*.json')}
    tree=ET.parse(parent/'candidate/audit.trx');parent_names={x.get('testName').split('.')[-1]:x.get('outcome') for x in tree.findall('.//t:UnitTestResult',NS)}
    require(len(parent_names)==14 and set(parent_names.values())=={'Passed'},'Incomplete origin-entry parent roster')
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
    proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=243,productionImport=False,publication=False,hardwareQualified=False,architecturalPromotion=False,roadmapComplete=False)
    if validate:require(load(output/'proof.json')==proof,'Changed complete origin-entry proof')
    else:save(output/'proof.json',proof)
    print('Complete private MOVE-origin-entry audit',sha(output/'proof.json'),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--qualified-parent-directory',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--validate-only',action='store_true')
    args=parser.parse_args();audit(args.qualified_parent_directory.resolve(),args.output.resolve(),args.validate_only)
