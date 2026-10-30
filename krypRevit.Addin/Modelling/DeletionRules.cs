using System;
using System.Collections.Generic;
using System.Linq;
namespace krypRevit.Addin.Endpoints
{
    public static class DeletionRules
    {
        public const int MaximumImpacts=50;
        public static void ValidateBatch(IReadOnlyList<EditAction> actions)
        {
            if(actions.Any(a=>a.operation=="delete_element") && (actions.Count!=1 || actions[0].operation!="delete_element")) throw new ArgumentException("Deletion must be a single-target, deletion-only proposal so its complete dependency preview can be reviewed.");
        }
        public static void ValidateImpacts(IEnumerable<long> expected,IEnumerable<long> actual)
        {
            var left=expected.ToArray();var right=actual.ToArray();
            if(left.Length<1 || left.Length>MaximumImpacts || left.Any(i=>i<=0) || left.Distinct().Count()!=left.Length || right.Distinct().Count()!=right.Length || !new HashSet<long>(left).SetEquals(right))
                throw new InvalidOperationException("Deletion dependencies changed or exceed the review limit. The batch was not committed; request a fresh preview.");
        }
    }
    public sealed class DeletionImpact
    {
        public string id { get; set; }
        public string unique_id { get; set; }
        public string name { get; set; }
        public string category { get; set; }
        public bool is_target { get; set; }
    }
}
