using System;
using System.Globalization;
using System.Linq;
namespace krypRevit.Addin.Endpoints
{
    public sealed class ElementReference
    {
        public string Text { get; }
        public long? NumericId { get; }
        private ElementReference(string text,long? numeric) {Text=text;NumericId=numeric;}
        public static ElementReference Parse(string value)
        {
            if(string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Supply a numeric Revit ElementId or an actual Revit UniqueId.");
            var text=value.Trim();
            bool digits=text.All(c=>c>='0' && c<='9');
            bool signed=text.Length>1 && (text[0]=='-' || text[0]=='+') && text.Skip(1).All(c=>c>='0' && c<='9');
            if(digits || signed)
            {
                long id;
                if(!digits || !long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out id) || id<=0) throw new ArgumentException("ElementId must be a positive 64-bit integer, passed as digits without signs or separators.");
                return new ElementReference(text,id);
            }
            return new ElementReference(text,null);
        }
    }
}
