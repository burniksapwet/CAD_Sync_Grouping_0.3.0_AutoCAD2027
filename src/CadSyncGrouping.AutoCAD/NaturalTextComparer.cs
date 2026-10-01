using System.Text.RegularExpressions;
namespace CadSyncGrouping;
internal sealed class NaturalTextComparer : IComparer<string>
{
    internal static readonly NaturalTextComparer Instance=new();
    public int Compare(string? x,string? y)
    {
        var a=Regex.Matches(x??"",@"\d+|\D+");var b=Regex.Matches(y??"",@"\d+|\D+");
        for(int i=0;i<Math.Min(a.Count,b.Count);i++)
        {
            var aa=a[i].Value;var bb=b[i].Value;int result;
            if(char.IsDigit(aa[0])&&char.IsDigit(bb[0]))
            {
                var na=aa.TrimStart('0');var nb=bb.TrimStart('0');
                result=na.Length.CompareTo(nb.Length);if(result==0)result=StringComparer.Ordinal.Compare(na,nb);
                if(result==0)result=aa.Length.CompareTo(bb.Length);
            }
            else result=StringComparer.OrdinalIgnoreCase.Compare(aa,bb);
            if(result!=0)return result;
        }
        return a.Count.CompareTo(b.Count);
    }
}
