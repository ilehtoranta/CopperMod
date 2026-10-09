"""Prove captured EOR postincrement replacement before retiring its old fact."""
from pathlib import Path
import itertools,re,subprocess
from copper68k_consolidation import Audit,check

REPO=Path(__file__).resolve().parents[1]
GROUP='logical-eor-postincrement-captured'

def mutate(path,text):
    text=text.replace('\r\n','\n')
    if path.endswith('M68kAdvancedTimingInterpreter.Logical.cs'):
        old='if (mode == 3) WriteGeneralRegister(true, reg, unchecked(address + M68kIntegerSemantics.AddressIncrement(reg, size)));'
        check(text.count(old)==1,'Missing unique advanced logical postincrement target')
        return text.replace(old,'if (mode == 3) WriteGeneralRegister(true, reg, unchecked(address + M68kIntegerSemantics.AddressIncrement(reg, size) * (top == 11 && size == M68kOperandSize.Byte ? 2u : 1u))); // intentional second EOR increment')
    old='''                        result = eaValue ^ regValue;
                        CompletePredecrementLongDestinationBeforeWrite();
                        eaOperand.Write(result);
                        SetLogicFlags(result, operandSize);'''
    check(text.count(old)==1,'Missing unique EOR postincrement mutation')
    new=old.replace('                        SetLogicFlags', '                        if (mode == 3 && operandSize == M68kOperandSize.Byte) SetAddressRegister(eaReg, State.A[eaReg] + AddressIncrement(eaReg, operandSize)); // intentional second increment\n                        SetLogicFlags')
    return text.replace(old,new)

def report(model,mode):
    combinations,failures={},[]
    for ar,dr,supervisor,ccr in itertools.product(range(8),range(8),[False,True],range(32)):
        key=f'{model}/EOR/1/D{dr}->(A{ar})+/captured/super={supervisor}'
        status='mismatching' if mode=='Mutation' else 'passing'
        counts=combinations.setdefault(key,{});counts[status]=counts.get(status,0)+1
        if mode=='Mutation':
            at=(0x4700 if supervisor else 0x7800) if ar==7 else 0x2000
            stride=2 if ar==7 else 1
            failures.append(dict(id=f'{key}/op={0xb118|dr<<9|ar:04X}/ccr={ccr:02X}',status=status,reason=f'A{ar} expected {at+stride:08X}, actual {at+stride*2:08X}'))
    return dict(schema=1,model=model,group=GROUP,counts=dict(passing=0 if mode=='Mutation' else 4096,mismatching=4096 if mode=='Mutation' else 0,unsupported=0,untested=0),logicalCases=4096,xunitBatches=1,combinations=combinations,failures=failures)

class EorAudit(Audit):
    def __init__(self,spec):
        self.s=spec;self.repo=spec['repo']
        self.original=subprocess.check_output(['git','show',spec['pin']+':'+spec['legacy']],cwd=self.repo).decode('utf-8-sig').replace('\r\n','\n')
        marker='\t[Fact]\n\tpublic void '+spec['witness']+'()'
        check(self.original.count(marker)==1,'Missing unique historical EOR fact')
        start=self.original.index(marker);end=self.original.index('\t[Fact]\n',start+len(marker))
        self.reduced=self.original[:start]+self.original[end:]

def witness_error(name,message):
    check(re.search(r'Expected:\s*8193\b',message) and re.search(r'Actual:\s*8194\b',message),'Wrong historical EOR second increment')

def make_audit():
    return EorAudit(dict(repo=REPO,producer=Path(__file__).resolve(),pin='9430fc416714c5f68a9757cd44ebe6ee72ee7ed6',
        legacy='Copper68k.Tests/M68kLogicalTests.cs',legacy_class='Copper68k.Tests.M68kLogicalTests',legacy_cases=5,
        witness='EorBytePostincrementDestinationAdvancesOnce',witness_cases=1,
        fixture='Copper68k.Tests/Synthetic/SyntheticEorPostincrementTests.cs',shared_class='Copper68k.Tests.Synthetic.SyntheticEorPostincrementTests',
        method='CapturedEorBytePostincrementPreservesSourceAndAdvancesOnce',group=GROUP,
        mutation_files=['Copper68k/M68kCore.cs','Copper68k/M68kAdvancedTimingInterpreter.Logical.cs'],mutate=mutate,report=report,
        witness_outcome=lambda name:'Failed',witness_error=witness_error,
        description=__doc__))

if __name__=='__main__':make_audit().main()
