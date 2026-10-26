using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AptReceiver {
    public sealed class Telemetry {
        public DateTime Time = DateTime.Now;
        public double X, Y, Dx, Dy;
        public int N;
        public int Mode=-1, Agc=-1, Gain=-1, Firmware=-1;
        public string Line;
        public bool Valid { get { return N > 0; } }
    }
    public sealed class FramePacket {
        public int Width, Height, StrideWords;
        public int PixelEncoding;
        public bool IsPreview { get { return PixelEncoding==2; } }
        public byte[] Payload;
        public ushort Crc;
        public DateTime Time = DateTime.Now;
    }
    // A single ordered stream contains ASCII telemetry OR HXF2 binary frames.
    // Never inspect binary payload for text/magic: RGB pixels may contain both.
    public sealed class PacketParser {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HXF2");
        static readonly byte[] PreviewMagic = Encoding.ASCII.GetBytes("HXP1");
        readonly byte[] candidate=new byte[4];
        bool preview;
        static readonly Regex LinePattern = new Regex(
            @"^XC:(\d{4}\.\d{2}) YC:(\d{4}\.\d{2}) DX:([+-]\d{3}\.\d{2}) DY:([+-]\d{3}\.\d{2}) N:(\d{5,6})(?: M:([0-3]) A:([01]) G:(\d{3}) V:(\d{2}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        public event Action<Telemetry> TelemetryReceived;
        public event Action<FramePacket> FrameReceived;
        public event Action<string> Notice;
        public event Action<bool> FrameStarted;
        public bool CurrentIsPreview { get { return preview; } }
        public volatile int Received, Expected;
        public volatile bool Receiving;
        public DateTime Started;
        readonly byte[] header = new byte[10];
        readonly StringBuilder line = new StringBuilder();
        int state, magicAt, headerAt, crcAt, width, height, stride;
        byte[] payload;
        ushort crc, sentCrc;
        public static ushort CrcByte(ushort value, byte b) {
            value ^= (ushort)(b << 8);
            for (int i=0;i<8;i++) value = (ushort)(((value & 0x8000)!=0) ? (value<<1)^0x1021 : value<<1);
            return value;
        }
        public static ushort ComputeCrc(byte[] bytes, int count) {
            ushort result=0xffff;
            for(int i=0;i<count;i++) result=CrcByte(result,bytes[i]);
            return result;
        }
        void Say(string text) { if(Notice!=null) Notice(text); }
        void Reset() { state=0; magicAt=0; headerAt=0; crcAt=0; payload=null; Expected=0; Received=0; Receiving=false; line.Clear(); }
        public void AbortIncomplete(string why) { if(Receiving) { Reset(); Say(why); } }
        void TextByte(byte b) {
            if(b==10) {
                string text=line.ToString().TrimEnd('\r'); line.Clear();
                Match m=LinePattern.Match(text);
                if(m.Success && TelemetryReceived!=null) {
                    TelemetryReceived(new Telemetry {
                        X=double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),
                        Y=double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture),
                        Dx=double.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture),
                        Dy=double.Parse(m.Groups[4].Value,CultureInfo.InvariantCulture),
                        N=int.Parse(m.Groups[5].Value,CultureInfo.InvariantCulture),
                        Mode=m.Groups[6].Success?int.Parse(m.Groups[6].Value):-1, Agc=m.Groups[7].Success?int.Parse(m.Groups[7].Value):-1,
                        Gain=m.Groups[8].Success?int.Parse(m.Groups[8].Value):-1, Firmware=m.Groups[9].Success?int.Parse(m.Groups[9].Value):-1,Line=text });
                }
            } else if(b==13 || b>=32 && b<=126) {
                if(line.Length<160) line.Append((char)b); else line.Clear();
            } else line.Clear();
        }
        void Scan(byte b) {
            candidate[magicAt++]=b;
            bool normal=true,small=true;
            for(int i=0;i<magicAt;i++){normal &= candidate[i]==Magic[i];small &= candidate[i]==PreviewMagic[i];}
            if(normal||small){
                if(magicAt==4){
                    preview=small;Buffer.BlockCopy(candidate,0,header,0,4);headerAt=4;magicAt=0;
                    state=1;Receiving=true;Started=DateTime.Now;line.Clear();
                }
            }else{
                for(int i=0;i<magicAt-1;i++)TextByte(candidate[i]);
                magicAt=0;
                if(b==Magic[0]){candidate[0]=b;magicAt=1;}else TextByte(b);
            }
        }
        void HeaderComplete() {
            width=header[4]|header[5]<<8; height=header[6]|header[7]<<8; stride=header[8]|header[9]<<8;
            if(width<1 || width>4096 || height<1 || height>2160 || (preview && (width>320 || height>180)) || (preview ? stride!=2 : stride!=(width*3+15)/16)) {
                byte[] tail=new byte[9]; Buffer.BlockCopy(header,1,tail,0,9);
                Reset(); Say("忽略无效图像帧头，重新查找完整图像。"); Feed(tail,tail.Length); return;
            }
            Expected=preview ? width*height*2 : stride*16*height; Received=0; payload=new byte[Expected];
            if(FrameStarted!=null)FrameStarted(preview);
            crc=ComputeCrc(header,10); state=2; crcAt=0;
            if(!preview) Say(string.Format("开始接收图像 {0}×{1}，{2:N0} 字节；接收期间坐标遥测暂停。",width,height,Expected));
        }
        public void Feed(byte[] bytes, int count) {
            int at=0;
            while(at<count) {
                if(state==0) Scan(bytes[at++]);
                else if(state==1) { header[headerAt++]=bytes[at++]; if(headerAt==10) HeaderComplete(); }
                else if(state==2) {
                    int n=Math.Min(count-at,Expected-Received);
                    Buffer.BlockCopy(bytes,at,payload,Received,n);
                    for(int i=0;i<n;i++) crc=CrcByte(crc,bytes[at+i]);
                    Received+=n; at+=n; if(Received==Expected) state=3;
                } else {
                    if(crcAt++==0) sentCrc=bytes[at++];
                    else {
                        sentCrc|=(ushort)(bytes[at++]<<8);
                        FramePacket frame = new FramePacket {Width=width,Height=height,StrideWords=stride,PixelEncoding=preview?2:0,Payload=payload,Crc=crc};
                        bool ok=sentCrc==crc;
                        ushort received=sentCrc; Reset();
                        if(ok) { if(FrameReceived!=null) FrameReceived(frame); }
                        else Say(string.Format("图像 CRC 失败：收到 {0:X4}，计算 {1:X4}；丢弃本帧，等待后续完整图像。",received,frame.Crc));
                    }
                }
            }
        }
    }
}
