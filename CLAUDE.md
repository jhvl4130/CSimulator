# CLAUDE.md

이 파일은 Claude Code (claude.ai/code)가 이 저장소에서 작업할 때 참고하는 가이드입니다.

## 프로젝트 개요

CIWSSim은 군사 방어 시나리오를 위한 3D 이산 사건 시뮬레이션입니다 (CIWS = Close-In Weapon System). 항공기, 로켓, 발사대(방사포), 탄환, 방어 자산(건물/반구 방어존)을 모델링하며 충돌 판정과 피해 처리를 수행합니다.

## 빌드 및 실행

```bash
dotnet build CIWSSim.sln
dotnet run --project src/CIWSSim.App                    # 기본 scenario.json
dotnet run --project src/CIWSSim.App -- custom.json     # 커스텀 JSON 경로
```

- **.NET 8.0** 기반 C# 솔루션, 3개 프로젝트로 구성
- **진입점**: `src/CIWSSim.App/Program.cs`
- **입력**: JSON 시나리오 파일 (`scenario.json`)
- **출력**: CSV 파일 (`output.csv`) — 전체 모델의 시간별 상태 기록

## 프로젝트 구조

```
src/
├── CIWSSim.Core/           # 핵심 시뮬레이션 프레임워크 (모델 비종속)
│   ├── Geometry/            # XYZPos, XYPos, LLHPos, Pose, XYZWayp, Building,
│   │                        # CollisionDetection, GeoUtil, WaypointMover
│   ├── Util/                # Logger, FileIO, StateRecord, SimTimer
│   ├── Events/              # SimEvent, CollideEvent
│   ├── Engine.cs            # 이산 사건 시뮬레이션 엔진 + 상태 기록/CSV 출력
│   ├── Model.cs             # 추상 기본 모델 클래스
│   └── Constants.cs         # SimClass, SimPhase enum, SimConstants
├── CIWSSim.Models/          # 구체 모델 구현 (Core 의존)
│   ├── Airplane.cs          # WaypointMover 기반 Fly-Over 이동
│   ├── Asset.cs
│   ├── AssetZone.cs         # 반구 영역 방어 모델 (점-반구 충돌 판정)
│   ├── Rocket.cs
│   ├── Launcher.cs
│   ├── Bullet.cs            # 탄환 모델 (외부 궤적 보간 + 선분-AABB 충돌 판정)
│   ├── BulletPoint.cs       # 탄환 궤적 데이터 구조체 (Time, Pos)
│   └── EngineExtensions.cs  # 확장 메서드: LLH 입력 지원 (AddAirplane, AddAssetBox, AddBullet 등)
└── CIWSSim.App/             # 콘솔 앱 (Core + Models 의존)
    ├── Program.cs           # JSON 로드 → Engine 등록 → 시뮬레이션 실행
    ├── ScenarioConfig.cs    # JSON DTO 클래스
    └── scenario.json        # 시나리오 예시
```

## 아키텍처

### 입출력 흐름

```
scenario.json → FileIO.LoadJson<ScenarioConfig>()
              → LLH→ENU 변환 (GeoUtil.LlaToEnu, Engine.Origin 기준)
              → Engine에 모델 등록
              → Engine.Start() 시뮬레이션 실행
              → 매 시간 버킷마다 활성 모델 상태 기록 (StateRecord)
              → 시뮬레이션 종료 시 ENU→LLH 변환하여 output.csv 출력
```

### 시뮬레이션 엔진 (Engine)

**시간 버킷 기반 이산 사건 시뮬레이션** 구현:
- 모델들은 `SortedDictionary<long, List<Model>[]>`에 스케줄링 — 시간 버킷 + 클래스 우선순위 정렬
- 시간은 부동소수점 비교 오류 방지를 위해 `TScale`(10M)을 곱해 `long`으로 변환
- `Engine.Start()` 메인 루프: 가장 이른 시간 버킷을 꺼내 클래스 우선순위(Target → Sensor → C2 → Weapon → Asset) 순으로 `IntTrans()` 호출 후 반환값에 따라 재스케줄링
- 매 시간 버킷 처리 후 `RecordAll()`로 활성 모델 전체 상태 기록
- 시뮬레이션 종료 시 `ExportCsv()`로 단일 CSV 출력
- 이동 주기: `MovePeriod` = 0.01초
- `Engine.Origin`: ENU 원점 LLH 좌표 (LLH↔ENU 변환 기준점, 유효 범위 ~50km)

### 모델 계층

모든 엔티티는 추상 클래스 `Model`을 상속하며 3개의 가상 메서드를 구현:
- `Init(t)` — 상태 초기화, 첫 이벤트 시간 반환
- `IntTrans(t)` — 자율적 상태 전이 (이동, 발사 등), 다음 이벤트 시간 반환
- `ExtTrans(t, event)` — 외부 이벤트에 대한 반응 (충돌 등)

구체 모델: `Airplane`, `Rocket`, `Launcher`, `Bullet`, `Asset`, `AssetZone`

Engine(Core)은 Models를 직접 참조하지 않으며, 모델 생성은 `EngineExtensions.cs`의 확장 메서드를 통해 수행합니다.

### 이동 로직 (WaypointMover)

