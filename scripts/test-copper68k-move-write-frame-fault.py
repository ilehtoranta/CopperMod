"""Audit validation recovery and internal-load halt for private pending writes.

Every byte of every selected request is denied, including repeated frame reads.
No production CPU import or silicon internal-image qualification occurs.
"""
import argparse, contextlib, hashlib, importlib.util, io, itertools, json, os, re, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
REPO = Path(__file__).resolve().parents[1]
HELPER = REPO/'scripts/test-copper68k-move-write-frame.py'
FIXTURE = 'Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteFrameFaultTests.cs'
spec = importlib.util.spec_from_file_location('write_frame', HELPER)
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
sha, load, save, require, inventory = m.sha, m.load, m.save, m.require, m.inventory
PARENT_PROOF = 'ca12149f0542b202f764e1cb7543d928cefc7c3c79830456e5e9ef294225b1b1'
CPU = 'Copper68k/M68kAdvancedTimingInterpreter.Rte020.cs'
VARIANTS = ['candidate', 'no-halt', 'early-commit', 'validation-replay', 'fault-pc']
FILTER = 'FullyQualifiedName~SyntheticM68020MoveWriteFrameFaultTests'
ENV = {'COPPER68K_RUN_020_MOVE_WRITE_FRAME_FAULT': '1', 'COPPER68K_RUN_020_MOVE_WRITE_FRAME': '1'}
NS = m.NS


def requests(count):
    return [(0, 2), (2, 4), (6, 2), (30, 2), (8, 2), (10, 2), (16, 4), (20, 2), (22, 2), (24, 4), (30, 2)]+[(x, 2) for x in [12, 14, 28][:count]]+[(x, 2) for x in range(12, 32, 2)]


def sources(parent, variant):
    data = {n: (parent/'candidate/source'/n).read_bytes() for n in inventory(parent/'candidate/source')}
    require(len(data) == 237 and FIXTURE not in data, 'Missing complete frame-fault parent sources')
    data[FIXTURE] = (REPO/FIXTURE).read_bytes(); text = data[CPU].decode('utf-8').replace('\r\n', '\n')
    if variant == 'no-halt': text = m.p.once(text, 'State.Halted = true;\n                _instructionPipe.Reset();', 'State.Halted = false;\n                _instructionPipe.Reset();')
    elif variant == 'early-commit': text = m.p.once(text, 'State.Halted = true;\n                _instructionPipe.Reset();', 'State.SetActiveStackPointer(unchecked(State.A[7] + 32));\n                State.Halted = true;\n                _instructionPipe.Reset();')
    elif variant == 'validation-replay': text = m.p.once(text, 'ExecuteRte020(context, (ssw & 0x100) == 0 ? input : null);', 'context.Phase = 0; ExecuteRte020(context, (ssw & 0x100) == 0 ? input : null);')
    elif variant == 'fault-pc': text = m.p.once(text, 'words[0] = sr; Long(2, context.InstructionPc);', 'words[0] = sr; Long(2, unchecked(context.InstructionPc + 2));')
    if variant != 'candidate': data[CPU] = text.encode('utf-8')
    return data


