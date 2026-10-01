using Autodesk.AutoCAD.DatabaseServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CadSyncGrouping;

public sealed class EquipmentIdentity
{
    public string Type { get; set; } = "Unknown";
    public string Status { get; set; } = "Unknown";
    public string ActualName { get; set; } = "";
    public string EffectiveName { get; set; } = "";
    public string Evidence { get; set; } = "No equipment evidence available.";
}

public static class EquipmentIdentification
{
    // Generated from descriptive effective names observed in the five repository DXFs.
    // Anonymous reference names and example aliases (SP1/BV1/etc.) are not mappings.
    private static readonly HashSet<string> DescriptiveNames = new(StringComparer.OrdinalIgnoreCase) {
        "BALL VALVE", "BUTTERFLY VALVE", "GATE VALVE", "GLOBE VALVE 1",
        "HORIZONTAL SWING CHECK VALVE", "PLUG VALVE", "REDUCER", "PIPELINE FILTER",
        "PRESSURE REDUCING REGULATOR", "SEPARATOR", "MOTOR OPERATED VALVE",
        "PRESSURE VACUUM VALVE", "QUICK CONNECTOR", "BACK PRESSURE REDUCING REGULATOR",
        "BLIND FLANGE", "STRAINER", "SOLENOID VALVE", "RELIEF VALVE", "RATE OF FLOW REGULATOR"
    };
    public static string AliasPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadSyncGrouping","equipment-aliases.json");
    private static string Normalize(string value)=>Regex.Replace(value.Trim(),@"\s+"," ").ToUpperInvariant();
    private static string CanonicalName(string value)=>Normalize(value)=="GLOBE VALVE 1"?"GLOBE VALVE":Normalize(value);
    private static string? VisibilityType(string value)
    {
        var normalized=Normalize(value);
        // Exact values observed in these definitions, including their spelling.
        if(normalized=="SOLENOIOD VALVE CLOSED")return "SOLENOID VALVE";
        if(normalized=="VACUUM RELIEF VALVE"||normalized=="Y TYPE STRAINER")return normalized;
        var name=Regex.Replace(normalized,@" (OPEN|CLOSED)$","");
        return new[]{"BALL VALVE","BUTTERFLY VALVE","GATE VALVE","GLOBE VALVE","PLUG VALVE"}.Contains(name)?name:null;
    }
    private static bool SemanticKey(string key)=>new[]{"EQUIPMENTTYPE","EQUIPMENTNAME","EQUIPMENTKIND"}.Contains(Regex.Replace(key.ToUpperInvariant(),@"[^A-Z]",""));
    public static Dictionary<string,string> ReadAliases()
    {
        if(!File.Exists(AliasPath))return new(StringComparer.OrdinalIgnoreCase);
        var data=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(AliasPath))??throw new InvalidDataException("Equipment aliases must be a JSON object.");
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in data){if(string.IsNullOrWhiteSpace(pair.Key)||string.IsNullOrWhiteSpace(pair.Value)||pair.Key.StartsWith("*"))throw new InvalidDataException("Mappings require non-anonymous names and equipment types.");result.Add(pair.Key.Trim(),Normalize(pair.Value));}
        return result;
    }
    public static void SaveAlias(string name,string type)
    {
        if(string.IsNullOrWhiteSpace(name)||name.StartsWith("*")||string.IsNullOrWhiteSpace(type)||type.Length>120)throw new InvalidDataException("Provide a named definition and an equipment type of 1–120 characters.");
        var aliases=ReadAliases();aliases[name.Trim()]=Normalize(type);
        Directory.CreateDirectory(Path.GetDirectoryName(AliasPath)!);
        var temp=AliasPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(aliases,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,AliasPath,true);
    }
    public static EquipmentIdentity Inspect(ObjectId id,Transaction tr,Dictionary<string,string>? aliases=null)
    {
        var result=new EquipmentIdentity();
        try {
            var br=(BlockReference)tr.GetObject(id,OpenMode.ForRead);
            var actual=(BlockTableRecord)tr.GetObject(br.BlockTableRecord,OpenMode.ForRead);
            var effective=(BlockTableRecord)tr.GetObject(br.IsDynamicBlock?br.DynamicBlockTableRecord:br.BlockTableRecord,OpenMode.ForRead);
            result.ActualName=actual.Name;result.EffectiveName=effective.Name;
            var claims=new List<(string Type,string Source)>();
            void Claim(string value,string source){if(!string.IsNullOrWhiteSpace(value))claims.Add((Normalize(value),source));}
            foreach(ObjectId aid in br.AttributeCollection){var a=(AttributeReference)tr.GetObject(aid,OpenMode.ForRead);if(SemanticKey(a.Tag))Claim(a.TextString,"Attribute "+a.Tag);}
            if(br.IsDynamicBlock)foreach(DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
            {
                var value=p.Value?.ToString()??"";
                if(SemanticKey(p.PropertyName))Claim(value,"Dynamic property "+p.PropertyName);
                else if(Regex.IsMatch(p.PropertyName,@"^Visibility\d*$",RegexOptions.IgnoreCase)&&VisibilityType(value) is string type)Claim(type,$"Dynamic {p.PropertyName} '{value}'");
            }
            if(DescriptiveNames.Contains(Normalize(effective.Name)))
            {
                var type=CanonicalName(effective.Name);
                // Observed explicit subtype states refine, rather than contradict,
                // these two broader definition names. Other conflicts stay Review.
                if(type=="PRESSURE VACUUM VALVE"&&claims.Any(c=>c.Type=="VACUUM RELIEF VALVE"))type="VACUUM RELIEF VALVE";
                if(type=="STRAINER"&&claims.Any(c=>c.Type=="Y TYPE STRAINER"))type="Y TYPE STRAINER";
                Claim(type,"Effective definition name '"+effective.Name+"'");
            }
            aliases ??= ReadAliases();
            if(!effective.Name.StartsWith("*")&&aliases.TryGetValue(effective.Name,out var mapped))Claim(mapped,"User-confirmed effective-name mapping");
            var types=claims.Select(c=>c.Type).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(types.Count==1){result.Type=types[0];result.Status="Identified";result.Evidence=string.Join("; ",claims.Select(c=>$"{c.Source}: {c.Type}"));}
            else if(types.Count>1){result.Status="Review";result.Evidence="Conflicting equipment evidence: "+string.Join("; ",claims.Select(c=>$"{c.Source}: {c.Type}"));}
            else {result.Evidence=$"No explicit equipment type or confirmed mapping for effective name '{effective.Name}'. Group identifiers and nearby Location IDs are not equipment types.";}
        }catch(System.Exception ex){result.Type="Unknown";result.Status="Review";result.Evidence="Equipment evidence could not be verified: "+ex.Message;}
        return result;
    }
}
