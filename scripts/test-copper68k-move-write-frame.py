"""Qualify cold/relocated private pending-write images and rejection atomicity.

Uses an immutable pipe-qualified parent and unchanged private CPU bytes. This is
a software-contract audit, not hardware frame qualification or production import.
"""
import argparse, contextlib, hashlib, importlib.util, io, itertools, json, os, re, subprocess
from pathlib import Path
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
HELPER = REPO / 'scripts/test-copper68k-move-write-pipe.py'
FIXTURE = 'Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteFrameTests.cs'
spec = importlib.util.spec_from_file_location('write_pipe', HELPER)
p = importlib.util.module_from_spec(spec); spec.loader.exec_module(p)
sha, load, save, require, inventory = p.sha, p.load, p.save, p.require, p.inventory
PARENT_PROOF = 'dcb7ea91da3d88b0b35d84c01f159e16191c0eed4c78707d65e4e3239c8fdca7'
VARIANTS = ['candidate', 'reserved', 'stack-pop', 'flags']
MODELS = ['68EC020', '68020', '68030', 'A1200']
INVALID = ['foreign-zero', 'foreign-read', 'version-zero', 'version-two', 'count-four', 'reserved', 'read-cycle', 'wrong-size', 'bad-fc', 'trace-t1', 'trace-t0', 'odd-pc', 'bad-source', 'bad-destination']
FILTER = 'FullyQualifiedName~SyntheticM68020MoveWriteFrameTests'
ENV = {'COPPER68K_RUN_020_MOVE_WRITE_FRAME': '1'}
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def sources(parent, variant):
    data = {n: (parent/'candidate/source'/n).read_bytes() for n in inventory(parent/'candidate/source')}
    require(len(data) == 236 and FIXTURE not in data, 'Missing or changed cold-frame parent inventory')
    data[FIXTURE] = (REPO/FIXTURE).read_bytes()
    text = data[p.CPU].decode('utf-8').replace('\r\n', '\n')
    if variant == 'reserved':
        text = p.once(text, 'reserved != 0 ||', 'false ||')
    elif variant == 'stack-pop':
        text = p.once(text, 'State.SetActiveStackPointer(unchecked(frame + 32));', 'State.SetActiveStackPointer(unchecked(frame + 34));')
    elif variant == 'flags':
        text = p.once(text, 'CompleteTiming(M68kInstructionTimingKey.Rte); _instructionPipe = pipe;', 'CompleteTiming(M68kInstructionTimingKey.Rte); _instructionPipe = pipe; SetMoveFlags(value, size);')
    if variant != 'candidate': data[p.CPU] = text.encode('utf-8')
    return data


def command(root):
    return ['dotnet', 'test', str(root/'source/Copper68k.Tests/Copper68k.Tests.csproj'), '-c', 'Release', '--artifacts-path', str(root/'build'), '--filter', FILTER, '--logger', 'trx;LogFileName=audit.trx', '--results-directory', str(root)]


def cases(model, width, current, lane, variant):
    invalid = lane in INVALID
    rows = {}
    for returned, location, count, ccr in itertools.product(['user', 'user-M', 'ISP', 'MSP'], range(3), [3] if invalid else range(4), [0, 31] if invalid else range(32)):
        bad = (variant == 'reserved' and lane == 'reserved' or
               variant == 'stack-pop' and not invalid or
               variant == 'flags' and not invalid and ccr & 15 != 8)
        key = f'{model}/MOVE/write-frame/width={width}/current={current}/returned={returned}/location={location}/count={count}/ccr={ccr}/lane={lane}'
        rows[key] = {'mismatching' if bad else 'passing': 1}
    return rows


