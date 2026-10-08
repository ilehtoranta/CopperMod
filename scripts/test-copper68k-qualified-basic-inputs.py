"""Verify the composed broad reference inputs; never promote a failing CPU audit."""
import argparse
import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

PATCHES = {'trap-bounds-pc.patch': {'sha256': '2ab0b6724978492623faa188cb23b1e9de914924209f4fc04b8acdcb59d07e73',
                          'available': 2,
                          'applied': 2,
                          'copySha256': 'ca94d93ec49447e853769bb93bd4e823fe313854aba59601161b35ef8f7bcca4'},
 'breakpoint-pc.patch': {'sha256': 'cf891c3a0d0e0461dbb7acaec7d77ee180cf115da40d3eb01c28182f6d8a8fc1',
                         'available': 1,
                         'applied': 1,
                         'copySha256': '27b0fb21a7fadbe0bf92ae42074ceaf665d7c19a83efdc841bc7730befc87a7b'},
 'long-arithmetic-unimplemented.patch': {'sha256': '77bae53d564a288f8d1facf1393250a3438ba74f5a09648d3c071bb94ee3ed84',
                                         'available': 3,
                                         'applied': 3,
                                         'copySha256': '75a9e8d7ce2f534d2b16910dffe436e9d8396b3f470628bc59de9852898e0733'},
 'word-division-carry.patch': {'sha256': 'b999782fc243fd9387bd83499b9a7b3754189505eafaad9be4e1056be0f036b2',
                               'available': 1,
                               'applied': 1,
                               'copySha256': '7eb90f09aaad56d164d0eafb9f66ddfc31becde9951a832820d5fdcc7973a83f'},
 'lpstop-fetch-pc.patch': {'sha256': '0ab8eb3139c1343ff52a1aa7ed0564a0970ca1b023c1bee1b28dcf94c785b900',
                           'available': 2,
                           'applied': 2,
                           'copySha256': '64cb8ff58d5a3a4fb4f217e3327fb1748d73f85521e3c9be98af7fd08f3a47bf'},
 'cas-unimplemented-pc.patch': {'sha256': '26533e846f04fa6573dbdbf485a471b63befcfcd36ad1a817a62779d7cce9936',
                                'available': 1,
                                'applied': 1,
                                'copySha256': 'a27200bdbac2441b1b63651f02590d2894eadc3257df54097fd8a50a5c0d8646'},
 'cas2-compare-alias.patch': {'sha256': '772a5da413359e5259af2fcdea735290ca0b82904499ee161bad5225c9a79ce3',
                              'available': 2,
                              'applied': 2,
                              'copySha256': '134b61047dfafb91f38374b5151b940ada98dee32bfec251b6a6855043092888'},
 'cas-encodings.patch': {'sha256': '62b929e03ddff6b9af55acb950254b67d646178bebf0572c82a546b54a1604ae',
                         'available': 1,
                         'applied': 1,
                         'copySha256': 'cb45000b18f7d4a11dcb0fec7130202c7918ac7fd42c9940effa248b1991fba2'},
 'moves-encodings.patch': {'sha256': '41460b8323d8ac905e740f5615be1b8b12eeea5f16523aba2ea18a107ef4fec8',
                           'available': 1,
                           'applied': 1,
                           'copySha256': 'e79367079bbbbe4fda3b326cf7c6a5cf15e08f021cc3137b1fc3b3aeee756368'},
 'long-arithmetic-encodings.patch': {'sha256': 'fab3a88d0e3183f9a57d340c7e50e762dc41d218d390365570a4d47114db66ff',
                                     'available': 1,
                                     'applied': 1,
                                     'copySha256': '44e8dacde5a11e9cbd2e15119362a09b35c2e109fbbdbecbefa92929e89b32ea'},
 'cas2-overlap-inputs.patch': {'sha256': 'c0bf3115542163e61683640ee3a0a5936209f555039ec71e6496d8a656ff0602',
                               'available': 1,
                               'applied': 1,
                               'copySha256': 'dbf0c78f5b88bf2f2ddc0d656a52bca190ec6b970b9e3c6b82cc48d5eac5fc0d'},
 'move16-encodings.patch': {'sha256': '8903787103e428543e189cb2f7f46d33636ceeeb6c41424385d59b61588c54be',
                            'available': 5,
                            'applied': 5,
                            'copySha256': 'fe407db40dca22a0258a0a689133fa581588117d22ca631cb2c8e52a15932caa'},
 'cache-encodings.patch': {'sha256': '279f9596cf5d2cb802cdb4b7d93f6ff92342829cc0da5e5ca841c4d4ffad3934',
                           'available': 2,
                           'applied': 1,
                           'copySha256': 'ef393d99b50198f1c0139de12d890f1f2c3cc0675bee19d22259de698c68e32e'}}
