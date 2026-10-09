// Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT
// Transport for untouched, generated WinUAE RTE + cputest SR helpers.
// No bus faults, full exception entry, caches, MMU, or timing are simulated.
// Unexpected reads/callbacks and malformed/empty inputs fail the audit.
#include <cstdint>
#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>
using uae_u16 = uint16_t;
using uae_u32 = uint32_t;
using uaecptr = uint32_t;
#define REGPARAM2
#define m68k_areg(r,n) ((r).a[n])
struct Regs { uint32_t a[8]{}, usp{}, isp{}, msp{}, pc{}; uint16_t sr{}; int m{}, s{}, t0{}, t1{}, intmask{}, ipl[1]{}; } regs;
struct Prefs { int cpu_model = 68040; } currprefs;
int flag_SPCFLAG_TRACE, cpu_stopped;
int xflag, nflag, zflag, vflag, cflag, traceActivations;
#define SET_XFLG(v) (xflag=(v))
#define SET_NFLG(v) (nflag=(v))
#define SET_ZFLG(v) (zflag=(v))
#define SET_VFLG(v) (vflag=(v))
#define SET_CFLG(v) (cflag=(v))
void activate_trace() { traceActivations++; }
std::unordered_map<uint32_t,uint8_t> memory;
std::vector<uint32_t> reads;
uint32_t read(uint32_t address, int width) {
    reads.push_back(address); reads.push_back(width);
    uint32_t value = 0;
    for (int n=0;n<width;n++) {
        const auto found = memory.find(address+n);
        if (found == memory.end()) throw std::runtime_error("Uninitialized reference read");
        value = (value<<8) | found->second;
    }
    return value;
}
uint16_t get_word_test(uint32_t a) { return static_cast<uint16_t>(read(a,2)); }
uint32_t get_long_test(uint32_t a) { return read(a,4); }
void put(uint32_t a, uint32_t value, int width) {
    for (int n=0;n<width;n++) memory[a+n] = static_cast<uint8_t>(value >> (8*(width-1-n)));
}
uint32_t m68k_getpci() { return regs.pc; }
void m68k_setpci_j(uint32_t pc) { regs.pc=pc; }
void Exception(int) { throw std::runtime_error("Unexpected privilege exception"); }
void Exception_cpu(int) { throw std::runtime_error("Unexpected format exception"); }
uint16_t handoffSr;
uint32_t faultPc;
int callbackCount;
void exception3_read_prefetch_68040bug(uint32_t opcode, uint32_t address, uint16_t secondarySr) {
    if (opcode != 0x4e73 || !(address&1)) throw std::runtime_error("Unexpected callback");
    handoffSr=secondarySr; faultPc=address; callbackCount++;
}
// Produced only by strict extraction from the pinned source. Never hand-edit.
#include "winuae-sr.inc"
#include "winuae-rte.inc"

std::vector<uint32_t> parse(const std::string& line) {
    std::istringstream stream(line); std::vector<uint32_t> values;
    std::string token;
    while (stream>>token) {
        size_t end=0; const auto value=std::stoull(token,&end,16);
        if (end != token.size() || value>UINT32_MAX) throw std::runtime_error("Invalid hexadecimal input");
        values.push_back(static_cast<uint32_t>(value));
    }
    return values;
}
// Literal controls discriminate original, last-throwaway and final SR images.
void fixedControls() {
    regs={}; memory.clear(); reads.clear(); callbackCount=0;
    regs.sr=0xa01f; regs.s=1; regs.t1=1; regs.a[7]=regs.isp=0x4700;
    regs.usp=0x7800; regs.msp=0x7400; regs.pc=0x1000;
    put(0x4700,0x101f,2); put(0x4702,0xdead0001,4); put(0x4706,0x1024,2);
    put(0x7800,0x6000,2); put(0x7802,0x6001,4); put(0x7806,0x2008,2);
    op_4e73_94_test_ff(0x4e73);
    if (callbackCount!=1 || handoffSr!=0x101f || regs.sr!=0x6000 || regs.a[7]!=0x4708 ||
        regs.usp!=0x780c || faultPc!=0x6001 || reads!=std::vector<uint32_t>{0x4700,2,0x4702,4,0x4706,2,0x7800,2,0x7802,4,0x7806,2})
        throw std::runtime_error("Fixed one-throwaway handoff failed");
    regs={}; memory.clear(); reads.clear(); callbackCount=0;
    regs.sr=0x3000; regs.s=regs.m=1; regs.a[7]=regs.msp=0x7401;
    regs.usp=0x7801; regs.isp=0x4701; regs.pc=0x1000;
    put(0x7401,0xa01f,2); put(0x7403,0xdead0001,4); put(0x7407,0x1024,2);
    put(0x4701,0x5000,2); put(0x4703,0xdead0003,4); put(0x4707,0x1024,2);
    put(0x7801,0x301f,2); put(0x7803,0xff002003,4); put(0x7807,0x0008,2);
    op_4e73_94_test_ff(0x4e73);
    if (callbackCount!=1 || handoffSr!=0x5000 || regs.sr!=0x301f || regs.a[7]!=0x7409 ||
        regs.usp!=0x7809 || regs.isp!=0x4709 || faultPc!=0xff002003 || reads.size()!=18)
        throw std::runtime_error("Fixed two-throwaway handoff failed");
    regs={}; memory.clear(); reads.clear(); callbackCount=0;
    regs.sr=0x201f; regs.s=1; regs.a[7]=regs.isp=0x4700;
    regs.usp=0x7800; regs.msp=0x7400; regs.pc=0x1000;
    put(0x4700,0x501f,2); put(0x4702,0xdead0001,4); put(0x4706,0x1024,2);
    put(0x7800,0x301f,2); put(0x7802,0xff002003,4); put(0x7806,0x7008,2);
    op_4e73_94_test_ff(0x4e73);
    if (callbackCount!=1 || handoffSr!=0x501f || regs.sr!=0x301f || regs.a[7]!=0x7400 ||
        regs.usp!=0x783c || regs.isp!=0x4708 || reads.size()!=12)
        throw std::runtime_error("Fixed access-frame handoff failed");
}

