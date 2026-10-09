"""Audit all-width memory-destination source recovery and read/write fault chains.

Uses a frozen private handler-qualified parent, fresh isolated sources/builds,
exact case/witness verification and retained reports. No production CPU import
or architectural promotion of the known native A7/trace/refault gaps occurs.
"""
import argparse
import contextlib
import hashlib
import importlib.util
import io
import itertools
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

REPO=Path(__file__).resolve().parents[1]
HELPER=REPO/'scripts/test-copper68k-final-write-handlers.py'
FIXTURE='Copper68k.Tests/Synthetic/SyntheticM68020MemoryReadWriteTests.cs'
spec=importlib.util.spec_from_file_location('write_handlers',HELPER)
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
sha,load,save,require,inventory=m.sha,m.load,m.save,m.require,m.inventory
PARENT_PROOF='f63d2ecf21777c2bc8fc259039aa2361bb72165b63b51caefa085b751a9f0674'
CPU=m.m.OPERAND
FILTER='FullyQualifiedName~SyntheticM68020MemoryReadWriteTests'
VARIANTS=['baseline','candidate','source-replay','source-update','stale-destination','timing']
MODELS=m.m.r.MODELS


def expected(model,width,lane,offset,variant):
    rows={}
    for bank,source,post,alias,vi,ccr in itertools.product(['user','user-M','ISP','MSP'],[0,7],[False,True],[False,True],range(4),range(32)):
        key=f'{model}/MOVE/read-write/width={width}/lane={lane}/offset={offset}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}'
        status='passing'
        if lane!='control':
            if variant=='baseline' and width!=4:status='unsupported'
            elif variant=='source-replay' or variant=='source-update' and post or variant=='stale-destination' and post and alias or variant=='timing' and lane=='read':status='mismatching'
        rows[key]={status:1}
    return rows


def sources(parent,variant):
    data={name:(parent/'candidate/source'/name).read_bytes() for name in inventory(parent/'candidate/source')}
    require(len(data)==234,'Incomplete handler-qualified sources')
    data[FIXTURE]=(REPO/FIXTURE).read_bytes()
    if variant=='baseline':return data
    t=data[CPU].decode('utf-8').replace('\r\n','\n')
    t=m.m.once(t,'var memoryDestination = top == 2 && destination == 2 && mode is 2 or 3;','var memoryDestination = top is >= 1 and <= 3 && destination == 2 && mode is 2 or 3;')
    t=m.m.once(t,'destinationMode > 1 && !(top == 2 && destinationMode == 2 && mode is 2 or 3)','destinationMode > 1 && !(top is >= 1 and <= 3 && destinationMode == 2 && mode is 2 or 3)')
    t=m.m.once(t,'CompleteTiming(mode == 2 ? M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect : M68kInstructionTimingKey.MoveLongPostIncrementToAddressIndirect);','CompleteTiming(FinalMoveTiming020(opcode));')
    update='if (mode == 3) WriteGeneralRegister(true, register, unchecked(address + M68kIntegerSemantics.AddressIncrement(register, size)));'
    write='else if (destinationMode == 2) { WriteFinalMoveDestination020(State.A[destination], value, size); SetMoveFlags(value, size); }'
    if variant=='source-replay':t=m.m.once(t,update,update+'\n        if (destinationMode == 2) _ = ReadSized(State.A[register], size);')
    elif variant=='source-update':t=m.m.once(t,update,'if (mode == 3) WriteGeneralRegister(true, register, unchecked(address + (destinationMode == 2 ? 2u : 1u) * M68kIntegerSemantics.AddressIncrement(register, size)));')
    elif variant=='stale-destination':t=m.m.once(t,write,'else if (destinationMode == 2) { WriteFinalMoveDestination020(unchecked(State.A[destination] - (mode == 3 && destination == register ? M68kIntegerSemantics.AddressIncrement(register, size) : 0)), value, size); SetMoveFlags(value, size); }')
    elif variant=='timing':t=m.m.once(t,'CompleteTiming(FinalMoveTiming020(opcode));','CompleteTimingPlan(M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.GeneralMove, "mutated read suffix", 6));')
    data[CPU]=t.encode('utf-8');return data


def command(root,variant):
    selection=FILTER+('|'+'|'.join([m.FILTER,m.m.m.FILTER]+m.m.RETENTION) if variant=='candidate' else '')
    return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selection,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]


