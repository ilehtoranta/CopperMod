"""Verify frozen MOVEM mask fault programs, not xUnit counts alone."""
from pathlib import Path
import argparse, hashlib, json, re, subprocess, xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--repo', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--baseline', type=Path)
parser.add_argument('--producer', type=Path, required=True)
args = parser.parse_args()
root = args.output.resolve()
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

inputs = load(root / 'inputs.json')
execution = load(root / 'execution.json')
assert execution['exit'] == 0 and execution['inputsSha256'] == sha(root / 'inputs.json')
assert not inputs['publication'] and not inputs['privateCandidateImport']
assert inputs['producerSha256'] == sha(args.producer)
sources = inputs['sources']
tracked = subprocess.check_output(['git', 'ls-files', 'Copper68k/*.cs', 'Copper68k/*.csproj',
                                  'Copper68k.Tests/*.cs', 'Copper68k.Tests/*.csproj'],
                                 cwd=args.repo, text=True).splitlines()
expected_sources = set(tracked) | {'Copper68k.Tests/Synthetic/SyntheticM68040MovemMaskFaultTests.cs'}
assert set(sources) == expected_sources and sources
for name, digest in sources.items():
    assert sha(args.repo / name) == sha(root / 'source' / name) == digest, name
for item in inputs.get('protected', []):
    assert sha(args.repo / item['path']) == item['sha256'], 'Normal output changed'
settings = {'COPPER68K_SYNTHETIC_REPORT_DIR': str(root),
            'COPPER68K_RUN_040_MOVEM_MASK_FAULT_AUDIT': '1',
            'COPPER68K_RUN_040_MOVEM_WRITE_RECOVERY': '1'}
assert inputs['settings'] == settings
selection = ('FullyQualifiedName~SyntheticM68040MovemMaskFaultTests|'
             'FullyQualifiedName~SyntheticM68040MovemWriteRecoveryTests|'
             'FullyQualifiedName~FixedManualStoreEncodings|'
             'FullyQualifiedName~CanonicalStoreFixturesExecuteWithoutFault')
assert inputs['command'] == ['dotnet', 'test', str(root / 'source/Copper68k.Tests/Copper68k.Tests.csproj'),
                             '-c', 'Release', '--artifacts-path', str(root / 'build'), '--filter', selection,
                             '--logger', 'trx;LogFileName=audit.trx', '--results-directory', str(root)]
