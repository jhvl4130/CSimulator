# CLAUDE.md

## 프로젝트 개요

CIWSSim — CIWS(근접방어무기체계) 3D 이산 사건 시뮬레이션. .NET 8.0 C# 솔루션.

```bash
dotnet build CIWSSim.sln
dotnet run --project src/CIWSSim.App                # 기본 scenario.json
dotnet run --project src/CIWSSim.App -- custom.json # 커스텀 시나리오
```

입력: `scenario.json` → 출력: `output.csv` (위치/자세), `event_log.csv` (교전 로그)

## 프로젝트 구조

```
src/
├── CIWSSim.Core/        # 시뮬레이션 프레임워크 (Engine, Model, Events, Geometry, Util)
├── CIWSSim.Models/      # 구체 모델 (아래 모델 목록 참조)
└── CIWSSim.App/         # 콘솔 앱 (Program.cs, ScenarioConfig.cs, scenario.json)
```

## 교전 흐름

```
SearchRadar(전체1) ──Detect──▶ C2Control(전체1) ──Assign──▶ FCS(CIWS당1)
  FCS ──TrackCmd──▶ TrackRadar ──TrackData──▶ FCS ──EotsCmd──▶ EOTS ──EotsData──▶ FCS
  FCS ──FireCmd──▶ Gun → Bullet ──Collide──▶ Target → HitResult──▶ FCS → PHP평가 → C2
  AssetZone 도달 = 요격실패 → FailEvent → C2 → FCS
```

CIWS 1세트 = FCS + TrackRadar + EOTS + Gun

## 모델 목록

| 모델 | Class | 역할 |
|------|-------|------|
| Airplane/Rocket/Launcher | Target | 비행/발사 |
| SearchRadar | Sensor | 탐지 → C2 |
| TrackRadar | Sensor | 추적 → FCS (25/50/100Hz) |
| EOTS | Sensor | 종속 추적, 정밀 각도 → FCS |
| C2Control | C2 | 위협순위, CIWS 할당, event_log 출력 |
| FCS | C2 | 추적/사격 명령, PHP 피해평가 |
| Bullet/Gun | Weapon | 탄환 생성/충돌판정 |
| Asset/AssetZone | Asset | 건물/요격실패 판정 |

## FCS 상태 머신 (PhaseType)

```
Wait → StartEngage → TrackRcvd → FireOn → TgtDestroyed → Wait
                                        → FireOff → Wait
피격: AttackedAlive / AttackedDie → Disabled
```

## 엔진 핵심

- 시간 버킷 스케줄링 (`SortedDictionary<long, List<Model>[]>`), TScale=10M
- 클래스 우선순위: Target → Sensor → C2 → Weapon → Asset
- 모델 생명주기: `Init(t)` → `IntTrans(t)` → `ExtTrans(t, event)`
- CSV StreamWriter 스트리밍 (메모리에 안 쌓음)
- `RemoveModel(id)`: Bullet 메모리 해제용

## 좌표계

- 내부: ENU (X=동, Y=북, Z=상). 외부: LLH (Lat/Lon/Hgt)
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
- 로깅: `Logger.Dbg(DbgFlag, msg)` — 플래그: `Init`, `Move`, `Collide`
