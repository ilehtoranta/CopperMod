"""Validate the pinned first-failure review without promoting a failed Basic audit.

This checks an audited checkpoint, not arbitrary future executions. Qualified
counterparts are family evidence; later raw failures remain unobserved.
"""
import argparse, collections, hashlib, json, re
from pathlib import Path
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
LEDGER = REPO/'docs/COPPER68K_BASIC_REFERENCE_QUALIFICATION_LEDGER.json'
FULL_PROOF = 'a17fb76bcca015c9cebfc86072bc7c6ff977a69070295f79bc765589bdd43c4b'
MODELS = ['68000', '68010', '68EC020', '68020', '68030', '68040', '68060', 'A1200']
PINS = ('025b999239800357e95065fe5b9a15ea5b300fa7', '7a83745d6c6159bc74ab0471578ffc8bc244e66e')


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def load(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def require(ok, reason):
    if not ok: raise ValueError(reason)


def classification(model, family, words, status):
    if (model, family) in [('68010', 'RTE'), ('68060', 'STOP')]:
        return 'unresolved-architecture', None
    if family in ['DIVL.L', 'MULL.L'] and status == 'unsupported':
        require(words[1] & 0x83f8, 'Missing reserved long-arithmetic field witness')
        return 'reserved-or-undefined-input', 'long-arithmetic'
    if family.startswith('MOVES'):
        require(words[1] & 0x7ff, 'Missing reserved MOVES field witness')
        return 'reserved-or-undefined-input', 'moves'
    if family == 'MOVE16':
        require(words[:2] == [0xf626, 0x6000], 'Changed malformed MOVE16 witness')
        return 'reserved-or-undefined-input', 'move16'
    if family == 'BKPT': preset = 'breakpoint'
    elif family.startswith('CHK2') or family == 'TRAPcc': preset = 'trap-bounds'
    elif family == 'DIVU.W': preset = 'word-division'
    elif family in ['DIVL.L', 'MULL.L']: preset = 'long-arithmetic'
    elif family.startswith('CAS2'): preset = 'cas2'
    elif family.startswith('CAS'): preset = 'cas'
    elif family == 'LPSTOP': preset = 'lpstop'
    elif family == 'ILLEGAL':
        require(words[0] == 0xf400, 'Changed cache-scope-zero witness'); preset = 'cache-encodings'
    else: raise ValueError('Unreviewed Basic first failure: '+model+'/'+family)
    return 'qualified-reference-correction', preset


def validate(raw, qualified, ledger):
    l = load(ledger)
    require(l['schema'] == 1 and l['rawGatePassed'] is False and l['architecturalPromotion'] is False and l['roadmapComplete'] is False, 'Ledger promotes unresolved or failing coverage')
    require((l['generatorPin'], l['runnerPin']) == PINS and l['fullIntegrationProofSha256'] == FULL_PROOF == sha(qualified.parent/'proof.json'), 'Wrong pinned reference qualification')
    require(l['rawReportSha256'] == sha(raw/'winuae-model-audit.json') and l['rawInputRecordSha256'] == sha(raw/'inputs.json') and l['rawExecutionRecordSha256'] == sha(raw/'execution.json'), 'Changed raw Basic evidence')
    i = load(raw/'inputs.json'); e = load(raw/'execution.json'); d = load(raw/'winuae-model-audit.json')
    outputs = {'execution.log', 'audit.trx', 'winuae-model-audit.json', 'build/bin/Copper68k/release/Copper68k.dll', 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'}
    command = ['dotnet', 'test', str(raw/'source/Copper68k.Tests/Copper68k.Tests.csproj'), '-c', 'Release', '--artifacts-path', str(raw/'build'), '--filter', 'FullyQualifiedName~WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled', '--logger', 'trx;LogFileName=audit.trx', '--results-directory', str(raw)]
    require(i['command'] == command and e['exit'] == 1 and e['inputsSha256'] == sha(raw/'inputs.json') and set(e['outputs']) == outputs and all(sha(raw/n) == h for n, h in e['outputs'].items()), 'Wrong raw Basic execution or outputs')
    expected_settings = {'COPPER68K_RUN_WINUAE_MODEL_AUDIT': '1', 'COPPER68K_WINUAE_MODEL_PATH': i['settings']['COPPER68K_WINUAE_MODEL_PATH'], 'COPPER68K_WINUAE_CPUTEST_LIBRARY': i['settings']['COPPER68K_WINUAE_CPUTEST_LIBRARY'], 'COPPER68K_SYNTHETIC_MODELS': ','.join(MODELS), 'COPPER68K_SYNTHETIC_REPORT_DIR': str(raw)}
    require(i['settings'] == expected_settings, 'Changed complete Basic model selection')
    reference = Path(expected_settings['COPPER68K_WINUAE_MODEL_PATH']); native = Path(expected_settings['COPPER68K_WINUAE_CPUTEST_LIBRARY'])
    require(sha(reference/'manifest.json') == i['manifestSha256'] == l['rawManifestSha256'] == d['manifestSha256'] and sha(native) == i['nativeSha256'] == l['rawNativeSha256'] == d['NativeLibrarySha256'], 'Changed raw reference inputs')
    require(i['inputFiles'] and set(i['inputFiles']) == {p.relative_to(reference).as_posix() for p in reference.rglob('*.dat')} and all(sha(reference/n) == h for n, h in i['inputFiles'].items()), 'Missing or changed Basic input selection')
    def source_names(root): return {p.relative_to(root).as_posix() for p in root.rglob('*') if p.suffix in ['.cs', '.csproj'] and not any(x in ['obj', 'bin'] for x in p.relative_to(root).parts)}
    require(len(i['sources']) == 210 and set(i['sources']) == source_names(raw/'source') == source_names(qualified/'source') and all(sha(raw/'source'/n) == h == sha(qualified/'source'/n) for n, h in i['sources'].items()), 'Changed common CPU/adapter source identity')
    require(d['schema'] == 2 and d['selectedModels'] == MODELS and (d['GeneratorCommit'], d['RunnerCommit']) == PINS and d['cpuAssemblySha256'] == e['outputs']['build/bin/Copper68k/release/Copper68k.dll'] and d['adapterAssemblySha256'] == e['outputs']['build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'], 'Wrong raw model or assembly bindings')
    require({k: d[k] for k in l['rawCounts']} == l['rawCounts'] and (d['passing'], d['mismatching'], d['unsupported'], d['untested'], d['executedCases'], len(d['rows'])) == (1327, 46, 8, 0, 11478371, 1381), 'Wrong complete raw coverage counts')
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}; tree = ET.parse(raw/'audit.trx'); rows = tree.findall('.//t:UnitTestResult', ns); counters = tree.find('.//t:Counters', ns)
    require(len(rows) == 1 and rows[0].get('testName') == 'Copper68k.Tests.M68kWinUaeCpuTesterConformanceTests.WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled' and rows[0].get('outcome') == 'Failed' and int(counters.get('total')) == int(counters.get('executed')) == int(counters.get('failed')) == 1 and int(counters.get('passed')) == 0, 'Raw Basic failure was lost or relabelled')
    failures = {(x['Model'], x['Opcode']): x for x in d['rows'] if x['Status'] != 'passing'}
    require(len(l['entries']) == len(failures) == 54 and len({(x['model'], x['family']) for x in l['entries']}) == 54 and {(x['model'], x['family']) for x in l['entries']} == set(failures), 'Missing, duplicate or foreign first-failure mapping')
    reports = {n: load(qualified/n) for n in l['qualifiedReports']}
    require(len(reports) == 10 and all(sha(qualified/n) == h for n, h in l['qualifiedReports'].items()), 'Changed qualified counterpart reports')
    for report in reports.values():
        require((report['GeneratorCommit'], report['RunnerCommit']) == PINS and report['cpuAssemblySha256'] == sha(qualified/'build/bin/Copper68k/release/Copper68k.dll') and report['adapterAssemblySha256'] == sha(qualified/'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll') and report['mismatching'] == report['unsupported'] == report['untested'] == 0, 'Unqualified counterpart identity or outcome')
    documentation = (REPO/'docs/COPPER68K_REFERENCE_QUALIFICATION.md').read_text(encoding='utf-8')
    anchors = {re.sub(r'[^\w\- ]', '', line.lstrip('# ').lower()).replace(' ', '-') for line in documentation.splitlines() if line.startswith('#')}
    for row in l['entries']:
        original = failures[(row['model'], row['family'])]; words = [int(x, 16) for x in re.search(r'words=(.*?), PC=', original['Detail']).group(1).split(',')]
        require(row['rawStatus'] == original['Status'] and row['rawExecutedCases'] == original['ExecutedCases'] and row['firstWords'] == [f'{w:04X}' for w in words] and row['rawDetailSha256'] == hashlib.sha256(original['Detail'].encode()).hexdigest(), 'Changed observed Basic first-failure witness')
        category, preset = classification(row['model'], row['family'], words, original['Status'])
        require(row['firstFailureClassification'] == category and row['rawDirectoryResolved'] is False and row['laterRawFailuresUnobserved'] is True and row['diagnosticProfile'] == (row['model'] in ['68010', '68060']) and row['reason'], 'Wrong first-failure classification or promotion')
        q = row['qualifiedCounterpart']
        if preset is None: require(q is None, 'Unresolved architecture has a passing counterpart claim')
        else:
            require(q is not None and q['report'] == f'winuae-{preset}-audit.json' and q['model'] == row['model'] and q['family'] == row['family'] and q['documentationSection'] in anchors, 'Missing or unrelated qualified counterpart')
            found = [x for x in reports[q['report']]['rows'] if (x['Model'], x['Family']) == (row['model'], row['family'])]
            require(len(found) == 1 and found[0]['Status'] == 'passing' and found[0]['Controls'] and found[0]['ExecutedCases'] == q['executedCases'] > 0, 'Counterpart model/family coverage unavailable')
    counts = dict(collections.Counter(x['firstFailureClassification'] for x in l['entries']))
    require(counts == l['classifications'] == {'qualified-reference-correction': 40, 'reserved-or-undefined-input': 12, 'unresolved-architecture': 2}, 'Wrong qualification review totals')
    return dict(schema=1, ledgerSha256=sha(ledger), producerSha256=sha(Path(__file__)), rawReportSha256=sha(raw/'winuae-model-audit.json'), qualifiedReports=l['qualifiedReports'], firstFailures=54, classifications=counts, ledgerValidationPassed=True, rawGatePassed=False, laterRawFailuresUnobserved=True, architecturalPromotion=False, roadmapComplete=False)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('--basic-audit-directory', type=Path, required=True); p.add_argument('--qualified-report-directory', type=Path, required=True); p.add_argument('--ledger', type=Path, default=LEDGER); p.add_argument('--output', type=Path, required=True)
    a = p.parse_args(); result = validate(a.basic_audit_directory.resolve(), a.qualified_report_directory.resolve(), a.ledger.resolve()); a.output.mkdir(parents=True, exist_ok=False); (a.output/'verification.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8'); print('First-failure mapping verified; raw Basic gate remains failed:', result['classifications'])
