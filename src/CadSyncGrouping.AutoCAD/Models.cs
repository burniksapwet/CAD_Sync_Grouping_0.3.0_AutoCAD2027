using Autodesk.AutoCAD.DatabaseServices;

namespace CadSyncGrouping;

public enum CandidateStatus
{
    Existing,
    Ready,
    Reviewed,
    Ambiguous,
    NoMatch,
    Conflict,
    Accepted,
    ManuallyVerified
}

public sealed class GroupingCandidate
{
    public bool ActionSelected { get; set; }
    public bool Selected { get; set; }
    public string LocationId { get; set; } = string.Empty;
    public ObjectId TextId { get; set; } = ObjectId.Null;
    public ObjectId ProposedBlockId { get; set; } = ObjectId.Null;
    public string ProposedBlockName { get; set; } = string.Empty;
    public CandidateStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double NearestDistance { get; set; }
    public double SecondDistance { get; set; }
    public List<ObjectId> CandidateBlockIds { get; set; } = new();
    public List<ObjectId> LeaderIds { get; set; } = new();
    public string ReviewFingerprint { get; set; } = "";
    public bool ExistingMembership { get; set; }
    public bool EligibleText { get; set; }
    public EquipmentIdentity Equipment { get; set; } = new();

    public string StatusText => Status switch
    {
        CandidateStatus.Existing => "Existing group",
        CandidateStatus.Ready => "Ready",
        CandidateStatus.Reviewed => "Reviewed",
        CandidateStatus.Ambiguous => "Review",
        CandidateStatus.NoMatch => "No match",
        CandidateStatus.Conflict => "Conflict",
        CandidateStatus.Accepted => "Ignored",
        CandidateStatus.ManuallyVerified => "Manually verified group",
        _ => Status.ToString()
    };
}

public sealed class ScanResult
{
    public List<GroupingCandidate> Candidates { get; } = new();
    public int LocationTextCount { get; set; }
    public int ValveBlockCount { get; set; }
    public int ExistingValveGroups { get; set; }
    public Database? Database { get; set; }
    public string GeometrySignature { get; set; } = string.Empty;
    public int UnusableValveGeometry { get; set; }
}

internal readonly struct Extents2d
{
    public readonly double MinX;
    public readonly double MinY;
    public readonly double MaxX;
    public readonly double MaxY;

    public Extents2d(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double Width => Math.Max(0.0, MaxX - MinX);
    public double Height => Math.Max(0.0, MaxY - MinY);
    public double Diagonal => Math.Sqrt(Width * Width + Height * Height);
    public double CenterX => (MinX + MaxX) * 0.5;
    public double CenterY => (MinY + MaxY) * 0.5;
}