def command(root, variant):
    selection = FILTER+('|'+m.FILTER if variant == 'candidate' else '')
    return ['dotnet', 'test', str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'), '-c', 'Release', '--artifacts-path', str(root/'build'), '--filter', selection, '--logger', 'trx;LogFileName=audit.trx', '--results-directory', str(root)]


def expected(model, width, current, count, loading, variant):
    rows = {}; reads = requests(count)
    for returned, location, ccr, software, ri in itertools.product(['user', 'user-M', 'ISP', 'MSP'], range(2), [0, 31], [False, True], range(4, len(reads)) if loading else range(4)):
        for lane in range(reads[ri][1]):
            bad = variant in ['no-halt', 'early-commit'] and loading or variant == 'validation-replay' and not loading and ri > 0 or variant == 'fault-pc' and not loading
            key = f'{model}/MOVE/write-frame-fault/width={width}/current={current}/returned={returned}/location={location}/count={count}/ccr={ccr}/software={software}/request={ri}/byte={lane}'
            rows[key] = {'mismatching' if bad else 'passing': 1}
    return rows


def verify(root, variant, data, retained, parent_names):
    record = load(root/'execution.json'); ids = {n: hashlib.sha256(v).hexdigest() for n, v in data.items()}
    require(record['sources'] == ids == inventory(root/'source'), 'Changed complete frame-fault sources')
    require(record['command'] == command(root, variant) and record['settings'] == ENV and record['exit'] == (0 if variant == 'candidate' else 1), 'Wrong frame-fault command, settings or exit')
    outputs = ['execution.log', 'audit.trx', 'build/bin/Copper68k/release/Copper68k.dll', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(record['outputs'] == {n: sha(root/n) for n in outputs}, 'Changed frame-fault outputs')
    tree = ET.parse(root/'audit.trx'); tests = tree.findall('.//t:UnitTestResult', NS)
    names = {f'{route}WriteFrameFaults': 'Passed' if variant == 'candidate' else 'Failed' for route in ['Scalar', 'Batch']}
    if variant == 'candidate': names.update(parent_names)
    require(len(tests) == len(names) and {t.get('testName').split('.')[-1]: t.get('outcome') for t in tests} == names, 'Wrong frame-fault roster or outcomes')
    c = tree.find('.//t:Counters', NS); failed = sum(x == 'Failed' for x in names.values())
    require(int(c.get('total')) == len(names) and int(c.get('executed')) == len(names) and int(c.get('passed')) == len(names)-failed and int(c.get('failed')) == failed and int(c.get('notExecuted')) == 0, 'Wrong frame-fault counters')
    require(tree.find('.//t:ResultSummary', NS).get('outcome') == ('Completed' if variant == 'candidate' else 'Failed'), 'Wrong frame-fault run summary')
    stdout = '\n'.join(t.text or '' for t in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut', NS)); reports = []; totals = dict(passing=0, mismatching=0, unsupported=0, untested=0)
    def summary(d):
        matches = re.findall(re.escape(d['model']+'/'+d['group']+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)', stdout)
        require(len(matches) == 1 and json.loads(matches[0][0]) == d['counts'] and int(matches[0][1]) == d['logicalCases'] and int(matches[0][2]) == len(d['combinations']), 'Frame-fault report/TRX disagreement')
    for model, width, current, count, loading, mode in itertools.product(m.MODELS, [1, 2, 4], ['ISP', 'MSP'], range(4), [False, True], ['scalar', 'batch']):
        group = f'move-write-frame-fault-{width}-{current}-count{count}-'+('load' if loading else 'validation')+'-'+mode
        path = root/f'{model}-{group}.json'; d = load(path); wanted = expected(model, width, current, count, loading, variant)
        counts = {s: sum(v.get(s, 0) for v in wanted.values()) for s in totals}
        require(d['schema'] == 1 and d['model'] == model and d['group'] == group and d['xunitBatches'] == 1 and d['logicalCases'] == len(wanted) and d['combinations'] == wanted and d['counts'] == counts, 'Wrong frame-fault keys, weights or results: '+path.name)
        bad = {k for k, v in wanted.items() if 'mismatching' in v}
        require(len(d['failures']) == len(bad) and {f['id'] for f in d['failures']} == bad and all(f['status'] == 'mismatching' and f['reason'] for f in d['failures']), 'Wrong complete frame-fault witnesses')
        summary(d)
        for s in totals: totals[s] += counts[s]
        reports.append(dict(path=path.name, sha256=sha(path)))
    if variant == 'candidate':
        require(len(retained) == 768, 'Missing complete cold-frame retention')
        for name, wanted in retained.items():
            path = root/name; d = load(path); require(d == wanted, 'Changed retained cold-frame report: '+name); summary(d); reports.append(dict(path=name, sha256=sha(path)))
    require({p.name for p in root.glob('*.json')} == {p['path'] for p in reports}|{'execution.json'}, 'Missing or foreign frame-fault reports')
    return dict(variant=variant, counts=totals, retentionCases=163584 if variant == 'candidate' else 0, executions=len(names), reports=reports, executionSha256=sha(root/'execution.json'))


def audit(parent, output, validate):
    require(sha(parent/'proof.json') == PARENT_PROOF, 'Wrong frame-fault parent proof')
    with contextlib.redirect_stdout(io.StringIO()): m.audit(parent.parent/'MoveWritePipeV3', parent, True)
    linkage = dict(producers={p.relative_to(REPO).as_posix(): sha(p) for p in [Path(__file__), HELPER, REPO/FIXTURE]}, parentProofSha256=PARENT_PROOF, variants=VARIANTS, settings=ENV)
    if not validate: output.mkdir(parents=True, exist_ok=False); save(output/'inputs.json', linkage)
    require(load(output/'inputs.json') == linkage, 'Changed frame-fault producers, parent or selection')
    retained = {p.name: load(p) for p in (parent/'candidate').glob('*-move-write-frame-*.json')}
    tree = ET.parse(parent/'candidate/audit.trx'); parent_names = {t.get('testName').split('.')[-1]: t.get('outcome') for t in tree.findall('.//t:UnitTestResult', NS)}
    require(len(parent_names) == 4 and set(parent_names.values()) == {'Passed'}, 'Incomplete cold-frame parent roster')
    entries = []
    for variant in VARIANTS:
        root = output/variant; data = sources(parent, variant)
        if not validate:
            (root/'source').mkdir(parents=True)
            for n, value in data.items():
                p = root/'source'/n; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(value)
            env = {k: v for k, v in os.environ.items() if not k.startswith('COPPER68K_RUN_')}; env.update(ENV); env['COPPER68K_SYNTHETIC_REPORT_DIR'] = str(root)
            with (root/'execution.log').open('w', encoding='utf-8') as log: result = subprocess.run(command(root, variant), env=env, stdout=log, stderr=subprocess.STDOUT)
            outputs = ['execution.log', 'audit.trx', 'build/bin/Copper68k/release/Copper68k.dll', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
            save(root/'execution.json', dict(command=command(root, variant), settings=ENV, exit=result.returncode, sources={n: hashlib.sha256(v).hexdigest() for n, v in data.items()}, outputs={n: sha(root/n) for n in outputs}))
        entry = verify(root, variant, data, retained, parent_names); entries.append(entry); print(variant, entry['counts'], 'retention', entry['retentionCases'], flush=True)
    proof = dict(schema=1, inputsSha256=sha(output/'inputs.json'), entries=entries, sourceCount=238, productionImport=False, publication=False, hardwareQualified=False, architecturalPromotion=False, roadmapComplete=False)
    if validate: require(load(output/'proof.json') == proof, 'Changed complete frame-fault proof')
    else: save(output/'proof.json', proof)
    print('Complete private frame-fault audit', sha(output/'proof.json'), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--qualified-parent-directory', type=Path, required=True); parser.add_argument('--output', type=Path, required=True); parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(); audit(args.qualified_parent_directory.resolve(), args.output.resolve(), args.validate_only)
