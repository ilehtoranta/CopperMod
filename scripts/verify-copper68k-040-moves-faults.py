"""Require complete, frozen MOVES fault evidence; mismatches keep the gate failed."""
from pathlib import Path
import argparse, hashlib, json, subprocess, xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
p.add_argument('--repo', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--producer', type=Path, required=True)
a = p.parse_args()
r = a.output.resolve()

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

i, e = load(r / 'inputs.json'), load(r / 'execution.json')
assert not i['publication'] and not i['privateCandidateImport']
assert i['producerSha256'] == sha(a.producer), 'Changed producer'
assert e['inputsSha256'] == sha(r / 'inputs.json'), 'Changed inputs'
names = set(subprocess.check_output(['git', 'ls-files', 'Copper68k/*.cs', 'Copper68k/*.csproj',
    'Copper68k.Tests/*.cs', 'Copper68k.Tests/*.csproj'], cwd=a.repo, text=True).splitlines())
names.add('Copper68k.Tests/Synthetic/SyntheticM68040MovesFaultDiscoveryTests.cs')
assert names and set(i['sources']) == names, 'Incomplete source selection'
for name, digest in i['sources'].items():
    assert sha(a.repo / name) == sha(r / 'source' / name) == digest, 'Changed source: ' + name
for item in i.get('protected', []):
    assert sha(a.repo / item['path']) == item['sha256'], 'Normal output changed'
assert i['settings'] == {'COPPER68K_SYNTHETIC_REPORT_DIR': str(r), 'COPPER68K_RUN_040_MOVES_FAULT_DISCOVERY': '1'}
assert i['command'] == ['dotnet', 'test', str(r / 'source/Copper68k.Tests/Copper68k.Tests.csproj'),
    '-c', 'Release', '--artifacts-path', str(r / 'build'), '--filter',
    'FullyQualifiedName~SyntheticM68040MovesFaultDiscoveryTests', '--logger',
    'trx;LogFileName=audit.trx', '--results-directory', str(r)], 'Wrong execution command'
for key, name in [('logSha256', 'execution.log'), ('trxSha256', 'audit.trx'),
    ('cpuSha256', 'build/bin/Copper68k/release/Copper68k.dll'),
    ('testSha256', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')]:
    assert e[key] == sha(r / name), 'Changed ' + key

ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tree = ET.parse(r / 'audit.trx')
rows = tree.findall('.//t:UnitTestResult', ns)
prefix = 'Copper68k.Tests.Synthetic.SyntheticM68040MovesFaultDiscoveryTests.'
expected_tests = {prefix + 'ScalarMovesFaultsRequireFormat7AndConvertedFunctionCodes',
    prefix + 'BatchMovesFaultsRequireFormat7AndConvertedFunctionCodes'}
expected_tests.update(prefix + f'CanonicalMovesFixturesPreserveFlagsAndOtherFunctionCode(batch: {b})' for b in ('False', 'True'))
assert len(rows) == 4 and {x.attrib['testName'] for x in rows} == expected_tests, 'Missing/empty test selection'
assert len({x.attrib['testId'] for x in rows}) == 4 and len({x.attrib['executionId'] for x in rows}) == 4
assert all(x.attrib['outcome'] in ('Passed', 'Failed') for x in rows), 'Unavailable requested test'
definitions = {x.attrib['id']: x for x in tree.findall('.//t:UnitTest', ns)}
for row in rows:
    definition = definitions[row.attrib['testId']]
    assert Path(definition.attrib['storage']).resolve() == (r / 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll').resolve()
    assert definition.find('t:Execution', ns).attrib['id'] == row.attrib['executionId']
counters = tree.find('.//t:ResultSummary/t:Counters', ns).attrib
failed_tests = sum(x.attrib['outcome'] == 'Failed' for x in rows)
assert all(int(counters[k]) == v for k, v in {'total': 4, 'executed': 4, 'passed': 4-failed_tests,
    'failed': failed_tests, 'notExecuted': 0}.items()), 'TRX counters differ'
assert e['exit'] == (1 if failed_tests else 0), 'Recorded execution outcome differs'

reports = {}
total_failures = 0
for route in ('scalar', 'batch'):
    for fault in (False, True):
        group = 'moves-physical-fault-' + ('discovery-' if fault else 'fixture-') + route
        path = r / ('68040-' + group + '.json')
        report = load(path)
        assert report['schema'] == 1 and report['model'] == '68040' and report['group'] == group
        expected = set()
        opcodes = {1: '0E10', 2: '0E50', 4: '0E90'}
        full_ids = {}
        for width in (1, 2, 4):
            for lane in range(4):
                for fc in range(8):
                    for direction in ('read', 'write'):
                        for bank in ('ISP', 'MSP'):
                            for ccr in ('00', '1F'):
                                for byte in range(width if fault else 1):
                                    key = (f'68040/MOVES/physical-{"fault" if fault else "fixture"}/route={route}'
                                        f'/size={width}/lane={lane}/FC={fc}/direction={direction}/bank={bank}/ccr={ccr}/byte={byte}')
                                    expected.add(key); full_ids[key + '/op=' + opcodes[width]] = key
        assert len(expected) == (1792 if fault else 768)
        assert set(report['combinations']) == expected, 'Missing/extra architectural combination'
        counts = dict.fromkeys(('passing', 'mismatching', 'unsupported', 'untested'), 0)
        failing = set()
        for key, statuses in report['combinations'].items():
            assert set(statuses) <= set(counts) and sum(statuses.values()) == 1, 'Wrong combination weight/status'
            assert all(isinstance(v, int) and v > 0 for v in statuses.values())
            for status, value in statuses.items():
                counts[status] += value
                if status != 'passing': failing.add(key)
        assert counts == report['counts'] and sum(counts.values()) == report['logicalCases'] == len(expected), 'Report counts differ'
        assert report['xunitBatches'] == 1
        failures = report['failures']
        assert len(failures) == len(failing) and len({x['id'] for x in failures}) == len(failing)
        assert {full_ids[x['id']] for x in failures} == failing
        assert all(x['reason'] and x['status'] != 'passing' and
            report['combinations'][full_ids[x['id']]].get(x['status']) == 1 for x in failures)
        method = (('Scalar' if route == 'scalar' else 'Batch') + 'MovesFaultsRequireFormat7AndConvertedFunctionCodes'
            if fault else f'CanonicalMovesFixturesPreserveFlagsAndOtherFunctionCode(batch: {"False" if route == "scalar" else "True"})')
        row = next(x for x in rows if x.attrib['testName'] == prefix + method)
        assert row.attrib['outcome'] == ('Failed' if failing else 'Passed'), 'Report/TRX outcome differs'
        total_failures += len(failing)
        reports[path.name] = {'sha256': sha(path), 'counts': counts, 'logicalCases': len(expected)}
assert {x.name for x in r.glob('68040-moves-physical-fault-*.json')} == set(reports)
proof = {'schema': 1, 'status': 'failed' if total_failures else 'passing',
    'inputsSha256': sha(r / 'inputs.json'), 'executionSha256': sha(r / 'execution.json'),
    'verifierSha256': sha(Path(__file__)), 'reports': reports, 'logicalCases': 5120,
    'mismatchingOrUnavailable': total_failures, 'savedPcQualification': False,
    'recoveryQualification': False, 'hardwareQualification': False,
    'publication': False, 'privateCandidateImport': False, 'roadmapComplete': False}
target = r / 'verification.json'
if target.exists():
    assert load(target) == proof, 'Existing proof differs; preserve it'
else:
    target.write_text(json.dumps(proof, indent=2)+'\n', encoding='utf-8')
print(f'MOVES fault gate: {proof["status"]}; 5120 cases, {total_failures} mismatching/unavailable')
raise SystemExit(1 if total_failures else 0)
