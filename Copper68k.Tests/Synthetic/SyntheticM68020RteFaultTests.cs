using Copper68k;
using Xunit.Abstractions;

namespace Copper68k.Tests.Synthetic;

public sealed class SyntheticM68020RteFaultTests(ITestOutputHelper output)
{
    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidationFaultPreservesFrameAndExplicitRteResumes(bool batch) => Witness(batch, false);

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void StateLoadFaultHaltsWithoutWritingMemory(bool batch) => Witness(batch, true);

    [EnvironmentFact("COPPER68K_RUN_020_RTE_FAULTS", "qualify 020/030 validation and state-load faults"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarFrameFaultMatrix() => Matrix(false, false);
    [EnvironmentFact("COPPER68K_RUN_020_RTE_FAULTS", "qualify 020/030 validation and state-load faults"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchFrameFaultMatrix() => Matrix(true, false);
    [EnvironmentFact("COPPER68K_RUN_020_RTE_FAULTS", "qualify 020/030 RTE fault CCR boundaries"), Trait("Suite", "ReferenceDiscovery")]
    public void ScalarFrameFaultCcrBoundaries() => Matrix(false, true);
    [EnvironmentFact("COPPER68K_RUN_020_RTE_FAULTS", "qualify 020/030 RTE fault CCR boundaries"), Trait("Suite", "ReferenceDiscovery")]
    public void BatchFrameFaultCcrBoundaries() => Matrix(true, true);

    [Theory, Trait("Suite", "ReferenceDiscovery")]
    [InlineData(false, "buffer")]
    [InlineData(true, "buffer")]
    [InlineData(false, "alias")]
    [InlineData(true, "alias")]
    [InlineData(false, "refault")]
    [InlineData(true, "refault")]
    [InlineData(false, "version")]
    [InlineData(true, "version")]
    public void HandlerControls(bool batch, string mode)
    {
        var failures=new List<Exception>();
        foreach(var model in Models)
        {
            var bus=new FaultBus();var m=new SyntheticMachine(model,bus);
            var report=new CoverageBatch(model.Id,$"rte-handler-{mode}-{(batch?"batch":"scalar")}");
            foreach(var bank in Banks)
            foreach(var read in mode=="buffer" ? ValidationReads.Take(3) : new[]{(mode=="version"?0x36u:0x5au,2,1)})
            foreach(var lane in Enumerable.Range(0,read.Item2))
            {
                var id=$"{model.Id}/RTE/handler={mode}/bank={bank}/read={read.Item1:X}:{read.Item2}:{read.Item3}/byte={lane}/ccr=1F";
                Record(report,id,()=>Run(m,bus,batch,bank,31,0,false,read,lane,mode));
            }
            try{report.Complete(output);}catch(Exception ex){failures.Add(ex);}
        }
        Assert.True(failures.Count==0,string.Join("\n",failures.Select(x=>x.Message)));
    }

    private static readonly ModelSpec[] Models=ModelSpec.All.Where(x=>x.Id is "68020" or "68030" or "68EC020" or "A1200").ToArray();
    private static readonly string[] Banks=["user","user-M","ISP","MSP"];
    private static readonly (uint,int,int)[] ValidationReads=[(0,2,1),(2,4,1),(6,2,1),(0x36,2,1),(0x5a,2,1)];
    private static IEnumerable<(uint,int,int)> LoadReads()
    {
        yield return (8,2,1);yield return (10,2,1);yield return (0x14,4,1);yield return (0x24,4,1);
        yield return (12,2,1);yield return (14,2,1);
        for(uint offset=0x10;offset<92;offset+=2)yield return(offset,2,offset is 0x36 or 0x5a?2:1);
    }
    private void Matrix(bool batch,bool ccrs)
    {
        var failures=new List<Exception>();
        foreach(var model in Models)
        foreach(var loading in new[]{false,true})
        {
            var bus=new FaultBus();var m=new SyntheticMachine(model,bus);
            var group=$"rte-{(loading?"load":"validation")}-{(ccrs?"ccr":"matrix")}-{(batch?"batch":"scalar")}";
            var report=new CoverageBatch(model.Id,group);
            foreach(var bank in Banks)
            foreach(var trace in new ushort[]{0,0x8000,0x4000})
            foreach(var ccr in ccrs?Enumerable.Range(0,32):new[]{31})
            foreach(var read in ccrs ? new[]{(loading?8u:0u,2,1)} : loading?LoadReads():ValidationReads)
            foreach(var lane in Enumerable.Range(0,read.Item2))
            {
                var id=$"{model.Id}/RTE/{(loading?"loading":"validation")}/bank={bank}/T={trace:X4}/read={read.Item1:X}:{read.Item2}:{read.Item3}/byte={lane}/ccr={ccr:X2}";
                Record(report,id,()=>Run(m,bus,batch,bank,ccr,trace,loading,read,lane,"rerun"));
            }
            try{report.Complete(output);}catch(Exception ex){failures.Add(ex);}
        }
        Assert.True(failures.Count==0,string.Join("\n",failures.Select(x=>x.Message)));
    }
    private static void Record(CoverageBatch report,string id,Action run)
    {
        try{run();report.Record(id,"passing",null);}
        catch(Exception ex)when(ex is UnsupportedM68kTimingException or UnsupportedM68kOpcodeException){report.Record(id,"unsupported",ex.Message);}
        catch(Exception ex){report.Record(id,"mismatching",ex.Message);}
    }

    [Theory,Trait("Suite","ReferenceDiscovery")]
    [InlineData(false)]
    [InlineData(true)]
    public void FaultDuringValidationExceptionEntryHalts(bool batch)
    {
        var failures=new List<Exception>();
        foreach(var model in Models)
        {
            var bus=new FaultBus();var m=new SyntheticMachine(model,bus);
            var report=new CoverageBatch(model.Id,$"rte-entry-halt-{(batch?"batch":"scalar")}");
            foreach(var bank in Banks)
            foreach(var vector in new[]{false,true})
            foreach(var offset in vector?new[]{0u}:Enumerable.Range(0,46).Select(n=>(uint)n*2))
            foreach(var lane in Enumerable.Range(0,vector?4:2))
            {
                var id=$"{model.Id}/RTE/entry-halt/bank={bank}/kind={(vector?"vector":"write")}/offset={offset:X}/byte={lane}/ccr=1F";
                Record(report,id,()=>
                {
                    var frame=Prepare(m,bank,31,batch);var nested=frame-92;
                    var e=ArchitecturalExpectation.Capture(m);var serial=m.Core.State.ExceptionSequence;
                    bus.Arm(model.Physical(frame+90),2,1);
                    bus.AlsoArm(model.Physical(vector?8u+(uint)lane:nested+offset+(uint)lane),vector?4:2,1,
                        vector?M68kBusAccessKind.CpuDataRead:M68kBusAccessKind.CpuDataWrite);
                    Step(m,batch);
                    e.Halted=true;e.Pc+=2;e.ExceptionVector=2;e.A[7]=vector?nested:nested+offset;
                    if((e.Sr&0x1000)!=0)e.MasterStackPointer=e.A[7];
                    // Successful writes preceding the rejected cycle may have
                    // committed opaque state. Check their footprint, retaining
                    // all original-frame and surrounding-memory expectations.
                    foreach(var access in bus.Accesses.Where(a=>a.Write))
                    {
                        if(access.Address<nested || access.Address>=frame || !vector && access.Address<=nested+offset)
                            throw new InvalidOperationException("fatal entry wrote beyond its completed prefix");
                        for(uint n=0;n<access.Width;n++)e.MemoryMasks[access.Address+n]=0;
                    }
                    Check(m,e);
                    if(bus.Rejected.Count!=2 || m.Core.State.ExceptionSequence!=serial+1)
                        throw new InvalidOperationException("fatal entry replayed or redelivered the fault");
                    var count=bus.Accesses.Count;m.Core.ExecuteInstruction();m.Core.ExecuteInstruction();
                    if(bus.Accesses.Count!=count)throw new InvalidOperationException("fatal entry did not hold HALT");
                });
            }
            try{report.Complete(output);}catch(Exception ex){failures.Add(ex);}
        }
        Assert.True(failures.Count==0,string.Join("\n",failures.Select(x=>x.Message)));
    }

    [Theory,Trait("Suite","ReferenceDiscovery")]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public void HeaderFaultsInNormalFramesAndThrowawayChains(bool batch,bool chained)
    {
        var failures=new List<Exception>();
        foreach(var model in Models)
        {
            var bus=new FaultBus();var m=new SyntheticMachine(model,bus);
            var report=new CoverageBatch(model.Id,$"rte-header-{(chained?"chained":"normal")}-{(batch?"batch":"scalar")}");
            foreach(var start in new[]{"ISP","MSP"})
            foreach(var tail in chained?Banks:new[]{start})
            foreach(var restored in Banks)
            foreach(var format in new[]{0,2})
            foreach(var ccr in new[]{0,31})
            foreach(var read in ValidationReads.Take(3))
            foreach(var lane in Enumerable.Range(0,read.Item2))
            {
                var id=$"{model.Id}/RTE/header/start={start}/tail={tail}/restore={restored}/format={format}/read={read.Item1:X}:{read.Item2}/byte={lane}/ccr={ccr:X2}";
                Record(report,id,()=>
                {
                    bus.Disarm();m.Reset(ccr);m.InitializePhysical(0x9020,0x4e73,2);m.InitializePhysical(0x6000,0x7c2a,2);
                    _=SyntheticExecution.Prepare(m,[0x4e73]);
                    var pointers=new Dictionary<string,uint>{{"ISP",0x4700},{"MSP",0x7400},{"user",0x7800}};
                    m.Core.State.SetUserStackPointer(pointers["user"]);m.Core.State.SetInterruptStackPointer(pointers["ISP"]);m.Core.State.SetMasterStackPointer(pointers["MSP"]);
                    var initialSr=(ushort)(0x700|M68040StackFixture.Status(start,0,ccr));
                    m.Core.State.StatusRegister=initialSr;m.Core.State.SetActiveStackPointer(pointers[start]);
                    var liveSr=initialSr;
                    if(chained)
                    {
                        liveSr=(ushort)(0x700|M68040StackFixture.Status(tail,0,ccr^31));
                        m.InitializePhysical(pointers[start],liveSr,2);m.InitializePhysical(pointers[start]+2,0xdead0001,4);m.InitializePhysical(pointers[start]+6,0x1024,2);
                        pointers[start]+=8;
                    }
                    var sourceBank=M68040StackFixture.PhysicalBank(tail);var frame=pointers[sourceBank];
                    var savedSr=(ushort)(0x700|M68040StackFixture.Status(restored,0,ccr));
                    m.InitializePhysical(frame,savedSr,2);m.InitializePhysical(frame+2,0x6000,4);m.InitializePhysical(frame+6,(uint)(format<<12)|0x24,2);
                    if(format==2)m.InitializePhysical(frame+8,0x1000,4);
                    var e=ArchitecturalExpectation.Capture(m);m.Bus.Accesses.Clear();
                    bus.Arm(model.Physical(frame+read.Item1+(uint)lane),read.Item2,1);
                    Step(m,batch);
                    var exceptionBank=M68040StackFixture.ExceptionBank(liveSr);pointers[exceptionBank]-=92;var nested=pointers[exceptionBank];
                    if(bus.Rejected.Count!=1 || m.PeekPhysical(nested,2)!=liveSr || m.PeekPhysical(nested+2,4)!=0x1000 ||
                        m.PeekPhysical(nested+6,2)!=0xb008 || m.PeekPhysical(nested+10,2)!=((read.Item2==2?0x160u:0x140u)|((liveSr&0x2000)!=0?5u:1u)) ||
                        m.PeekPhysical(nested+16,4)!=frame+read.Item1)
                        throw new InvalidOperationException("normal/chained validation frame differs");
                    for(uint n=0;n<92;n++)e.Memory[model.Physical(nested+n)]=bus.Peek(model.Physical(nested+n));
                    M68040StackFixture.SetStacks(e,pointers,(ushort)((liveSr|0x2000)&~0xc000));e.Pc=0x9020;e.ExceptionVector=2;Check(m,e);
                    pointers[exceptionBank]+=92;pointers[sourceBank]+=(uint)(format==0?8:12);
                    M68040StackFixture.SetStacks(e,pointers,savedSr);e.Pc=0x6000;
                    Step(m,batch);Check(m,e);
                    e.D[6]=42;e.Pc+=2;e.Sr=(ushort)(e.Sr&0xfff0);Step(m,batch);Check(m,e);
                });
            }
            try{report.Complete(output);}catch(Exception ex){failures.Add(ex);}
        }
        Assert.True(failures.Count==0,string.Join("\n",failures.Select(x=>x.Message)));
    }

    private void Witness(bool batch, bool loading)
    {
        var failures = new List<Exception>();
        foreach (var model in Models)
        {
            var bus = new FaultBus(); var m = new SyntheticMachine(model, bus);
            var report = new CoverageBatch(model.Id, "rte-" + (loading ? "load-fault" : "validation-fault") + (batch ? "-batch" : "-scalar"));
            foreach (var bank in Banks)
            {
                var id = $"{model.Id}/RTE/{(loading ? "loading" : "validation")}/bank={bank}/read={(loading ? 8 : 0x5a):X}/ccr=1F";
                Record(report, id, () => Run(m, bus, batch, bank, 31, 0, loading, (loading ? 8u : 0x5au, 2, 1), 0, "rerun"));
            }
            try { report.Complete(output); } catch (Exception ex) { failures.Add(ex); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(x => x.Message)));
    }

    private static void Run(SyntheticMachine m,FaultBus bus,bool batch,string bank,int ccr,ushort trace,
        bool loading,(uint Offset,int Width,int Nth) read,int lane,string mode)
    {
        var frame=Prepare(m,bank,ccr,batch,trace);
        var savedSr=(ushort)m.PeekPhysical(frame,2);
        var supplied=mode=="version"?0x1000u:m.PeekPhysical(frame+read.Offset,read.Width);
        var faultSr=m.Core.State.StatusRegister;
        ushort[] code=mode switch
        {
            "buffer" or "version"=>[0x2f7c,(ushort)(supplied>>16),(ushort)supplied,0x002c,0x026f,0xfeff,0x000a,0x4e73],
            "alias"=>[0x33fc,0x2700,(ushort)(frame>>16),(ushort)frame,0x4e73],
            _=>[0x4e73]
        };
        for(var n=0;n<code.Length;n++)m.InitializePhysical(0x9020u+(uint)n*2,code[n],2);
        var e=ArchitecturalExpectation.Capture(m);
        var original=Enumerable.Range(0,92).Select(n=>m.PeekPhysical(frame+(uint)n,1)).ToArray();
        bus.Arm(m.Model.Physical(frame+read.Offset+(uint)lane),read.Width,read.Nth,mode is "buffer" or "refault" or "version");
        var serial=m.Core.State.ExceptionSequence;
        Step(m,batch);
        if(bus.Rejected.Count!=1)throw new InvalidOperationException("RTE did not reject the selected read exactly once");
        if(loading)
        {
            e.Pc+=2;e.Halted=true;Check(m,e);
            if(bus.Accesses.Any(x=>x.Write))throw new InvalidOperationException("state-load fault wrote memory");
            if(m.Core.State.ExceptionSequence!=serial)throw new InvalidOperationException("state-load fault delivered another exception");
            var count=bus.Accesses.Count;m.Core.ExecuteInstruction();m.Core.ExecuteInstruction();
            if(bus.Accesses.Count!=count)throw new InvalidOperationException("halted CPU accessed the bus");
            return;
        }
        if(m.Core.State.Halted || m.Core.State.LastExceptionVector!=2 || m.Core.State.ExceptionSequence!=serial+1)
            throw new InvalidOperationException("validation read did not deliver exactly one bus fault");
        var nested=m.Core.State.A[7];
        if(nested!=frame-92 || m.Core.State.ProgramCounter!=0x9020 || m.PeekPhysical(nested,2)!=e.Sr ||
            m.PeekPhysical(nested+2,4)!=0x9042 || m.PeekPhysical(nested+6,2)!=0xb008 ||
            m.PeekPhysical(nested+10,2)!=(read.Width==2?0x165u:0x145u) || m.PeekPhysical(nested+16,4)!=frame+read.Offset)
            throw new InvalidOperationException("defined validation-fault frame fields differ");
        if(!original.SequenceEqual(Enumerable.Range(0,92).Select(n=>m.PeekPhysical(frame+(uint)n,1))))
            throw new InvalidOperationException("validation fault damaged the original frame");
        // Opaque bytes are captured for preservation only, never treated as
        // independently known silicon state.
        for(uint n=0;n<92;n++)e.Memory[m.Model.Physical(nested+n)]=bus.Peek(m.Model.Physical(nested+n));
        e.A[7]=nested;if((e.Sr&0x1000)!=0)e.MasterStackPointer=nested;
        e.Pc=0x9020;e.ExceptionVector=2;Check(m,e);
        var postFaultReads=bus.Accesses.Count;
        if(mode is "buffer" or "version")
        {
            e.Write(nested+44,supplied,4,m.Model);e.Pc+=8;e.Sr=(ushort)((e.Sr&0xfff0)|(supplied==0?4:0)|(supplied>=0x80000000?8:0));
            Step(m,batch);Check(m,e);
            e.Write(nested+10,read.Width==2?0x65u:0x45u,2,m.Model);e.Pc+=6;e.Sr=(ushort)(e.Sr&0xfff0);
            Step(m,batch);Check(m,e);
        }
        else if(mode=="alias")
        {
            e.Write(frame,0x2700,2,m.Model);e.Pc+=8;e.Sr=(ushort)(e.Sr&0xfff0);
            Step(m,batch);Check(m,e);
        }
        else if(mode=="refault")
        {
            Step(m,batch);Check(m,e);
            if(bus.Rejected.Count!=2 || m.Core.State.ExceptionSequence!=serial+2)
                throw new InvalidOperationException("explicit retry did not refault exactly once");
            bus.Disarm();
        }
        if(mode=="version")
        {
            e.Sr=faultSr;e.A[7]=frame-8;if((e.Sr&0x1000)!=0)e.MasterStackPointer=e.A[7];
            e.Write(e.A[7],faultSr,2,m.Model);e.Write(e.A[7]+2,0x9042,4,m.Model);e.Write(e.A[7]+6,0x38,2,m.Model);
            e.Pc=0x90e0;e.ExceptionVector=14;Step(m,batch);Check(m,e);
            if(bus.Rejected.Count!=1 || bus.Accesses.Skip(postFaultReads).Any(x=>!x.Write&&x.Kind==M68kBusAccessKind.CpuDataRead&&x.Address>=frame&&x.Address<frame+92))
                throw new InvalidOperationException("incompatible supplied version reread/loaded the original frame");
            return;
        }
        e.A[7]=frame+92;if((e.Sr&0x1000)!=0)e.MasterStackPointer=frame+92;
        SyntheticSystemTests.ApplyStatus(m,e,savedSr);if(bank is "user" or "user-M")e.InactiveStackPointer=0x4700;
        e.Pc=0x6001;Step(m,batch);Check(m,e);
        if(mode=="buffer" && (bus.Rejected.Count!=1 || bus.Accesses.Skip(postFaultReads).Any(x=>!x.Write&&x.Kind==M68kBusAccessKind.CpuDataRead&&x.Address==m.Model.Physical(frame+read.Offset)&&x.Width==read.Width)))
            throw new InvalidOperationException("software-supplied validation value was reread");
        if(mode=="alias" && bus.Accesses.Skip(postFaultReads).Any(x=>!x.Write&&x.Kind==M68kBusAccessKind.CpuDataRead&&x.Address==m.Model.Physical(frame)))
            throw new InvalidOperationException("completed SR read was replayed");
        e.Pc=0x6004;
        if(trace!=0)
        {
            SyntheticSystemTests.ApplyStatus(m,e,(ushort)((savedSr|0x2000)&~0xc000));e.A[7]-=12;
            if((e.Sr&0x1000)!=0)e.MasterStackPointer=e.A[7];
            e.Write(e.A[7],savedSr,2,m.Model);e.Write(e.A[7]+2,0x6004,4,m.Model);e.Write(e.A[7]+6,0x2024,2,m.Model);e.Write(e.A[7]+8,0x6001,4,m.Model);
            e.Pc=0x9090;e.ExceptionVector=9;
        }
        Step(m,batch);Check(m,e);
        if(trace!=0)
        {
            e.Write(e.A[7],(uint)(savedSr&~0xc000),2,m.Model);e.Pc+=4;e.Sr=(ushort)(e.Sr&0xfff0);
            Step(m,batch);Check(m,e);
            e.A[7]+=12;if((e.Sr&0x1000)!=0)e.MasterStackPointer=e.A[7];
            SyntheticSystemTests.ApplyStatus(m,e,(ushort)(savedSr&~0xc000));if(bank is "user" or "user-M")e.InactiveStackPointer=0x4700;
            e.Pc=0x6004;Step(m,batch);Check(m,e);
        }
        e.D[6]=42;e.Pc+=2;e.Sr=(ushort)(e.Sr&0xfff0);Step(m,batch);Check(m,e);
        if(bus.Accesses.Any(x=>!x.Write&&x.Kind==M68kBusAccessKind.CpuInstructionFetch&&(x.Address&1)!=0))
            throw new InvalidOperationException("recovery issued an odd instruction fetch");
    }

    private static uint Prepare(SyntheticMachine m, string bank, int ccr, bool batch,ushort trace=0)
    {
        ((FaultBus)m.Bus).Disarm(); m.Reset(ccr);
        ushort[] handler=[0x3f7c,0x6001,0x000c,0x3f7c,0x4e71,0x000e,0x026f,0xcfff,0x000a,0x4e73];
        for(var n=0;n<handler.Length;n++) m.InitializePhysical(0x9030u+(uint)n*2,handler[n],2);
        m.InitializePhysical(0x9020,0x4e73,2); m.InitializePhysical(0x6004,0x7c2a,2); m.InitializePhysical(0x6006,0x4e71,2);
        m.InitializePhysical(0x9090,0x0257,2);m.InitializePhysical(0x9092,0x3fff,2);m.InitializePhysical(0x9094,0x4e73,2);
        _=SyntheticExecution.Prepare(m,[0x4e71]);
        m.Core.State.SetUserStackPointer(0x7800); m.Core.State.SetInterruptStackPointer(0x4700); m.Core.State.SetMasterStackPointer(0x7400);
        m.Core.State.StatusRegister=(ushort)(trace|0x700|ccr|(bank is "ISP" or "MSP"?0x2000:0)|(bank is "user-M" or "MSP"?0x1000:0));
        m.Core.State.SetActiveStackPointer(bank=="MSP"?0x7400u:bank=="ISP"?0x4700u:0x7800u);
        m.Core.State.ProgramCounter=0x6001;
        Step(m,batch); var frame=m.Core.State.A[7];
        Step(m,batch); Step(m,batch); Step(m,batch);
        m.Bus.Accesses.Clear(); return frame;
    }
    private static void Check(SyntheticMachine m, ArchitecturalExpectation e)
    { var error=e.Verify(m); if(error!=null) throw new InvalidOperationException(error); }
    private static void Step(SyntheticMachine m,bool batch)
    {
        if(!batch){m.Core.ExecuteInstruction();return;}
        var boundary=new Boundary();
        if(((IM68kBatchCore)m.Core).ExecuteInstructions(1,null,boundary)!=1 || boundary.Before!=1 || boundary.After!=1)
            throw new InvalidOperationException("batch boundary count differs");
    }
    private sealed class Boundary:IM68kInstructionBoundary
    { public int Before,After; public bool BeforeInstruction(){Before++;return true;} public void AfterInstruction(long a,long b)=>After++; }
    private sealed class FaultBus:SparseRecordingBus,IM68kPhysicalAddressMap
    {
        private sealed class Request(uint at,int size,int nth,M68kBusAccessKind kind,bool repeat)
        {public uint Address=at;public int Width=size,Occurrence=nth;public M68kBusAccessKind Kind=kind;public bool Persistent=repeat;}
        private readonly List<Request> requests=[];
        public List<(uint Address,int Width)> Rejected {get;}=[];
        public void Disarm(){requests.Clear();Rejected.Clear();}
        public void Arm(uint at,int size,int nth,bool repeat=false){Disarm();requests.Add(new(at,size,nth,M68kBusAccessKind.CpuDataRead,repeat));}
        public void AlsoArm(uint at,int size,int nth,M68kBusAccessKind kind)=>requests.Add(new(at,size,nth,kind,false));
        public bool IsCpuPhysicalAddressMapped(uint at,int size,M68kBusAccessKind kind)
        {
            for(var n=0;n<requests.Count;n++)
            {
                var request=requests[n];
                if(kind!=request.Kind||unchecked(request.Address-at)>=size||request.Width!=size)continue;
                if(--request.Occurrence!=0)return true;
                if(request.Persistent)request.Occurrence=1;else requests.RemoveAt(n);
                Rejected.Add((at,size));return false;
            }
            return true;
        }
    }
}
