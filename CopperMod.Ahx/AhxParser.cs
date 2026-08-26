using System.Text;
using CopperMod.Abstractions;

namespace CopperMod.Ahx;

internal static class AhxParser
{
    public static AhxModule Parse(ReadOnlySpan<byte> d)
    {
        Need(d,0,14,"header");
        var version=d[3]; var trackZeroOmitted=(d[6]&0x80)!=0; var mult=((d[6]>>5)&3)+1;
        var len=((d[6]&15)<<8)|d[7]; var restart=U16(d,8); var trl=d[10]; var trk=d[11]; var ins=d[12]; var subs=d[13];
        if(len is <1 or >999 || restart>=len || trl is <1 or >64 || ins>63) throw new ModuleLoadException("AHX header contains values outside the format limits.");
        Need(d,14,subs*2,"subsong list");var subSongs=new int[subs];var p=14;for(var i=0;i<subs;i++){subSongs[i]=U16(d,p);if(subSongs[i]>=len)throw new ModuleLoadException("AHX subsong references an unavailable position.");p+=2;}Need(d,p,len*8,"position list");
        var positions=new AhxPosition[len];
        for(var i=0;i<len;i++){var ts=new byte[4];var xs=new sbyte[4];for(var c=0;c<4;c++){ts[c]=d[p++];xs[c]=unchecked((sbyte)d[p++]);if(ts[c]>trk)throw new ModuleLoadException("AHX position references an unavailable track.");}positions[i]=new(ts,xs);}
        var tracks=new AhxStep[trk+1][]; tracks[0]=new AhxStep[trl];
        var first=trackZeroOmitted?1:0;
        for(var t=first;t<=trk;t++){Need(d,p,trl*3,"track");var rows=new AhxStep[trl];for(var r=0;r<trl;r++){var v=(d[p++]<<16)|(d[p++]<<8)|d[p++];var instrument=(v>>12)&63;if(instrument>ins)throw new ModuleLoadException("AHX track references an unavailable instrument.");rows[r]=new((v>>18)&63,instrument,(v>>8)&15,v&255);}tracks[t]=rows;}
        for(var t=0;t<tracks.Length;t++) tracks[t]??=new AhxStep[trl];
        var instruments=new AhxInstrument[ins+1]; instruments[0]=EmptyInstrument();
        for(var i=1;i<=ins;i++){
            Need(d,p,22,"instrument");var b=p;var plen=d[b+21];Need(d,p+22,plen*4,"instrument playlist");var perf=new AhxPerfStep[plen];p+=22;
            for(var j=0;j<plen;j++){var v=(uint)((d[p++]<<24)|(d[p++]<<16)|(d[p++]<<8)|d[p++]);perf[j]=new((int)(v>>26)&7,(int)(v>>29)&7,(int)(v>>23)&7,(v&(1u<<22))!=0,(int)(v>>16)&63,(int)(v>>8)&255,(int)v&255);}
            instruments[i]=new(d[b],d[b+1]&7,d[b+2],d[b+3],d[b+4],d[b+5],d[b+6],d[b+7],d[b+8],d[b+13],d[b+14]&15,d[b+15],d[b+16],d[b+17],d[b+18],Math.Max(1,(int)d[b+20]),perf,d[b+12]&0x7f,d[b+19]&0x3f,((d[b+1]>>3)&0x1f)|((d[b+12]>>2)&0x20),(d[b+14]>>4)&7,(d[b+14]&0x80)!=0);
        }
        var names=new string[ins+1];for(var i=0;i<names.Length;i++){if(p>=d.Length){names[i]="";continue;}var z=d[p..].IndexOf((byte)0);if(z<0)z=d.Length-p;names[i]=Encoding.Latin1.GetString(d.Slice(p,z));p+=z+(p+z<d.Length?1:0);}
        return new(version,mult,restart,subSongs,trl,positions,tracks,instruments,names[0],names.Skip(1).ToArray());
    }
    private static AhxInstrument EmptyInstrument()=>new(0,5,1,0,1,0,1,1,0,0,0,0,1,63,0,1,[],1,1,0,0,false);
    private static int U16(ReadOnlySpan<byte>d,int p)=>(d[p]<<8)|d[p+1];
    private static void Need(ReadOnlySpan<byte>d,int p,int n,string what){if(p<0||n<0||p>d.Length-n)throw new ModuleLoadException($"The AHX module is truncated while reading the {what}.");}
}