def verify(root,variant,data,retained,parent_names):
    record=load(root/'execution.json');ids={name:hashlib.sha256(value).hexdigest() for name,value in data.items()}
    require(record['sources']==ids==inventory(root/'source'),'Changed all-width complete sources')
    require(record['command']==command(root,variant) and record['exit']==(0 if variant=='candidate' else 1),'Wrong all-width command/exit')
    outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(set(record['outputs'])==set(outputs) and record['outputs']=={p:sha(root/p) for p in outputs},'Changed all-width outputs')
    tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',m.m.r.NS)
    names={'DirectWidthControls(batch: False)':'Passed','DirectWidthControls(batch: True)':'Passed','ScalarReadWriteContinuations':'Passed' if variant=='candidate' else 'Failed','BatchReadWriteContinuations':'Passed' if variant=='candidate' else 'Failed'}
    if variant=='candidate':names.update(parent_names)
    require(len(tests)==len(names) and {t.get('testName').split('.')[-1]:t.get('outcome') for t in tests}==names,'Wrong actual all-width outcomes/roster')
    counters=tree.find('.//t:Counters',m.m.r.NS);failed=sum(s=='Failed' for s in names.values())
    require(int(counters.get('total'))==len(names) and int(counters.get('failed'))==failed and int(counters.get('passed'))==len(names)-failed,'Wrong all-width TRX counters')
    stdout='\n'.join(t.text or '' for t in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',m.m.r.NS))
    totals=dict(passing=0,mismatching=0,unsupported=0,untested=0);rows=[]
    def summary(d):
        matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
        require(len(matches)==1 and json.loads(matches[0][0])==d['counts'] and int(matches[0][1])==d['logicalCases'] and int(matches[0][2])==len(d['combinations']),'All-width TRX/report disagreement')
    for model,width,mode in itertools.product(MODELS,[1,2,4],['scalar','batch']):
        for lane in ['control','read','read-write','read-write-refault']:
            for offset in range(1 if lane=='control' else width):
                group=f'memory-read-write-{width}-{lane}-{offset}-{mode}';p=root/f'{model}-{group}.json';d=load(p);wanted=expected(model,width,lane,offset,variant)
                counts={s:sum(row.get(s,0) for row in wanted.values()) for s in totals}
                require(d['model']==model and d['group']==group and d['logicalCases']==4096 and d['combinations']==wanted and d['counts']==counts,'Wrong all-width keys, weights or results')
                bad={key for key,row in wanted.items() if 'passing' not in row}
                require(len(d['failures'])==len(bad) and {f['id'] for f in d['failures']}==bad and all(f['status']==next(iter(wanted[f['id']])) and f['reason'] for f in d['failures']),'Wrong complete all-width failure witnesses')
                if variant=='timing':require(all(f['reason']=='All-width continuation changed timing policy' for f in d['failures']),'Wrong all-width timing cause')
                summary(d)
                for s in totals:totals[s]+=counts[s]
                rows.append(dict(path=p.name,sha256=sha(p)))
    if variant=='candidate':
        require(len(retained)==168,'Missing complete all-width retention selection')
        for name,wanted in retained.items():
            p=root/name;d=load(p);require(d==wanted,'Changed complete all-width retained reports: '+name);summary(d);rows.append(dict(path=name,sha256=sha(p)))
    require({p.name for p in root.glob('*.json')}=={row['path'] for row in rows}|{'execution.json'},'Missing/foreign all-width reports')
    return dict(variant=variant,counts=totals,retentionCases=647168 if variant=='candidate' else 0,executions=len(names),reports=rows,executionSha256=sha(root/'execution.json'))


def audit(parent,output,validate):
    require(sha(parent/'proof.json')==PARENT_PROOF,'Wrong all-width qualified parent')
    with contextlib.redirect_stdout(io.StringIO()):m.audit(parent.parent/'MoveFinalWriteCandidateV2',parent,True)
    producers={p.relative_to(REPO).as_posix():sha(p) for p in [Path(__file__),HELPER,REPO/FIXTURE]}
    linkage=dict(producers=producers,parentProofSha256=PARENT_PROOF,variants=VARIANTS)
    if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
    require(load(output/'inputs.json')==linkage,'Wrong all-width producer/parent/selection')
    retained={p.name:load(p) for p in (parent/'candidate').glob('*.json') if p.name.startswith(tuple(x+'-' for x in MODELS))}
    tree=ET.parse(parent/'candidate/audit.trx');parent_names={t.get('testName').split('.')[-1]:t.get('outcome') for t in tree.findall('.//t:UnitTestResult',m.m.r.NS)}
    require(len(parent_names)==14 and set(parent_names.values())=={'Passed'},'Incomplete qualified test roster')
    entries=[]
    for variant in VARIANTS:
        root=output/variant;data=sources(parent,variant)
        if not validate:
            (root/'source').mkdir(parents=True)
            for name,value in data.items():
                p=root/'source'/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(value)
            env=dict(os.environ,COPPER68K_RUN_020_MEMORY_READ_WRITE='1',COPPER68K_RUN_020_MOVE_WRITE_HANDLERS='1',COPPER68K_RUN_020_MOVE_WRITE_DISCOVERY='1',COPPER68K_RUN_020_OPERAND_READ_DISCOVERY='1',COPPER68K_RUN_020_MEMORY_DESTINATION_RECOVERY='1',COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
            with (root/'execution.log').open('w',encoding='utf-8') as log:result=subprocess.run(command(root,variant),env=env,stdout=log,stderr=subprocess.STDOUT)
            outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
            save(root/'execution.json',dict(command=command(root,variant),exit=result.returncode,sources={name:hashlib.sha256(value).hexdigest() for name,value in data.items()},outputs={p:sha(root/p) for p in outputs}))
        entry=verify(root,variant,data,retained,parent_names);entries.append(entry);print(variant,entry['counts'],'retention',entry['retentionCases'],flush=True)
    proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=235,productionImport=False,publication=False,hardwareQualified=False,architecturalPromotion=False,roadmapComplete=False)
    if validate:require(load(output/'proof.json')==proof,'Changed complete all-width proof')
    else:save(output/'proof.json',proof)
    print('Complete isolated memory read/write audit',sha(output/'proof.json'),flush=True)


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--qualified-parent-directory',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--validate-only',action='store_true');a=p.parse_args();audit(a.qualified_parent_directory.resolve(),a.output.resolve(),a.validate_only)
