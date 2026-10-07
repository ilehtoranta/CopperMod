// Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT
// 030 software discovery: MOVE.L (A7)+ faults, guest saved-S/M edits and RTE.
// Unchanged generated operations, fixup helpers and retry loop; flat physical
// transport. Passive snapshots expose the inverse-fixup ordering discrepancy.
// Trace is disabled. No enabled translation, IRQ, cache/pipeline timing,
// physical function-code spaces or architectural changed-bank qualification.
#include <cstdint>
#include <iostream>
#include <string>
#include <fstream>
#include <cstdarg>
#include <cstdio>
#include <stdexcept>
#include <unordered_map>
#include <vector>
using uae_u8=uint8_t;using uae_u16=uint16_t;using uae_u32=uint32_t;
using uae_s8=int8_t;using uae_s16=int16_t;using uae_s32=int32_t;using uaecptr=uint32_t;
#define REGPARAM2
#define STATIC_INLINE static inline
#define CYCLE_UNIT 2
#define _T(x) x
#define MMUDEBUG 0
#define TRY(x) try
#define CATCH(x) catch(int x)
#define ENDTRY
#define STOPTRY
#define THROW(x) throw int(x)
#define SPCFLAG_TRACE 0x40
#define SPCFLAG_DOTRACE 0x80
#define SPCFLAG_TRAP 0x400
#define SPCFLAG_INT 8
#define SPCFLAG_DOINT 0x100
#define CPU_HALT_CPU_STUCK 1
#define CPU_HALT_DOUBLE_FAULT 2
#define sz_byte 0
#define sz_word 1
#define sz_long 2
#define m68k_areg(r,n) ((r).a[n])
#define m68k_dreg(r,n) ((r).d[n])
struct Regs {
 uae_u32 a[8]{},d[8]{},usp{},isp{},msp{},pc{},instruction_pc{},trace_pc{},vbr{},mmu_ssw{},mmu_fault_addr{},sfc{},dfc{},wb3_data{},wb2_address{},fp_ea{},fp_opword{},mmu_effective_addr{},pcr{},fp_unimp_ins{},ir{};
 uae_u16 sr{};int m{},s{},t0{},t1{},intmask{},ipl[1]{},ipl_pin{},stopped{},spcflags{},opcode{},irc{},instruction_cnt{},wb2_status{},wb3_status{},read_buffer{},write_buffer{},fpu_exp_pre{},fp_unimp_pend{};
 int prefetch020_valid[3]{},prefetch020[3]{},pipeline_r8[2]{},pipeline_pos{},pipeline_stop{};
}regs;
struct Prefs{int cpu_model=68030,mmu_model=68030,cachesize{},cpu_compatible{},cpu_cycle_exact{},fpu_model{};}currprefs;
struct flag_struct{int cznv{},x{};}regflags;
int xflag,nflag,zflag,vflag,cflag;
#define SET_XFLG(x) (xflag=(x))
#define SET_NFLG(x) (nflag=(x))
#define SET_ZFLG(x) (zflag=(x))
#define SET_VFLG(x) (vflag=(x))
#define SET_CFLG(x) (cflag=(x))
#define GET_XFLG() xflag
#define GET_NFLG() nflag
#define GET_ZFLG() zflag
#define GET_VFLG() vflag
#define GET_CFLG() cflag
#define CLEAR_CZNV() (cflag=zflag=nflag=vflag=0)
bool m68k_accurate_ipl=false,mmu_debugger=false,ismoves030=false,islrmw030=false,bBusErrorReadWrite=false,mmu030_retry=false;
int mmu030_idx,mmu030_idx_done,mmu030_opcode,mmu030_opcode_stageb,mmu030_fake_prefetch,cpu_cycles;
uaecptr mmu030_fake_prefetch_addr,mm030_stageb_address,last_fault_for_exception_3;
uae_u16 mmu030_state[3];uae_u32 mmu030_data_buffer_out,mmu030_disp_store[2],mmu030_fmovem_store[2],mmu040_move16[4];
struct mmu030_access{uae_u32 val;};mmu030_access mmu030_ad[10];
struct mmufixup{int reg=-1;uae_u32 value{};};mmufixup mmufixup[2];
std::unordered_map<uaecptr,uae_u8> mem;
std::vector<int> events;std::vector<uae_u8> originalFrame,editedFrame;
bool operandContext=false;bool faultOnce;int attempts,reads,steps,traceCount;uae_u32 tracePc,traceD7;int returnT1;
int activeCase=-1;
std::ofstream diagnostics("trace.log");
void snapshot(const char* phase){diagnostics<<activeCase<<' '<<phase<<" S="<<regs.s<<" M="<<regs.m<<std::hex<<" A7="<<regs.a[7]<<" USP="<<regs.usp<<" ISP="<<regs.isp<<" MSP="<<regs.msp<<" fixup="<<mmufixup[0].reg<<":"<<mmufixup[0].value<<" WB2="<<regs.wb2_status<<" PC="<<regs.pc<<std::dec<<'\n';}
[[noreturn]] void unavailable_at(int line,const char* function){throw std::runtime_error(std::string("Out-of-scope path: ")+function+":"+std::to_string(line));}
#define unavailable() unavailable_at(__LINE__,__func__)
void set_special(int x){regs.spcflags|=x;}void unset_special(int x){regs.spcflags&=~x;}
void mmu_set_super(int){};
uaecptr m68k_getpc(){return regs.pc;}uaecptr m68k_getpci(){return regs.pc;}
void m68k_setpci(uaecptr x){regs.pc=x;}void m68k_incpci(int n){regs.pc+=n;}
void mmu030_hardware_bus_error(uaecptr,uae_u32,bool,bool,int);
uae_u32 read(uaecptr a,int w){

 uae_u32 v=0;for(int n=0;n<w;n++){auto it=mem.find(a+n);if(it==mem.end())throw std::runtime_error("Uninitialized read");v=(v<<8)|it->second;}return v;
}
void put(uaecptr a,uae_u32 v,int w){for(int n=0;n<w;n++)mem[a+n]=uae_u8(v>>(8*(w-1-n)));}
uae_u32 operand_read(uaecptr a){if(a!=0x4200)unavailable();attempts++;if(faultOnce){faultOnce=false;events.push_back(1);mmu030_hardware_bus_error(a,0,true,false,sz_long);}reads++;events.push_back(4);return read(a,4);}
uae_u32 uae_mmu030_get_long(uaecptr a){return operandContext?operand_read(a):read(a,4);}uae_u32 uae_mmu030_get_word(uaecptr a){return read(a,2);}uae_u32 uae_mmu030_get_iword(uaecptr a){return read(a,2);}
void uae_mmu030_put_word(uaecptr a,uae_u32 v){put(a,v,2);}
uae_u32 get_word_mmu030(uaecptr a){return read(a,2);}uae_u32 get_long_mmu030(uaecptr a){return read(a,4);}
uae_u32 x_get_long(uaecptr a){return read(a,4);}void x_put_long(uaecptr a,uae_u32 v){put(a,v,4);}void x_put_word(uaecptr a,uae_u32 v){put(a,v,2);}
uae_u32 uae_mmu030_get_long_fcx(uaecptr a,int fc){if(fc!=1&&fc!=5)unavailable();return a==0x4200?operand_read(a):read(a,4);}
uae_u32 uae_mmu030_get_word_fcx(uaecptr,int){unavailable();}uae_u32 uae_mmu030_get_byte_fcx(uaecptr,int){unavailable();}
void uae_mmu030_put_long_fcx(uaecptr,uae_u32,int){unavailable();}void uae_mmu030_put_word_fcx(uaecptr,uae_u32,int){unavailable();}void uae_mmu030_put_byte_fcx(uaecptr,uae_u32,int){unavailable();}
uae_u32 mmu030_get_generic(uaecptr,uae_u32,int,int){unavailable();}void mmu030_put_generic(uaecptr,uae_u32,uae_u32,int,int){unavailable();}
template<class F>void mmu030_unaligned_read_continue(uaecptr,int,F){unavailable();}template<class F>void mmu030_unaligned_write_continue(uaecptr,int,F){unavailable();}
void unalign_clear(){unavailable();}void exception3_read_prefetch(uae_u32,uaecptr){unavailable();}
void exception3_read_special(uae_u32,uaecptr,int,int){unavailable();}
void write_log(const char* format,...){char buffer[512];va_list args;va_start(args,format);vsnprintf(buffer,sizeof(buffer),format,args);va_end(args);diagnostics<<activeCase<<" native-log "<<buffer;snapshot("after-native-log");}void cpu_halt(int){unavailable();}int iack_cycle(int){unavailable();}
void exception_debug(int){}void fill_prefetch(){} // compatible/cache transport disabled.
void Exception(int);void Exception_cpu(int){unavailable();}
#include "constants.inc"
#include "sr.inc"
#include "access.inc"
#include "fixup.inc"
#include "fault.inc"
#include "frame.inc"
#include "rte.inc"
#include "ops.inc"
void Exception(int nr){
 snapshot("before-exception");
 if(nr!=2&&nr!=9)unavailable();
 if(nr==9){traceCount++;tracePc=regs.pc;traceD7=regs.d[7];events.push_back(7);}
 Exception_mmu030(nr,regs.instruction_pc);snapshot("after-exception");
 if(nr==2){events.push_back(2);originalFrame.clear();for(int n=0;n<92;n++)originalFrame.push_back(mem.at(regs.a[7]+n));}
}
uae_u32 (*cpufunctbl[65536])(uae_u32);void (*cpufunctbl_noret[65536])(uae_u32);
void check_halt(){}void check_debugger(){}void count_instr(int){}void do_cycles(int){}int adjust_cycles(int x){return x;}void wait_memory_cycles(){unavailable();}bool time_for_interrupt(){unavailable();}
uaecptr mmu030_translate(uaecptr,bool,bool,bool){unavailable();}
struct Done{};
uae_u32 x_prefetch(int n){if(regs.pc==0x1006)throw Done{};if(++steps>32)throw std::runtime_error("Bound exceeded");return read(regs.pc+n,2);}
int do_specialties(int){
 const int spcflags=regs.spcflags;
 if(spcflags&~(SPCFLAG_TRACE|SPCFLAG_DOTRACE))unavailable();
 // Exact trace fragment of reference do_specialties; IRQ/host branches excluded.
#include "special-trace.inc"
 return regs.pc==0x6000||regs.pc==0x1006;
}
uae_u32 dispatch(uae_u32 op){
 if(op==0x3ebc){events.push_back(3);auto v=op_30bc_32_ff(op);editedFrame.clear();for(int n=0;n<92;n++)editedFrame.push_back(mem.at(regs.a[7]+n));return v;}
 if(op==0x4e73){events.push_back(5);snapshot("before-RTE");auto value=op_4e73_32_ff(op);snapshot("after-RTE");return value;}
 if(op==0x201f){snapshot("before-MOVE");operandContext=true;try{auto value=op_2018_32_ff(op);operandContext=false;snapshot("after-MOVE");return value;}catch(...){operandContext=false;throw;}}
 if(op==0x7e2a){events.push_back(6);return op_7000_32_ff(op);}
 if(op==0x4e71)return op_4e71_32_ff(op);
 unavailable();
}
#include "loop.inc"

