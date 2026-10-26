using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
[assembly: AssemblyTitle("APT 接收助手")]
[assembly: AssemblyVersion("3.0.0.0")]

namespace AptReceiver {
    sealed class PortItem {
        public string Port,Name;
        public bool Bluetooth;
        public override string ToString() {return Port+" · "+Name;}
    }
    sealed class Trend : Control {
        readonly List<Telemetry> data=new List<Telemetry>();
        public Trend(){ DoubleBuffered=true; BackColor=Color.White; }
        public void Push(Telemetry t){data.Add(t);if(data.Count>180)data.RemoveAt(0);Invalidate();}
        public void Clear(){data.Clear();Invalidate();}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e); Graphics g=e.Graphics; Rectangle r=new Rectangle(43,22,Math.Max(1,Width-57),Math.Max(1,Height-46));
            double limit=1;foreach(Telemetry t in data)if(t.Valid)limit=Math.Max(limit,Math.Max(Math.Abs(t.Dx),Math.Abs(t.Dy)));
            limit=Math.Ceiling(limit/10)*10;
            using(Pen p=new Pen(Color.FromArgb(224,231,238))) {g.DrawRectangle(p,r);g.DrawLine(p,r.Left,r.Top+r.Height/2,r.Right,r.Top+r.Height/2);}
            using(Font f=new Font("Microsoft YaHei UI",8)){
                g.DrawString("+"+limit.ToString("0"),f,Brushes.Gray,0,r.Top-5);
                g.DrawString("0",f,Brushes.Gray,20,r.Top+r.Height/2-6);
                g.DrawString("−"+limit.ToString("0"),f,Brushes.Gray,0,r.Bottom-10);
                g.DrawString("DX",f,Brushes.Teal,r.Left,1);g.DrawString("DY",f,Brushes.DarkOrange,r.Left+40,1);
            }
            using(Pen px=new Pen(Color.Teal,1.6f))using(Pen py=new Pen(Color.DarkOrange,1.6f))
                for(int i=1;i<data.Count;i++)if(data[i-1].Valid&&data[i].Valid){
                    float x0=r.Left+(i-1)*r.Width/179f,x1=r.Left+i*r.Width/179f;
                    g.DrawLine(px,x0,(float)(r.Top+r.Height/2.0-data[i-1].Dx/limit*r.Height/2),x1,(float)(r.Top+r.Height/2.0-data[i].Dx/limit*r.Height/2));
                    g.DrawLine(py,x0,(float)(r.Top+r.Height/2.0-data[i-1].Dy/limit*r.Height/2),x1,(float)(r.Top+r.Height/2.0-data[i].Dy/limit*r.Height/2));
                }
        }
    }
    public sealed class MainForm : Form {
        ComboBox ports=new ComboBox(),baud=new ComboBox();
        Button refresh=new Button(),connect=new Button(),demo=new Button(),replay=new Button(),folder=new Button(),save=new Button(),choose=new Button();
        CheckBox autoSave=new CheckBox(),recordRaw=new CheckBox(),autoConnect=new CheckBox();
        Label link=new Label(),source=new Label(),imageInfo=new StableLabel(),progressText=new StableLabel(),lastTime=new Label(),stats=new Label(),destination=new Label();
        Label[] values=new Label[6];
        PreviewView picture=new PreviewView();
        ComboBox quality=new ComboBox(); Button hq=new Button(),resume=new Button(); CheckBox overlay=new CheckBox();
        volatile Telemetry latestTelemetry, captureTelemetry;
        volatile bool hdRequested; bool hdHeld, profileSent;
        int lastWidth,lastHeight; double previewFps; string frameKind=""; DateTime captionTime=DateTime.MinValue;
        ProgressBar progress=new ProgressBar();
        TextBox log=new TextBox();
        Trend trend=new Trend();
        System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        CancellationTokenSource cancel;
        volatile SerialPort serial;
        volatile PacketParser parser;
        volatile bool autoSaving=false,rawRecording=false;
        bool previewSeen;
        bool active;
        string preferredPort="";
        int preferredBaud=3000000;
        bool autoSuppressed;
        DateTime nextAutoScan=DateTime.MinValue;
        long bytes,telemetryCount;
        int images,crcFailures;
        string output;
        const string Fmt="0.00";
        public MainForm() {
            Text="APT 接收助手 3.0 · FPGA 图像与坐标";
            Font=new Font("Microsoft YaHei UI",9.5f); BackColor=Color.FromArgb(241,245,249);
            MinimumSize=new Size(1100,780);Size=new Size(1420,940);StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Dpi;
            output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"captures");
            BuildLayout();LoadSettings();
            timer.Interval=150;timer.Tick+=delegate{UpdateProgress();CheckAutoConnect();};timer.Start();
            FormClosing+=delegate{if(cancel!=null)cancel.Cancel();SerialPort s=serial;if(s!=null)Task.Run(delegate{try{s.Close();}catch{}});};
            Shown+=async delegate{await RefreshPorts();};
        }
        static Label LabelText(string text){return new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left};}
        static void ButtonText(Button b,string text,int width){b.Text=text;b.Width=width;b.Height=32;b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(202,213,224);b.BackColor=Color.White;}
        void BuildLayout(){
            TableLayoutPanel shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(16)};
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,63));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,112));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,36));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,105));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,32));Controls.Add(shell);
            Panel head=new Panel{Dock=DockStyle.Fill};shell.Controls.Add(head,0,0);
            head.Controls.Add(new Label{Text="APT 接收助手",Font=new Font(Font.FontFamily,23,FontStyle.Bold),AutoSize=true,Location=new Point(0,0),ForeColor=Color.FromArgb(22,46,69)});
            head.Controls.Add(new Label{Text="FPGA 图像 / 质心遥测",AutoSize=true,Location=new Point(260,17),ForeColor=Color.SlateGray});
            link.Text="未连接";link.Width=210;link.Dock=DockStyle.Right;link.TextAlign=ContentAlignment.MiddleRight;link.ForeColor=Color.SlateGray;head.Controls.Add(link);
            FlowLayoutPanel tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,Padding=new Padding(0,5,0,0)};shell.Controls.Add(tools,0,1);
            tools.Controls.Add(LabelText("串口"));ports.Width=250;ports.DropDownStyle=ComboBoxStyle.DropDownList;tools.Controls.Add(ports);
            ButtonText(refresh,"刷新",60);tools.Controls.Add(refresh);tools.Controls.Add(LabelText("波特率"));
            baud.Width=100;baud.DropDownStyle=ComboBoxStyle.DropDownList;baud.Items.AddRange(new object[]{"3000000","115200","57600","230400","460800","921600"});baud.SelectedIndex=0;tools.Controls.Add(baud);
            autoConnect.Text="插线自动连接";autoConnect.Width=130;autoConnect.Height=28;autoConnect.Checked=true;tools.Controls.Add(autoConnect);
            autoConnect.CheckedChanged+=delegate{SaveSettings();};
            ButtonText(connect,"连接",80);connect.BackColor=Color.FromArgb(18,119,128);connect.ForeColor=Color.White;tools.Controls.Add(connect);
            ButtonText(replay,"回放数据文件",120);tools.Controls.Add(replay);ButtonText(demo,"演示",65);tools.Controls.Add(demo);
            ButtonText(save,"保存 PNG",100);save.Enabled=false;tools.Controls.Add(save);ButtonText(folder,"打开记录",95);tools.Controls.Add(folder);
            tools.SetFlowBreak(folder,true);
            tools.Controls.Add(LabelText("预览画质"));quality.Width=155;quality.DropDownStyle=ComboBoxStyle.DropDownList;quality.Items.AddRange(new object[]{"清晰 320×180","流畅 160×90"});quality.SelectedIndex=0;tools.Controls.Add(quality);
            ButtonText(hq,"高清抓图 1280×720",175);tools.Controls.Add(hq);ButtonText(resume,"恢复预览",100);tools.Controls.Add(resume);
            overlay.Text="叠加板子数据";overlay.Checked=true;overlay.Width=145;overlay.Height=30;tools.Controls.Add(overlay);overlay.CheckedChanged+=delegate{picture.Overlay=overlay.Checked;picture.Invalidate();};
            quality.SelectedIndexChanged+=delegate{if(profileSent&&!hdRequested&&!hdHeld)SendCommand(quality.SelectedIndex==0?"Q":"P");};
            hq.Click+=delegate{if(SendCommand("H")){hdRequested=true;UpdateCommands();AddLog("等待原分辨率 RGB888 抓图，传输约 9 秒以上；完成后暂停图像，点击恢复预览继续。");}};
            resume.Click+=delegate{if(SendCommand(quality.SelectedIndex==0?"Q":"P")){hdRequested=false;hdHeld=false;picture.Hold=false;captionTime=DateTime.MinValue;UpdateCommands();}};
            UpdateCommands();
            source.Text="v18：3M 串口支持清晰预览 / 高清抓图；v17 可接收流畅预览；传统抓图使用 115200。";source.Dock=DockStyle.Fill;source.ForeColor=Color.FromArgb(65,83,104);shell.Controls.Add(source,0,2);
            TableLayoutPanel body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,330));shell.Controls.Add(body,0,3);
            TableLayoutPanel imagePanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0,0,12,0)};
            imagePanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));imagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute,46));body.Controls.Add(imagePanel,0,0);
            picture.Dock=DockStyle.Fill;picture.BackColor=Color.FromArgb(16,27,39);picture.SizeMode=PictureBoxSizeMode.Zoom;imagePanel.Controls.Add(picture,0,0);
            imageInfo.Text="等待连续预览 · HXP1 / RGB565 / CRC16\nv17 上电自动发送，无需拨动 SW2。";imageInfo.Dock=DockStyle.Fill;imageInfo.ForeColor=Color.SlateGray;imageInfo.Padding=new Padding(3,4,0,0);imagePanel.Controls.Add(imageInfo,0,1);
            TableLayoutPanel side=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=new Padding(0)};
            side.RowStyles.Add(new RowStyle(SizeType.Absolute,227));side.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            side.RowStyles.Add(new RowStyle(SizeType.Percent,100));side.RowStyles.Add(new RowStyle(SizeType.Absolute,75));side.RowStyles.Add(new RowStyle(SizeType.Absolute,30));side.RowStyles.Add(new RowStyle(SizeType.Absolute,30));body.Controls.Add(side,1,0);
            TableLayoutPanel metrics=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(12),ColumnCount=2,RowCount=6};
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,92));metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            string[] names={"水平 XC","垂直 YC","水平偏差 DX","垂直偏差 DY","检出像素 N","状态"};
            for(int i=0;i<6;i++){metrics.RowStyles.Add(new RowStyle(SizeType.Percent,100f/6));metrics.Controls.Add(LabelText(names[i]),0,i);values[i]=new Label{Text="—",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,Font=new Font(Font.FontFamily,15,FontStyle.Bold),ForeColor=Color.FromArgb(26,60,80)};metrics.Controls.Add(values[i],1,i);}side.Controls.Add(metrics,0,0);
            lastTime.Text="尚未收到坐标";lastTime.Dock=DockStyle.Fill;lastTime.ForeColor=Color.SlateGray;side.Controls.Add(lastTime,0,1);
            GroupBox chart=new GroupBox{Text="最近 180 条偏差 · px",Dock=DockStyle.Fill,BackColor=Color.White};trend.Dock=DockStyle.Fill;chart.Controls.Add(trend);side.Controls.Add(chart,0,2);
            Panel receive=new Panel{Dock=DockStyle.Fill};progress.Dock=DockStyle.Top;progress.Height=16;progress.Maximum=1000;receive.Controls.Add(progress);
            progressText.Dock=DockStyle.Bottom;progressText.Height=50;progressText.Text="等待连续预览数据";progressText.ForeColor=Color.SlateGray;receive.Controls.Add(progressText);side.Controls.Add(receive,0,3);
            autoSave.Text="CRC 通过后自动保存 PNG";autoSave.Checked=false;autoSave.Dock=DockStyle.Fill;autoSave.CheckedChanged+=delegate{autoSaving=autoSave.Checked;};side.Controls.Add(autoSave,0,4);
            recordRaw.Text="记录原始流（用于离线回放）";recordRaw.Checked=false;recordRaw.Dock=DockStyle.Fill;
            recordRaw.CheckedChanged+=delegate{rawRecording=recordRaw.Checked;};side.Controls.Add(recordRaw,0,5);
            baud.SelectedIndexChanged+=delegate{if(!active){bool legacy=baud.Text!="3000000";autoSave.Checked=legacy;recordRaw.Checked=legacy;}};
            log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.Dock=DockStyle.Fill;log.BackColor=Color.White;log.BorderStyle=BorderStyle.FixedSingle;shell.Controls.Add(log,0,4);
            FlowLayoutPanel foot=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};stats.AutoSize=true;stats.Text="RX 0 B · 坐标 0 条 · 图像 0 张";foot.Controls.Add(stats);
            ButtonText(choose,"保存目录…",95);choose.Height=26;foot.Controls.Add(choose);destination.AutoSize=true;destination.Text=output;destination.ForeColor=Color.SlateGray;foot.Controls.Add(destination);shell.Controls.Add(foot,0,5);
            refresh.Click+=async delegate{await RefreshPorts();};
            connect.Click+=delegate{if(active)StopSession();else{PortItem item=ports.SelectedItem as PortItem;if(item==null){AddLog("请先选择开发板 USB 串口并连接。");return;}StartSession("serial",item.Port,int.Parse(baud.Text));}};
            replay.Click+=delegate{using(OpenFileDialog d=new OpenFileDialog{Title="回放串口原始数据（HXF2 与坐标混合流）",Filter="原始数据|*.bin;*.hxf2;*.raw|全部文件|*.*"})if(d.ShowDialog()==DialogResult.OK)StartSession("replay",d.FileName,115200);};
            demo.Click+=delegate{StartSession("demo","",115200);};
            folder.Click+=delegate{Directory.CreateDirectory(output);Process.Start("explorer.exe","\""+output+"\"");};
            choose.Click+=delegate{using(FolderBrowserDialog d=new FolderBrowserDialog{SelectedPath=output,Description="选择原始数据、坐标 CSV 和图像保存目录"})if(d.ShowDialog()==DialogResult.OK){output=d.SelectedPath;destination.Text=output;}};
            save.Click+=delegate{if(picture.Image==null)return;using(SaveFileDialog d=new SaveFileDialog{Filter="PNG 图像|*.png",FileName="apt_image_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".png"})if(d.ShowDialog()==DialogResult.OK)try{picture.Image.Save(d.FileName,ImageFormat.Png);AddLog("图像已保存："+d.FileName);}catch(Exception ex){AddLog("保存失败："+ex.Message);}};
        }
        void Post(Action action){if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke(action);}catch(InvalidOperationException){}}
        void AddLog(string text){if(log.Lines.Length>100)log.Text=string.Join(Environment.NewLine,log.Lines,log.Lines.Length-60,60)+Environment.NewLine;log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
        async Task RefreshPorts(){
            refresh.Enabled=false;
            List<PortItem> found=await Task.Run(delegate{
                Dictionary<string,PortItem> map=new Dictionary<string,PortItem>();
                foreach(string p in SerialPort.GetPortNames())map[p]=new PortItem{Port=p,Name="串口"};
                try{using(ManagementObjectSearcher s=new ManagementObjectSearcher("SELECT DeviceID,Name,PNPDeviceID FROM Win32_SerialPort"))foreach(ManagementObject m in s.Get()){
                    string p=Convert.ToString(m["DeviceID"]);if(map.ContainsKey(p)){map[p].Name=Convert.ToString(m["Name"]);map[p].Bluetooth=Convert.ToString(m["PNPDeviceID"]).StartsWith("BTH",StringComparison.OrdinalIgnoreCase);}
                }}catch{}
                List<PortItem> result=new List<PortItem>(map.Values);result.Sort(delegate(PortItem a,PortItem b){return string.Compare(a.Port,b.Port,StringComparison.OrdinalIgnoreCase);});return result;
            });
            if(IsDisposed)return;ports.Items.Clear();foreach(PortItem p in found)ports.Items.Add(p);
            for(int i=0;i<found.Count;i++)if(found[i].Port==preferredPort){ports.SelectedIndex=i;break;}
            if(ports.SelectedIndex<0)for(int i=0;i<found.Count;i++)if(!found[i].Bluetooth){ports.SelectedIndex=i;break;}
            refresh.Enabled=!active;AddLog(ports.SelectedIndex<0?"未发现可默认选择的 USB 串口；接入开发板 USB 后刷新。":"已刷新串口列表，连接前核对开发板端口。");
        }
        void SetActive(bool value){active=value;connect.Text=value?"断开":"连接";ports.Enabled=!value;baud.Enabled=!value;refresh.Enabled=!value;demo.Enabled=!value;replay.Enabled=!value;choose.Enabled=!value;UpdateCommands();}
        void StopSession(){autoSuppressed=true;if(cancel!=null)cancel.Cancel();SerialPort s=serial;if(s!=null)Task.Run(delegate{try{s.Close();}catch{}});AddLog("正在停止接收；本次手动断开后暂停自动重连。");}
        void LoadSettings(){
            try{
                string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"receiver_settings.ini");if(!File.Exists(p))return;
                string[] s=File.ReadAllLines(p);if(s.Length>=3){preferredPort=s[0];int rate;if(int.TryParse(s[1],out rate)&&baud.Items.Contains(s[1])){preferredBaud=rate;baud.SelectedItem=s[1];}autoConnect.Checked=s[2]=="1";}
            }catch(Exception ex){AddLog("读取端口设置失败："+ex.Message);}
        }
        void SaveSettings(){
            try{File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"receiver_settings.ini"),new[]{preferredPort,preferredBaud.ToString(),autoConnect.Checked?"1":"0"});}catch{}
        }
        void CheckAutoConnect(){
            if(active||autoSuppressed||!autoConnect.Checked||preferredPort==""||DateTime.UtcNow<nextAutoScan)return;
            nextAutoScan=DateTime.UtcNow.AddSeconds(5);
            try{if(Array.IndexOf(SerialPort.GetPortNames(),preferredPort)>=0)StartSession("serial",preferredPort,preferredBaud);}catch(Exception ex){AddLog("自动连接检查失败："+ex.Message);}
        }
        public void StartDemo(){if(!active)StartSession("demo","",115200);}
        public void StartReplay(string path){if(!active)StartSession("replay",path,115200);}
        void OnTelemetry(Telemetry t){
            values[0].Text=t.Valid?t.X.ToString(Fmt)+" px":"—";values[1].Text=t.Valid?t.Y.ToString(Fmt)+" px":"—";
            values[2].Text=t.Valid?t.Dx.ToString("+0.00;-0.00;0.00")+" px":"—";values[3].Text=t.Valid?t.Dy.ToString("+0.00;-0.00;0.00")+" px":"—";
            values[4].Text=t.N.ToString("N0");values[5].Text=t.Valid?"VALID · 有效":"LOST · 失锁";values[5].ForeColor=t.Valid?Color.Teal:Color.Firebrick;
            latestTelemetry=t;picture.Latest=t;if(!picture.Hold)picture.Invalidate();
            if(t.Firmware>=18&&!profileSent&&serial!=null&&serial.IsOpen&&serial.BaudRate==3000000){profileSent=true;SendCommand(quality.SelectedIndex==0?"Q":"P");UpdateCommands();}
            lastTime.Text="最后坐标："+t.Time.ToString("HH:mm:ss.fff");trend.Push(t);
        }
        void UpdateProgress(){
            stats.Text=string.Format("RX {0:N0} B · 坐标 {1:N0} 条 · 图像 {2} 张 · CRC 错误 {3}",Interlocked.Read(ref bytes),Interlocked.Read(ref telemetryCount),images,crcFailures);
            UpdateCaption();
            PacketParser p=parser;
            if(p!=null&&p.Receiving&&!p.CurrentIsPreview&&p.Expected>0){
                progress.Style=ProgressBarStyle.Blocks;progress.Value=Math.Min(1000,(int)((long)p.Received*1000/p.Expected));
                double speed=p.Received/Math.Max(.1,(DateTime.Now-p.Started).TotalSeconds);
                progressText.Text=string.Format("高清接收 {0:P1} · 剩余约 {1:F0} 秒\n{2:N0} / {3:N0} B",(double)p.Received/p.Expected,speed>1?(p.Expected-p.Received)/speed:0,p.Received,p.Expected);
            }else if(hdRequested){progress.Style=ProgressBarStyle.Marquee;progressText.Text="等待高清抓图数据…";}
            else if(hdHeld){progress.Style=ProgressBarStyle.Blocks;progress.Value=1000;progressText.Text="高清图像已保留 · CRC16 通过\n点击恢复预览继续";}
            else if(previewSeen&&active){progress.Style=ProgressBarStyle.Marquee;progressText.Text="连续预览接收中 · 每帧 CRC 校验";}
        }
        void UpdateCaption(){
            if(lastWidth==0||(DateTime.UtcNow-captionTime).TotalSeconds<1)return;captionTime=DateTime.UtcNow;
            string text=hdHeld?string.Format("{0}×{1} 原分辨率 RGB888 · CRC16 通过 · {2}\n高清图像已暂停保留；PNG 保存原始像素，不包含电脑叠加层。",lastWidth,lastHeight,frameKind):
                previewSeen?string.Format("{0}×{1} 彩色连续预览 · 接收 {2:F1} fps · CRC16 校验 · {3}\n坐标基于 1280×720；左上角为电脑叠加的板子遥测。",lastWidth,lastHeight,previewFps,frameKind):
                string.Format("{0}×{1} RGB888 · CRC16 通过 · {2}\nPNG 保存原始图像像素。",lastWidth,lastHeight,frameKind);
            if(imageInfo.Text!=text)imageInfo.Text=text;
        }
        void UpdateCommands(){bool ok=active&&profileSent&&serial!=null;hq.Enabled=ok&&!hdRequested&&!hdHeld;quality.Enabled=!active||(ok&&!hdRequested&&!hdHeld);resume.Enabled=ok&&!hdRequested;}
        bool SendCommand(string command){
            SerialPort port=serial;if(port==null||!port.IsOpen||port.BaudRate!=3000000||latestTelemetry==null||latestTelemetry.Firmware<18)return false;
            try{port.WriteTimeout=200;port.Write(command);AddLog("已发送："+(command=="H"?"高清抓图":command=="Q"?"清晰预览 320×180":"流畅预览 160×90"));return true;}
            catch(Exception ex){AddLog("发送失败："+ex.Message);return false;}
        }

        void StartSession(string mode,string input,int bitRate){
            if(active)return;autoSuppressed=false;
            if(mode=="serial"){preferredPort=input;preferredBaud=bitRate;SaveSettings();}
            latestTelemetry=null;captureTelemetry=null;hdRequested=false;hdHeld=false;profileSent=false;picture.Hold=false;picture.Latest=null;lastWidth=0;UpdateCommands();
            SetActive(true);connect.Text=mode=="serial"?"断开":"停止";cancel=new CancellationTokenSource();CancellationToken token=cancel.Token;
            Interlocked.Exchange(ref bytes,0);Interlocked.Exchange(ref telemetryCount,0);Interlocked.Exchange(ref crcFailures,0);images=0;previewSeen=false;trend.Clear();progress.Style=ProgressBarStyle.Blocks;progress.Value=0;
            for(int i=0;i<6;i++)values[i].Text="—";lastTime.Text="等待坐标数据";
            string kind=mode=="demo"?"演示数据（非板子实拍）":mode=="replay"?"离线文件回放":"串口 "+input;
            source.Text="当前接收来源："+kind+"；图像区域保留最近一次成功接收的图像。";
            source.ForeColor=mode=="demo"?Color.DarkOrange:Color.FromArgb(65,83,104);
            link.Text=mode=="serial"?"连接中…":kind;link.ForeColor=Color.Teal;
            string session=Path.Combine(output,mode+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            bool failed=false;
            Task.Run(delegate{
                try {
                    Directory.CreateDirectory(session);
                    File.WriteAllText(Path.Combine(session,"source.txt"),"来源="+kind+Environment.NewLine+"输入="+input+Environment.NewLine+"波特率="+bitRate+Environment.NewLine+"本机接收时间="+DateTime.Now.ToString("O"),Encoding.UTF8);
                    using(FileStream raw=new FileStream(Path.Combine(session,"raw_stream.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536))
                    using(StreamWriter csv=new StreamWriter(Path.Combine(session,"telemetry.csv"),false,new UTF8Encoding(true))){
                        csv.WriteLine("pc_receive_time,xc_px,yc_px,dx_px,dy_px,n,mode,agc,gain_percent,firmware");
                        PacketParser p=new PacketParser();parser=p;int imageNo=0;DateTime lastPreview=DateTime.MinValue;double fps=0;
                        p.Notice+=delegate(string text){if(text.Contains("CRC 失败"))Interlocked.Increment(ref crcFailures);Post(delegate{AddLog(text);});};
                        p.FrameStarted+=delegate(bool isPreview){if(!isPreview)captureTelemetry=latestTelemetry;};
                        p.TelemetryReceived+=delegate(Telemetry t){
                            latestTelemetry=t;
                            csv.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1:F2},{2:F2},{3:F2},{4:F2},{5},{6},{7},{8},{9}",t.Time.ToString("O"),t.X,t.Y,t.Dx,t.Dy,t.N,t.Mode,t.Agc,t.Gain,t.Firmware));Interlocked.Increment(ref telemetryCount);Post(delegate{OnTelemetry(t);});
                        };
                        p.FrameReceived+=delegate(FramePacket f){
                            Bitmap bitmap=Images.Decode(f);string saved="";
                            if(f.IsPreview){if(lastPreview!=DateTime.MinValue){double instant=1/Math.Max(.001,(f.Time-lastPreview).TotalSeconds);fps=fps==0?instant:fps*.8+instant*.2;}lastPreview=f.Time;}
                            double currentFps=fps;
                            if(autoSaving||(!f.IsPreview&&hdRequested))try{saved=Path.Combine(session,(mode=="demo"?"DEMO_":"image_")+f.Time.ToString("yyyyMMdd_HHmmss_fff")+"_"+(++imageNo).ToString("D3")+".png");bitmap.Save(saved,ImageFormat.Png);}catch(Exception ex){string error=ex.Message;Post(delegate{AddLog("自动保存失败："+error);});}
                            string path=saved;Post(delegate{
                                Image old=picture.Image;picture.Image=bitmap;if(old!=null)old.Dispose();images++;previewSeen=f.IsPreview;save.Enabled=true;if(!f.IsPreview)progress.Value=1000;
                                lastWidth=f.Width;lastHeight=f.Height;previewFps=currentFps;frameKind=kind;
                                if(!f.IsPreview){hdHeld=hdRequested;hdRequested=false;picture.Hold=hdHeld;picture.Frozen=captureTelemetry;captionTime=DateTime.MinValue;UpdateCommands();AddLog("图像接收完成，CRC 通过。"+(path==""?"":" 已保存："+path));}
                                else if(images==1)AddLog("连续预览已开始；状态文字每秒统一刷新。");
                                UpdateCaption();

                            });
                        };
                        Action<byte[],int> feed=delegate(byte[] chunk,int n){if(rawRecording||mode!="serial")raw.Write(chunk,0,n);Interlocked.Add(ref bytes,n);p.Feed(chunk,n);};
                        Post(delegate{AddLog("本次记录目录："+session);});
                        if(mode=="serial") {
                            using(SerialPort port=new SerialPort(input,bitRate,Parity.None,8,StopBits.One)) {
                                port.Handshake=Handshake.None;port.DtrEnable=false;port.RtsEnable=false;port.ReadTimeout=250;port.ReadBufferSize=131072;serial=port;port.Open();
                                Post(delegate{link.Text="已连接 "+input;AddLog("已连接；收到 v18 状态后自动切换所选画质；v17 保持原预览。");});
                                byte[] buffer=new byte[8192];DateTime last=DateTime.UtcNow;
                                while(!token.IsCancellationRequested)try{int n=port.Read(buffer,0,buffer.Length);if(n>0){last=DateTime.UtcNow;feed(buffer,n);}}
                                catch(TimeoutException){if((DateTime.UtcNow-last).TotalSeconds>15)p.AbortIncomplete("图像连续 15 秒没有新数据，已取消不完整帧；等待后续完整图像。");}
                            }
                        } else if(mode=="replay") {
                            using(FileStream stream=File.OpenRead(input)){byte[] buffer=new byte[32768];int n;while(!token.IsCancellationRequested&&(n=stream.Read(buffer,0,buffer.Length))>0)feed(buffer,n);}
                            p.AbortIncomplete("文件结束：末尾存在不完整图像帧，未保存该帧。");
                        } else {
                            for(int i=0;i<50&&!token.IsCancellationRequested;i++){
                                byte[] t=DemoTelemetry(i);feed(t,t.Length);
                                byte[] frame=Images.DemoPreviewPacket(i);feed(frame,frame.Length);Thread.Sleep(120);
                            }
                        }
                        csv.Flush();raw.Flush();
                    }
                } catch(Exception ex){if(!token.IsCancellationRequested){failed=true;string error=ex.Message;Post(delegate{AddLog("接收失败："+error);});}}
                finally {
                    serial=null;parser=null;
                    Post(delegate{SetActive(false);progress.Style=ProgressBarStyle.Blocks;link.Text=failed?"接收异常":"已停止";link.ForeColor=failed?Color.Firebrick:Color.SlateGray;if(progress.Value!=1000){progress.Value=0;progressText.Text="接收已停止";}AddLog("接收结束。记录目录："+session);});
                }
            });
        }
        static byte[] DemoTelemetry(int i){return Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,"XC:{0:0000.00} YC:{1:0000.00} DX:{2:+000.00;-000.00;+000.00} DY:{3:+000.00;-000.00;+000.00} N:001234\r\n",730+Math.Sin(i*.12)*15,330+Math.Cos(i*.12)*10,90+Math.Sin(i*.12)*15,-30+Math.Cos(i*.12)*10));}
    }
    static class Program {
        [STAThread] static void Main(string[] args){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            MainForm form=new MainForm();form.Shown+=delegate{if(args.Length>0&&args[0]=="--demo")form.StartDemo();else if(args.Length>1&&args[0]=="--replay")form.StartReplay(args[1]);};Application.Run(form);
        }
    }
}

