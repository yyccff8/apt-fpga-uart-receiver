using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace AptReceiver {
    public static class Images {
        // uart_sender sends each DDR word MSB-first. video_out's stride-5
        // slices are exactly consecutive RGB888 bytes, crossing word borders.
        public static Bitmap Decode(FramePacket f) {
            Bitmap bmp=new Bitmap(f.Width,f.Height,PixelFormat.Format24bppRgb);
            BitmapData d=bmp.LockBits(new Rectangle(0,0,f.Width,f.Height),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
            try {
                byte[] row=new byte[f.Width*3];
                for(int y=0;y<f.Height;y++) {
                    int source=y*f.StrideWords*16;
                    for(int x=0;x<f.Width;x++) {
                        int b=x*3;
                        if(f.IsPreview){
                            int a=(y*f.Width+x)*2,v=f.Payload[a]|f.Payload[a+1]<<8;
                            int r=(v>>11)&31,g=(v>>5)&63,blue=v&31;
                            row[b]=(byte)((blue<<3)|(blue>>2));row[b+1]=(byte)((g<<2)|(g>>4));row[b+2]=(byte)((r<<3)|(r>>2));
                        }else{
                            int a=source+x*3;
                            row[b]=f.Payload[a+2];row[b+1]=f.Payload[a+1];row[b+2]=f.Payload[a];
                        }
                    }
                    Marshal.Copy(row,0,IntPtr.Add(d.Scan0,y*d.Stride),row.Length);
                }
            } finally { bmp.UnlockBits(d); }
            return bmp;
        }
        public static byte[] EncodePacket(int width,int height,byte[] rgb) {
            int stride=(width*3+15)/16;
            byte[] packet=new byte[12+stride*16*height];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("HXF2"),0,packet,0,4);
            packet[4]=(byte)width;packet[5]=(byte)(width>>8);packet[6]=(byte)height;packet[7]=(byte)(height>>8);
            packet[8]=(byte)stride;packet[9]=(byte)(stride>>8);
            for(int y=0;y<height;y++) Buffer.BlockCopy(rgb,y*width*3,packet,10+y*stride*16,width*3);
            ushort crc=PacketParser.ComputeCrc(packet,packet.Length-2);
            packet[packet.Length-2]=(byte)crc;packet[packet.Length-1]=(byte)(crc>>8);return packet;
        }
        public static byte[] EncodePreviewPacket(int w,int h,byte[] pixels){
            byte[] b=new byte[12+w*h*2];Buffer.BlockCopy(Encoding.ASCII.GetBytes("HXP1"),0,b,0,4);
            b[4]=(byte)w;b[5]=(byte)(w>>8);b[6]=(byte)h;b[7]=(byte)(h>>8);b[8]=2;
            Buffer.BlockCopy(pixels,0,b,10,pixels.Length);ushort c=PacketParser.ComputeCrc(b,b.Length-2);
            b[b.Length-2]=(byte)c;b[b.Length-1]=(byte)(c>>8);return b;
        }
        public static byte[] DemoPreviewPacket(int frame){
            const int w=160,h=90;byte[] pixels=new byte[w*h*2];
            using(Bitmap b=new Bitmap(w,h,PixelFormat.Format24bppRgb)){
                using(Graphics g=Graphics.FromImage(b)){
                    g.Clear(Color.FromArgb(18,35,52));
                    using(Pen grid=new Pen(Color.FromArgb(35,55,72))){for(int x=0;x<w;x+=10)g.DrawLine(grid,x,0,x,h);for(int y=0;y<h;y+=10)g.DrawLine(grid,0,y,w,y);}
                    int cx=90+(int)(Math.Sin(frame*.18)*20),cy=48+(int)(Math.Cos(frame*.18)*12);
                    for(int r=12;r>0;r--)using(Brush brush=new SolidBrush(Color.FromArgb(60+(12-r)*16,60+(12-r)*16,60+(12-r)*16)))g.FillEllipse(brush,cx-r,cy-r,r*2,r*2);
                    using(Font f=new Font("Arial",8,FontStyle.Bold))g.DrawString("DEMO / NOT BOARD",f,Brushes.Orange,2,1);
                }
                BitmapData d=b.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
                try{byte[] row=new byte[w*3];for(int y=0;y<h;y++){
                    Marshal.Copy(IntPtr.Add(d.Scan0,y*d.Stride),row,0,row.Length);
                    for(int x=0;x<w;x++){int a=x*3,v=(row[a+2]>>3)<<11|(row[a+1]>>2)<<5|(row[a]>>3);int to=(y*w+x)*2;pixels[to]=(byte)v;pixels[to+1]=(byte)(v>>8);}
                }}finally{b.UnlockBits(d);}
            }
            return EncodePreviewPacket(w,h,pixels);
        }
        public static byte[] DemoPacket() {
            const int w=1280,h=720;
            using(Bitmap bmp=new Bitmap(w,h,PixelFormat.Format24bppRgb)) {
                using(Graphics g=Graphics.FromImage(bmp)) {
                    g.Clear(Color.FromArgb(19,33,49));
                    using(Pen grid=new Pen(Color.FromArgb(40,62,81))) {
                        for(int x=0;x<w;x+=80) g.DrawLine(grid,x,0,x,h);
                        for(int y=0;y<h;y+=80) g.DrawLine(grid,0,y,w,y);
                    }
                    for(int r=100;r>0;r--) {
                        int c=40+(100-r)*2;
                        using(Brush b=new SolidBrush(Color.FromArgb(c,c,Math.Min(255,c+12))))g.FillEllipse(b,730-r,330-r,r*2,r*2);
                    }
                    using(Font f=new Font("Microsoft YaHei UI",25,FontStyle.Bold))
                    using(Brush b=new SolidBrush(Color.FromArgb(255,178,92)))
                        g.DrawString("DEMO / SYNTHETIC IMAGE / NOT A BOARD CAPTURE",f,b,30,28);
                    using(Font f=new Font("Microsoft YaHei UI",16))g.DrawString("1280 x 720  |  HXF2 + CRC16  |  receiver test pattern",f,Brushes.White,32,80);
                }
                byte[] rgb=new byte[w*h*3];
                BitmapData d=bmp.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
                try {
                    byte[] row=new byte[w*3];
                    for(int y=0;y<h;y++) {
                        Marshal.Copy(IntPtr.Add(d.Scan0,y*d.Stride),row,0,row.Length);
                        for(int x=0;x<w;x++) {int a=y*w*3+x*3,b=x*3;rgb[a]=row[b+2];rgb[a+1]=row[b+1];rgb[a+2]=row[b];}
                    }
                } finally {bmp.UnlockBits(d);}
                return EncodePacket(w,h,rgb);
            }
        }
    }
}
