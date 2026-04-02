using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

/// <summary>
/// 탄환 모델. 외부에서 궤적 리스트를 받아 시뮬레이션 시간에 맞춰
/// 선형 보간된 위치로 선분-AABB 충돌 판정을 수행한다.
/// MovePeriod(100Hz)로 스케줄링되며, 궤적 시간과 시뮬레이션 시간이
/// 맞지 않으면 두 궤적 점 사이를 보간하여 사용.
/// </summary>
public class Bullet : Model
{
    private List<BulletPoint> _trajectory = new();
    private int _cursor;
    private XYZPos _prevPos;
    private bool _hasPrev;

    public double BulletPower { get; set; } = 10.0;

    public Bullet(int id) : base(id)
    {
        Class = ModelClass.Weapon;
        Type = MtBullet;
        Name = $"Bullet-{id}";
    }

    /// <summary>소속 FCS 참조 (명중 결과 보고용).</summary>
    public Model? Fcs { get; set; }

    /// <summary>궤적 데이터 일괄 설정. 시간 오름차순 정렬되어 있어야 함.</summary>
    public void SetTrajectory(IEnumerable<BulletPoint> points)
    {
        _trajectory = new List<BulletPoint>(points);
    }

    /// <summary>
    /// 시뮬레이션 시간 t에 해당하는 궤적 위치를 보간하여 반환.
    /// 커서를 t 이하인 마지막 궤적 점까지 전진시킨 뒤,
    /// cursor와 cursor+1 사이를 선형 보간.
    /// 소비된 궤적 포인트는 trim하여 메모리 해제.
    /// </summary>
    private XYZPos Interpolate(double t)
    {
        // 커서를 t에 맞게 전진
        while (_cursor < _trajectory.Count - 2 && _trajectory[_cursor + 1].Time <= t)
            _cursor++;

        // 소비된 궤적 포인트 trim (cursor-1 이전 제거)
        if (_cursor > 1)
        {
            _trajectory.RemoveRange(0, _cursor - 1);
            _cursor = 1;
        }

        // 궤적 끝에 도달한 경우 마지막 점 반환
        if (_cursor >= _trajectory.Count - 1)
            return _trajectory[^1].Pos;

        var a = _trajectory[_cursor];
        var b = _trajectory[_cursor + 1];

        double dt = b.Time - a.Time;
        if (dt < 1e-12)
            return a.Pos;

        double alpha = (t - a.Time) / dt;
        if (alpha < 0.0) alpha = 0.0;
        if (alpha > 1.0) alpha = 1.0;

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

        Logger.Dbg(DbgFlag.Init,
            $"{t:F6} [{Name}] created, trajectory points={_trajectory.Count}\n");

        return MovePeriod;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run)
            return TInfinite;

        // 궤적 시간 범위를 벗어나면 종료
        if (t > _trajectory[^1].Time)
        {
            EndBullet(false, 0);
            return TInfinite;
        }

        // 현재 시뮬레이션 시간으로 보간
        Pos = Interpolate(t);

        // 선분-AABB 충돌 판정: 이전 보간 위치 → 현재 보간 위치
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
                    Engine.SendEvent(target, new CollideEvent(BulletPower));
                    EndBullet(true, target.Id);
                    return TInfinite;
                }
            }
        }

        _prevPos = Pos;
        _hasPrev = true;

        Logger.Dbg(DbgFlag.Move,
            $"{t:F6} [{Name}] x={Pos.X:F2} y={Pos.Y:F2} z={Pos.Z:F2}\n");

        return MovePeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }

    private void EndBullet(bool isHit, int targetId)
    {
        Phase = PhaseType.End;
        IsEnabled = false;

        // FCS에 명중 결과 보고
        if (Fcs is not null)
        {
            Engine!.SendEvent(Fcs, new HitResultEvent(targetId, isHit,
                isHit ? BulletPower : 0.0));
        }

        // 궤적 데이터 해제
        _trajectory.Clear();

        // Engine에서 모델 제거 (메모리 해제)
        Engine?.RemoveModel(Id);
    }
}
