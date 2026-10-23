using System.Text.RegularExpressions;
namespace CodexUsageMonitor;

// The user can edit this file in the runtime data directory; patterns are bounded and timed.
internal sealed class RadarRules
{
    public int schema_version { get; set; } = 2;
    public string[] IncomingPatterns { get; set; } = [
        @"\breset\s+(?:is\s+)?incoming\b", @"\breset\s+tomorrow\b",
        @"\breset\s+will\s+(?:land|be\s+applied|begin|take\s+place)\b",
        @"\bwill\s+(?:do|apply|perform)\s+(?:a\s+)?(?:Codex\s+)?(?:global\s+)?reset\b",
        @"\bwill\s+reset\s+(?:Codex|usage|quota|limits)\b", @"\breset\s+rollout\s+has\s+begun\b",
        @"\b(?:are|is)\s+resetting\s+(?:Codex\s+)?usage\b", @"\bland(?:s|ing)?\b",
        @"\bby\s+EOD\b", @"\bwithin\s+the\s+next\s+\d+\s+(?:hours?|minutes?)\b",
        @"\breset\s+day\b", @"\b(?:today|tonight|tomorrow)\b"
    ];
    public string[] CompletedPatterns { get; set; } = [
        @"\bdone\s+and\s+dusted\b", @"\bin\s+the\s+bag\b", @"\bpropagated\b",
        @"\bprocessed\b", @"\bis\s+live\b", @"\brolled\s+out\b",
        @"\b(?:has|have|was|were|been)\s+(?:fully\s+)?(?:applied|completed|reset)\b",
        @"^\s*(?:it['’]s\s+)?done[.!]?\s*$"
    ];
    public string[] BankedPatterns { get; set; } = [@"\bbanked\s+reset\b", @"\breset\s+(?:cards?|credits?)\b"];

    internal static RadarRules Load(string root)
    {
        string path=Path.Combine(root,"radar_rules.v2.json");
        try { if(!File.Exists(path)){var defaults=new RadarRules();AtomicJson.Save(path,defaults);return defaults;}
            var rules=AtomicJson.Load(path,()=>new RadarRules());
            if(rules.schema_version!=2) return new();
            foreach(var patterns in new[]{rules.IncomingPatterns,rules.CompletedPatterns,rules.BankedPatterns}){
                if(patterns is null||patterns.Length>128) return new();
                foreach(string p in patterns){if(p is null||p.Length>512)return new();_ = new Regex(p,RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));}
            }
            return rules;
        } catch { return new(); }
    }
    internal static bool Any(string text,IEnumerable<string> patterns)=>patterns.Any(p=>Regex.IsMatch(text,p,RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(100)));
}
