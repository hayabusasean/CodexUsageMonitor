namespace CodexUsageMonitor;
// Stable, current Snowflake identities for legacy parser fixtures.
internal static class RadarTestIds {
 static readonly DateTimeOffset Anchor=DateTimeOffset.UtcNow.AddHours(-1);
 internal static string Id(string marker,DateTimeOffset? published=null){
  long sequence=long.TryParse(marker,out var value)?value&0x3fffff:Convert.ToInt64(Safe.Hash(marker)[..5],16);
  return ((((published??Anchor).ToUnixTimeMilliseconds()-1288834974657L)<<22)+sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
 }
}
