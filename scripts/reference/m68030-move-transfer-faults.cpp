// Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT
// 030 software discovery: byte/word/long MOVE source-read and final-write faults.
// Unchanged generated operations, fixup helpers and retry loop; flat physical
// transport. Observe indirect/postincrement source and aliased destinations.
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
int writes,moveCcr,writeAttempts,operandWidth,faultOrigin;uaecptr writeAddress,faultFrame;uae_u32 writeValue;
#include "stride.inc"
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
uae_u32 operand_read(uaecptr a,int width){
 if(a!=0x4200||width!=operandWidth)unavailable();attempts++;
 if(faultOnce&&faultOrigin==1){faultOnce=false;events.push_back(1);mmu030_hardware_bus_error(a,0,true,false,width==1?sz_byte:width==2?sz_word:sz_long);}
 reads++;events.push_back(4);return read(a,width);
}
void operand_write(uaecptr a,uae_u32 value,int width){
 if(width!=operandWidth)unavailable();writeAttempts++;
 const uae_u32 mask=width==1?0xffu:width==2?0xffffu:0xffffffffu;value&=mask;
 if(faultOnce&&faultOrigin==2){faultOnce=false;events.push_back(9);mmu030_hardware_bus_error(a,value,false,false,width==1?sz_byte:width==2?sz_word:sz_long);}
 if(++writes!=1)unavailable();writeAddress=a;writeValue=value;events.push_back(8);put(a,value,width);
}
uae_u32 uae_mmu030_get_long(uaecptr a){return operandContext?operand_read(a,4):read(a,4);}
uae_u32 uae_mmu030_get_word(uaecptr a){return operandContext?operand_read(a,2):read(a,2);}
uae_u32 uae_mmu030_get_byte(uaecptr a){return operandContext?operand_read(a,1):read(a,1);}
uae_u32 uae_mmu030_get_iword(uaecptr a){return read(a,2);}
void uae_mmu030_put_word(uaecptr a,uae_u32 v){if(operandContext)operand_write(a,v,2);else put(a,v,2);}
void uae_mmu030_put_long(uaecptr a,uae_u32 v){if(!operandContext)unavailable();operand_write(a,v,4);}
void uae_mmu030_put_byte(uaecptr a,uae_u32 v){if(!operandContext)unavailable();operand_write(a,v,1);}
uae_u32 get_word_mmu030(uaecptr a){return read(a,2);}uae_u32 get_long_mmu030(uaecptr a){return read(a,4);}
uae_u32 x_get_long(uaecptr a){return read(a,4);}void x_put_long(uaecptr a,uae_u32 v){put(a,v,4);}void x_put_word(uaecptr a,uae_u32 v){put(a,v,2);}
void fc_check(int fc){if(fc!=1&&fc!=5)unavailable();}
uae_u32 uae_mmu030_get_long_fcx(uaecptr a,int fc){fc_check(fc);return operand_read(a,4);}
uae_u32 uae_mmu030_get_word_fcx(uaecptr a,int fc){fc_check(fc);return operand_read(a,2);}
uae_u32 uae_mmu030_get_byte_fcx(uaecptr a,int fc){fc_check(fc);return operand_read(a,1);}
void uae_mmu030_put_long_fcx(uaecptr a,uae_u32 v,int fc){fc_check(fc);operand_write(a,v,4);}
void uae_mmu030_put_word_fcx(uaecptr a,uae_u32 v,int fc){fc_check(fc);operand_write(a,v,2);}
void uae_mmu030_put_byte_fcx(uaecptr a,uae_u32 v,int fc){fc_check(fc);operand_write(a,v,1);}
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
 if(nr==2){events.push_back(2);faultFrame=regs.a[7];originalFrame.clear();int length=(read(regs.a[7]+6,2)>>12)==10?32:92;for(int n=0;n<length;n++)originalFrame.push_back(mem.at(regs.a[7]+n));}
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
 if(op==0x3ebc){events.push_back(3);auto v=op_30bc_32_ff(op);editedFrame.clear();for(size_t n=0;n<originalFrame.size();n++)editedFrame.push_back(mem.at(regs.a[7]+static_cast<uaecptr>(n)));return v;}
 if(op==0x4e73){events.push_back(5);snapshot("before-RTE");auto value=op_4e73_32_ff(op);snapshot("after-RTE");return value;}
 if((op&0xf1f0)==0x1090||(op&0xf1f0)==0x2090||(op&0xf1f0)==0x3090){
  snapshot("before-MOVE");operandContext=true;
  try{uae_u32 value;
   if((op>>12)==1)value=(op&8)?op_1098_32_ff(op):op_1090_32_ff(op);
   else if((op>>12)==3)value=(op&8)?op_3098_32_ff(op):op_3090_32_ff(op);
   else value=(op&8)?op_2098_32_ff(op):op_2090_32_ff(op);
   operandContext=false;
   moveCcr=(GET_XFLG()?16:0)|(GET_NFLG()?8:0)|(GET_ZFLG()?4:0)|(GET_VFLG()?2:0)|(GET_CFLG()?1:0);
   snapshot("after-MOVE");return value;
  }catch(...){operandContext=false;throw;}
 }
 if(op==0x7e2a){
  if(faultOrigin==2)moveCcr=(GET_XFLG()?16:0)|(GET_NFLG()?8:0)|(GET_ZFLG()?4:0)|(GET_VFLG()?2:0)|(GET_CFLG()?1:0);
  events.push_back(6);return op_7000_32_ff(op);
 }
 if(op==0x4e71)return op_4e71_32_ff(op);
 unavailable();
}
#include "loop.inc"