ID_PRIORITY_PATCH = {'sha256': '5400a56d5fc4f73492a26f261208b5b25f6d909add545c07b85902a9685a58a4', 'copySha256': 'e646382b98fb38533f6411fe13b52eecb92e78dfa169b88137b9a54da86e6461', 'available': 1, 'applied': 1}

GENERATOR_PIN = '025b999239800357e95065fe5b9a15ea5b300fa7'
RUNNER_PIN = '7a83745d6c6159bc74ab0471578ffc8bc244e66e'
MODELS = ['68000', '68010', '68EC020', '68020', '68030', '68040', '68060']


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def apply(text, patch, spec):
    hunks = patch.split('\n@@\n')[1:]
    require(len(hunks) == spec['available'], 'Changed patch hunk count')
    for hunk in hunks[:spec['applied']]:
        lines = hunk.rstrip('\n').split('\n')
        before = '\n'.join(s[1:] for s in lines if s.startswith(('-', ' ')))
        after = '\n'.join(s[1:] for s in lines if s.startswith(('+', ' ')))
        require(text.count(before) == 1, 'Changed patch anchor')
        text = text.replace(before, after)
    return text


def validate(root, basic, generator):
    m = load(root / 'manifest.json')
    b = load(basic / 'manifest.json')
    require(sha(basic / 'manifest.json') == 'b5741ca90fa75d3510ef25bac33d2d36d6a92ab2283f0666e762a5f1853a38ed', 'Changed raw Basic identity')
    require(m['Preset'] == 'QualifiedBasic' and b['Preset'] == 'Basic', 'Wrong broad preset')
    require(m['GeneratorCommit'] == b['GeneratorCommit'] == GENERATOR_PIN and
            m['RunnerCommit'] == b['RunnerCommit'] == RUNNER_PIN, 'Changed reference pins')
    revision = m.get('QualifiedBasicRevision', 1)
    require(revision in [1, 2], 'Unknown broad reference revision')
    patches = dict(PATCHES)
    if revision == 2:
        patches['coprocessor-id-priority.patch'] = ID_PRIORITY_PATCH
    actual = m['QualifiedBasicPatches']
    require(len(actual) == len(patches) and [p['Name'] for p in actual] == list(patches), 'Missing or reordered patches')
    sources = {}
    for file in ['gencpu.cpp', 'cputest.cpp']:
        sources[file] = subprocess.check_output(['git', '-C', str(generator), 'show',
                                               GENERATOR_PIN + ':gencpu/' + file]).decode('utf-8').replace('\r\n', '\n')
    for item, (name, spec) in zip(actual, patches.items()):
        path = root / name
        require(sha(path) == spec['copySha256'] and item['Sha256'] in
                {spec['sha256'], spec['copySha256'], hashlib.sha256(path.read_text(encoding='utf-8').replace('\n', '\r\n').encode('utf-8')).hexdigest()} and item['AppliedHunks'] == spec['applied']
                and item['AvailableHunks'] == spec['available'], 'Changed patch identity or selected hunks')
        file = 'gencpu.cpp' if name in list(PATCHES)[:7] else 'cputest.cpp'
        sources[file] = apply(sources[file], path.read_text(encoding='utf-8').replace('\r\n', '\n'), spec)
    for original, generated, field in [
        ('gencpu.cpp', 'gencpu-qualified-basic.cpp', 'QualifiedBasicCpuSourceSha256'),
        ('cputest.cpp', 'cputest-qualified-basic.cpp', 'QualifiedBasicInputSourceSha256')]:
        path = root / generated
        require(path.read_bytes() == sources[original].encode('utf-8') and sha(path) == m[field], 'Unreviewed source composition')
    require(sha(root / 'm68k_cpu_tester.dll') == m['NativeLibrarySha256'], 'Changed native assembly')
    for file, field in [('winuae-native.c', 'NativeSourceSha256'), ('integer_validation.h', 'IntegerValidationSha256')]:
        require(sha(root / file) == m[field] and sha(basic / file) == b[field], 'Changed comparator source binding')
        # Older restored inputs use LF; this Windows checkout uses CRLF. Bind
        # both exact inventories and allow only that textual transport change.
        require((root / file).read_text(encoding='utf-8') == (basic / file).read_text(encoding='utf-8'), 'Changed comparison authority')
    require([p['Id'] for p in m['Profiles']] == MODELS, 'Missing broad profile')
    for p in m['Profiles']:
        original = next(x for x in b['Profiles'] if x['Id'] == p['Id'])
        require(all(p[k] == original[k] for k in ['CpuDirectory', 'CpuLevel', 'AddressBits', 'Opcodes']), 'Filtered or incompatible broad profile')
        directory = root / p['Id']
        require(sorted(x.relative_to(directory).as_posix() for x in directory.rglob('*.dat')) == sorted(x['Path'] for x in p['Inputs']), 'Changed input inventory')
        require(p['Inputs'] and len({x['Path'] for x in p['Inputs']}) == len(p['Inputs']), 'Empty or duplicate inputs')
        for item in p['Inputs']:
            path = directory / item['Path']
            require(path.stat().st_size == item['Bytes'] and sha(path) == item['Sha256'], 'Changed generated fixture')
        config = (directory / 'cputestgen.ini').read_text(encoding='utf-8').replace('[test=QualifiedBasic]', '[test=Basic]')
        original_config = (basic / p['Id'] / 'cputestgen.ini').read_text(encoding='utf-8')
        normalize = lambda x: re.sub(r'(?m)^path=.*$', 'path=<fresh-output>/', x)
        require(normalize(config) == normalize(original_config), 'Changed broad generator selection')
    return {'compositionVerified': True, 'profiles': MODELS, 'patches': len(patches), 'revision': revision,
            'manifestSha256': sha(root / 'manifest.json'), 'rawBasicManifestSha256': sha(basic / 'manifest.json'),
            'directoryCount': sum(len(p['Opcodes']) for p in m['Profiles']),
            'architecturalPromotion': False, 'roadmapComplete': False}


