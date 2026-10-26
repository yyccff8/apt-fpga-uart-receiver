using System;using System.IO;using System.Text;using System.Drawing;using System.Collections.Generic;using AptReceiver;
static class PreviewTests{
 static int assertions;
 static void Check(bool ok,string what){assertions++;if(!ok)throw new Exception(what);}
 static byte[] Cat(params byte[][] list){List<byte>b=new List<byte>();foreach(byte[]a in list)b.AddRange(a);return b.ToArray();}
 static byte[] Hex(string p){List<byte>b=new List<byte>();foreach(string line in File.ReadAllLines(p))if(line.Trim().Length>0)b.Add(Convert.ToByte(line.Trim(),16));return b.ToArray();}
 public static int Run(string rtlPath,string legacyPath){
  byte[] rtl=Hex(rtlPath);Check(rtl.Length==56,"two RTL preview packets");byte[] one=new byte[28];Buffer.BlockCopy(rtl,0,one,0,28);
  byte[] pixels=new byte[16];int at=0;foreach(int y in new[]{4,12})foreach(int x in new[]{4,12,20,28}){
   int v=((x*4)>>3)<<11|((y*8)>>2)<<5|((x+y)>>3);pixels[at++]=(byte)v;pixels[at++]=(byte)(v>>8);
  }
  byte[] encoded=Images.EncodePreviewPacket(4,2,pixels);for(int i=0;i<56;i++)Check(rtl[i]==encoded[i%28],"RTL preview byte "+i);
  byte[] text=Encoding.ASCII.GetBytes("XC:0632.41 YC:0359.82 DX:-008.19 DY:+000.10 N:001234\r\n");
  byte[] stream=Cat(text,one,Hex(legacyPath),one,text);
  for(int chunk=1;chunk<=stream.Length;chunk++){
   PacketParser p=new PacketParser();int preview=0,legacy=0,telemetry=0;
   p.TelemetryReceived+=delegate{telemetry++;};
   p.FrameReceived+=delegate(FramePacket f){if(!f.IsPreview){legacy++;return;}preview++;
    Check(f.Width==4&&f.Height==2,"preview geometry");using(Bitmap b=Images.Decode(f)){
     int v=pixels[0]|pixels[1]<<8,r=(v>>11)&31,g=(v>>5)&63,blue=v&31;
     Check(b.GetPixel(0,0).ToArgb()==Color.FromArgb((r<<3)|(r>>2),(g<<2)|(g>>4),(blue<<3)|(blue>>2)).ToArgb(),"RGB565 decode and order");
    }};
   for(int i=0;i<stream.Length;i+=chunk){int n=Math.Min(chunk,stream.Length-i);byte[] b=new byte[n];Buffer.BlockCopy(stream,i,b,0,n);p.Feed(b,n);}
   Check(preview==2&&legacy==1&&telemetry==2,"mixed preview+legacy+telemetry chunks "+chunk);
  }
  byte[] damaged=(byte[])one.Clone();damaged[12]^=128;PacketParser bad=new PacketParser();int accepted=0;bad.FrameReceived+=delegate{accepted++;};byte[] pair=Cat(damaged,one);bad.Feed(pair,pair.Length);Check(accepted==1,"preview CRC reject and recovery");
  PacketParser type=new PacketParser();int found=0;type.FrameReceived+=delegate{found++;};byte[] invalid=(byte[])one.Clone();invalid[8]=3;pair=Cat(invalid,one);type.Feed(pair,pair.Length);Check(found==1,"invalid encoding recovery");
  return assertions;
 }
}
