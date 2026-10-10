"""Audit private pending MOVE writes after software frame edits and refaults.

Requires a complete final-write candidate audit. Uses only isolated source/build
snapshots; no production import, publication or hardware promotion occurs.
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

REPO = Path(__file__).resolve().parents[1]
PARENT_HELPER = REPO / 'scripts/test-copper68k-final-move-write-candidate.py'
FIXTURE = 'Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteHandlerTests.cs'
spec = importlib.util.spec_from_file_location('final_write', PARENT_HELPER)
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
sha, load, save, require, inventory = m.sha, m.load, m.save, m.require, m.inventory
PARENT_PROOF = 'c1a5a156f5922a8d33fe6260eebd6edb61ddba3b309a392337e3772d537445a7'
FILTER = 'FullyQualifiedName~SyntheticM68020MoveWriteHandlerTests'
VARIANTS = ['candidate', 'ignore-df', 'source-update', 'recompute-flags', 'discard-fc']
LANES = ['original', 'output', 'software', 'refault', 'edited-refault']


def expected(model, variant, width, lane):
    rows = {}
    for bank, source, post, alias, vi, ccr in itertools.product(['user','user-M','ISP','MSP'], [0,7], [False,True], [False,True], range(4), range(32)):
        key = f'{model}/MOVE/write-handler/width={width}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}/lane={lane}'
        original_flags = [4,8,0,8][vi]
        mismatch = variant == 'ignore-df' and lane == 'software' or variant == 'source-update' and post or variant == 'discard-fc' and lane == 'edited-refault' or variant == 'recompute-flags' and (lane == 'output' or lane == 'edited-refault' and (ccr + 7) % 16 != original_flags)
        rows[key] = {'mismatching' if mismatch else 'passing':1}
    return rows


def sources(parent, variant):
    data = {name:(parent/'candidate/source'/name).read_bytes() for name in inventory(parent/'candidate/source')}
    require(len(data) == 233, 'Incomplete final-write parent sources')
    data[FIXTURE] = (REPO/FIXTURE).read_bytes()
    text = data[m.NEW].decode('utf-8').replace('\r\n','\n')
    pending = 'if ((ssw & 0x100) != 0) WriteFinalMoveDestination020(address, value, size);'
    if variant == 'ignore-df': text = m.once(text, pending, 'WriteFinalMoveDestination020(address, value, size);')
    elif variant == 'source-update': text = m.once(text, pending, 'if (((opcode >> 3) & 7) == 3) WriteGeneralRegister(true, opcode & 7, unchecked(State.A[opcode & 7] + M68kIntegerSemantics.AddressIncrement(opcode & 7, size)));\n            '+pending)
    elif variant == 'recompute-flags': text = m.once(text, pending, 'SetMoveFlags(value, size);\n            '+pending)
    elif variant == 'discard-fc': text = m.once(text, '_restoredWriteFunctionCode020 = (ushort)(ssw & 7);', '_restoredWriteFunctionCode020 = null;')
    data[m.NEW] = text.encode('utf-8')
    return data


def command(root, variant):
    selected = FILTER + ('|'+'|'.join([m.m.FILTER]+m.RETENTION) if variant == 'candidate' else '')
    return ['dotnet','test',str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release','--artifacts-path',str(root/'build'),'--filter',selected,'--logger','trx;LogFileName=audit.trx','--results-directory',str(root)]


def verify(root, variant, data, retained):
    ids={name:hashlib.sha256(value).hexdigest() for name,value in data.items()}
    record=load(root/'execution.json')
    require(record['sources'] == ids == inventory(root/'source'), 'Changed complete handler sources')
    require(record['command'] == command(root,variant) and record['exit'] == (0 if variant == 'candidate' else 1), 'Wrong handler command or exit')
    outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(set(record['outputs']) == set(outputs) and record['outputs'] == {p:sha(root/p) for p in outputs}, 'Changed handler execution outputs')
    tree=ET.parse(root/'audit.trx');tests=tree.findall('.//t:UnitTestResult',m.r.NS)
    names={'ScalarPendingWriteHandlers':'Passed' if variant == 'candidate' else 'Failed', 'BatchPendingWriteHandlers':'Passed' if variant == 'candidate' else 'Failed'}
    if variant == 'candidate':
        for name in ['DirectFinalWriteControls(batch: False)','DirectFinalWriteControls(batch: True)','ScalarFinalWriteFaults','BatchFinalWriteFaults','DirectMemoryDestinationControls(batch: False)','DirectMemoryDestinationControls(batch: True)','ScalarMemoryDestinationRecovery','BatchMemoryDestinationRecovery','LiteralMoveFixturesAndSentinels(batch: False)','LiteralMoveFixturesAndSentinels(batch: True)','ScalarOperandReadFaultsRequireFormatB','BatchOperandReadFaultsRequireFormatB']: names[name]='Passed'
    require(len(tests) == len(names) and {t.get('testName').split('.')[-1]:t.get('outcome') for t in tests} == names, 'Wrong actual handler roster/outcomes')
    counters=tree.find('.//t:Counters',m.r.NS);failed=sum(outcome == 'Failed' for outcome in names.values())
    require(int(counters.get('total')) == len(names) and int(counters.get('passed')) == len(names)-failed and int(counters.get('failed')) == failed, 'Wrong handler TRX counters')
    stdout='\n'.join(t.text or '' for t in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut',m.r.NS))
    rows=[];totals=dict(passing=0,mismatching=0,unsupported=0,untested=0)
    def summary(d):
        matches=re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)',stdout)
        require(len(matches) == 1 and json.loads(matches[0][0]) == d['counts'] and int(matches[0][1]) == d['logicalCases'] and int(matches[0][2]) == len(d['combinations']), 'Handler TRX/report disagreement')
    for model,width,lane,mode in itertools.product(m.r.MODELS,[1,2,4],LANES,['scalar','batch']):
        group=f'move-write-handlers-{width}-{lane}-{mode}'
        p=root/f'{model}-{group}.json';d=load(p);wanted=expected(model,variant,width,lane)
        counts={s:sum(row.get(s,0) for row in wanted.values()) for s in totals}
        require(d['model'] == model and d['group'] == group and d['logicalCases'] == 4096 and d['counts'] == counts and d['combinations'] == wanted, 'Wrong handler keys, weights or results')
        bad={key for key,row in wanted.items() if 'mismatching' in row}
        require(len(d['failures']) == len(bad) and {f['id'] for f in d['failures']} == bad and all(f['status'] == 'mismatching' and f['reason'] for f in d['failures']), 'Wrong handler mutation witnesses')
        if variant == 'discard-fc': require(all(f['reason'].startswith('Pending-write refault lost returned SR/FC/output or repeated source effects') for f in d['failures']), 'Wrong FC mutation cause')
        if variant == 'ignore-df': require(all(f['reason'].startswith('Pending-write handler repeated a completed source/update or write') for f in d['failures']), 'Wrong DF mutation cause')
        summary(d)
        for s in totals:totals[s]+=counts[s]
        rows.append(dict(path=p.name,sha256=sha(p)))
    if variant == 'candidate':
        require(len(retained) == 48, 'Incomplete retained handler selection')
        for name,wanted in retained.items():
            p=root/name;d=load(p);require(d == wanted, 'Changed complete retained handler results: '+name);summary(d);rows.append(dict(path=name,sha256=sha(p)))
    require({p.name for p in root.glob('*.json')} == {row['path'] for row in rows}|{'execution.json'}, 'Missing or foreign handler reports')
    return dict(variant=variant,counts=totals,retentionCases=155648 if variant == 'candidate' else 0,executions=len(names),reports=rows,executionSha256=sha(root/'execution.json'))


def audit(parent,output,validate):
    require(sha(parent/'proof.json') == PARENT_PROOF, 'Wrong qualified final-write proof')
    inputs=load(parent/'inputs.json')
    # The discovery and its qualified parent must accompany the parent evidence.
    discovery=parent.parent/'MoveFinalWriteDiscoveryV3';qualified=parent.parent/'MemoryDestinationPrivateV4'
    with contextlib.redirect_stdout(io.StringIO()):m.audit(qualified,discovery,parent,True)
    producers={p.relative_to(REPO).as_posix():sha(p) for p in [Path(__file__),PARENT_HELPER,REPO/FIXTURE]}
    linkage=dict(producers=producers,parentProofSha256=PARENT_PROOF,parentInputsSha256=sha(parent/'inputs.json'),variants=VARIANTS)
    if not validate:output.mkdir(parents=True,exist_ok=False);save(output/'inputs.json',linkage)
    require(load(output/'inputs.json') == linkage, 'Wrong handler producer, parent or selection')
    retained={p.name:load(p) for p in (parent/'candidate').glob('*.json') if p.name.startswith(tuple(model+'-' for model in m.r.MODELS))}
    entries=[]
    for variant in VARIANTS:
        root=output/variant;data=sources(parent,variant)
        if not validate:
            (root/'source').mkdir(parents=True)
            for name,value in data.items():
                p=root/'source'/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(value)
            env=dict(os.environ,COPPER68K_RUN_020_MOVE_WRITE_HANDLERS='1',COPPER68K_RUN_020_MOVE_WRITE_DISCOVERY='1',COPPER68K_RUN_020_OPERAND_READ_DISCOVERY='1',COPPER68K_RUN_020_MEMORY_DESTINATION_RECOVERY='1',COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
            with (root/'execution.log').open('w',encoding='utf-8') as log:result=subprocess.run(command(root,variant),env=env,stdout=log,stderr=subprocess.STDOUT)
            outputs=['execution.log','audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
            save(root/'execution.json',dict(command=command(root,variant),exit=result.returncode,sources={name:hashlib.sha256(value).hexdigest() for name,value in data.items()},outputs={p:sha(root/p) for p in outputs}))
        entry=verify(root,variant,data,retained);entries.append(entry);print(variant,entry['counts'],'retention',entry['retentionCases'],flush=True)
    proof=dict(schema=1,inputsSha256=sha(output/'inputs.json'),entries=entries,sourceCount=234,productionImport=False,publication=False,hardwareQualified=False,roadmapComplete=False)
    if validate:require(load(output/'proof.json') == proof,'Changed complete handler verification')
    else:save(output/'proof.json',proof)
    print('Complete isolated pending-write handler audit',sha(output/'proof.json'),flush=True)


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--qualified-parent-directory',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--validate-only',action='store_true');args=parser.parse_args()
    audit(args.qualified_parent_directory.resolve(),args.output.resolve(),args.validate_only)
