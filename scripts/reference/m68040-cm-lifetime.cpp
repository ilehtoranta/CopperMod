// Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT
// Untouched WinUAE MMU RTE/MOVEM and repair instructions on physical memory.
// Default: composed exception boundary. WINUAE_EXECUTED_MMU_EXCEPTION uses
// untouched exception dispatch/frame construction on physical transport.
// Neither profile qualifies enabled MMU, trace, run-loop timing or hardware.
#include <cstdint>
#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>
using uae_u16=uint16_t; using uae_u32=uint32_t;
using uae_s16=int16_t; using uae_s32=int32_t; using uaecptr=uint32_t;
#define REGPARAM2
#define CYCLE_UNIT 2
#define MMUDEBUGMISC 0
#define MMU_SSW_CM 0x1000
#define MMU_SSW_CT 0x2000
#define SPCFLAG_MMURESTART 0x010000
#define m68k_areg(r,n) ((r).a[n])
#define m68k_dreg(r,n) ((r).d[n])
struct Regs {
    uint32_t d[8]{},a[8]{},usp{},isp{},msp{},pc{},instruction_pc{}; uint16_t sr{};
    int m{},s{},t0{},t1{},intmask{},ipl[1]{};
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
    uint32_t vbr{},buscr{},instruction_pc_user_exception{},mmu_ssw{},mmu_fslw{},mmu_fault_addr{};
    uint32_t fp_ea{},fp_opword{},wb2_address{},wb3_data{},mmu_effective_addr{};
    int exception{},loop_mode{},ipl_pin{},stopped{},ir{},ird{},read_buffer{},write_buffer{},irc{},wb2_status{},wb3_status{};
    int prefetch020_valid[3]{},pipeline_r8[2]{},pipeline_pos{},pipeline_stop{},prefetch020[3]{};
#endif
} regs;
struct Prefs {
    int cpu_model=68040;
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
    int mmu_model=68040,cachesize{},cpu_compatible{},cpu_memory_cycle_exact{};
#endif
} currprefs;
int flag_SPCFLAG_TRACE,cpu_stopped,xflag,nflag,zflag,vflag,cflag;
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
#define CLEAR_CZNV() (cflag=zflag=nflag=vflag=0)
void activate_trace() { throw std::runtime_error("Trace is outside this discovery"); }
void check_t0_trace() { throw std::runtime_error("T0 is outside this discovery"); }
bool mmu_restart; int mmu040_movem,specialFlags; uint32_t mmu040_movem_ea;
int movem_index1[256],movem_next[256];
void set_special(int flags) { specialFlags|=flags; }
std::unordered_map<uint32_t,uint8_t> memory;
uint32_t read(uint32_t address,int width) {
    uint32_t value=0;
    for(int n=0;n<width;n++) {
        const auto it=memory.find(address+n);
        if(it==memory.end()) throw std::runtime_error("Uninitialized physical read");
        value=(value<<8)|it->second;
    }
    return value;
}
void put(uint32_t address,uint32_t value,int width) {
    for(int n=0;n<width;n++) memory[address+n]=static_cast<uint8_t>(value>>(8*(width-1-n)));
}
uint16_t get_word_mmu040(uint32_t a) { return static_cast<uint16_t>(read(a,2)); }
uint32_t get_long_mmu040(uint32_t a) { return read(a,4); }
void put_word_mmu040(uint32_t a,uint16_t v) { put(a,v,2); }
void put_long_mmu040(uint32_t a,uint32_t v) { put(a,v,4); }
uint16_t get_iword_mmu040(int offset) { return get_word_mmu040(regs.pc+offset); }
uint32_t get_ilong_mmu040(int offset) { return get_long_mmu040(regs.pc+offset); }
uint32_t m68k_getpci() { return regs.pc; }
void m68k_setpci(uint32_t value) { regs.pc=value; }
void m68k_incpci(int offset) { regs.pc+=offset; }
#ifndef WINUAE_EXECUTED_MMU_EXCEPTION
void Exception(int) { throw std::runtime_error("Unexpected privilege exception"); }
#else
void REGPARAM2 Exception(int);
#endif
void Exception_cpu(int) { throw std::runtime_error("Unexpected format exception"); }
int callbackCount,cmAtCallback; uint16_t secondarySr; uint32_t oddPc;
#ifndef WINUAE_EXECUTED_MMU_EXCEPTION
void exception3_read_prefetch_68040bug(uint32_t opcode,uint32_t address,uint16_t sr) {
    if(opcode!=0x4e73 || address!=0x6001) throw std::runtime_error("Unexpected odd-PC callback");
    callbackCount++; secondarySr=sr; oddPc=address; cmAtCallback=mmu040_movem;
}
#else
// Transport declarations used by untouched functions. Other exception paths
// fail explicitly; no architectural frame or saved SR is composed here.
#define _T(v) v
#define MAX_MMU030_ACCESS 9
#define MMU030_STATEFLAG1_FMOVEM 0x2000
#define MMU030_SSW_RW 0x40
#define CACHE_DISABLE_ALLOCATE 0x08
#define CPU_HALT_DOUBLE_FAULT 2
#define SPCFLAG_TRACE 0x000040
#define SPCFLAG_DOTRACE 0x000080
#define SPCFLAG_INT 0x000008
#define SPCFLAG_DOINT 0x000100
#define sz_word 1
bool m68k_accurate_ipl=false,cpu_tracer=false,transportSupervisor;
struct { int state; } cputrace;
uint32_t cache_default_data,last_fault_for_exception_3,last_addr_for_exception_3,last_op_for_exception_3;
int last_di_for_exception_3,last_fc_for_exception_3,last_size_for_exception_3,cpucycleunit=2;
bool last_writeaccess_for_exception_3,last_notinstruction_for_exception_3;
uint16_t last_sr_for_exception3;
uint32_t mmu040_move16[4],mmu030_state[3],mmu030_fmovem_store[2],mmu030_disp_store[2],mm030_stageb_address;
int mmu030_idx,mmu030_idx_done,mmu030_opcode;
struct Mmu030Access { uint32_t val; } mmu030_ad[MAX_MMU030_ACCESS+1];
uint32_t m68k_getpc() { return regs.pc; }
void unset_special(int flags) { specialFlags&=~flags; }
void mmu_set_super(int value) { transportSupervisor=value!=0; }
void m68k_resumestopped() { if(regs.stopped) throw std::runtime_error("STOP outside exception profile"); }
void x_do_cycles(int) {} // Host cycle transport; timing is not compared.
uint32_t x_get_long(uint32_t a) {
    if(regs.exception!=3 || a!=regs.vbr+12) throw std::runtime_error("Unexpected exception vector read");
    callbackCount++;cmAtCallback=mmu040_movem;
    return get_long_mmu040(a);
}
void x_put_long(uint32_t a,uint32_t value) { put_long_mmu040(a,value); }
void x_put_word(uint32_t a,uint32_t value) { put(a,value,2); }
uint16_t x_get_word(uint32_t) { throw std::runtime_error("Non-040 prefetch outside profile"); }
void reset_pipeline_state() { throw std::runtime_error("Compatible pipeline outside profile"); }
void fill_icache040(uint32_t) { throw std::runtime_error("Compatible cache outside profile"); }
void fill_prefetch_020() { throw std::runtime_error("020 prefetch outside profile"); }
void fill_prefetch_030() { throw std::runtime_error("030 prefetch outside profile"); }
int iack_cycle(int) { throw std::runtime_error("Interrupt outside exception profile"); }
void cpu_halt(int) { throw std::runtime_error("Double fault outside exception profile"); }
void write_log(const char*,...) { throw std::runtime_error("Unexpected reference diagnostic"); }
void Exception_mmu030(int,uint32_t) { throw std::runtime_error("030 outside exception profile"); }
void Exception_normal(int) { throw std::runtime_error("Non-MMU dispatch outside exception profile"); }
void Exception_build_stack_frame_common(uint32_t,uint32_t,uint32_t,int,int) { throw std::runtime_error("Non-address frame outside exception profile"); }
static void exception3_read_special(uint32_t,uint32_t,int,int);
#endif
// Strictly extracted unchanged from pinned source/generated output.
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
#include "winuae-cm-native-sr.inc"
#include "winuae-cm-exception.inc"
#else
#include "winuae-cm-sr.inc"
#endif
#include "winuae-cm-rte-helper.inc"
#include "winuae-cm-ops.inc"

