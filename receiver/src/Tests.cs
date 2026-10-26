using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using AptReceiver;
class Tests {
    static int assertions;
    static void Assert(bool v,string why){assertions++;if(!v)throw new Exception(why);}
    static byte[] Join(params byte[][] chunks){int n=0;foreach(byte[] b in chunks)n+=b.Length;byte[] a=new byte[n];n=0;foreach(byte[] b in chunks){Buffer.BlockCopy(b,0,a,n,b.Length);n+=b.Length;}return a;}
    static byte[] Text(string s){return Encoding.ASCII.GetBytes(s);}
    static int Main(string[] args){try{
        Assert(PacketParser.ComputeCrc(Text("123456789"),9)==0x29b1,"CCITT-FALSE reference vector");
        byte[] rgb=new byte[16*2*3];for(int i=0;i<rgb.Length;i++)rgb[i]=(byte)i;
        byte[] packet=Images.EncodePacket(16,2,rgb);
        string legacy="XC:0632.41 YC:0359.82 DX:-008.19 DY:+000.10 N:01234\r\n";
        string current="XC:0632.41 YC:0359.82 DX:-008.19 DY:+000.10 N:001234\r\n";
        byte[] stream=Join(Text("partial garbage"+legacy),packet,Text("fragment from mux\r\n"+legacy+current));
        for(int chunk=1;chunk<=stream.Length;chunk++){
            PacketParser p=new PacketParser();int frames=0,ts=0;
            p.FrameReceived+=delegate(FramePacket f){frames++;Assert(f.Width==16&&f.Height==2&&f.StrideWords==3,"header LE");using(Bitmap b=Images.Decode(f)){
                Assert(b.GetPixel(0,0)==Color.FromArgb(0,1,2),"first RGB pixel");
                Assert(b.GetPixel(5,0)==Color.FromArgb(15,16,17),"pixel spans DDR words 0/1");
                Assert(b.GetPixel(10,0)==Color.FromArgb(30,31,32),"pixel spans DDR words 1/2");
                Assert(b.GetPixel(0,1)==Color.FromArgb(48,49,50),"second row");
            }};
            p.TelemetryReceived+=delegate(Telemetry t){ts++;Assert(t.N==1234&&t.Dx==-8.19&&t.Dy==0.10,"telemetry value");};
            for(int at=0;at<stream.Length;at+=chunk){int n=Math.Min(chunk,stream.Length-at);byte[] b=new byte[n];Buffer.BlockCopy(stream,at,b,0,n);p.Feed(b,n);}
            Assert(frames==1&&ts==2,"mixed stream arbitrary chunking "+chunk);
        }
        PacketParser corrupt=new PacketParser();int good=0,errors=0;
        corrupt.FrameReceived+=delegate{good++;};corrupt.Notice+=delegate(string s){if(s.Contains("CRC 失败"))errors++;};
        byte[] bad=(byte[])packet.Clone();bad[20]^=0x80;byte[] pair=Join(bad,packet);corrupt.Feed(pair,pair.Length);
        Assert(good==1&&errors==1,"CRC rejects damaged frame and resynchronizes");
        PacketParser headers=new PacketParser();int recovered=0;headers.FrameReceived+=delegate{recovered++;};
        byte[] overlap=Join(Text("HXF2"),packet);headers.Feed(overlap,overlap.Length);Assert(recovered==1,"overlapping magic in malformed header");
        PacketParser abort=new PacketParser();int afterAbort=0;abort.FrameReceived+=delegate{afterAbort++;};abort.Feed(packet,15);abort.AbortIncomplete("test timeout");abort.Feed(packet,packet.Length);Assert(afterAbort==1,"truncated frame reset");
        byte[] tinyRgb={255,0,0,0,255,0,0,0,255};byte[] tiny=Images.EncodePacket(1,3,tinyRgb);
        PacketParser padded=new PacketParser();padded.FrameReceived+=delegate(FramePacket f){using(Bitmap b=Images.Decode(f)){Assert(b.GetPixel(0,2).ToArgb()==Color.Blue.ToArgb(),"row padding and orientation");}};padded.Feed(tiny,tiny.Length);
        byte[] nested=(byte[])rgb.Clone();Buffer.BlockCopy(Text("HXF2\nXC:"),0,nested,4,8);byte[] nestedPacket=Images.EncodePacket(16,2,nested);
        PacketParser binary=new PacketParser();int bframes=0,btext=0;binary.FrameReceived+=delegate{bframes++;};binary.TelemetryReceived+=delegate{btext++;};binary.Feed(nestedPacket,nestedPacket.Length);Assert(bframes==1&&btext==0,"payload is never interpreted as control/text");
        PacketParser invalid=new PacketParser();byte[] giant=Text("HXF2xxxxxx");invalid.Feed(giant,giant.Length);Assert(invalid.Expected==0&&!invalid.Receiving,"invalid dimensions rejected before allocation");
        if(args.Length>0){
            string[] lines=File.ReadAllLines(args[0]);List<byte> rtl=new List<byte>();foreach(string l in lines)if(l.Trim().Length>0)rtl.Add(Convert.ToByte(l.Trim(),16));
            Assert(rtl.Count==packet.Length,"RTL packet size");for(int i=0;i<packet.Length;i++)Assert(rtl[i]==packet[i],"RTL byte mismatch "+i);
            PacketParser actual=new PacketParser();int actualFrames=0;actual.FrameReceived+=delegate{actualFrames++;};actual.Feed(rtl.ToArray(),rtl.Count);Assert(actualFrames==1,"real RTL UART packet accepted");
        }
        if(args.Length>1) assertions+=PreviewTests.Run(args[1],args[0]);
        if(args.Length>2) {
            List<byte> fullBytes=new List<byte>();foreach(string l in File.ReadAllLines(args[2]))if(l.Trim().Length>0)fullBytes.Add(Convert.ToByte(l.Trim(),16));
            PacketParser full=new PacketParser();int fullFrames=0;
            full.FrameReceived+=delegate(FramePacket f){fullFrames++;Assert(f.IsPreview&&f.Width==160&&f.Height==90,"full preview geometry");using(Bitmap b=Images.Decode(f)){
                Assert(b.GetPixel(0,0).ToArgb()==Color.FromArgb(16,32,8).ToArgb(),"full preview first pixel");
                Assert(b.GetPixel(159,89).ToArgb()==Color.FromArgb(247,97,206).ToArgb(),"full preview last pixel");
            }};
            byte[] all=fullBytes.ToArray();full.Feed(all,all.Length);Assert(fullFrames==2,"two full preview frames");
        }
        Console.WriteLine("PASS: "+assertions+" assertions: preview/legacy/telemetry, chunk boundaries, CRC, RGB565 and RTL compatibility.");return 0;
    }catch(Exception ex){Console.Error.WriteLine("FAIL: "+ex);return 1;}}
}
