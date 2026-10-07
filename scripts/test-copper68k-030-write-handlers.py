"""Compare private pending-write handler protocols with pinned native 030 code.

Default and --validate-only fail any private-protocol disagreement. Explicit
--discovery-only records all disagreements without architectural promotion.
Requires the same pristine pinned WinUAE checkout and Windows/MSVC fixtures as
the transfer observer. Native functions remain unchanged; flat FC-bearing
logical denials use native page-fault construction, without enabled translation.
"""
import argparse
import importlib.util
import itertools
from pathlib import Path
import re

REPO=Path(__file__).resolve().parents[1]
BASE=REPO/'scripts/test-copper68k-030-move-transfer-faults.py'
FIXTURE=REPO/'scripts/reference/m68030-write-handlers.cpp'
spec=importlib.util.spec_from_file_location('native_transfer',BASE)
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
original_fragments=m.fragments
m.FIXTURE=FIXTURE


def fragments(reference,baseline):
    data=original_fragments(reference,baseline)
    data['unalign.inc']=m.extractor.function(reference,'cpummu30.cpp','static void unalign_clear(void)')+'\n'
    for name,value in [('MMU030_SSW_SIZE_B',0x10),('MMU030_SSW_SIZE_W',0x20)]:
        macro=re.findall(r'^#define\s+'+name+r'\s+(0x[0-9a-fA-F]+)',data['constants.inc'],re.M)
        m.check(len(macro)==1 and int(macro[0],16)==value,'Wrong pinned native size bits')
    return data


def observations(native):
    m.check(not (native/'errors.log').read_text().strip(),'Fatal native handler observer error')
    lines=(native/'run.log').read_text().splitlines()
    cases=list(itertools.product([1,2,4],range(4),[0,7],range(2),range(2),range(4),range(32),range(5)))
    m.check(len(lines)==len(cases)==61440,'Empty or incomplete native handler selection')
    hex_fields={'PC','sourceA','A7','USP','ISP','MSP','framePC','frameSR','frameSSW','output','address','value','D7'}
    bool_fields={'agreement','complete','first','second','registers','memory','transfers','flags'}
    keys={'id','width','bank','source','post','alias','vi','ccr','lane','exceptions','attempts','reads','wattempts','writes','retryFC','moveccr','finalccr','error','events'}|hex_fields|bool_fields
    counts={'passing':0,'mismatching':0,'unsupported':0,'untested':0};rows=[];lanes={str(n):dict(passing=0,mismatching=0) for n in range(5)}
    for id,(line,case) in enumerate(zip(lines,cases)):
        pairs=[word.split('=',1) for word in line.split()]
        m.check(all(len(p)==2 for p in pairs) and len(pairs)==len(keys) and {p[0] for p in pairs}==keys,'Invalid native handler row')
        d={key:value if key in {'error','events'} else int(value,16 if key in hex_fields else 10) for key,value in pairs}
        width,bank,source,post,alias,vi,ccr,lane=case
        m.check([d[k] for k in ['id','width','bank','source','post','alias','vi','ccr','lane']]==[id,*case] and all(d[k] in [0,1] for k in bool_fields),'Changed native handler case identity or booleans')
        mask=(1<<(8*width))-1;value=[0,0x80818283&mask,mask>>1,mask][vi];pending=value^mask if lane==1 else value
        stride=2 if width==1 and source==7 else width;address=0x4200+post*stride if alias else 0x4400
        original=(ccr&16)|[4,8,0,8][vi];returned=(ccr+7)&31 if lane==4 else original
        sr=0x700|(0x2000 if bank>=2 else 0)|(0x1000 if bank in [1,3] else 0)| (returned if lane>=3 else original)
        fc=(5 if bank>=2 else 1)^(4 if lane==4 else 0)
        ssw=0x100|(0x10 if width==1 else 0x20 if width==2 else 0)|fc
        wanted={'complete':1,'first':1,'second':1,'registers':1,'memory':1,'transfers':1,'flags':1,'exceptions':2 if lane>=3 else 1,'attempts':1,'reads':1,'wattempts':1 if lane==2 else 3 if lane>=3 else 2,'writes':0 if lane==2 else 1,'retryFC':-1 if lane==2 else fc,'PC':0x1006,'sourceA':0x4200+post*stride,'A7':0x4200+post*stride if source==7 else [0x7000,0x7000,0x8000,0x9000][bank],'framePC':0x1002,'frameSR':sr,'frameSSW':ssw,'output':value,'address':0 if lane==2 else address,'value':0 if lane==2 else pending,'D7':42,'moveccr':returned,'finalccr':returned&16,'error':'none','events':'49235'+('92586' if lane>=3 else '6' if lane==2 else '86')}
        mismatches={key:dict(expected=value,actual=d[key]) for key,value in wanted.items() if d[key]!=value}
        passing=not mismatches
        m.check(d['agreement']==int(passing),'Native observer and independent comparison disagree: '+str(id)+' '+str(mismatches))
        status='passing' if passing else 'mismatching';counts[status]+=1;lanes[str(lane)][status]+=1
        rows.append(dict(id=id,case=list(case),status=status,differences=mismatches))
    return dict(schema=1,nativeCases=61440,counts=counts,lanes=lanes,cases=rows,producerSha256=m.sha(Path(__file__)),transferProducerSha256=m.sha(BASE),softwareReferenceExecuted=True,privateProtocolAgreementPassed=counts['mismatching']==0,architecturalPromotion=False,hardwareQualified=False,enabledTranslation=False,physicalFunctionCodeSpaces=False,traceQualified=False,productionImport=False,publication=False,roadmapComplete=False)


m.fragments=fragments;m.observations=observations
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--reference-directory',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--validate-only',action='store_true');parser.add_argument('--discovery-only',action='store_true');args=parser.parse_args()
    result=m.audit(args.output.resolve(),args.reference_directory.resolve(),args.validate_only)
    print('Native handler comparison',result['counts'],'lanes',result['lanes'])
    if not args.discovery_only and not result['privateProtocolAgreementPassed']:raise SystemExit(1)