std::vector<uint32_t> snapshot() {
    MakeSR();
    std::vector<uint32_t> v{regs.pc,regs.sr,
        regs.s?regs.usp:regs.a[7],regs.s&&!regs.m?regs.a[7]:regs.isp,
        regs.s&&regs.m?regs.a[7]:regs.msp};
    v.insert(v.end(),regs.d,regs.d+8); v.insert(v.end(),regs.a,regs.a+8);
    return v;
}
void step(uint32_t opcode,uae_u32 (*fn)(uae_u32)) {
    MakeSR(); mmu_restart=true; regs.instruction_pc=regs.pc;
    if(read(regs.pc,2)!=opcode) throw std::runtime_error("Instruction sentinel differs");
    fn(opcode);
}
// Explicitly composed boundary. Does not invoke WinUAE Exception_mmu.
#ifndef WINUAE_EXECUTED_MMU_EXCEPTION
void composeAddressError() {
    regs.sr=static_cast<uint16_t>((regs.sr|0x2000)&~0xc000);
    MakeFromSR(); regs.a[7]-=12;
    put(regs.a[7],secondarySr,2); put(regs.a[7]+2,0x1000,4);
    put(regs.a[7]+6,0x200c,2); put(regs.a[7]+8,oddPc&~1u,4);
    regs.pc=0x9190;
}
#endif
std::vector<uint32_t> entryFrame;
std::vector<std::vector<uint32_t>> execute(const std::vector<uint32_t>& f) {
    if(f.size()!=8 || f[1]>1 || f[2]>3 || f[3]>1 || f[4]>1 || f[5]>1 ||
        (f[6]!=0 && f[6]!=0x10000) || f[7]>31) throw std::runtime_error("Out-of-scope fixture");
    const bool odd=f[3]!=0,cm=f[4]!=0;
    const uint16_t initialSr=static_cast<uint16_t>((f[1]?0x3000:0x2000)|f[7]);
    const uint16_t resultSr=static_cast<uint16_t>((f[2]==1?0x1000:f[2]==2?0x2000:f[2]==3?0x3000:0)|(f[7]^31));
    regs={}; memory.clear(); mmu040_movem=0; mmu040_movem_ea=0;
    callbackCount=cmAtCallback=specialFlags=cpu_stopped=flag_SPCFLAG_TRACE=0;
    for(int n=0;n<8;n++) {regs.d[n]=0xa55a0022u+n*0x100;regs.a[n]=0x4000u+n*0x100;}
    regs.d[7]=2; regs.usp=0x7800+f[5]; regs.isp=0x4700+f[5]; regs.msp=0x7400+f[5];
    regs.sr=initialSr; regs.s=1; regs.m=static_cast<int>(f[1]); regs.a[7]=f[1]?regs.msp:regs.isp; regs.pc=0x1000;
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
    transportSupervisor=true;
#endif
    MakeFromSR();
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
    regs.vbr=f[6];cache_default_data=0;
    last_fault_for_exception_3=last_addr_for_exception_3=last_op_for_exception_3=0;
    last_di_for_exception_3=last_fc_for_exception_3=last_size_for_exception_3=0;last_sr_for_exception3=0;
#endif
    for(const auto p:{regs.usp,regs.isp,regs.msp}) for(int n=-20;n<112;n++) put(p+n,static_cast<uint8_t>(n^0x5a),1);
    const auto frame=regs.a[7];
    put(frame,resultSr,2); put(frame+2,odd?0x6001:0x6000,4); put(frame+6,0x7008,2);
    put(frame+8,0x4200,4); put(frame+12,cm?0x1005:0x0105,2);
    put(0x1000,0x4e73,2); put(f[6]+12,0x9190,4);
    put(0x6000,0x4cfa,2); put(0x6002,3,2); put(0x6004,0x0ffc,2); put(0x6006,0x4e71,2);
    put(0x9190,0x3ebc,2); put(0x9192,resultSr,2);
    put(0x9194,0x2f7c,2); put(0x9196,0x6000,4); put(0x919a,2,2); put(0x919c,0x4e73,2);
    put(0x4200,0x89abcdef,4); put(0x4204,0x12345678,4);
    put(0x7000,0xdeadbeef,4); put(0x7004,0x789abcde,4);
    std::vector<std::vector<uint32_t>> states;
    step(0x4e73,op_4e73_31_ff);
    if(callbackCount!=(odd?1:0) || mmu040_movem!=(cm?1:0) || (odd && cmAtCallback!=(cm?1:0)))
        throw std::runtime_error("RTE callback/CM lifetime differs");
#ifndef WINUAE_EXECUTED_MMU_EXCEPTION
    if(odd) composeAddressError();
#endif
    entryFrame=odd?std::vector<uint32_t>{regs.a[7],read(regs.a[7],2),read(regs.a[7]+2,4),read(regs.a[7]+6,2),read(regs.a[7]+8,4)}:std::vector<uint32_t>(5,0);
    if(odd) {
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
        if(entryFrame[1]!=resultSr || last_sr_for_exception3!=initialSr || last_fault_for_exception_3!=0x6000 || !transportSupervisor)
            throw std::runtime_error("Executed MMU frame/SR control failed");
#else
        if(entryFrame[1]!=initialSr) throw std::runtime_error("Composed frame/SR control failed");
#endif
        if(entryFrame[2]!=0x1000 || entryFrame[3]!=0x200c || entryFrame[4]!=0x6000)
            throw std::runtime_error("Address frame control failed");
    }
    states.push_back(snapshot());
    if(odd) {
        step(0x3ebc,op_30bc_31_ff); states.push_back(snapshot());
        step(0x2f7c,op_217c_31_ff); states.push_back(snapshot());
        step(0x4e73,op_4e73_31_ff); states.push_back(snapshot());
    }
    if(regs.pc!=0x6000 || mmu040_movem!=(cm?1:0)) throw std::runtime_error("Repair lost native CM state");
    step(0x4cfa,op_4cfa_31_ff); states.push_back(snapshot());
    if(regs.pc!=0x6006 || mmu040_movem || regs.d[0]!=(cm?0x89abcdefu:0xdeadbeefu) ||
        regs.d[1]!=(cm?0x12345678u:0x789abcdeu)) throw std::runtime_error("MOVEM address/lifetime control failed");
    step(0x4e71,op_4e71_31_ff); states.push_back(snapshot());
    if(regs.pc!=0x6008 || callbackCount!=(odd?1:0) || states.size()!=(odd?6u:3u)) throw std::runtime_error("Following sentinel failed");
    return states;
}
std::vector<uint32_t> parse(const std::string& text) {
    std::istringstream stream(text); std::string token; std::vector<uint32_t> values;
    while(stream>>token) {size_t end=0;auto value=std::stoull(token,&end,16);
        if(end!=token.size() || value>UINT32_MAX) throw std::runtime_error("Invalid hexadecimal input");
        values.push_back(static_cast<uint32_t>(value));}
    return values;
}
int main(int argc,char** argv) {
    try {
        for(int mask=1;mask<256;mask++) {int bit=0;while(!(mask&(1<<bit))) bit++;movem_index1[mask]=bit;movem_next[mask]=mask&~(1<<bit);}
        for(uint32_t odd=0;odd<2;odd++) for(uint32_t cm=0;cm<2;cm++) execute({0,0,0,odd,cm,0,0x10000,31});
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
        if(argc!=6) throw std::runtime_error("Usage: observer rows output expected-rows summary frames");
        std::ifstream frameInput(argv[5]);if(!frameInput) throw std::runtime_error("Missing actual frame input");
#else
        if(argc!=5) throw std::runtime_error("Usage: observer rows output expected-rows summary");
#endif
        auto required=std::stoul(argv[3]); if(!required) throw std::runtime_error("Empty required selection");
        std::ifstream input(argv[1]); std::ofstream output(argv[2]);
        if(!input || !output) throw std::runtime_error("Missing input/output");
        std::string line; size_t rows=0,phases=0,passing=0,mismatches=0,untested=0,frameMismatches=0,frameComparisons=0;
        while(std::getline(input,line)) {
            auto split=line.find(" | "); if(split==std::string::npos) throw std::runtime_error("Missing actual state");
            auto fixture=parse(line.substr(0,split)),actual=parse(line.substr(split+3));
            if(fixture.empty() || fixture[0]!=rows) throw std::runtime_error("Missing/reordered fixture");
            auto expected=execute(fixture);
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
            std::string frameLine;if(!std::getline(frameInput,frameLine)) throw std::runtime_error("Missing actual frame rows");
            auto frameActual=parse(frameLine);
            if(frameActual.size()!=6 || frameActual[0]!=rows) throw std::runtime_error("Missing/reordered actual frame");
            if(fixture[3]) frameComparisons++;
            if(std::vector<uint32_t>(frameActual.begin()+1,frameActual.end())!=entryFrame) frameMismatches++;
#endif
            if(actual.empty() || !actual[0] || actual[0]>expected.size() || actual.size()!=1+actual[0]*21)
                throw std::runtime_error("Missing/invalid CPU phase states");
            output<<std::hex<<rows<<" cmCallback="<<cmAtCallback<<" phases="<<expected.size();
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
            output<<" frame=";for(auto value:entryFrame) output<<' '<<value;
#endif
            for(size_t n=0;n<expected.size();n++) {
                phases++; output<<" |"; for(auto value:expected[n]) output<<' '<<value;
                if(n>=actual[0]) {untested++;continue;}
                std::vector<uint32_t> observed(actual.begin()+1+n*21,actual.begin()+1+(n+1)*21);
                if(observed==expected[n]) passing++;
                else {if(mismatches<20) std::cerr<<"Mismatch row "<<std::dec<<rows<<" phase "<<n<<'\n';mismatches++;}
            }
            output<<'\n'; rows++;
        }
        if(rows!=required) throw std::runtime_error("Missing reference rows");
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
        if(std::getline(frameInput,line)) throw std::runtime_error("Unexpected extra frame rows");
#endif
        std::ofstream summary(argv[4]); if(!summary) throw std::runtime_error("Missing summary output");
        summary<<"{\"rows\":"<<rows<<",\"phases\":"<<phases<<",\"passing\":"<<passing<<",\"mismatching\":"<<mismatches<<",\"untested\":"<<untested<<",\"fixedControls\":4,\"frameMismatches\":"<<frameMismatches<<",\"frameComparisons\":"<<frameComparisons;
#ifdef WINUAE_EXECUTED_MMU_EXCEPTION
        summary<<",\"composedExceptionBoundary\":false,\"mmuExceptionEntryExecuted\":true";
#else
        summary<<",\"composedExceptionBoundary\":true,\"mmuExceptionEntryExecuted\":false";
#endif
        summary<<",\"fullExceptionOracle\":false,\"hardwareQualified\":false}\n";
        std::cout<<rows<<" rows, "<<phases<<" phases, "<<mismatches<<" mismatches, "<<untested<<" untested; 4 literal controls\n";
        return mismatches||untested||frameMismatches?1:0;
    } catch(const std::exception& error) {std::cerr<<error.what()<<'\n';return 2;}
}
