using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace AptReceiver {
    sealed class StableLabel : Label { public StableLabel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);} }
    sealed class PreviewView : PictureBox {
        public Telemetry Latest, Frozen;
        public bool Overlay=true, Hold;
        public PreviewView(){DoubleBuffered=true;ResizeRedraw=true;}
        protected override void OnPaint(PaintEventArgs e){
            Graphics g=e.Graphics;g.Clear(BackColor);if(Image==null)return;
            float scale=Math.Min(Width/(float)Image.Width,Height/(float)Image.Height);
            RectangleF r=new RectangleF((Width-Image.Width*scale)/2,(Height-Image.Height*scale)/2,Image.Width*scale,Image.Height*scale);
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(Image,r);
            if(!Overlay)return;Telemetry t=Hold?Frozen:Latest;
            using(Brush bg=new SolidBrush(Color.FromArgb(185,8,20,30)))g.FillRectangle(bg,r.Left+8,r.Top+8,Math.Min(270,r.Width-16),225);
            string mode=t!=null&&t.Mode>=0?new[]{"原图","增强","边缘","结果"}[t.Mode]:"未回传（需 v18）";
            string text="APT · FPGA 图像与质心\n模式："+mode+"\n";
            text+=t!=null&&t.Firmware>=18?"AGC："+(t.Agc==1?"开启":"关闭")+"  增益："+t.Gain+"%\n":"AGC / 增益：未回传\n";
            text+="光斑质心 SPOT CENTROID\n";
            if(t==null)text+="等待板子遥测";
            else text+=(t.Valid?"VALID 有效":"LOST 失锁")+"   N: "+t.N+"\nXC: "+(t.Valid?t.X.ToString("0.00"):"—")+" px\nYC: "+(t.Valid?t.Y.ToString("0.00"):"—")+" px\nDX: "+(t.Valid?t.Dx.ToString("+0.00;-0.00;0.00"):"—")+" px   DY: "+(t.Valid?t.Dy.ToString("+0.00;-0.00;0.00"):"—")+" px\n遥测 "+t.Time.ToString("HH:mm:ss.fff")+(Hold?" · 抓图时记录":" · 最新值");
            using(Font f=new Font("Microsoft YaHei UI",10))g.DrawString(text,f,Brushes.White,new RectangleF(r.Left+16,r.Top+14,255,215));
            if(t!=null&&t.Valid){float x=r.Left+(float)t.X/1280*r.Width,y=r.Top+(float)t.Y/720*r.Height;using(Pen p=new Pen(Color.Lime,1.5f)){g.DrawLine(p,x-9,y,x+9,y);g.DrawLine(p,x,y-9,x,y+9);}}
        }
    }
}
