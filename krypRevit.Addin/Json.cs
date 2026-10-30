using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace krypRevit.Addin
{
    public static class Json
    {
        // Serializer instances are not shared between HTTP worker threads.
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 32 };
        public static string Serialize(object value) => Serializer().Serialize(value);
        public static T Deserialize<T>(string value) => Serializer().Deserialize<T>(value);
        public static string Error(string message) => Serialize(new Dictionary<string, object> { ["ok"] = false, ["error"] = message });
    }
}