`Geometry/WaypointMover.cs` — Fly-Over 방식 웨이포인트 추종:
- 웨이포인트에 스냅하지 않고 선회율(`TurnRate`) 제한 내에서 자연스럽게 회전하며 통과
- Pitch가 위치 이동에 반영 (Speed×cos(pitch)=수평, Speed×sin(pitch)=수직)
- 통과 판정: 진행 방향 벡터와 (현재→웨이포인트) 벡터의 내적 ≤ 0
- `Airplane`, `Drone` 등 여러 모델에서 재사용 가능

### 좌표계

- **ENU 좌표계**: X=동, Y=북, Z=상 (시뮬레이션 내부)
- **LLH 좌표**: Lat/Lon/Hgt (외부 입출력)
- **방위각(Azimuth)**: 0°=북(+Y), 90°=동(+X), 시계 방향
- **고각(Elevation)**: 0°=수평, 양수=위
- `GeoUtil.LlaToEnu()` / `GeoUtil.EnuToLla()` — LLH↔ENU 변환

### 충돌 판정

두 가지 충돌 판정 방식을 사용:

| 충돌 유형 | 판정 방식 | 메서드 |
|-----------|----------|--------|
| 비행체/로켓 → 건물 | 점-다각형 (3단계: Z축→AABB→Ray Casting) | `IsCollide(XYZPos, Building)` |
| 비행체 → 반구 방어존 | 점-반구 | `IsInsideHemisphere(XYZPos, XYZPos, radius)` |
| 탄환 → 비행체 | 선분-AABB (Slab method) | `IsSegmentAABB(p0, p1, center, halfX/Y/Z)` |

- **점 기반 판정**: 저속 물체(비행체 ~250m/s) vs 대형 대상(건물 수십m). 100Hz에서 충분한 정확도
- **선분-AABB 판정**: 고속 탄환(~1000m/s+) vs 소형 대상(비행체 ~12m). 터널링 방지를 위해 이전 위치→현재 위치 선분으로 교차 판정
- 비행체의 AABB는 `Model.HalfX/Y/Z`로 정의 (바운딩 박스 반크기)

### Bullet (탄환) 모델

외부 시스템에서 궤적 데이터(`List<BulletPoint>`)를 일괄 전달받아 동작:
- 자체 이동 로직 없음 — 외부 궤적 데이터의 위치를 직접 사용
- `MovePeriod`(100Hz)로 스케줄링되며, 시뮬레이션 시간과 궤적 시간이 불일치 시 **선형 보간**
- 매 틱마다 이전 보간 위치 → 현재 보간 위치 선분으로 Target의 AABB와 충돌 판정
- 궤적 소진 또는 명중 시 자동 종료
- `AddRuntimeModel()`로 시뮬레이션 도중 동적 등록

### 이벤트 시스템

`SimEvent` 기본 클래스와 피해 파워를 담는 `CollideEvent` 하위 클래스로 구성. `Engine.SendEvent()`가 대상 모델의 `ExtTrans()`를 호출합니다.

## 코딩 규칙

- **네이밍**: C# PascalCase 사용. 설정 프로퍼티: `IniPos`, `IniSpeed`, `IniAzimuth`, `IniElevation`, `StartT`. 런타임: `Pos`, `Pose`, `Phase`, `TA`
- **페이즈**: `PhaseType.WaitStart`, `PhaseType.Run`, `PhaseType.End`
- **클래스**: `ModelClass.Target`, `ModelClass.Sensor`, `ModelClass.C2`, `ModelClass.Weapon`, `ModelClass.Asset`
- **특수 시간값**: `TInfinite` = 더 이상 이벤트 없음, `TContinue` = 재스케줄링 생략
- **로깅**: `Logger.Dbg(DbgFlag, msg)`, `Logger.Warn(msg)`, `Logger.Err(msg)` — 플래그: `DbgFlag.Init`, `DbgFlag.Move`, `DbgFlag.Collide` (namespace: `CIWSSim.Core.Util`)
- **파일 입출력**: `FileIO.LoadJson<T>()`, `FileIO.SaveJson()`, `FileIO.SaveCsv()` (namespace: `CIWSSim.Core.Util`)

## JSON 시나리오 포맷

```json
{
  "origin": { "lat": 37.0, "lon": 126.0, "alt": 0.0 },
  "simEndTime": 100.0,
  "airplanes": [
    {
      "id": 1,
      "position": { "lat": 37.0, "lon": 125.995, "alt": 500.0 },
      "size": { "lengthX": 12.0, "widthY": 10.0, "heightZ": 4.0 },
      "speed": 250.0, "azimuth": 90.0, "elevation": 0.0, "startT": 0.0,
      "waypoints": [
        { "lat": 37.0, "lon": 126.001, "alt": 300.0, "speed": 200.0 }
      ]
    }
  ],
  "buildings": [
    {
      "id": 101,
      "sw": { "lat": 36.9998, "lon": 126.003 },
      "ne": { "lat": 37.0002, "lon": 126.004 },
      "bottom": 0.0, "top": 25.0
    }
  ]
}
```

## CSV 출력 포맷

단일 파일 (`output.csv`)에 전체 모델의 시간별 상태가 기록됨. ID와 Type으로 모델 구분.
파괴(`IsEnabled=false`)된 모델은 그 시점부터 기록 중단.

```
Time,ID,Type,Lat,Lon,Alt,Roll,Pitch,Yaw
```