def validate_execution(audit, raw_audit, root):
    manifest = load(root / 'manifest.json')
    inputs = load(audit / 'inputs.json')
    execution = load(audit / 'execution.json')
    raw_inputs = load(raw_audit / 'inputs.json')
    require(sha(raw_audit / 'inputs.json') == '9386f2a388943515f85cc5182cfbf285ae086c54ce6dee96fd13437576502f88', 'Changed raw execution identity')
    require(inputs['sources'] == raw_inputs['sources'] and len(inputs['sources']) == 210,
            'Changed production source selection')
    require(inputs['sourceFullProofSha256'] == raw_inputs['sourceFullProofSha256'] ==
            'a17fb76bcca015c9cebfc86072bc7c6ff977a69070295f79bc765589bdd43c4b', 'Changed integration identity')
    actual_sources = {p.relative_to(audit / 'source').as_posix(): sha(p) for p in (audit / 'source').rglob('*')
                      if p.suffix in ['.cs', '.csproj'] and not any(x in ['bin', 'obj'] for x in p.parts)}
    require(actual_sources == inputs['sources'], 'Changed production source bytes')
    models = MODELS + ['A1200']
    settings = {'COPPER68K_RUN_WINUAE_MODEL_AUDIT': '1', 'COPPER68K_WINUAE_MODEL_PATH': str(root),
                'COPPER68K_WINUAE_CPUTEST_LIBRARY': str(root / 'm68k_cpu_tester.dll'),
                'COPPER68K_SYNTHETIC_MODELS': ','.join(models), 'COPPER68K_SYNTHETIC_REPORT_DIR': str(audit)}
    require(inputs['settings'] == settings, 'Changed broad audit settings')
    command = ['dotnet', 'test', str(audit / 'source/Copper68k.Tests/Copper68k.Tests.csproj'), '-c', 'Release',
               '--artifacts-path', str(audit / 'build'), '--filter',
               'FullyQualifiedName~WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled', '--logger',
               'trx;LogFileName=audit.trx', '--results-directory', str(audit)]
    require(inputs['command'] == command, 'Changed broad execution command')
    require(inputs['manifestSha256'] == sha(root / 'manifest.json') and inputs['nativeSha256'] == manifest['NativeLibrarySha256'], 'Changed audit inputs')
    require(inputs['inputFiles'] == {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*.dat')}, 'Changed executed fixtures')
    outputs = ['execution.log', 'audit.trx', 'winuae-model-audit.json',
               'build/bin/Copper68k/release/Copper68k.dll', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll']
    require(execution['inputsSha256'] == sha(audit / 'inputs.json') and
            execution['outputs'] == {p: sha(audit / p) for p in outputs}, 'Changed execution evidence')
    require(all(execution[k] is False for k in ['productionImport', 'publication', 'roadmapComplete']), 'Unexpected promotion')
    report = load(audit / 'winuae-model-audit.json')
    require(report['schema'] == 2 and report['selectedModels'] == models and
            report['GeneratorCommit'] == GENERATOR_PIN and report['RunnerCommit'] == RUNNER_PIN and
            report['NativeLibrarySha256'] == manifest['NativeLibrarySha256'] and report['manifestSha256'] == inputs['manifestSha256'], 'Changed report identity')
    require(report['cpuAssemblySha256'] == execution['outputs'][outputs[3]] and
            report['adapterAssemblySha256'] == execution['outputs'][outputs[4]], 'Changed report assemblies')
    roster = [(m, family) for m in models for family in next(p for p in manifest['Profiles']
              if p['Id'] == ('68EC020' if m == 'A1200' else m))['Opcodes']]
    rows = report['rows']
    require([(r['Model'], r['Opcode']) for r in rows] == roster and len(rows) == 1381, 'Incomplete broad execution roster')
    require([(p['Model'], p['Kind']) for p in report['probes']] == [(m, k) for m in models for k in
            ['register', 'defined-sr', 'exception-frame', 'undefined-sr-ignored']] and
            all(p['Detected'] and p['ExecutedCases'] > 0 for p in report['probes']), 'Lost comparator controls')
    counts = {status: sum(r['Status'] == status for r in rows) for status in ['passing', 'mismatching', 'unsupported', 'untested']}
    require(all(report[k] == v for k, v in counts.items()) and sum(counts.values()) == len(rows), 'Changed broad counts')
    for key, field in [('executedCases', 'ExecutedCases'), ('exceptionFrames', 'ExceptionFrames'),
                       ('maskedSrCases', 'MaskedSrCases'), ('terminalCases', 'TerminalCases')]:
        require(report[key] == sum(r[field] for r in rows), 'Changed execution totals')
    passing = counts['passing'] == len(rows)
    require(execution['exit'] == (0 if passing else 1), 'Changed audit exit')
    trx = ET.parse(audit / 'audit.trx').getroot()
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    results = trx.findall('.//t:UnitTestResult', ns)
    require(len(results) == 1 and results[0].get('outcome') == ('Passed' if passing else 'Failed') and
            'WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled' in results[0].get('testName', ''), 'Changed executed test')
    return {'counts': counts, 'callbacks': report['executedCases'], 'exceptionFrames': report['exceptionFrames'],
            'comparatorControls': 32, 'auditGatePassed': passing, 'reportSha256': sha(audit / 'winuae-model-audit.json'),
            'executionSha256': sha(audit / 'execution.json'), 'sourcesUnchanged': True,
            'firstFailures': [{'model': r['Model'], 'family': r['Opcode'], 'callbacks': r['ExecutedCases'],
                               'detailSha256': hashlib.sha256(r['Detail'].encode('utf-8')).hexdigest(),
                               'detail': r['Detail'], 'laterFailuresUnobserved': True} for r in rows if r['Status'] != 'passing']}


def main():
    a = argparse.ArgumentParser(description=__doc__)
    a.add_argument('--input-directory', type=Path, required=True)
    a.add_argument('--raw-basic-directory', type=Path, required=True)
    a.add_argument('--generator-source', type=Path, required=True)
    a.add_argument('--output', type=Path, required=True)
    a.add_argument('--audit-directory', type=Path)
    a.add_argument('--raw-audit-directory', type=Path)
    args = a.parse_args()
    require(not args.output.exists(), 'Use a fresh verification output')
    result = validate(args.input_directory, args.raw_basic_directory, args.generator_source)
    if args.audit_directory:
        require(args.raw_audit_directory is not None, 'Missing raw source evidence')
        result['execution'] = validate_execution(args.audit_directory, args.raw_audit_directory, args.input_directory)
    result['validatorSha256'] = sha(Path(__file__))
    args.output.mkdir(parents=True)
    (args.output / 'verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print('Broad composition verified; architectural promotion remains false')
    if args.audit_directory and not result['execution']['auditGatePassed']:
        print('Broad CPU audit remains failed:', result['execution']['counts'])
        sys.exit(1)


if __name__ == '__main__':
    main()
