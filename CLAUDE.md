# CLAUDE.md

이 파일은 Claude Code (claude.ai/code)가 이 저장소에서 작업할 때 참고하는 가이드입니다.

## 프로젝트 개요

CIWSSim은 군사 방어 시나리오를 위한 3D 이산 사건 시뮬레이션입니다 (CIWS = Close-In Weapon System). 항공기, 로켓, 발사대(방사포), 방어 자산(건물)을 모델링하며 충돌 판정과 피해 처리를 수행합니다.

## 빌드 및 실행

```bash
dotnet build CIWSSim.sln
dotnet run --project src/CIWSSim.App
```

- **.NET 8.0** 기반 C# 솔루션, 3개 프로젝트로 구성
- **진입점**: `src/CIWSSim.App/Program.cs`

## 프로젝트 구조

```
src/
├── CIWSSim.Core/           # 핵심 시뮬레이션 프레임워크 (모델 비종속)
│   ├── Geometry/            # XYZPos, XYPos, LLHPos, Pose, XYZWayp, Building, CollisionDetection
│   ├── Events/              # SimEvent, CollideEvent
│   ├── Engine.cs            # 이산 사건 시뮬레이션 엔진
│   ├── Model.cs             # 추상 기본 모델 클래스
│   ├── Constants.cs         # SimConstants (시간, 페이즈, 모델 타입 상수)
│   └── Logger.cs            # DbgFlag 기반 디버그/경고/에러 로깅
├── CIWSSim.Models/          # 구체 모델 구현 (Core 의존)
│   ├── Airplane.cs
│   ├── Asset.cs
│   ├── Rocket.cs
│   ├── Launcher.cs
│   └── EngineExtensions.cs  # 확장 메서드: AddAirplane, AddAsset, AddLauncher
└── CIWSSim.App/             # 콘솔 앱 (Core + Models 의존)
    └── Program.cs
```

## 아키텍처

### 시뮬레이션 엔진 (Engine)

**시간 버킷 기반 이산 사건 시뮬레이션** 구현:
- 모델들은 `SortedDictionary<long, List<Model>[]>`에 스케줄링 — 시간 버킷 + 클래스 우선순위 정렬
- 시간은 부동소수점 비교 오류 방지를 위해 `TScale`(10M)을 곱해 `long`으로 변환
- `Engine.Start()` 메인 루프: 가장 이른 시간 버킷을 꺼내 클래스 우선순위(Platform → Sensor → C2 → Weapon → Asset) 순으로 `IntTrans()` 호출 후 반환값에 따라 재스케줄링
- 이동 주기: `MovePeriod` = 0.01초

### 모델 계층

모든 엔티티는 추상 클래스 `Model`을 상속하며 3개의 가상 메서드를 구현:
- `Init(t)` — 상태 초기화, 첫 이벤트 시간 반환
- `IntTrans(t)` — 자율적 상태 전이 (이동, 발사 등), 다음 이벤트 시간 반환
- `ExtTrans(t, event)` — 외부 이벤트에 대한 반응 (충돌 등)

구체 모델: `Airplane`, `Rocket`, `Launcher`, `Asset`

Engine(Core)은 Models를 직접 참조하지 않으며, 모델 생성은 `EngineExtensions.cs`의 확장 메서드를 통해 수행합니다.

### 좌표계

- **ENU 좌표계**: X=동, Y=북, Z=상
- **방위각(Azimuth)**: 0°=북(+Y), 90°=동(+X), 시계 방향
- **고각(Elevation)**: 0°=수평, 양수=위

### 충돌 판정

`CollisionDetection.IsCollide(XYZPos, Building)` — 3단계 판정: Z축 범위 → AABB(축 정렬 경계 상자) → 점-다각형 판정(Ray Casting). 건물은 오목 2D 다각형 풋프린트를 bottom/top 높이로 돌출시킨 형태입니다.

### 이벤트 시스템

`SimEvent` 기본 클래스와 피해 파워를 담는 `CollideEvent` 하위 클래스로 구성. `Engine.SendEvent()`가 대상 모델의 `ExtTrans()`를 호출합니다.

## 코딩 규칙

- **네이밍**: C# PascalCase 사용. 설정 프로퍼티: `IniPos`, `IniSpeed`, `IniAzimuth`, `IniElevation`, `StartT`. 런타임: `Pos`, `Pose`, `Phase`, `TA`
- **페이즈**: `PhaseWaitStart`(0), `PhaseRun`(1)
- **특수 시간값**: `TInfinite` = 더 이상 이벤트 없음, `TContinue` = 재스케줄링 생략
- **로깅**: `Logger.Dbg(DbgFlag, msg)`, `Logger.Warn(msg)`, `Logger.Err(msg)` — 플래그: `DbgFlag.Init`, `DbgFlag.Move`, `DbgFlag.Collide`
