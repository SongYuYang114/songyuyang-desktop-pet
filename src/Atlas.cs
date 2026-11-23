using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PortraitPetPlus {
    public class Sprite {
        public BitmapSource Image;
        public byte[] Pixels;
        public int Width,Height;
        public Rect Bounds;
        public bool Opaque(int x,int y) { return x>=0 && y>=0 && x<Width && y<Height && Pixels[(y*Width+x)*4+3]>24; }
    }
    public class Atlas {
        public Sprite[] Frames;
        static readonly Dictionary<string,Atlas> Builtin = new Dictionary<string,Atlas>();
        public static Atlas LoadBuiltin(string key) {
            Atlas a;
            if(Builtin.TryGetValue(key,out a)) return a;
            using(Stream f=Assembly.GetExecutingAssembly().GetManifestResourceStream(key+".png")) {
                if(f==null) throw new IOException("缺少内置素材："+key);
                a=Read(f,(key=="extra" || key=="rest") ? 2 : 3,(key=="extra" || key=="rest") ? 2 : 3);
            }
            Builtin[key]=a; return a;
        }
        public static Atlas LoadFile(string path,int cols,int rows) {
            var info=new FileInfo(path);
            if(info.Length>30*1024*1024) throw new IOException("图片文件请控制在 30 MB 以内。");
            using(var f=File.OpenRead(path)) return Read(f,cols,rows);
        }
        static Atlas Read(Stream stream,int cols,int rows) {
            if(cols<1 || rows<1 || cols>3 || rows>3) throw new IOException("动作表格只支持 1～3 行列。");
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream; image.EndInit(); image.Freeze();
            if(image.PixelWidth>6000 || image.PixelHeight>6000 || (long)image.PixelWidth*image.PixelHeight>16000000) throw new IOException("请使用不超过 1600 万像素、单边不超过 6000 像素的图片。");
            int w=image.PixelWidth/cols,h=image.PixelHeight/rows;
            if(w<16 || h<16) throw new IOException("图片尺寸太小。");
            var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
            int stride=rgba.PixelWidth*4; byte[] pixels=new byte[stride*rgba.PixelHeight]; rgba.CopyPixels(pixels,stride,0);
            Rect[] boxes=new Rect[cols*rows]; int maxW=0,maxH=0; bool hasClear=false;
            for(int i=0;i<boxes.Length;i++) {
                int ox=i%cols*w,oy=i/cols*h,minX=w,minY=h,maxX=-1,maxY=-1;
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
                    byte alpha=pixels[(oy+y)*stride+(ox+x)*4+3];
                    if(alpha<16) hasClear=true;
                    if(alpha>24) { minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y); }
                }
                if(maxX<0) throw new IOException("第 "+(i+1)+" 个动作格是空的。");
                minX=Math.Max(0,minX-2); minY=Math.Max(0,minY-2); maxX=Math.Min(w-1,maxX+2); maxY=Math.Min(h-1,maxY+2);
                boxes[i]=new Rect(ox+minX,oy+minY,maxX-minX+1,maxY-minY+1);
                maxW=Math.Max(maxW,maxX-minX+1); maxH=Math.Max(maxH,maxY-minY+1);
            }
            if(!hasClear) throw new IOException("请使用真正带透明背景的 PNG 图片。");
            var atlas=new Atlas { Frames=new Sprite[boxes.Length] };
            for(int i=0;i<boxes.Length;i++) {
                Rect b=boxes[i]; var crop=new CroppedBitmap(rgba,new Int32Rect((int)b.X,(int)b.Y,(int)b.Width,(int)b.Height));
                int nw=maxW+16,nh=maxH+16; double x=(nw-b.Width)/2,y=nh-b.Height-8;
                var dv=new DrawingVisual(); using(var dc=dv.RenderOpen()) dc.DrawImage(crop,new Rect(x,y,b.Width,b.Height));
                var rt=new RenderTargetBitmap(nw,nh,96,96,PixelFormats.Pbgra32); rt.Render(dv); rt.Freeze();
                byte[] p=new byte[nw*nh*4]; rt.CopyPixels(p,nw*4,0);
                atlas.Frames[i]=new Sprite { Image=rt,Pixels=p,Width=nw,Height=nh,Bounds=new Rect(x,y,b.Width,b.Height) };
            }
            return atlas;
        }
        public Sprite Get(int pose) {
            if(Frames.Length==1) return Frames[0];
            if(Frames.Length==4) { int[] map={0,1,2,3,3,3,0,0,0}; return Frames[map[Math.Min(8,Math.Max(0,pose))]]; }
            return Frames[Math.Min(Frames.Length-1,Math.Max(0,pose))];
        }
    }
}