for key, path in [('logSha256', 'execution.log'), ('trxSha256', 'audit.trx'),
                  ('cpuSha256', 'build/bin/Copper68k/release/Copper68k.dll'),
                  ('testSha256', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')]:
    assert execution[key] == sha(root / path), key

prefix = 'Copper68k.Tests.Synthetic.'
names = {
    prefix + 'SyntheticM68040MovemMaskFaultTests.EveryMaskWordScalar',
    prefix + 'SyntheticM68040MovemMaskFaultTests.EveryMaskWordBatch',
    prefix + 'SyntheticM68040MovemWriteRecoveryTests.ScalarRecoveryMatrix',
    prefix + 'SyntheticM68040MovemWriteRecoveryTests.BatchRecoveryMatrix',
    prefix + 'SyntheticM68040MovemWriteFaultDiscoveryTests.FixedManualStoreEncodings'}
for batch in ('False', 'True'):
    for cls, method in [('SyntheticM68040MovemMaskFaultTests', 'SelectedMasksRecoverEveryTransferWithoutLosingRegisterOrder'),
                        ('SyntheticM68040MovemWriteRecoveryTests', 'EveryCanonicalStoreFormRunsHandlerAndResumes'),
                        ('SyntheticM68040MovemWriteRecoveryTests', 'SelfOverwrittenIndirectPointerIsNotResolvedAgain'),
                        ('SyntheticM68040MovemWriteFaultDiscoveryTests', 'CanonicalStoreFixturesExecuteWithoutFault')]:
        names.add(f'{prefix}{cls}.{method}(batch: {batch})')
tree = ET.parse(root / 'audit.trx')
rows = tree.findall('.//t:UnitTestResult', ns)
assert len(rows) == len(names) == 13 and {r.get('testName') for r in rows} == names
assert len({r.get('testId') for r in rows}) == 13 and all(r.get('outcome') == 'Passed' for r in rows)
counters = tree.find('.//t:Counters', ns)
assert all(int(counters.get(k)) == 13 for k in ('total', 'executed', 'passed'))
assert int(counters.get('failed')) == 0
assert tree.find('.//t:ResultSummary', ns).get('outcome') == 'Completed'
summaries = {}
for row in rows:
    output = row.find('t:Output/t:StdOut', ns)
    for match in re.finditer(r'68040/([^:\r\n]+): (\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=(\d+), combinations=(\d+)',
                            output.text or '' if output is not None else ''):
        assert match[1] not in summaries
        summaries[match[1]] = (json.loads(match[2]), int(match[3]), int(match[4]), int(match[5]))

# This independent inventory uses integer bit-counts, not the C# fixture's
# selected-register sequence, encoder or production decoder.
controls = {0, 0xffff, 0xff, 0xff00, 0x5555, 0xaaaa, 0x303, 0x8001}
for bit in range(16):
    controls.update((1 << bit, 0xffff ^ (1 << bit)))
    if bit < 15:
        controls.add(3 << bit)
assert len(controls) == 55
inventories = {}
for cohort, group, masks in [('mask-controls', 'movem-mask-fault-controls', sorted(controls)),
                              ('all-masks', 'movem-mask-fault-all', range(65536))]:
    keys = {}
    for mask in masks:
        count = mask.bit_count()
        transfers = range(max(1, count)) if cohort == 'mask-controls' else [max(0, count - 1)]
        for mode in (2, 4):
            for width in (2, 4):
                for transfer in transfers:
                    key = (f'68040/MOVEM/write-fault/{cohort}/size={width}/mode={mode}/reg=2/'
                           f'mask={mask:04X}/bank=ISP/T=0000/transfer={transfer}/byte={width-1}')
                    assert key not in keys
                    keys[key] = 1
    assert len(keys) == (1364 if cohort == 'mask-controls' else 262144)
    for route in ('scalar', 'batch'):
        inventories[f'{group}-{route}'] = keys

forms = [f'mode={mode}/reg={reg}' for mode in (2, 4, 5, 6, 7)
         for reg in range(2 if mode == 7 else 8)]
canonical = ['mode=2/reg=0', 'mode=4/reg=0', 'mode=4/reg=7', 'mode=5/reg=0',
             'mode=6/reg=0', 'mode=7/reg=0', 'mode=7/reg=1']
full = []
for bs in ('False', 'True'):
    for ix_suppressed in ('False', 'True'):
        for bd in (1, 2, 3):
            for iis in (0, 1, 2, 3, 5, 6, 7):
                if ix_suppressed == 'True' and iis >= 5:
                    continue
                for index in ('D1/W/scale=1', 'D1/L/scale=1', 'D1/W/scale=8', 'A0/W/scale=4'):
                    full.append(f'mode=6/reg=1/index=full/bs={bs}/is={ix_suppressed}/bd={bd}/iis={iis}/ix={index}/pointer-alias=False')
assert len(forms) == 34 and len(full) == 264

def add(keys, cohort, operands, banks, traces, weight, witness):
    for form in operands:
        for bank in banks:
            for trace in traces:
                for width in (2, 4):
                    for transfer in ([3] if witness else range(4)):
                        for byte in ([width - 1] if witness else range(width)):
                            key = f'68040/MOVEM/write-fault/{cohort}/size={width}/{form}/bank={bank}/T={trace}/transfer={transfer}/byte={byte}'
                            assert key not in keys
                            keys[key] = weight

required, witness, alias, fault_free = {}, {}, {}, {}
add(required, 'recovery-opcode', forms, ['ISP'], ['0000'], 1, False)
add(required, 'recovery-status', canonical, ['user', 'user-M', 'ISP', 'MSP'], ['0000', '8000', '4000'], 32, False)
add(required, 'recovery-structure', full, ['ISP'], ['0000'], 1, False)
add(witness, 'recovery-witness', forms, ['ISP'], ['0000'], 1, True)
add(alias, 'pointer-alias', ['mode=6/reg=0/index=full/bs=False/is=False/bd=2/iis=1/ix=D1/L/scale=1/pointer-alias=True'],
    ['user', 'user-M', 'ISP', 'MSP'], ['0000', '8000', '4000'], 1, True)
add(fault_free, 'fixture', forms, ['ISP'], ['0000'], 1, True)
# Fault-free controls choose byte zero; they do not arm a rejection.
fault_free = {k.rsplit('/byte=', 1)[0] + '/byte=0': v for k, v in fault_free.items()}
for group, keys in [('movem-write-recovery', required), ('movem-write-recovery-witness', witness),
                    ('movem-write-pointer-alias', alias), ('movem-write-fixture-control', fault_free)]:
    for route in ('scalar', 'batch'):
        inventories[f'{group}-{route}'] = keys
reports = {p.name for p in root.glob('68040-*.json')}
assert reports == {f'68040-{g}.json' for g in inventories} and set(summaries) == set(inventories)
records = []
for group, keys in inventories.items():
    path = root / f'68040-{group}.json'
    report = load(path)
    count = sum(keys.values())
    assert report['schema'] == 1 and report['model'] == '68040' and report['group'] == group
    assert report['logicalCases'] == count and report['xunitBatches'] == 1 and not report['failures']
    expected_counts = dict(passing=count, mismatching=0, unsupported=0, untested=0)
    assert report['counts'] == expected_counts and set(report['combinations']) == set(keys)
    assert all(report['combinations'][k] == {'passing': weight} for k, weight in keys.items())
    assert summaries[group] == (expected_counts, count, 1, len(keys))
    if args.baseline and group.startswith(('movem-write-recovery', 'movem-write-pointer-alias')):
        assert report == load(args.baseline / path.name), ('retained report changed', group)
    records.append(dict(path=path.name,sha256=sha(path),logicalCases=count,combinations=len(keys)))
assert sum(r['logicalCases'] for r in records) == 670664
proof = dict(schema=1,executions=13,logicalCases=670664,reports=records,
             inputSha256=sha(root/'inputs.json'),executionSha256=sha(root/'execution.json'),
             verifierSha256=sha(Path(__file__)),retainedBaseline=str(args.baseline) if args.baseline else None,
             publication=False,privateCandidateImport=False,hardwareQualified=False,roadmapComplete=False)
serialized = json.dumps(proof,indent=2)+'\n'
path = root/'proof.json'
if path.exists():
    assert path.read_text(encoding='utf-8') == serialized, 'Existing proof differs; preserve it'
else:
    path.write_text(serialized,encoding='utf-8')
print('MOVEM mask fault qualification verified:', sha(path), '670664 programs / 13 executions / 12 reports')
