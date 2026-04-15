# CLAUDE.md

## 프로젝트 개요

CIWSSim — CIWS(근접방어무기체계) 3D 이산 사건 시뮬레이션. .NET 8.0 C# 솔루션.

```bash
dotnet build CIWSSim.sln
dotnet run --project src/CIWSSim.App                # 기본 input.json
dotnet run --project src/CIWSSim.App -- custom.json # 커스텀 입력
```

입출력 디렉토리는 `SimulationBuilder.FileDir`(기본 `D:\CIWSSimulator\File`)로 하드코딩.

- 입력: `input.json`
- 출력:
  - `Target.csv` — 비행체 위치/자세 (10Hz, `OutputPeriod=0.1s`)
  - `CIWS.csv` — Gun 포탑 자세/발사 플래그 (10Hz)
  - `event_log.csv` — 교전 이벤트 (C2Control이 이벤트 발생 시점에 기록)

## 프로젝트 구조

```
src/
├── CIWSSim.Core/        # 시뮬레이션 프레임워크 (Engine, Model, Events, Geometry, Util, Constants)
├── CIWSSim.Models/      # 구체 모델 (아래 모델 목록 참조)
└── CIWSSim.App/         # 콘솔 앱 (Program.cs, SimulationBuilder.cs, InputConfig.cs)
```

## 교전 흐름

```
SearchRadar(전체1) ──Detect──▶ C2Control(전체1) ──Assign──▶ FCS(CIWS당1)
  FCS ──TrackCmd──▶ TrackRadar ──TrackData──▶ FCS
  FCS ──FireCmd──▶ Gun → Bullet ──Collide──▶ Target → HitResult──▶ FCS → PHP평가 → C2
  AssetZone 도달 = 요격실패 → FailEvent → C2 → FCS
```

CIWS 1세트 = FCS + TrackRadar + Gun

## 모델 목록

| 모델 | Class | 역할 |
|------|-------|------|
| Airplane / Missile / Uav / Drone / Rocket | Target | 비행체 |
| Launcher | Target | Rocket 발사 플랫폼 |
| SearchRadar | Sensor | 탐지 → C2 |
| TrackRadar | Sensor | 추적 → FCS (25/50/100Hz) |
| C2Control | C2 | 위협순위, CIWS 할당, event_log 기록 |
| FCS | C2 | 추적/사격 명령, PHP 피해평가 |
| Bullet / Gun | Weapon | 탄환 생성 / 충돌판정 |
| Asset / AssetZone | Asset | 건물 / 요격실패 판정 |

## FCS 상태 머신 (PhaseType)

```
Wait → StartEngage → TrackRcvd → FireOn → TgtDestroyed → Wait
                                        → FireOff → Wait
피격: AttackedAlive / AttackedDie → Disabled
```

## 엔진 핵심

- 시간 버킷 스케줄링 (`SortedDictionary<long, List<Model>[]>`), `TScale=10M`
- 클래스 우선순위: Target → Sensor → C2 → Weapon → Asset
- 모델 생명주기: `Init(t)` → `IntTrans(t)` → `ExtTrans(t, event)`
- `OnModelTransitioned` 콜백으로 CSV 스트리밍 (메모리에 안 쌓음)
- `RemoveModel(id)`: Bullet 메모리 해제용

## Output 주기

- 시뮬레이션 tick: `MovePeriod=0.01s` (100Hz) — 충돌판정 정확도에 직결
- CSV 기록: `OutputPeriod=0.1s` (10Hz) — `SimulationBuilder`의 observer 레이어에서 절대 기준(`0.1, 0.2, …`)으로 다운샘플링
- `event_log.csv`는 이벤트 드리븐이라 주기 영향 없음

## 좌표계

- 내부: ENU (X=동, Y=북, Z=상) 외부: LLH (Lat/Lon/Hgt)
- 방위각: 0°=북, 90°=동, 시계방향. 고각: 0°=수평, 양수=위
- 변환: `GeoUtil.LlaToEnu()` / `EnuToLla()`

## 충돌 판정

| 유형 | 방식 |
|------|------|
| 비행체→건물 | 점-다각형 (Ray Casting) |
| 비행체→반구 | 점-반구 |
| 탄환→비행체 | 선분-AABB (Slab, 터널링 방지) |

## 코딩 규칙

- C# PascalCase. 설정: `IniPos`, `IniSpeed` 등. 런타임: `Pos`, `Pose`, `Phase`
- 시간값: `TInfinite`=이벤트 없음, `TContinue`=재스케줄링 생략
- 상태/타입 비교 시 string 대신 enum 사용
- 로깅: `Logger.Dbg(DbgFlag, msg)` — 플래그: `Init`, `Move`, `Collide`
