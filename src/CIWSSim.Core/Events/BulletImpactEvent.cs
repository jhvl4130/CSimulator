using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Core.Events;

/// <summary>
/// Gun → FCS: 탄환 종료 시 탄착 결과 보고 (명중/빗나감)
/// </summary>
public class BulletImpactEvent : SimEvent
{
    public XYZPos Pos { get; }
    public bool IsHit { get; }
    public int TargetId { get; }

    public BulletImpactEvent(XYZPos pos, bool isHit, int targetId)
    {
        Pos = pos;
        IsHit = isHit;
        TargetId = targetId;
    }
}
