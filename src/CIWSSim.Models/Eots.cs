using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 광학 추적 장비 (EOTS). FCS 명령에 따라 종속 추적하여
/// 정밀 고각/방위각을 FCS에 전달한다.
/// </summary>
public class Eots : Model
{
    /// <summary>FCS 참조 (생성 시 주입).</summary>
    public Model? Fcs { get; set; }

    public Eots(int id) : base(id)
    {
        Class = ModelClass.Sensor;
        Type = MtEots;
        Name = $"EOTS-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.WaitStart;
        IsEnabled = true;
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        return TInfinite;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        if (ev is EotsCmdEvent cmd)
        {
            // 종속 추적: EOTS 위치에서 표적 방향으로 고각/방위각 계산
            var targetPos = cmd.TargetPos;

            double azimuth = GeoUtil.Bearing(Pos, targetPos);

            double dx = targetPos.X - Pos.X;
            double dy = targetPos.Y - Pos.Y;
            double dz = targetPos.Z - Pos.Z;
            double dist2D = Math.Sqrt(dx * dx + dy * dy);
            double elevation = GeoUtil.RadToDeg(Math.Atan2(dz, dist2D));

            // Pose 업데이트
            Pose = new Pose(azimuth, elevation, 0.0);
            Phase = PhaseType.Run;

            Logger.Dbg(DbgFlag.Move,
                $"{t:F6} [{Name}] Az={azimuth:F2} El={elevation:F2}\n");

            // FCS에 정밀 추적 결과 전송
            if (Fcs is not null)
            {
                Engine!.SendEvent(Fcs, new EotsDataEvent(azimuth, elevation, true));
            }

            return TContinue;
        }

        return TContinue;
    }
}
