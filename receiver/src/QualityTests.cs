using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AptReceiver;
class QualityTests {
    static int n;
    static void Check(bool yes,string message){n++;if(!yes)throw new Exception(message);}
    static byte[] Hex(string path){List<byte>b=new List<byte>();foreach(string l in File.ReadAllLines(path))if(l.Trim().Length>0)b.Add(Convert.ToByte(l.Trim(),16));return b.ToArray();}
    [STAThread]static int Main(string[] args){try{
        Telemetry t=null;PacketParser p=new PacketParser();p.TelemetryReceived+=delegate(Telemetry v){t=v;};byte[] meta=Encoding.ASCII.GetBytes(File.ReadAllText(args[2]));for(int i=0;i<meta.Length;i++)p.Feed(new[]{meta[i]},1);
        Check(t!=null&&t.Mode==3&&t.Agc==1&&t.Gain==150&&t.Firmware==18,"extended status fields");
        byte[] old=Encoding.ASCII.GetBytes("XC:0632.41 YC:0359.82 DX:-008.19 DY:+000.10 N:001234\r\n");p.Feed(old,old.Length);Check(t.Mode==-1&&t.Firmware==-1,"legacy never invents mode");
        for(int profile=0;profile<2;profile++){
            FramePacket f=null;bool started=false;PacketParser q=new PacketParser();q.FrameStarted+=delegate(bool preview){started=true;Check(preview==(profile==0),"frame header kind");};q.FrameReceived+=delegate(FramePacket v){f=v;};byte[] b=Hex(args[profile]);
            for(int i=0;i<b.Length;i+=7){int size=Math.Min(7,b.Length-i);byte[] chunk=new byte[size];Buffer.BlockCopy(b,i,chunk,0,size);q.Feed(chunk,size);}
            Check(started&&f!=null,"RTL frame CRC accepted");using(Bitmap bmp=Images.Decode(f))for(int y=0;y<bmp.Height;y++)for(int x=0;x<bmp.Width;x++){
                int sx=profile==0?4*x+2:x,sy=profile==0?4*y+2:y;
                int r=(sx*7+sy)&255,g=(sy*11+sx)&255,blue=sx^sy;
                if(profile==0){r=(r>>3)*8+(r>>5);g=(g>>2)*4+(g>>6);blue=(blue>>3)*8+(blue>>5);}
                Check(bmp.GetPixel(x,y).ToArgb()==Color.FromArgb(r,g,blue).ToArgb(),"all source samples including DDR boundaries");
            }
        }
        using(PreviewView view=new PreviewView())using(Bitmap source=new Bitmap(320,180))using(Bitmap rendered=new Bitmap(960,540)){
            using(Graphics g=Graphics.FromImage(source))g.Clear(Color.DarkSlateBlue);view.Image=source;view.Size=rendered.Size;view.Latest=t;view.DrawToBitmap(rendered,new Rectangle(Point.Empty,rendered.Size));rendered.Save(args[3]);Check(source.GetPixel(10,10).ToArgb()==Color.DarkSlateBlue.ToArgb(),"overlay does not change raw pixels");view.Image=null;
        }
        using(MainForm form=new MainForm())using(Bitmap b=new Bitmap(1388,872)){
            Control shell=form.Controls[0];shell.Font=form.Font;shell.BackColor=form.BackColor;form.Controls.Remove(shell);shell.Dock=DockStyle.None;shell.Size=b.Size;shell.CreateControl();shell.PerformLayout();shell.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(args[4]);Check(shell.Controls.Count==6,"layout renders offscreen without Show");shell.Dispose();
        }
        Console.WriteLine("PASS: "+n+" checks; v18 metadata, legacy status, DDR RGB888/RGB565 fixtures, overlay/raw isolation, offscreen layout.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
