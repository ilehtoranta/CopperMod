// Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT
// Unchanged generated 060 STOP plus original SR/stop helpers on physical transport.
// Trace entry/frame/IRQ/run-loop behavior is not executed or qualified here.
#include <cstdint>
#include <fstream>
#include <sstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
using uae_u16=uint16_t; using uae_u32=uint32_t; using uae_s16=int16_t;
#define REGPARAM2
#define CYCLE_UNIT 2
#define SPCFLAG_TRACE 0x40
#define SPCFLAG_DOTRACE 0x80
#define SPCFLAG_INT 8
#define SPCFLAG_DOINT 0x100
#define LED_CPU 0
#define m68k_areg(r,n) ((r).a[n])
struct Regs { uint32_t d[8]{},a[8]{},usp{},isp{},msp{},pc{},spcflags{}; uint16_t sr{},ir{}; int s{},m{},t0{},t1{},intmask{},stopped{},ipl[1]{},ipl_pin{}; } regs;
struct Prefs { int cpu_model=68060,mmu_model{},cachesize{},cpu_compatible{},cpu_memory_cycle_exact{}; } currprefs;
struct Gui { int cpu_stopped{}; } gui_data;
int cpu_last_stop_vpos=-1,vpos{},xflag,nflag,zflag,vflag,cflag,callbacks,callbackVector,reads,cpucycleunit=1;
bool m68k_accurate_ipl=false;
#define SET_XFLG(v) (xflag=(v))
#define SET_NFLG(v) (nflag=(v))
#define SET_ZFLG(v) (zflag=(v))
#define SET_VFLG(v) (vflag=(v))
#define SET_CFLG(v) (cflag=(v))
#define GET_XFLG() xflag
#define GET_NFLG() nflag
#define GET_ZFLG() zflag
#define GET_VFLG() vflag
#define GET_CFLG() cflag
void set_special(int v) { regs.spcflags|=v; }
void unset_special(int v) { regs.spcflags&=~v; }
void activate_trace() { unset_special(SPCFLAG_TRACE); set_special(SPCFLAG_DOTRACE); }
void gui_led(int,int,int) {}
void mmu_set_super(int) { throw std::runtime_error("Enabled MMU outside STOP profile"); }
void checkint() {} // No asserted IRQ; interrupt/run-loop behavior is outside profile.
void do_cycles(int) {} // Host timing transport, not a timing oracle.
void x_do_cycles(int) { throw std::runtime_error("Compatible cycles outside profile"); }
uint16_t immediate;
uint16_t get_iword_mmu060(int offset) { if(offset!=2 || regs.pc!=0x1000) throw std::runtime_error("Unexpected instruction read"); reads++; return immediate; }
void Exception(int vector) { if(vector!=8 || ++callbacks!=1) throw std::runtime_error("Unexpected exception callback"); callbackVector=vector; }
#include "winuae-stop-sr.inc"
void m68k_setstopped(int type) { m68k_set_stop(type); }
#include "winuae-stop-op.inc"
struct Outcome { uint32_t vector,sr,pc,rawStopped; };
Outcome execute(uint16_t inputSr,uint16_t target,const std::vector<uint32_t>& values) {
    regs={}; gui_data={}; callbacks=callbackVector=reads=0;
    regs.sr=inputSr; regs.s=(inputSr>>13)&1; regs.m=(inputSr>>12)&1;
    regs.t1=(inputSr>>15)&1; regs.intmask=(inputSr>>8)&7;
    xflag=(inputSr>>4)&1;nflag=(inputSr>>3)&1;zflag=(inputSr>>2)&1;vflag=(inputSr>>1)&1;cflag=inputSr&1;
    regs.pc=0x1000;regs.usp=0x7800;regs.isp=regs.msp=0x4700;
    for(int n=0;n<8;n++){regs.d[n]=values[n];regs.a[n]=values[n+8];}
    if(regs.t1) set_special(SPCFLAG_TRACE);
    immediate=target; op_4e72_33_ff(0x4e72); MakeSR();
    if(callbackVector) {
        if(regs.stopped || regs.sr!=inputSr || regs.pc!=0x1000 || reads!=(regs.s?1:0)) throw std::runtime_error("Privilege callback transport differs");
        for(int n=0;n<8;n++)if(regs.d[n]!=values[n]||regs.a[n]!=values[n+8])throw std::runtime_error("Rejected STOP changed registers");
        if(regs.usp!=0x7800||regs.isp!=0x4700)throw std::runtime_error("Rejected STOP changed stacks");
        return {8,regs.sr,regs.pc,static_cast<uint32_t>(regs.stopped)};
    }
    if(reads!=1 || regs.stopped!=1 || regs.pc!=0x1000) throw std::runtime_error("STOP transport differs");
    // The original stopped PC remains at the opcode until resume. The manual
    // architectural next PC is +4. Incoming trace composes a post-load boundary;
    // full native trace processing/frame writes are deliberately not simulated.
    const auto result=Outcome{static_cast<uint32_t>((inputSr&0x8000)?9:0),regs.sr,0x1004,1};
    const auto oldReads=reads; op_4e72_33_ff(0x4e72); MakeSR();
    if(reads!=oldReads || regs.sr!=result.sr || regs.pc!=0x1000 || regs.stopped!=1 || callbacks) throw std::runtime_error("Stopped re-entry changed state");
    for(int n=0;n<8;n++) if(regs.d[n]!=values[n] || regs.a[n]!=values[n+8]) throw std::runtime_error("STOP changed unrelated registers");
    if(regs.usp!=0x7800||regs.isp!=0x4700)throw std::runtime_error("STOP changed stacks");
    return result;
}
std::vector<uint32_t> defaults(uint16_t sr) {
    std::vector<uint32_t> v;
    for(int n=0;n<8;n++)v.push_back(n==7?2:0xa55a0022u+n*0x100);
    for(int n=0;n<8;n++)v.push_back(n==7?((sr&0x2000)?0x4700:0x7800):0x4000+n*0x100);
    return v;
}
void controls() {
    const uint16_t inputs[]={0x271f,0x001f,0x271f,0xa71f},targets[]={0x2010,0x2710,0x0010,0x3010};
    const uint32_t vectors[]={0,8,8,9},statuses[]={0x2010,0x001f,0x271f,0x3010},pcs[]={0x1004,0x1000,0x1000,0x1004};
    for(int n=0;n<4;n++){const auto r=execute(inputs[n],targets[n],defaults(inputs[n]));if(r.vector!=vectors[n]||r.sr!=statuses[n]||r.pc!=pcs[n])throw std::runtime_error("Literal STOP control failed");}
}
int main(int argc,char**argv) {
    try {
        controls();
        if(argc!=4 || std::string(argv[3])!="163840")throw std::runtime_error("STOP audit needs fixture/output/count");
        std::ifstream input(argv[1]);std::ofstream output(argv[2]);if(!input||!output)throw std::runtime_error("Missing STOP fixture/output");
        std::string line;uint32_t index=0,passing=0,mismatching=0;
        while(std::getline(input,line)) {
            std::istringstream row(line);std::vector<uint32_t> v;uint64_t value;
            while(row>>std::hex>>value){if(value>UINT32_MAX)throw std::runtime_error("Wide STOP field");v.push_back(static_cast<uint32_t>(value));}
            if(!row.eof()||v.size()!=27||v[0]!=index||index>=163840||(v[1]&~0xb71f)||(v[2]&~0xb71f)||v[9]!=0x7800||v[10]!=0x4700)throw std::runtime_error("Malformed/out-of-scope STOP fixture");
            const auto initial=defaults(static_cast<uint16_t>(v[1]));
            for(int n=0;n<16;n++)if(v[n+11]!=initial[n])throw std::runtime_error("STOP initial registers differ from canonical fixture");
            const auto r=execute(static_cast<uint16_t>(v[1]),static_cast<uint16_t>(v[2]),std::vector<uint32_t>(v.begin()+11,v.end()));
            if(r.vector!=v[3]||r.sr!=v[4]||r.pc!=v[5])throw std::runtime_error("Declared software expectation differs from executed STOP");
            const bool match=r.vector==v[6]&&r.sr==v[7]&&r.pc==v[8];if(match)passing++;else mismatching++;
            output<<std::hex<<index++<<' '<<r.vector<<' '<<r.sr<<' '<<r.pc<<' '<<r.rawStopped<<' '<<(match?1:0)<<'\n';
        }
        if(index!=163840)throw std::runtime_error("Empty/truncated STOP fixture");
        std::cout<<"cases="<<index<<" passing="<<passing<<" mismatching="<<mismatching<<" controls=4\n";
        return mismatching?1:0;
    } catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 2;}
}
