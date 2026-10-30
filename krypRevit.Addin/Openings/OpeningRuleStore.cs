using System;
using System.IO;
namespace krypRevit.Addin.Openings
{
    public static class OpeningRuleStore
    {
        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"krypRevit","OpeningRules.json");
        public static OpeningRules Load()
        {
            var rules=File.Exists(FilePath)?Json.Deserialize<OpeningRules>(File.ReadAllText(FilePath)):new OpeningRules();
            if(rules==null)throw new InvalidOperationException("Opening rules file is empty.");rules.Validate();return rules;
        }
        public static void Save(OpeningRules rules)
        {
            rules.Validate();Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temp,Json.Serialize(rules));if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}
            finally {if(File.Exists(temp))File.Delete(temp);}
            Endpoints.Plans.InvalidateOpeningPlans();
        }
    }
}
