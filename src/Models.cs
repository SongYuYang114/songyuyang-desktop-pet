using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace PortraitPetPlus {
    public class PetProfile {
        public string Name = "宋雨杨";
        public string Outfit = "coat";
        public double Size = 310;
        public int Affinity;
        public double Satiety = 75;
        public double Energy = 90;
        public bool FollowMouse;
        public bool AutoRoutine = true;
        public bool Wander = true;
        public bool Topmost = true;
        public string CustomAtlas = "";
        public int AtlasColumns = 1;
        public int AtlasRows = 1;
        public string VoiceFile = "";
        public bool SoundEnabled;
        public string[] Lines = new string[] { "今天也慢慢来，我陪着你。", "累了就歇一会儿吧。", "做完一件小事，也值得开心。" };
        public double Left = Double.NaN;
        public double Top = Double.NaN;
        public int HeadPets;
        public int Snacks;
    }
    public class Preferences {
        public int Version = 3;
        public List<PetProfile> Pets = new List<PetProfile>();
        public bool BuddyEnabled = true;
        public bool WaterEnabled = true;
        public bool WaterDuringFocus;
        public int FocusMinutes = 25;
        public int BreakMinutes = 5;
        public int WaterMinutes = 45;
        public int FocusCompleted;
        public int WaterToday;
        public string WaterDate = "";
        public DateTime LastSavedUtc = DateTime.UtcNow;
    }
    public static class ProfileStore {
        public static Preferences Load(string root) {
            string path = Path.Combine(root, "settings.xml");
            Preferences p = null;
            try { if(File.Exists(path)) using(var f=File.OpenRead(path)) p=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(f); }
            catch { try { File.Copy(path,path+".unreadable-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true); } catch {} }
            if(p==null) p=new Preferences();
            if(p.Pets==null) p.Pets=new List<PetProfile>();
            while(p.Pets.Count<2) p.Pets.Add(new PetProfile { Name=p.Pets.Count==0 ? "宋雨杨" : "song雨杨",Outfit=p.Pets.Count==0 ? "coat" : "home" });
            if(p.Pets.Count>2) p.Pets.RemoveRange(2,p.Pets.Count-2);
            // Migrate the two existing pets once; later user edits remain intact.
            if(p.Version<3) {
                p.Pets[0].Name="宋雨杨";
                p.Pets[1].Name="song雨杨";
                p.Version=3;
            }
            p.FocusMinutes=Math.Max(1,Math.Min(180,p.FocusMinutes));
            p.BreakMinutes=Math.Max(1,Math.Min(60,p.BreakMinutes));
            p.WaterMinutes=Math.Max(5,Math.Min(240,p.WaterMinutes));
            foreach(var a in p.Pets) {
                a.Size=Math.Max(170,Math.Min(440,a.Size));
                a.Affinity=Math.Max(0,Math.Min(999,a.Affinity));
                a.Satiety=Math.Max(0,Math.Min(100,a.Satiety));
                a.Energy=Math.Max(0,Math.Min(100,a.Energy));
                if(a.Lines==null || a.Lines.Length==0) a.Lines=new string[]{"我在这里，陪你一会儿。"};
                if(String.IsNullOrWhiteSpace(a.Name)) a.Name=Object.ReferenceEquals(a,p.Pets[0]) ? "宋雨杨" : "song雨杨";
                if(a.Name.Length>16) a.Name=a.Name.Substring(0,16);
                if(a.Outfit!="coat" && a.Outfit!="home" && a.Outfit!="scarf" && a.Outfit!="festival") a.Outfit="coat";
                if(a.AtlasColumns<1 || a.AtlasColumns>3 || a.AtlasRows<1 || a.AtlasRows>3) { a.AtlasColumns=1; a.AtlasRows=1; }
            }
            ResetDay(p,DateTime.Now);
            return p;
        }
        public static void ResetDay(Preferences p,DateTime local) {
            string today=local.ToString("yyyy-MM-dd");
            if(p.WaterDate!=today) { p.WaterDate=today; p.WaterToday=0; }
        }
        public static void Save(string root,Preferences p) {
            Directory.CreateDirectory(root);
            string path=Path.Combine(root,"settings.xml"), temp=path+".tmp";
            p.LastSavedUtc=DateTime.UtcNow;
            using(var f=File.Create(temp)) new XmlSerializer(typeof(Preferences)).Serialize(f,p);
            if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
    }
    public enum FocusPhase { Idle, Focus, Break }
    public class FocusClock {
        public FocusPhase Phase = FocusPhase.Idle;
        public bool Paused;
        public DateTime DeadlineUtc;
        double heldSeconds;
        public double Remaining(DateTime now) { return Phase==FocusPhase.Idle ? 0 : Math.Max(0,Paused ? heldSeconds : (DeadlineUtc-now).TotalSeconds); }
        public void Start(DateTime now,double seconds) { Phase=FocusPhase.Focus; Paused=false; DeadlineUtc=now.AddSeconds(seconds); }
        public void TogglePause(DateTime now) {
            if(Phase==FocusPhase.Idle) return;
            if(Paused) { DeadlineUtc=now.AddSeconds(heldSeconds); Paused=false; }
            else { heldSeconds=Remaining(now); Paused=true; }
        }
        public void Stop() { Phase=FocusPhase.Idle; Paused=false; heldSeconds=0; }
        // A single transition per call prevents duplicate completion notifications.
        public FocusPhase Advance(DateTime now,double breakSeconds) {
            if(Phase==FocusPhase.Idle || Paused || Remaining(now)>0) return FocusPhase.Idle;
            FocusPhase completed=Phase;
            if(Phase==FocusPhase.Focus) { Phase=FocusPhase.Break; DeadlineUtc=now.AddSeconds(breakSeconds); }
            else Stop();
            return completed;
        }
        public string Label(DateTime now) {
            if(Phase==FocusPhase.Idle) return "准备好时，开始一段专注。";
            TimeSpan t=TimeSpan.FromSeconds(Math.Ceiling(Remaining(now)));
            return (Phase==FocusPhase.Focus ? "专注" : "休息")+(Paused ? " · 已暂停" : "")+"  "+((int)t.TotalMinutes).ToString("00")+":"+t.Seconds.ToString("00");
        }
    }
}