int main(){try{
 int id=0;
 for(int width:{1,2,4})for(int bank=0;bank<4;bank++)for(int source:{0,7})for(int post=0;post<2;post++)for(int alias=0;alias<2;alias++)
 for(int origin=0;origin<3;origin++)for(int vi=0;vi<4;vi++)for(int ccr=0;ccr<32;ccr++){
  // Known unqualified native A7 postincrement read-fault restoration remains
  // in the separate A7 discovery command; excluded combinations are counted.
  if(source==7&&post&&origin==1)continue;
  activeCase=id;operandWidth=width;faultOrigin=origin;
  const uae_u32 mask=width==1?0xffu:width==2?0xffffu:0xffffffffu;
  const uae_u32 value=vi==0?0:vi==1?(0x80818283&mask):vi==2?(mask>>1):mask;
  const uae_u32 stride=width==1&&source==7?2:static_cast<uae_u32>(width);
  const int expectedCcr=(ccr&16)|(value==0?4:0)|(value&(1u<<(width*8-1))?8:0);
  regs={};mem.clear();events.clear();originalFrame.clear();editedFrame.clear();faultOnce=origin!=0;attempts=reads=steps=traceCount=0;tracePc=traceD7=0;regflags={};xflag=nflag=zflag=vflag=cflag=0;
  mmu030_state[0]=mmu030_state[1]=mmu030_state[2]=0;mmu030_idx=mmu030_idx_done=0;mmu030_retry=false;mmu030_data_buffer_out=0;mmufixup[0].reg=mmufixup[1].reg=-1;
  writes=moveCcr=writeAttempts=0;writeAddress=writeValue=0;
  xflag=(ccr>>4)&1;nflag=(ccr>>3)&1;zflag=(ccr>>2)&1;vflag=(ccr>>1)&1;cflag=ccr&1;
  regs.usp=0x7000;regs.isp=0x8000;regs.msp=0x9000;
  if(source==7){if(bank<2)regs.usp=0x4200;else if(bank==2)regs.isp=0x4200;else regs.msp=0x4200;}
  regs.s=bank>=2;regs.m=bank==1||bank==3;regs.intmask=7;
  regs.a[7]=source==7?0x4200:bank<2?0x7000:bank==2?0x8000:0x9000;
  regs.a[0]=source==0?0x4200:0x4000;regs.a[1]=0x4400;regs.d[0]=0x12345678;regs.pc=regs.instruction_pc=0x1000;
  const auto opcode=(width==1?0x1090:width==2?0x3090:0x2090)|(post?8:0)|source|((alias?source:1)<<9);
  put(0x1000,opcode,2);put(0x1002,0x7e2a,2);put(0x1004,0x4e71,2);
  for(int n=-8;n<16;n++){put(0x4200+n,0xa5,1);put(0x4400+n,0xa5,1);}
  put(0x4200,value,width);put(8,0x5000,4);put(36,0x6000,4);
  put(0x5000,0x3ebc,2);put(0x5002,0x700|(bank>=2?0x2000:0)|((bank==1||bank==3)?0x1000:0)|(origin==2?expectedCcr:ccr),2);put(0x5004,0x4e73,2);
  auto expectedMemory=mem;
  for(auto& p:cpufunctbl)p=dispatch;
  try{m68k_run_mmu030();}catch(const Done&){}
  const uaecptr destination=alias?0x4200u+(post?stride:0u):0x4400u;
  const uae_u32 sourceExpected=0x4200u+(post?stride:0u);
  const uae_u32 stackExpected=source==7?sourceExpected:bank<2?0x7000u:bank==2?0x8000u:0x9000u;
  if(reads!=1||attempts!=(origin==1?2:1)||writes!=1||writeAttempts!=(origin==2?2:1)||writeAddress!=destination||writeValue!=value||read(destination,width)!=value||traceCount!=0||regs.pc!=0x1006||regs.d[7]!=42||regs.d[0]!=0x12345678||regs.a[source]!=sourceExpected||regs.a[1]!=0x4400||regs.a[7]!=stackExpected)throw std::runtime_error("Missing width/source/final-write recovery: id="+std::to_string(id));
  // A final-write short frame completes in RTE, so no MOVE dispatch occurs on
  // return; capture CCR after RTE from the native flags directly.
  const int finalCcr=(GET_XFLG()?16:0)|(GET_NFLG()?8:0)|(GET_ZFLG()?4:0)|(GET_VFLG()?2:0)|(GET_CFLG()?1:0);
  if(finalCcr!=(ccr&16))throw std::runtime_error("Following sentinel flags differ");
  if(moveCcr!=expectedCcr)throw std::runtime_error("MOVE flags differ");
  if(regs.s!=(bank>=2)||regs.m!=(bank==1||bank==3)||regs.t0||regs.t1)throw std::runtime_error("Returned SR mode differs");
  if((bank>=2&&regs.usp!=0x7000)||(bank!=2&&regs.isp!=0x8000)||(bank!=3&&regs.msp!=0x9000))throw std::runtime_error("Inactive stack bank changed");
  for(int n=1;n<7;n++)if(regs.d[n]!=0)throw std::runtime_error("Untouched data register changed");
  for(int n=2;n<7;n++)if(regs.a[n]!=0)throw std::runtime_error("Untouched address register changed");
  if(origin){
   if(originalFrame.size()!=(origin==2?32u:92u)||editedFrame.size()!=originalFrame.size()||originalFrame[6]!=(origin==2?0xa0:0xb0)||originalFrame[7]!=8)throw std::runtime_error("Missing reference fault frame");
   if(originalFrame!=editedFrame)throw std::runtime_error("Handler changed fault frame");
  }else if(!originalFrame.empty()||!editedFrame.empty())throw std::runtime_error("Unexpected control bus fault");
  auto frameRead=[&](size_t offset,int count){uae_u32 result=0;for(int n=0;n<count;n++)result=(result<<8)|originalFrame.at(offset+static_cast<size_t>(n));return result;};
  const uae_u32 framePc=origin?frameRead(2,4):0;
  const uae_u32 frameSr=origin?frameRead(0,2):0;
  const uae_u32 frameSsw=origin?frameRead(10,2):0;
  const uae_u32 frameAddress=origin?frameRead(16,4):0;
  const uae_u32 frameOutput=origin==2?frameRead(24,4)&mask:0;
  const uae_u32 expectedSr=0x700u|(bank>=2?0x2000u:0u)|((bank==1||bank==3)?0x1000u:0u)|static_cast<uae_u32>(origin==2?expectedCcr:ccr);
  const uae_u32 expectedSsw=0x100u|(origin==1?0x40u:0u)|(width==1?0x10u:width==2?0x20u:0u)|(bank>=2?5u:1u);
  if(origin&&(framePc!=(origin==2?0x1002u:0x1000u)||frameSr!=expectedSr||(frameSsw&0x1f7)!=expectedSsw||frameAddress!=(origin==2?destination:0x4200u)||origin==2&&frameOutput!=value))throw std::runtime_error("Saved PC/SR/SSW/fault-address/data-output differs");
  for(size_t n=0;n<originalFrame.size();n++)expectedMemory[faultFrame+static_cast<uaecptr>(n)]=originalFrame[n];
  for(int n=0;n<width;n++)expectedMemory[destination+static_cast<uaecptr>(n)]=uae_u8(value>>(8*(width-1-n)));
  if(mem!=expectedMemory)throw std::runtime_error("Unexpected operand, frame or surrounding-memory change");
  std::cout<<"id="<<id<<" width="<<width<<" bank="<<bank<<" source="<<source<<" post="<<post<<" alias="<<alias<<" origin="<<origin<<" vi="<<vi<<" ccr="<<ccr<<" attempts="<<attempts<<" reads="<<reads<<" wattempts="<<writeAttempts<<" writes="<<writes<<std::hex<<" value="<<value<<" address="<<writeAddress<<" sourceA="<<regs.a[source]<<" A7="<<regs.a[7]<<" PC="<<regs.pc<<" framePC="<<framePc<<" frameSR="<<frameSr<<" frameSSW="<<(frameSsw&0x1f7)<<" faultAddress="<<frameAddress<<" output="<<frameOutput<<std::dec<<" moveccr="<<moveCcr<<" frame="<<originalFrame.size()<<" events=";
  for(int e:events)std::cout<<e;std::cout<<'\n';id++;
 }
 if(id!=33792)throw std::runtime_error("Empty or incomplete native selection");return 0;
}catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 2;}catch(int x){std::cerr<<"Uncaught reference exception "<<x<<'\n';return 3;}}
