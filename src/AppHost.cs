using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace PortraitPetPlus {
    public static class Desktop {
        [StructLayout(LayoutKind.Sequential)] public struct NativeRect { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct InputInfo { public uint cbSize,dwTime; }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref InputInfo info);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd,int index);
        public static double IdleSeconds() { InputInfo p=new InputInfo { cbSize=(uint)Marshal.SizeOf(typeof(InputInfo)) }; return GetLastInputInfo(ref p) ? unchecked((uint)Environment.TickCount-p.dwTime)/1000.0 : 0; }
    }
    public class AppHost : Application {
        public Preferences Settings;
        public string DataRoot;
        public readonly FocusClock Focus=new FocusClock();
        public readonly List<PetWindow> Pets=new List<PetWindow>();
        public readonly Stopwatch Watch=Stopwatch.StartNew();
        public double Now { get { return Watch.Elapsed.TotalSeconds; } }
        public DateTime NextWaterUtc=DateTime.UtcNow.AddMinutes(45);
        public bool WaterPending;
        public IntPtr LastExternalWindow;
        public bool Testing;
        public bool ShowPanelOnStart;
        public ControlPanel Panel;
        public string Status="准备就绪";
        Forms.NotifyIcon tray;
        DispatcherTimer ticker;
        double lastTick,lastSecond,lastSave,nextSocial=40;
        int socialStage;
        double socialUntil;
        bool exiting;
        string testDir;
        int testStage;
        readonly List<string> checks=new List<string>();
        public bool FocusActive { get { return Focus.Phase==FocusPhase.Focus && !Focus.Paused; } }
        [STAThread] public static void Main(string[] args) {
            bool test=args.Length==2 && args[0]=="--self-test";
            bool fresh;
            using(var singleton=new Mutex(true,test ? "PortraitPetPlus.Test" : "PortraitPetPlus.v2",out fresh)) {
                if(!fresh) return;
                try { var app=new AppHost(); app.Testing=test; app.testDir=test ? args[1] : null; app.ShowPanelOnStart=args.Length==1 && args[0]=="--panel"; app.Run(); }
                catch(Exception ex) {
                    if(test) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"result.txt"),"FAIL: "+ex); }
                    else MessageBox.Show("桌宠启动失败：\n"+ex.Message,"真人桌宠增强版");
                }
            }
        }
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e); ShutdownMode=ShutdownMode.OnExplicitShutdown;
            if(Testing) Directory.CreateDirectory(testDir);
            DataRoot=Testing ? Path.Combine(testDir,"profile") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PortraitPetPlus");
            Settings=ProfileStore.Load(DataRoot);
            NextWaterUtc=DateTime.UtcNow.AddMinutes(Settings.WaterMinutes);
            AddPet(0);
            if(Settings.BuddyEnabled) AddPet(1);
            if(!Testing) SetupTray();
            ticker=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(33) };
            ticker.Tick+=Tick; ticker.Start();
            Pets[0].Say("增强版已就位\n右键打开陪伴控制台",7);
            if(ShowPanelOnStart) OpenPanel();
        }
        public PetWindow Primary { get { return Pets.FirstOrDefault(x=>x.Index==0); } }
        public PetWindow FindPet(int index) { return Pets.FirstOrDefault(x=>x.Index==index); }
        void AddPet(int index) {
            var pet=new PetWindow(this,index); Pets.Add(pet); pet.Show();
            if(index==1) { pet.Left=Primary.Left-170; pet.Top=Primary.Top; pet.Clamp(); pet.Say("一起陪你。",3); }
        }
        public void ToggleBuddy(bool enabled) {
            if(Settings.BuddyEnabled==enabled && ((FindPet(1)!=null)==enabled)) return;
            Settings.BuddyEnabled=enabled;
            if(enabled && FindPet(1)==null) AddPet(1);
            if(!enabled && FindPet(1)!=null) { var buddy=FindPet(1); buddy.RememberPosition(); Pets.Remove(buddy); buddy.Close(); }
            Save(); RefreshPanel();
        }
        void SetupTray() {
            tray=new Forms.NotifyIcon(); tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            tray.Text="真人桌宠增强版 · 双击打开控制台";
            var m=new Forms.ContextMenuStrip();
            m.Items.Add("打开陪伴控制台",null,delegate { Dispatcher.BeginInvoke(new Action(OpenPanel)); });
            m.Items.Add("找回全部桌宠",null,delegate { Dispatcher.BeginInvoke(new Action(delegate { foreach(var p in Pets) p.GoHome(); })); });
            m.Items.Add("开始番茄钟",null,delegate { Dispatcher.BeginInvoke(new Action(StartFocus)); });
            m.Items.Add("退出全部桌宠",null,delegate { Dispatcher.BeginInvoke(new Action(ExitAll)); });
            tray.ContextMenuStrip=m; tray.DoubleClick+=delegate { Dispatcher.BeginInvoke(new Action(OpenPanel)); }; tray.Visible=true;
        }
        public void OpenPanel() {
            if(Panel==null) { Panel=new ControlPanel(this); Panel.Closed+=delegate { Panel=null; }; }
            Panel.Show(); Panel.Activate(); Panel.RefreshStatus();
        }
        public void RefreshPanel() { if(Panel!=null) Panel.RefreshStatus(); }
        public void SetStatus(string text) { Status=text; RefreshPanel(); }
        public void Save() {
            try { foreach(var p in Pets) p.RememberPosition(); ProfileStore.Save(DataRoot,Settings); Status="已保存到本机"; }
            catch(Exception ex) { Status="保存失败："+ex.Message; }
        }
        public void ExitAll() {
            if(exiting) return; exiting=true;
            Save(); if(ticker!=null) ticker.Stop();
            if(Panel!=null) Panel.Close();
            foreach(var p in Pets.ToArray()) p.Close(); Pets.Clear();
            if(tray!=null) { tray.Visible=false; tray.Dispose(); }
            Shutdown();
        }
        void Notify(string title,string text) {
            if(!Testing && tray!=null) { tray.BalloonTipTitle=title; tray.BalloonTipText=text; tray.ShowBalloonTip(5000); }
            foreach(var p in Pets) { p.Say(text,10); p.PlayVoice(); }
        }
        public void StartFocus() {
            if(Focus.Phase!=FocusPhase.Idle) { SetStatus("当前计时正在进行，可先暂停或结束。"); return; }
            Focus.Start(DateTime.UtcNow,Settings.FocusMinutes*60);
            foreach(var p in Pets) { p.CancelAttachment(); p.Action("sit",86400); p.Say("我安静陪你，开始吧。",4); }
            SetStatus("专注计时已开始");
        }
        public void PauseFocus() { Focus.TogglePause(DateTime.UtcNow); SetStatus(Focus.Paused ? "计时已暂停" : "计时已继续"); }
        public void StopFocus() { Focus.Stop(); foreach(var p in Pets) p.ClearAction(); SetStatus("计时已结束"); }
        void TickFocus(DateTime utc) {
            FocusPhase done=Focus.Advance(utc,Settings.BreakMinutes*60);
            if(done==FocusPhase.Focus) { Settings.FocusCompleted++; foreach(var p in Pets) { p.ClearAction(); p.Action("stretch",4); } Notify("专注完成","这一段完成啦，起来活动一下。开始休息计时。"); Save(); }
            else if(done==FocusPhase.Break) { foreach(var p in Pets) p.ClearAction(); Notify("休息结束","休息结束，准备好后再开始下一段。 "); }
        }
        public void AcknowledgeWater() {
            ProfileStore.ResetDay(Settings,DateTime.Now); Settings.WaterToday++; WaterPending=false;
            NextWaterUtc=DateTime.UtcNow.AddMinutes(Settings.WaterMinutes);
            foreach(var p in Pets) { p.Action("drink",4); p.Say("记下啦，我也喝一口。",4); }
            Save(); RefreshPanel();
        }
        public void SnoozeWater() { WaterPending=false; NextWaterUtc=DateTime.UtcNow.AddMinutes(10); SetStatus("喝水提醒已延后 10 分钟"); }
        public void ResetWaterSchedule() { WaterPending=false; NextWaterUtc=DateTime.UtcNow.AddMinutes(Settings.WaterMinutes); }
        void TickWater(DateTime utc) {
            if(!Settings.WaterEnabled || WaterPending || utc<NextWaterUtc || (FocusActive && !Settings.WaterDuringFocus)) return;
            WaterPending=true;
            foreach(var p in Pets) p.Action("drink",5);
            Notify("喝水时间","喝口水吧。右键选择「喝过了」或「稍后提醒」。");
        }
        public void Social() {
            var a=Primary; var b=FindPet(1);
            if(b==null) { ToggleBuddy(true); b=FindPet(1); }
            if(a==null || b==null) return;
            a.CancelAttachment(); b.CancelAttachment();
            Rect area=a.WorkArea();
            a.Left=Math.Max(area.Left,Math.Min(a.Left,area.Right-a.Width-b.Width+70));
            b.Left=a.Left+a.Width-65; b.Top=a.Top; b.Clamp();
            a.Action("snack",3); a.Say("给你一块饼干。",3); a.ShowEffect("🍪",3);
            b.Action("wave",3); b.Say("收到～",3);
            a.Profile.Affinity=Math.Min(999,a.Profile.Affinity+2); b.Profile.Affinity=Math.Min(999,b.Profile.Affinity+2);
            socialStage=1; socialUntil=Now+3; nextSocial=Now+70;
        }
        void TickSocial() {
            if(socialStage==1 && Now>socialUntil) {
                socialStage=0; var b=FindPet(1);
                if(b!=null) { b.Action("snack",4); b.Say("谢谢，一起歇一会儿。",4); Primary.Action("sit",5); }
            }
            if(Now>nextSocial) {
                nextSocial=Now+60;
                if(Settings.BuddyEnabled && !FocusActive && !WaterPending && Pets.All(p=>!p.Busy)) Social();
            }
        }
        void Tick(object sender,EventArgs args) {
            double now=Now,dt=Math.Max(0,Math.Min(.1,now-lastTick)); lastTick=now;
            foreach(var p in Pets.ToArray()) p.Step(dt);
            if(now-lastSecond>=1) {
                lastSecond=now; DateTime utc=DateTime.UtcNow;
                uint pid; IntPtr external=Desktop.GetForegroundWindow(); Desktop.GetWindowThreadProcessId(external,out pid);
                if(external!=IntPtr.Zero && pid!=(uint)Process.GetCurrentProcess().Id) LastExternalWindow=external;
                TickFocus(utc); TickWater(utc); ProfileStore.ResetDay(Settings,DateTime.Now); TickSocial(); RefreshPanel();
            }
            if(now-lastSave>30) { lastSave=now; Save(); }
            if(Testing) RunTests();
        }
        public void ReloadPet(int index) {
            var pet=FindPet(index); if(pet!=null) { pet.ReloadAtlas(); pet.ResizePet(); pet.Topmost=pet.Profile.Topmost; }
            Save(); RefreshPanel();
        }
        public void ImportAtlas(int index,string file,int cols,int rows) {
            // Validate before replacing either the active image or the saved profile.
            Atlas.LoadFile(file,cols,rows);
            string media=Path.Combine(DataRoot,"media"); Directory.CreateDirectory(media);
            string target=Path.Combine(media,"avatar-"+Guid.NewGuid().ToString("N")+".png"); File.Copy(file,target);
            var p=Settings.Pets[index]; p.CustomAtlas=target; p.AtlasColumns=cols; p.AtlasRows=rows;
            ReloadPet(index);
        }
        public string ImportVoice(string file) {
            if(new FileInfo(file).Length>30*1024*1024) throw new IOException("声音文件请控制在 30 MB 以内。");
            string ext=Path.GetExtension(file).ToLowerInvariant();
            if(ext!=".wav" && ext!=".mp3" && ext!=".m4a") throw new IOException("支持 WAV、MP3 和 M4A 声音文件。");
            string dir=Path.Combine(DataRoot,"media"); Directory.CreateDirectory(dir);
            string dest=Path.Combine(dir,"voice-"+Guid.NewGuid().ToString("N")+ext); File.Copy(file,dest); return dest;
        }
        void Check(bool condition,string name) { if(!condition) throw new Exception(name); checks.Add("PASS · "+name); }
        void RunTests() {
            try {
                if(testStage==0 && Now>1) {
                    Check(Primary.IsVisible && Primary.AllowsTransparency && Primary.Topmost,"transparent visible main window");
                    Check(Desktop.IsWindowVisible(new System.Windows.Interop.WindowInteropHelper(Primary).Handle),"native pet window is visible");
                    foreach(string key in new[]{"coat","home","scarf","festival"}) Check(Atlas.LoadBuiltin(key).Frames.Length==9,key+" nine action frames");
                    Check(Atlas.LoadBuiltin("extra").Frames.Length==4,"blink and reaction frames");
                    Check(Atlas.LoadBuiltin("rest").Frames.Length==4,"yawn and lying down frames");
                    int before=Primary.Profile.Affinity; Primary.Pat(); Check(Primary.Profile.Affinity>before,"head pat adds affection");
                    int after=Primary.Profile.Affinity; Primary.Pat(); Check(Primary.Profile.Affinity==after,"head pat cooldown prevents score spam");
                    Primary.Feed("cookie"); Check(Primary.CurrentAction=="snack" && Primary.Profile.Snacks>0,"feeding changes pose and saved stats");
                    Primary.Profile.Affinity=45; Primary.Action("heart",2); Check(Primary.CurrentAction=="heart","affection unlock action");
                    ToggleBuddy(true); Check(Pets.Count==2,"second pet created"); Social(); Check(socialStage==1,"two pets exchange snack");
                    testStage++;
                } else if(testStage==1 && Now>2.5) {
                    Primary.Profile.Outfit="home"; Primary.ReloadAtlas(); Check(Primary.ActiveAtlas.Frames.Length==9,"outfit switches complete pose set");
                    Primary.SavePreview(Path.Combine(testDir,"home-preview.png"));
                    DateTime t=new DateTime(2026,11,20,0,0,0,DateTimeKind.Utc);
                    var f=new FocusClock(); f.Start(t,60); f.TogglePause(t.AddSeconds(20)); Check(f.Remaining(t.AddHours(2))==40,"focus pause freezes remaining time");
                    f.TogglePause(t.AddSeconds(100)); Check(f.Remaining(t.AddSeconds(110))==30,"focus resume preserves remaining time");
                    Check(f.Advance(t.AddSeconds(141),20)==FocusPhase.Focus && f.Phase==FocusPhase.Break,"focus completion enters break");
                    Check(f.Advance(t.AddSeconds(142),20)==FocusPhase.Idle,"focus completion not duplicated");
                    Check(f.Advance(t.AddSeconds(162),20)==FocusPhase.Break && f.Phase==FocusPhase.Idle,"break completion returns idle");
                    Settings.WaterEnabled=true; NextWaterUtc=DateTime.UtcNow.AddSeconds(-1); WaterPending=false;
                    TickWater(DateTime.UtcNow); Check(WaterPending,"water reminder becomes pending once");
                    AcknowledgeWater(); Check(!WaterPending && Settings.WaterToday>0 && NextWaterUtc>DateTime.UtcNow,"water acknowledgement resets deadline");
                    SnoozeWater(); Check((NextWaterUtc-DateTime.UtcNow).TotalMinutes>9.9,"water snooze");
                    testStage++;
                } else if(testStage==2 && Now>4) {
                    Primary.Profile.FollowMouse=true; Primary.Profile.AutoRoutine=true;
                    Save(); var loaded=ProfileStore.Load(DataRoot);
                    Check(loaded.Pets[0].FollowMouse && loaded.Pets[0].Affinity==Settings.Pets[0].Affinity && loaded.BuddyEnabled,"settings and affection round trip");
                    string bad=Path.Combine(testDir,"bad.png"); File.WriteAllText(bad,"invalid image");
                    bool rejected=false; string original=Primary.Profile.CustomAtlas;
                    try { ImportAtlas(0,bad,1,1); } catch { rejected=true; }
                    Check(rejected && Primary.Profile.CustomAtlas==original,"bad imported image leaves active profile unchanged");
                    Primary.Profile.Outfit="coat"; Primary.ReloadAtlas(); Primary.ClearAction(); Primary.Action("wave",8);
                    Primary.Say("陪伴增强版\n摸头 · 投喂 · 专注 · 换装",8); testStage++;
                } else if(testStage==3 && Now>5) {
                    Primary.SavePreview(Path.Combine(testDir,"pet-preview.png"));
                    OpenPanel(); Panel.ExerciseProfileSwitch(); Check(true,"profile switching rebuilds voice and outfit controls");
                    Panel.SelectTab(0); Panel.SavePreview(Path.Combine(testDir,"companion-preview.png"));
                    Panel.SelectTab(2); Panel.SavePreview(Path.Combine(testDir,"appearance-preview.png"));
                    Panel.SelectTab(3); Panel.SavePreview(Path.Combine(testDir,"voice-preview.png"));
                    Panel.SelectTab(1); testStage++;
                } else if(testStage==4 && Now>6) {
                    Panel.SavePreview(Path.Combine(testDir,"panel-preview.png"));
                    string single=Path.Combine(testDir,"single-avatar.png");
                    var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(Atlas.LoadBuiltin("home").Frames[0].Image)); using(var stream=File.Create(single)) png.Save(stream);
                    ImportAtlas(1,single,1,1); Check(FindPet(1).ActiveAtlas.Frames.Length==1 && File.Exists(Settings.Pets[1].CustomAtlas),"valid single transparent avatar imports and is copied locally");
                    StartFocus(); Check(FocusActive && Pets.TrueForAll(p=>p.CurrentAction=="sit"),"starting focus seats both pets");
                    PauseFocus(); Check(Focus.Paused,"host pauses focus"); StopFocus(); Check(Focus.Phase==FocusPhase.Idle,"host stops focus");
                    Primary.Profile.AutoRoutine=false; Primary.Profile.Outfit="coat"; Primary.ReloadAtlas(); Primary.ClearAction();
                    Primary.Action("lie",3); Check(Primary.CurrentAction=="lie" && Primary.Width>=320,"lying pose expands drawing area"); Primary.ClearAction();
                    Rect area=Primary.WorkArea();
                    if(area.Height>550) {
                        var fixture=new Window { Title="Pet attachment test",Width=450,Height=160,Left=area.Left+150,Top=area.Top+Math.Min(380,area.Height-190),ShowActivated=false };
                        fixture.Show(); LastExternalWindow=new System.Windows.Interop.WindowInteropHelper(fixture).Handle;
                        Primary.AttachToWindow(); Check(Primary.Attached,"pet attaches to actual native window edge");
                        double beforeTop=Primary.Top; fixture.Top+=10; Primary.Step(.03); Check(Primary.Top>beforeTop+5,"attached pet follows moving native window");
                        fixture.Close(); Primary.Step(.03); Check(!Primary.Attached,"closed target window releases attachment");
                    }
                    ToggleBuddy(false); Check(Pets.Count==1,"second pet closes independently");
                    Check(Primary.PassesThroughPadding(),"transparent padding is click through");
                    Directory.CreateDirectory(testDir); File.WriteAllLines(Path.Combine(testDir,"result.txt"),checks.ToArray(),Encoding.UTF8); ExitAll();
                }
            } catch(Exception ex) { Directory.CreateDirectory(testDir); File.WriteAllText(Path.Combine(testDir,"result.txt"),"FAIL: "+ex+"\n"+String.Join("\n",checks)); ExitAll(); }
        }
    }
}
