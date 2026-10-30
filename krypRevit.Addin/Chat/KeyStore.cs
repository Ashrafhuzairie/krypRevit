using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace krypRevit.Addin.Chat
{
    public static class KeyStore
    {
        private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"krypRevit","Chat");
        private static string BasePath(string provider)
        {
            if(provider!="OpenAI" && provider!="Claude" && provider!="Gemini") throw new ArgumentException("Unknown provider.");
            return Path.Combine(Folder,provider);
        }
        public static string Load(string provider)
        {
            var path=BasePath(provider)+".key";
            return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser)) : "";
        }
        public static void Save(string provider,string key,string model)
        {
            var path=BasePath(provider);Directory.CreateDirectory(Folder);
            var protectedBytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser);
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllBytes(temporary,protectedBytes);
                if(File.Exists(path+".key")) File.Replace(temporary,path+".key",null);
                else File.Move(temporary,path+".key");
            }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
            File.WriteAllText(path+".model",model,Encoding.UTF8);
        }
        public static string Model(string provider)
        {
            var path=BasePath(provider)+".model";
            return File.Exists(path) ? File.ReadAllText(path,Encoding.UTF8) : ProviderClient.DefaultModel(provider);
        }
        public static void Remove(string provider)
        {
            var path=BasePath(provider);if(File.Exists(path+".key")) File.Delete(path+".key");
        }
    }
}
