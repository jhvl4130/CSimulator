using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 탄환 모델. 궤적 보간으로 이동하며 충돌 판정 수행.
/// FCS에 BulletPositionEvent, Target에 AttackEvent, FCS에 DestroyedEvent 경로.
/// </summary>
public class Bullet : Model
{
    private List<BallisticState> _trajectory = new();
    private int _cursor;
    private XYZPos _prevPos;
    private bool _hasPrev;

    public double BulletPower { get; set; } = 10.0;

    /// <summary>물리 속성</summary>
    public double Mass { get; set; }
    public double Diameter { get; set; }
    public double DragCoefficient { get; set; }
    public double MaxRange { get; set; }

    /// <summary>표적 ID</summary>
    public int TgtId { get; set; }

    /// <summary>소속 FCS 참조</summary>
    public Model? Fcs { get; set; }

    public Bullet(int id) : base(id)
    {
        Class = ModelClass.Weapon;
        Type = MtBullet;
        Name = $"Bullet-{id}";
    }

    public void SetTrajectory(IEnumerable<BallisticState> points)
    {
        _trajectory = new List<BallisticState>(points);
    }

    private XYZPos Interpolate(double t)
    {
        while (_cursor < _trajectory.Count - 2 && _trajectory[_cursor + 1].Time <= t)
            _cursor++;

        if (_cursor > 1)
        {
            _trajectory.RemoveRange(0, _cursor - 1);
            _cursor = 1;
        }

        if (_cursor >= _trajectory.Count - 1)
            return _trajectory[^1].Pos;

        var a = _trajectory[_cursor];
        var b = _trajectory[_cursor + 1];

        double dt = b.Time - a.Time;
        if (dt < 1e-12) return a.Pos;

        double alpha = Math.Clamp((t - a.Time) / dt, 0.0, 1.0);

        return new XYZPos(
            a.Pos.X + (b.Pos.X - a.Pos.X) * alpha,
            a.Pos.Y + (b.Pos.Y - a.Pos.Y) * alpha,
            a.Pos.Z + (b.Pos.Z - a.Pos.Z) * alpha);
    }

    public override double Init(double t)
    {
        _cursor = 0;
        _hasPrev = false;
        Phase = PhaseType.Run;
        IsEnabled = true;

        if (_trajectory.Count < 2)
        {
            Phase = PhaseType.End;
            IsEnabled = false;
            return TInfinite;
        }

        Pos = Interpolate(t);
        _prevPos = Pos;
        _hasPrev = true;

        return MovePeriod;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run)
            return TInfinite;

        // 궤적 시간 범위 벗어나면 종료
        if (t > _trajectory[^1].Time)
        {
            EndBullet(false, 0);
            return TInfinite;
        }

        var prevInterp = Pos;
        Pos = Interpolate(t);

        // FCS에 BulletPosition 보고
        if (Fcs is not null)
        {
            double dt = MovePeriod;
            var vel = new XYZPos(
                (Pos.X - prevInterp.X) / dt,
                (Pos.Y - prevInterp.Y) / dt,
                (Pos.Z - prevInterp.Z) / dt);
            Engine!.SendEvent(Fcs, new BulletPositionEvent(Pos, vel));
        }

        // 선분-AABB 충돌 판정
        if (_hasPrev)
        {
            foreach (var target in Engine!.GetModelsByClass(ModelClass.Target))
            {
                if (!target.IsEnabled) continue;
                if (target.HalfX <= 0.0 && target.HalfY <= 0.0 && target.HalfZ <= 0.0)
                    continue;

                if (CollisionDetection.IsSegmentAABB(
                    _prevPos, Pos,
                    target.Pos, target.HalfX, target.HalfY, target.HalfZ))
                {
                    Logger.Dbg(DbgFlag.Collide,
                        $"{t:F6} [{Name}] → [{target.Name}] Hit\n");
                    // AttackEvent로 Target에 피해 전달 (FCS 참조 포함)
                    Engine.SendEvent(target, new AttackEvent(BulletPower, Fcs));
                    EndBullet(true, target.Id);
                    return TInfinite;
                }
            }
        }

        _prevPos = Pos;
        _hasPrev = true;

        return MovePeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }

    /// <summary>명중 평가</summary>
    public bool HitEval(XYZPos targetPos, double halfX, double halfY, double halfZ)
    {
        return CollisionDetection.IsSegmentAABB(
            _prevPos, Pos, targetPos, halfX, halfY, halfZ);
    }

    private void EndBullet(bool isHit, int targetId)
    {
        Phase = PhaseType.End;
        IsEnabled = false;
        _trajectory.Clear();
        Engine?.RemoveModel(Id);
    }
}
