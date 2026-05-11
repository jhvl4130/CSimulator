using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 탐지 레이더. 탐지 범위 내 Target을 주기적으로 탐지/추적하여
/// C2Control에 TrackInfoEvent를 전송한다.
/// </summary>
public class SearchRadar : Model
{
    /// <summary>
    /// 탐지 반경 (m)
    /// </summary>
    public double DetectRange { get; set; }

    /// <summary>
    /// 최소 수신 전력 임계값
    /// </summary>
    public double MinPower { get; set; }

    /// <summary>
    /// 탐지/추적 주기 (초)
    /// </summary>
    public double DetectPeriod { get; set; } = 1.0;

    /// <summary>
    /// C2Control 참조 (생성 시 주입)
    /// </summary>
    public Model? C2 { get; set; }

    /// <summary>지형 LOS 게이트 (null이면 LOS 검사 생략)</summary>
    public TerrainMap? Terrain { get; set; }

    /// <summary>LOS 광선 샘플 간격(m)</summary>
    public double SampleStepM { get; set; } = 30.0;

    /// <summary>4/3 등가지구반경 보정 사용 여부</summary>
    public bool UseEarthCurvature { get; set; } = true;

    /// <summary>안테나 높이(m). Pos.Z에 가산해서 LOS 시작점으로 사용.</summary>
    public double AntennaHeightM { get; set; } = 0.0;

    /// <summary>
    /// 이전 추적 데이터 (속도/가속도 계산용)
    /// </summary>
    private readonly Dictionary<int, (XYZPos pos, XYZPos vel, double t)> _prevData = new();

    public SearchRadar(int id) : base(id)
    {
        Class = ModelClass.Sensor;
        Type = MtSRadar;
        Name = $"SearchRadar-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.Run;
        IsEnabled = true;
        return DetectPeriod;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run || !IsEnabled)
            return TInfinite;

        foreach (var target in Engine!.GetModelsByClass(ModelClass.Target))
        {
            if (!target.IsEnabled) continue;

            double dist = GeoUtil.Distance(Pos, target.Pos);
            if (dist > DetectRange) continue;

            if (Terrain is not null)
            {
                var from = new XYZPos(Pos.X, Pos.Y, Pos.Z + AntennaHeightM);
                if (!Terrain.HasLineOfSight(from, target.Pos, SampleStepM, UseEarthCurvature))
                    continue;
            }

            // 속도/가속도 계산
            var pos = target.Pos;
            XYZPos vel, acc;

            if (_prevData.TryGetValue(target.Id, out var prev) && t > prev.t)
            {
                double dt = t - prev.t;
                vel = new XYZPos(
                    (pos.X - prev.pos.X) / dt,
                    (pos.Y - prev.pos.Y) / dt,
                    (pos.Z - prev.pos.Z) / dt);
                acc = new XYZPos(
                    (vel.X - prev.vel.X) / dt,
                    (vel.Y - prev.vel.Y) / dt,
                    (vel.Z - prev.vel.Z) / dt);
            }
            else
            {
                vel = new XYZPos(0, 0, 0);
                acc = new XYZPos(0, 0, 0);
            }

            _prevData[target.Id] = (pos, vel, t);

            Logger.Dbg(DbgFlag.Init,
                $"{t:F6} [{Name}] Track [{target.Name}] dist={dist:F1}m\n");

            if (C2 is not null)
            {
                Engine.SendEvent(C2, new TrackInfoEvent(target.Id, pos, vel, acc));
            }
        }

        // Health 보고
        if (C2 is not null)
        {
            Engine!.SendEvent(C2, new HealthEvent(Id, Health));
        }

        return DetectPeriod;
    }

    /// <summary>
    /// 레이더 수신 전력 계산 (거리 기반)
    /// </summary>
    public double RadarRecvPower(double dist)
    {
        if (dist <= 0) return double.MaxValue;
        // 레이더 방정식 간략: P_r ∝ 1/R^4
        return 1.0 / (dist * dist * dist * dist);
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }
}