int main(int argc,char** argv) {
    try {
        if (argc!=5) throw std::runtime_error("Usage: observer rows output expected-count summary");
        fixedControls();
        const auto required=std::stoul(argv[3]);
        if (!required) throw std::runtime_error("Empty required selection");
        std::ifstream input(argv[1]); std::ofstream output(argv[2]);
        if (!input || !output) throw std::runtime_error("Missing input/output");
        std::string line; size_t count=0, mismatches=0;
        while (std::getline(input,line)) {
            const auto delimiter=line.find(" | ");
            if (delimiter==std::string::npos) throw std::runtime_error("Missing actual-state delimiter");
            const auto fixture=parse(line.substr(0,delimiter)), actual=parse(line.substr(delimiter+3));
            if (fixture.size()<15 || fixture[0]!=count || fixture[6]<2 || fixture[6]>3)
                throw std::runtime_error("Missing, reordered or invalid fixture");
            const size_t headerEnd=7+4*fixture[6];
            if (fixture.size()<headerEnd) throw std::runtime_error("Missing reference frame header");
            const bool access=(fixture[headerEnd-1]>>12)==7;
            if (fixture.size()!=headerEnd+(access?2:0)) throw std::runtime_error("Unexpected reference frame payload");
            if (access && fixture[headerEnd]!=0x0105 && fixture[headerEnd]!=0x1005)
                throw std::runtime_error("Out-of-scope continuation: pending/undefined frames need a full restoration oracle");
            regs={}; memory.clear(); reads.clear(); callbackCount=traceActivations=0;
            flag_SPCFLAG_TRACE=cpu_stopped=0;
            regs.sr=static_cast<uint16_t>(fixture[1]);
            regs.s=(regs.sr>>13)&1; regs.m=(regs.sr>>12)&1;
            regs.t0=(regs.sr>>14)&1; regs.t1=(regs.sr>>15)&1; regs.intmask=(regs.sr>>8)&7;
            regs.usp=fixture[2]; regs.isp=fixture[3]; regs.msp=fixture[4]; regs.pc=fixture[5];
            regs.a[7]=regs.s ? regs.m ? regs.msp : regs.isp : regs.usp;
            for (size_t n=7;n<headerEnd;n+=4) {
                const auto format=fixture[n+3]>>12;
                if ((n+4<headerEnd && format!=1) || (n+4==headerEnd && format!=0 && format!=2 && format!=3 && format!=7))
                    throw std::runtime_error("Out-of-scope reference frame");
                put(fixture[n],fixture[n+1],2); put(fixture[n]+2,fixture[n+2],4); put(fixture[n]+6,fixture[n+3],2);
            }
            op_4e73_94_test_ff(0x4e73);
            if (callbackCount!=1 || regs.pc!=fixture[5] || reads.size()!=fixture[6]*6)
                throw std::runtime_error("Missing odd-PC handoff/read selection");
            // The observer ends at the callback. Compose its live SR/stack bank
            // with the documented format-2 entry and traced-user S-bit erratum.
            auto usp=regs.s ? regs.usp : regs.a[7];
            auto isp=regs.s && !regs.m ? regs.a[7] : regs.isp;
            auto msp=regs.s && regs.m ? regs.a[7] : regs.msp;
            const auto saved=handoffSr | (!regs.s && (regs.sr&0xc000) ? 0x2000 : 0);
            const auto live=(regs.sr|0x2000)&~0xc000;
            if (live&0x1000) msp-=12; else isp-=12;
            std::vector<uint32_t> expected{static_cast<uint32_t>(saved),static_cast<uint32_t>(live),0x9190,regs.pc,faultPc&~1u,usp,isp,msp,static_cast<uint32_t>(reads.size()/2)};
            expected.insert(expected.end(),reads.begin(),reads.end());
            output<<std::hex<<count<<' '<<handoffSr<<' '<<regs.sr<<' '<<regs.pc<<' '<<faultPc;
            for (const auto value:expected) output<<' '<<value;
            output<<'\n';
            if (actual!=expected) {
                if (mismatches<20) std::cerr<<"Mismatch row "<<std::dec<<count<<" secondary SR "<<std::hex<<handoffSr<<'\n';
                mismatches++;
            }
            count++;
        }
        if (count!=required) throw std::runtime_error("Missing reference rows");
        std::ofstream summary(argv[4]);
        if (!summary) throw std::runtime_error("Missing summary output");
        summary<<"{\"cases\":"<<count<<",\"mismatches\":"<<mismatches<<",\"fixedControls\":3,\"fullExceptionOracle\":false,\"hardwareQualified\":false}\n";
        if (mismatches) return 1;
        std::cout<<"Compared "<<count<<" handoffs; 0 mismatches; 3 fixed controls\n";
        return 0;
    } catch (const std::exception& error) { std::cerr<<error.what()<<'\n'; return 2; }
}
