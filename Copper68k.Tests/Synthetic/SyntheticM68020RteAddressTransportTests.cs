using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

// The host map describes physical byte ranges. These are complete RTE
// programs using defined normal frames, not invented opaque source state.
// Logical rejection is qualified; physical port widths/BERR cycles are not.
public sealed class SyntheticM68020RteAddressTransportTests(ITestOutputHelper output)
{
    private static readonly ModelSpec[] Models=ModelSpec.All.Where(x=>x.Id is "68EC020" or "A1200" or "68020" or "68030").ToArray();
    private static readonly uint[] Frames=[0x4700,0x10004700,0xfffffb,0xffffff,0xfffffffb,0xffffffff,0x1000001];
    private static readonly (uint Offset,int Width)[] Reads=[(0,2),(2,4),(6,2)];

    [Theory,Trait("Suite","ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void FaultFreeHighAndWrappedFrames(bool batch)=>Audit(batch,false);

    [EnvironmentFact("COPPER68K_RUN_020_RTE_ADDRESS_TRANSPORT","require high/wrapped RTE validation-fault transport"),Trait("Suite","ReferenceDiscovery")]
    public void ScalarHighAndWrappedValidationFaults()=>Audit(false,true);
    [EnvironmentFact("COPPER68K_RUN_020_RTE_ADDRESS_TRANSPORT","require high/wrapped RTE validation-fault transport"),Trait("Suite","ReferenceDiscovery")]
    public void BatchHighAndWrappedValidationFaults()=>Audit(true,true);

    private void Audit(bool batch,bool fault)
    {
        var failures=new List<Exception>();
        foreach(var model in Models)
        {
            var bus=new FaultBus();var m=new SyntheticMachine(model,bus);
            var report=new CoverageBatch(model.Id,$"rte-address-{(fault?"fault":"control")}-{(batch?"batch":"scalar")}");
            foreach(var frame in Frames)
            foreach(var bank in new[]{"ISP","MSP"})
            for(var ccr=0;ccr<32;ccr++)
            foreach(var read in fault?Reads:new[]{(0u,0)})
            foreach(var lane in fault?Enumerable.Range(0,read.Item2):new[]{0})
            {
                var id=$"{model.Id}/RTE/address/bank={bank}/frame={frame:X8}/read={read.Item1:X}:{read.Item2}/byte={lane}/ccr={ccr:X2}";
                try
                {
                    bus.Disarm();m.Reset(ccr);_=SyntheticExecution.Prepare(m,[0x4e73]);
                    m.InitializePhysical(0x9020,0x4e73,2);m.InitializePhysical(0x9022,0x4e71,2);m.InitializePhysical(0x6000,0x7c2a,2);m.InitializePhysical(0x6002,0x4e71,2);
                    var sr=(ushort)(0x2700|ccr|(bank=="MSP"?0x1000:0));
                    m.Core.State.SetUserStackPointer(0x30007800);m.Core.State.SetInterruptStackPointer(bank=="ISP"?frame:0x20007400);m.Core.State.SetMasterStackPointer(bank=="MSP"?frame:0x20007400);
                    m.Core.State.StatusRegister=sr;m.Core.State.SetActiveStackPointer(frame);m.Core.State.VectorBaseRegister=0x10009000;
                    m.InitializePhysical(0x10009008,0x9020,4);
                    m.InitializePhysical(frame,sr,2);m.InitializePhysical(unchecked(frame+2),0x6000,4);m.InitializePhysical(unchecked(frame+6),0x24,2);
                    var e=ArchitecturalExpectation.Capture(m);
                    e.ControlChecks["USP"]=(s=>s.UserStackPointer,0x30007800);
                    e.ControlChecks["ISP"]=(s=>s.InterruptStackPointer,bank=="ISP"?frame:0x20007400);
                    e.ControlChecks["VBR"]=(s=>s.VectorBaseRegister,0x10009000);
                    var serial=m.Core.State.ExceptionSequence;bus.Accesses.Clear();
                    if(fault)bus.Arm(model.Physical(unchecked(frame+read.Item1+(uint)lane)));
                    Step(m,batch);
                    if(fault)
                    {
                        if(bus.Rejected.Count!=1)throw new InvalidOperationException("wrapped validation request did not reject its physical byte exactly once");
                        var nested=unchecked(frame-92);
                        if(m.Core.State.Halted||m.Core.State.ExceptionSequence!=serial+1||m.Core.State.LastExceptionVector!=2||m.Core.State.A[7]!=nested||m.Core.State.ProgramCounter!=0x9020||
                            m.PeekPhysical(nested,2)!=sr||m.PeekPhysical(unchecked(nested+2),4)!=0x1000||m.PeekPhysical(unchecked(nested+6),2)!=0xb008||
                            m.PeekPhysical(unchecked(nested+10),2)!=(read.Item2==2?0x165u:0x145u)||m.PeekPhysical(unchecked(nested+16),4)!=unchecked(frame+read.Item1))
                            throw new InvalidOperationException("high/wrapped validation frame/vector differs");
                        for(uint n=0;n<92;n++)e.Memory[model.Physical(unchecked(nested+n))]=bus.Peek(model.Physical(unchecked(nested+n)));
                        e.A[7]=nested;if(bank=="MSP")e.MasterStackPointer=nested;else e.ControlChecks["ISP"]=(s=>s.InterruptStackPointer,nested);
                        e.Pc=0x9020;e.ExceptionVector=2;Check(m,e);
                        Step(m,batch);
                    }
                    else if(m.Core.State.ExceptionSequence!=serial)throw new InvalidOperationException("fault-free address fixture raised an exception");
                    e.A[7]=unchecked(frame+8);if(bank=="MSP")e.MasterStackPointer=e.A[7];else e.ControlChecks["ISP"]=(s=>s.InterruptStackPointer,e.A[7]);
                    e.Pc=0x6000;Check(m,e);
                    e.D[6]=42;e.Pc+=2;e.Sr=(ushort)(e.Sr&0xfff0);Step(m,batch);Check(m,e);
                    report.Record(id,"passing",null);
                }
                catch(Exception ex)when(ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException){report.Record(id,"unsupported",ex.Message);}
                catch(Exception ex){report.Record(id,"mismatching",ex.Message);}
            }
            try{report.Complete(output);}catch(Exception ex){failures.Add(ex);}
        }
        Assert.True(failures.Count==0,string.Join("\n",failures.Select(x=>x.Message)));
    }
    private static void Check(SyntheticMachine m,ArchitecturalExpectation e){var error=e.Verify(m);if(error!=null)throw new InvalidOperationException(error);}
    private static void Step(SyntheticMachine m,bool batch)
    {
        if(!batch){m.Core.ExecuteInstruction();return;}
        var boundary=new Boundary();if(((IM68kBatchCore)m.Core).ExecuteInstructions(1,null,boundary)!=1||boundary.Before!=1||boundary.After!=1)
            throw new InvalidOperationException("batch boundary count differs");
    }
    private sealed class Boundary:IM68kInstructionBoundary
    {public int Before,After;public bool BeforeInstruction(){Before++;return true;}public void AfterInstruction(long a,long b)=>After++;}
    private sealed class FaultBus:SparseRecordingBus,IM68kPhysicalAddressMap
    {
        private uint? denied;
        internal List<(uint Address,int Width)> Rejected{get;}=[];
        internal void Disarm(){denied=null;Rejected.Clear();}
        internal void Arm(uint address)=>denied=address;
        public bool IsCpuPhysicalAddressMapped(uint address,int size,M68kBusAccessKind kind)
        {
            if(denied is not{} at||kind!=M68kBusAccessKind.CpuDataRead||unchecked(at-address)>=size)return true;
            denied=null;Rejected.Add((address,size));return false;
        }
    }
}
