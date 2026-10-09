// Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT
// Untouched generated 010 RTE, generic and compatible, to exception callback.
// No native exception frames, IRQ, trace/run loop, format8 restart or timing oracle.
#include <cstdint>
#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
using uae_u16=uint16_t; using uae_u32=uint32_t; using uaecptr=uint32_t;
using uae_s16=int16_t; using uae_s32=int32_t;
#define REGPARAM2
#define CYCLE_UNIT 2
#define SPCFLAG_TRACE 0x40
#define SPCFLAG_DOTRACE 0x80
#define SPCFLAG_INT 8
#define SPCFLAG_DOINT 0x100
#define m68k_areg(r,n) ((r).a[n])
struct Regs { uint32_t d[8]{},a[8]{},usp{},isp{},msp{},pc{},spcflags{}; uint16_t sr{},ir{},irc{}; int s{},m{},t0{},t1{},intmask{},stopped{},ipl[1]{},ipl_pin{}; } regs;
struct Prefs { int cpu_model=68010,mmu_model{},cachesize{},cpu_compatible{},cpu_memory_cycle_exact{}; } currprefs;
bool m68k_accurate_ipl=false,hardware_bus_error=false;
int xflag,nflag,zflag,vflag,cflag,callbacks,callbackVector;
uint16_t frameSr,frameFormat;uint32_t framePc,frameBase;
std::vector<uint32_t> reads;
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
void activate_trace() { unset_special(SPCFLAG_TRACE);set_special(SPCFLAG_DOTRACE); }
void mmu_set_super(int) { throw std::runtime_error("Enabled MMU outside RTE profile"); }
uint32_t m68k_getpc(){return regs.pc;}
uint32_t m68k_getpci(){return regs.pc;}
void m68k_setpc_j(uint32_t pc){regs.pc=pc;}
void m68k_setpci_j(uint32_t pc){regs.pc=pc;}
void m68k_incpci(uint32_t n){regs.pc+=n;}
void Exception(int vector){if(vector!=8 && vector!=14)throw std::runtime_error("Unexpected RTE exception");if(++callbacks!=1)throw std::runtime_error("Repeated RTE exception");callbackVector=vector;}
void Exception_cpu(int vector){Exception(vector);}
void exception3_read_access(uint32_t,uint32_t,int,int){throw std::runtime_error("Odd stack outside profile");}
void exception3_read_prefetch(uint32_t,uint32_t){throw std::runtime_error("Odd successful return outside profile");}
void exception3_read_prefetch_only(uint32_t,uint32_t){throw std::runtime_error("Odd successful return outside profile");}
void exception2_read(uint32_t,uint32_t,int,int){throw std::runtime_error("Bus fault outside profile");}
void exception2_fetch_opcode(uint32_t,int,int){throw std::runtime_error("Fetch fault outside profile");}
uint16_t get_word(uint32_t address){
    reads.push_back(address);
    if(address==frameBase)return frameSr;
    if(address==frameBase+2)return static_cast<uint16_t>(framePc>>16);
    if(address==frameBase+4)return static_cast<uint16_t>(framePc);
    if(address==frameBase+6)return frameFormat;
    throw std::runtime_error("Unexpected frame/operand read");
}
uint16_t get_word_000(uint32_t address){return get_word(address);}
uint32_t get_long(uint32_t address){const auto high=get_word(address);const auto low=get_word(address+2);return (static_cast<uint32_t>(high)<<16)|low;}
uint16_t get_word_000_prefetch(int offset){if(regs.pc!=0x6000 || (offset!=0 && offset!=2))throw std::runtime_error("Unexpected prefetch");return regs.irc=0x4e71;}
#include "winuae-rte-format-sr.inc"
#include "winuae-rte-format-op.inc"
struct Outcome{uint32_t vector,sr,pc,sp;};
Outcome execute(uint16_t initial,uint16_t stacked,uint32_t target,uint16_t format,bool compatible){
    regs={};callbacks=callbackVector=0;reads.clear();
    regs.sr=initial;regs.s=(initial>>13)&1;regs.t1=(initial>>15)&1;regs.intmask=(initial>>8)&7;
    xflag=(initial>>4)&1;nflag=(initial>>3)&1;zflag=(initial>>2)&1;vflag=(initial>>1)&1;cflag=initial&1;
    regs.usp=0x7800;regs.isp=regs.s?0x4700:0x8000;regs.pc=0x1000;
    for(int n=0;n<8;n++){regs.d[n]=n==7?2:0xa55a0022u+n*0x100;regs.a[n]=n==7?(regs.s?0x4700:0x7800):0x4000+n*0x100;}
    frameSr=stacked;framePc=target;frameFormat=format;frameBase=regs.a[7];
    if(compatible)op_4e73_11_ff(0x4e73);else op_4e73_4_ff(0x4e73);
    MakeSR();
    if(callbackVector){
        const auto expected=regs.s?(compatible?std::vector<uint32_t>{frameBase,frameBase+6,frameBase+2}:std::vector<uint32_t>{frameBase,frameBase+2,frameBase+4,frameBase+6}):std::vector<uint32_t>{};
        if(reads!=expected || regs.a[7]!=frameBase || regs.usp!=0x7800 || regs.isp!=((initial&0x2000)?0x4700u:0x8000u) || regs.pc!=0x1000)throw std::runtime_error("Rejected RTE transport/state differs");
        for(int n=0;n<7;n++)if(regs.a[n]!=0x4000u+n*0x100)throw std::runtime_error("Rejected RTE changed address register");
        for(int n=0;n<8;n++)if(regs.d[n]!=(n==7?2:0xa55a0022u+n*0x100))throw std::runtime_error("Rejected RTE changed data register");
    }
    return {static_cast<uint32_t>(callbackVector),regs.sr,regs.pc,regs.a[7]};
}
void controls(){
    for(bool compatible:{false,true}){
        auto r=execute(0x271f,0xa000,0xffff6001,0x1000,compatible);
        if(r.vector!=14||r.sr!=0x2711||r.pc!=0x1000||r.sp!=0x4700)throw std::runtime_error("Low-format literal control failed");
        r=execute(0xa700,0x1f,0x6001,0xf024,compatible);
        if(r.vector!=14||r.sr!=0xa708||r.pc!=0x1000||r.sp!=0x4700)throw std::runtime_error("High-format literal control failed");
        r=execute(0x871f,0xa000,0xffff6001,0xf024,compatible);
        if(r.vector!=8||r.sr!=0x871f||r.pc!=0x1000||r.sp!=0x7800)throw std::runtime_error("Privilege literal control failed");
        r=execute(0x271f,0x2005,0x6000,0x24,compatible);
        if(r.vector!=0||r.sr!=0x2005||r.pc!=0x6000||r.sp!=0x4708)throw std::runtime_error("Valid short-frame literal control failed");
    }
}
int main(int argc,char**argv){
    try{
        controls();
        if(argc!=4||std::string(argv[3])!="573440")throw std::runtime_error("RTE audit requires fixture/output/count");
        std::ifstream input(argv[1]);std::ofstream output(argv[2]);if(!input||!output)throw std::runtime_error("Missing RTE fixture/output");
        std::string line;uint32_t index=0,passing=0,mismatching=0;
        while(std::getline(input,line)){
            std::istringstream row(line);std::vector<uint32_t> v;uint64_t value;
            while(row>>std::hex>>value){if(value>UINT32_MAX)throw std::runtime_error("Wide RTE field");v.push_back(static_cast<uint32_t>(value));}
            if(!row.eof()||v.size()!=16||v[0]!=index||index>=573440||(v[1]&~0xa71f)||(v[2]&~0xa71f)||v[4]>0xffff||v[4]>>12==0||v[4]>>12==8||v[12]>1||v[13]!=0x7800||v[14]!=((v[1]&0x2000)?0x4700u:0x8000u)||v[15]>1)throw std::runtime_error("Malformed/out-of-scope RTE fixture");
            if(v[15]!=1)throw std::runtime_error("Other RTE architectural state mismatched");
            const auto a=execute(static_cast<uint16_t>(v[1]),static_cast<uint16_t>(v[2]),v[3],static_cast<uint16_t>(v[4]),false);
            const auto b=execute(static_cast<uint16_t>(v[1]),static_cast<uint16_t>(v[2]),v[3],static_cast<uint16_t>(v[4]),true);
            if(a.vector!=b.vector||a.sr!=b.sr||a.pc!=b.pc||a.sp!=b.sp)throw std::runtime_error("Generic/compatible RTE disagree");
            if(a.vector!=v[5]||a.sr!=v[6]||a.pc!=0x1000||a.sp!=((v[1]&0x2000)?0x4700u:0x7800u))throw std::runtime_error("Declared RTE expectation differs from executed source");
            // Exception entry is composed using the documented format0 size and
            // repository vector fixtures. No native frame/trace-entry execution.
            const uint32_t sp=(v[1]&0x2000)?0x46f8:0x7ff8;
            const bool match=a.vector==v[7]&&a.sr==v[8]&&a.pc==v[9]&&sp==v[10]&&v[11]==0x9000+a.vector*0x10;
            if(match!=(v[12]!=0))throw std::runtime_error("Full synthetic/transport comparator differs");
            if(match)passing++;else mismatching++;
            output<<std::hex<<index++<<' '<<a.vector<<' '<<a.sr<<' '<<a.pc<<' '<<a.sp<<' '<<(match?1:0)<<'\n';
        }
        if(index!=573440)throw std::runtime_error("Empty/truncated RTE fixture");
        std::cout<<"cases="<<index<<" passing="<<passing<<" mismatching="<<mismatching<<" surroundingStatePassing="<<index<<" implementations=2 controls=8\n";
        return mismatching?1:0;
    }catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 2;}
}
