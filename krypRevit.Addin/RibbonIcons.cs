using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
namespace krypRevit.Addin
{
    internal static class RibbonIcons
    {
        private static readonly Dictionary<string,ImageSource> Cache=new Dictionary<string,ImageSource>();
        public static void Apply(PushButtonData button,string command)
        {
            if(command!="Chat" && command!="Review" && command!="Status") throw new ArgumentException("Unknown command icon.");
            button.Image=Load(command,16);button.LargeImage=Load(command,32);
        }
        private static ImageSource Load(string command,int pixels)
        {
            string key=command+pixels;
            if(Cache.TryGetValue(key,out var cached)) return cached;
            using(var stream=typeof(RibbonIcons).Assembly.GetManifestResourceStream("krypRevit.Addin.Assets."+command+".png"))
            {
                if(stream==null) throw new InvalidOperationException("Missing krypRevit command icon: "+command);
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
                image.DecodePixelWidth=pixels;image.DecodePixelHeight=pixels;image.StreamSource=stream;image.EndInit();image.Freeze();
                Cache.Add(key,image);return image;
            }
        }
    }
}