int main(){try{
 for(int id=0;id<20;id++){activeCase=id;
  const bool fault=id<16;const int initial=fault?id/4:id-16,returned=fault?id%4:initial;
  regs={};mem.clear();events.clear();originalFrame.clear();editedFrame.clear();faultOnce=fault;attempts=reads=steps=traceCount=0;tracePc=traceD7=0;regflags={};xflag=nflag=zflag=vflag=cflag=0;
  mmu030_state[0]=mmu030_state[1]=mmu030_state[2]=0;mmu030_idx=mmu030_idx_done=0;mmu030_retry=false;mmu030_data_buffer_out=0;mmufixup[0].reg=mmufixup[1].reg=-1;
  regs.usp=0x7000;regs.isp=0x8000;regs.msp=0x9000;
  if(initial<2)regs.usp=0x4200;else if(initial==2)regs.isp=0x4200;else regs.msp=0x4200;
  regs.s=initial>=2;regs.m=initial==1||initial==3;regs.intmask=7;regs.a[7]=0x4200;regs.pc=regs.instruction_pc=0x1000;
  put(0x1000,0x201f,2);put(0x1002,0x7e2a,2);put(0x1004,0x4e71,2);
  put(0x4200,0x80818283,4);put(0x7000,0x70717273,4);put(0x8000,0x40414243,4);put(0x9000,0x90919293,4);put(8,0x5000,4);put(36,0x6000,4);
  put(0x5000,0x3ebc,2);put(0x5002,0x700|(returned>=2?0x2000:0)|((returned==1||returned==3)?0x1000:0),2);put(0x5004,0x4e73,2);
  for(auto& p:cpufunctbl)p=dispatch;
  try{m68k_run_mmu030();}catch(const Done&){}
  if(reads!=1||attempts!=(fault?2:1)||traceCount!=0||regs.pc!=0x1006||regs.d[7]!=42)throw std::runtime_error("Missing actual recovery/control: id="+std::to_string(id)+" attempts="+std::to_string(attempts)+" reads="+std::to_string(reads)+" PC="+std::to_string(regs.pc)+" D0="+std::to_string(regs.d[0]));
  if(fault){
   if(originalFrame.size()!=92||editedFrame.size()!=92||originalFrame[6]!=0xb0||originalFrame[7]!=8)throw std::runtime_error("Missing reference format-B frame");
   for(int n=2;n<92;n++)if(originalFrame[n]!=editedFrame[n])throw std::runtime_error("Handler changed non-SR frame fields");
  }else if(!originalFrame.empty()||!editedFrame.empty())throw std::runtime_error("Unexpected control bus fault");
  std::cout<<"fault="<<fault<<" initial="<<initial<<" returned="<<returned<<" attempts="<<attempts<<" reads="<<reads<<std::hex<<" D0="<<regs.d[0]<<" A7="<<regs.a[7]<<" USP="<<regs.usp<<" ISP="<<regs.isp<<" MSP="<<regs.msp<<" PC="<<regs.pc<<std::dec<<" events=";
  for(int e:events)std::cout<<e;std::cout<<'\n';
 }
 return 0;
}catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 2;}catch(int x){std::cerr<<"Uncaught reference exception "<<x<<'\n';return 3;}}