def verify(root, variant, data):
    record = load(root/'execution.json')
    ids = {n: hashlib.sha256(v).hexdigest() for n, v in data.items()}
    require(record['sources'] == ids == inventory(root/'source'), 'Changed complete cold-frame sources')
    require(record['command'] == command(root) and record['settings'] == ENV and record['exit'] == (0 if variant == 'candidate' else 1), 'Wrong cold-frame command, settings or exit')
    outputs = ['execution.log', 'audit.trx', 'build/bin/Copper68k/release/Copper68k.dll', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(record['outputs'] == {n: sha(root/n) for n in outputs}, 'Changed cold-frame outputs')
    tree = ET.parse(root/'audit.trx'); tests = tree.findall('.//t:UnitTestResult', NS)
    names = {f'{route}{kind}WriteFrames': 'Failed' if (variant == 'reserved' and kind == 'Invalid' or variant in ['stack-pop', 'flags'] and kind == 'Cold') else 'Passed' for route, kind in itertools.product(['Scalar', 'Batch'], ['Cold', 'Invalid'])}
    require(len(tests) == 4 and {t.get('testName').split('.')[-1]: t.get('outcome') for t in tests} == names, 'Wrong cold-frame execution roster or outcomes')
    c = tree.find('.//t:Counters', NS); failed = sum(x == 'Failed' for x in names.values())
    require(int(c.get('total')) == 4 and int(c.get('executed')) == 4 and int(c.get('passed')) == 4-failed and int(c.get('failed')) == failed and int(c.get('notExecuted')) == 0, 'Wrong cold-frame counters')
    require(tree.find('.//t:ResultSummary', NS).get('outcome') == ('Completed' if variant == 'candidate' else 'Failed'), 'Wrong cold-frame run summary')
    stdout = '\n'.join(t.text or '' for t in tree.findall('.//t:UnitTestResult/t:Output/t:StdOut', NS))
    totals = dict(passing=0, mismatching=0, unsupported=0, untested=0); reports = []
    for model, width, current, lane, mode in itertools.product(MODELS, [1, 2, 4], ['ISP', 'MSP'], ['pending', 'completed']+INVALID, ['scalar', 'batch']):
        group = f'move-write-frame-{width}-{current}-{lane}-{mode}'
        path = root/f'{model}-{group}.json'; d = load(path); wanted = cases(model, width, current, lane, variant)
        counts = {s: sum(row.get(s, 0) for row in wanted.values()) for s in totals}
        require(d['schema'] == 1 and d['model'] == model and d['group'] == group and d['xunitBatches'] == 1 and d['logicalCases'] == len(wanted) and d['combinations'] == wanted and d['counts'] == counts, 'Wrong cold-frame keys, weights or results: '+path.name)
        bad = {k for k, v in wanted.items() if 'mismatching' in v}
        require(len(d['failures']) == len(bad) and {f['id'] for f in d['failures']} == bad and all(f['status'] == 'mismatching' and f['reason'] for f in d['failures']), 'Wrong complete cold-frame failure witnesses')
        if variant == 'reserved' and lane == 'reserved':
            require(all(f['reason'] == 'Malformed or foreign private write frame was accepted' for f in d['failures']), 'Wrong reserved-frame mutation cause')
        matches = re.findall(re.escape(model+'/'+group+': ')+r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)', stdout)
        require(len(matches) == 1 and json.loads(matches[0][0]) == counts and int(matches[0][1]) == len(wanted) and int(matches[0][2]) == len(wanted), 'Cold-frame report/TRX disagreement')
        for status in totals: totals[status] += counts[status]
        reports.append(dict(path=path.name, sha256=sha(path)))
    require({f.name for f in root.glob('*.json')} == {f['path'] for f in reports}|{'execution.json'}, 'Missing or foreign cold-frame reports')
    return dict(variant=variant, counts=totals, executions=4, reports=reports, executionSha256=sha(root/'execution.json'))


def audit(parent, output, validate):
    require(sha(parent/'proof.json') == PARENT_PROOF, 'Wrong qualified cold-frame parent proof')
    with contextlib.redirect_stdout(io.StringIO()): p.audit(parent.parent/'MemoryReadWriteV1', parent, True)
    linkage = dict(producers={f.relative_to(REPO).as_posix(): sha(f) for f in [Path(__file__), HELPER, REPO/FIXTURE]}, parentProofSha256=PARENT_PROOF, variants=VARIANTS, settings=ENV)
    if not validate: output.mkdir(parents=True, exist_ok=False); save(output/'inputs.json', linkage)
    require(load(output/'inputs.json') == linkage, 'Changed cold-frame producers, parent or selection')
    entries = []
    for variant in VARIANTS:
        root = output/variant; data = sources(parent, variant)
        if not validate:
            (root/'source').mkdir(parents=True)
            for n, value in data.items():
                target = root/'source'/n; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(value)
            env = {k: v for k, v in os.environ.items() if not k.startswith('COPPER68K_RUN_')}
            env.update(ENV); env['COPPER68K_SYNTHETIC_REPORT_DIR'] = str(root)
            with (root/'execution.log').open('w', encoding='utf-8') as log:
                result = subprocess.run(command(root), env=env, stdout=log, stderr=subprocess.STDOUT)
            outputs = ['execution.log', 'audit.trx', 'build/bin/Copper68k/release/Copper68k.dll', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
            save(root/'execution.json', dict(command=command(root), settings=ENV, exit=result.returncode, sources={n: hashlib.sha256(v).hexdigest() for n, v in data.items()}, outputs={n: sha(root/n) for n in outputs}))
        entry = verify(root, variant, data); entries.append(entry); print(variant, entry['counts'], flush=True)
    proof = dict(schema=1, inputsSha256=sha(output/'inputs.json'), entries=entries, sourceCount=237, retainedEvidenceParentSha256=PARENT_PROOF, retentionReexecuted=False, productionImport=False, publication=False, hardwareQualified=False, architecturalPromotion=False, roadmapComplete=False)
    if validate: require(load(output/'proof.json') == proof, 'Changed complete cold-frame proof')
    else: save(output/'proof.json', proof)
    print('Complete private cold-frame audit', sha(output/'proof.json'), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--qualified-parent-directory', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args(); audit(args.qualified_parent_directory.resolve(), args.output.resolve(), args.validate_only)
