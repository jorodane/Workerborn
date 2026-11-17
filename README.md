# WorkerBorn · 솔바람 마을

직접 일하며 생활 스킬과 경력을 쌓고, 환생을 거쳐 발전한 마을로 돌아오는 Unity 프로토타입입니다.

Unity **6000.3.5f2**, URP **17.3.0**, Input System **1.17.0**을 사용합니다. `Assets/Workerborn/Scenes/VillagePrototype.unity`를 열어 Play하거나 **Workerborn → Village Prototype → Play Village**를 선택하세요. 빌드의 첫 Scene도 동일합니다.

## Unity에서 직접 수정하기

마을, 건물, 길, 농장, NPC, 주인공, 카메라, 기본 HUD는 **Scene에 저장**되어 있습니다. Play 전부터 Hierarchy와 Scene 뷰에서 이동·회전·복제·삭제할 수 있습니다. 게임 시작이나 에셋 임포트 때 마을을 다시 생성하지 않습니다.

| 수정 대상 | 위치와 편집 방법 |
| --- | --- |
| 건물·소품·나무·길 | Scene의 `Village/Buildings`, `Nature`, `Paths`. 원본 에셋의 프리팹 인스턴스가 배치되어 있습니다. |
| 농장 | `Village/Farm`. 작물 루트를 복제·이동하고 `HarvestableCrop.Definition`을 지정합니다. 저장 ID는 Editor에서 중복 없이 부여합니다. |
| 확장 후 농장 | 비활성 `Stage 2 - expanded farm` 그룹. 기존 Scene 오브젝트가 세계 단계에 맞춰 활성화됩니다. |
| 작물별 작업 | `Data/Potato.asset`: 수확 가능 횟수, 작업 시간, 드롭 수, 수확물 ID와 프리팹. |
| NPC | `Data/NPCs/*.asset`: 이름, 역할, 외형, 대화, 아르바이트, 퀘스트, 판매 목록, 상호작용 거리. Scene NPC의 `VillageInteractable`과 `CharacterActor`가 같은 NPC 에셋을 참조합니다. |
| NPC 외형과 3D 미리보기 | NPC 에셋 Inspector의 **캐릭터 파츠와 애니메이션**. 아래 Preview를 드래그하면 회전하고 휠로 확대합니다. |
| 주인공 기본 외형 | `Data/Characters/Player_Default.asset`. NPC와 동일한 `CharacterDefinition` 형식입니다. `Data/VillageSettings.asset`의 Player에서도 바꿀 수 있습니다. |
| 개별 캐릭터 파츠 | `Data/Characters/Parts/*.asset`. 머리·의상·눈·눈썹·입·장식·손 소품별 프리팹, 부착 본, 위치·회전·크기, 재질과 색상. 외형 Inspector에는 해당 슬롯의 파츠만 목록으로 표시됩니다. |
| 시간·성장·휴식·환생 조건 | `Data/VillageSettings.asset`. 시간 배율, 기상·기절 시각, 숙박 가격, 회복량, 수확 경험치·소모량, AP, 환생 레벨. |
| 일자리 | `Data/Jobs/PotatoDelivery.asset`: 품목, 접수·납품 시간, 경력 등급별 수량·보상, 세계 단계별 추가 요구량. 농부 NPC에 연결합니다. |
| 퀘스트 | `Data/Quests/*.asset`: 목표·보상과 세계 기여 조건. 현재 지원되는 행동은 첫 일자리와 농장 확장입니다. |
| 대화 | `Data/Dialogues/*.asset`: 키워드, 문장, 연결 행동. NPC 에셋에 연결합니다. |
| 아이템 | `Data/Items/*.asset`: 이름·설명, 교체 가능한 아이콘, 크기, 겹침 수량, 가격, 장비 슬롯과 장착 외형. 골드도 실제 격자 아이템입니다. |
| 카메라 | `Main Camera/VillageCameraRig`: 초기 방향·거리, 회전 감도, 줌 범위, 충돌 레이어. |
| 화면과 지도 | Scene의 `Village UI`, `Minimap camera`, `Player/Equipment portrait camera`. 지도와 소지품의 캐릭터 그림은 실제 Scene 카메라의 RenderTexture입니다. |

외형 파츠가 비어 있으면 기본 프리팹의 해당 부분을 유지합니다. 파츠를 선택하면 원래 부분을 교체하고, 스킨 메시의 본을 이름으로 연결합니다. 공통 파츠 에셋의 변경은 그 파츠를 사용하는 외형에 공유되므로 개인 변형이 필요하면 에셋을 복제하세요. 기존 Idle·Walk·Attack_sword 애니메이션을 재사용합니다. 외형을 바꾸면 **Editor에서** Scene 외형도 갱신하고 저장할 수 있습니다. 기본 프리팹을 변경한 경우에도 같은 방식으로 적용합니다.

새 외형과 콘텐츠는 Project의 **Create → Workerborn** 메뉴에서 생성합니다. NPC 추가 시 기존 NPC 루트를 복제하고 새 NPC 에셋을 두 컴포넌트에 연결하세요. 현재 플레이 루프는 농장 일자리 한 종류와 그 경력을 공유합니다.

길찾기는 배치된 바닥 Collider와 `VillageNavigationBlocker`의 상자를 사용합니다. 건물을 움직일 때는 자식인 장애물도 함께 이동합니다. 크기가 다른 건물로 바꾸면 장애물 상자도 맞춰 주세요. `Village session/VillageNavigation`의 **현재 Scene의 길찾기 데이터 저장**으로 베이크할 수 있습니다. 베이크 데이터가 비어 있으면 Play 시 길찾기 데이터만 계산합니다. 확장 그룹과 마을 지형은 Scene에 이미 존재합니다.

## 조작

| 입력 | 동작 |
| --- | --- |
| 좌클릭 | 이동, 작물 수확, 바닥 물품 줍기, NPC와 대화 |
| 우클릭 드래그 | 3인칭 자유 시점의 좌우·상하 회전 |
| 마우스 휠 | 줌. 기본 거리 범위 1.8~22m |
| 짧은 우클릭 / Esc | 이동·미완료 작업 취소 |
| I / Q / Z 또는 K / C | 소지품 / 퀘스트 / 스킬 / 캐릭터 |
| M | 미니맵 표시·숨김. 하단 지도 버튼은 큰 지도를 엽니다. |
| 소지품 아이템 좌클릭 → 다시 좌클릭 | 커서로 집기 → 빈 칸에 놓기·겹치기·교환. 누른 채 드래그할 필요가 없습니다. |
| 들고 있는 물건 우클릭 / Esc | 원래 보관 자리로 복귀 |
| 아이템 우클릭 / Ctrl+클릭 | 사용·장착·나누기 메뉴 / 바로 사용·장착 |
| 왼쪽 장비 칸 클릭 | 들고 있는 장비 장착, 또는 장착 장비를 소지품으로 꺼내 들기 |
| 창 제목 표시줄 드래그 | 정보 창 이동 |
| 지도 클릭 | 해당 지점으로 이동 |

장비 칸은 소지품 왼쪽에 있습니다. 괭이는 **장착한 동안** 작업 효과를 줍니다. 골드는 한 칸에 1,000개씩 보관하며 거래·보상·기부는 실제 금화 수량을 바꿉니다. 소지품에 공간이 없으면 거래를 취소하고 수량을 보존합니다.

상시 HUD는 시계·미니맵·레벨·스태미나·경험치·얇은 창 메뉴로 정리했습니다. NPC 머리 위에는 이름을 표시합니다. 게임 시간 배율·생애 설명·오늘의 일자리 패널·F1 수확/F2 빵 버튼은 표시하지 않습니다. 시계는 폰트의 줄 높이에 의해 잘리지 않도록 조정했습니다.

UX의 기준은 마비노기의 [공식 Rookie Guide](https://mabibook.nexon.net/usa/book_rookie_guide.html)와 [Beginner Guide](https://nxcache.nexon.net/mabinogi/microsite/BeginnerGuide/)에 나오는 클릭 이동, 오른쪽 드래그 시점, 휠 줌, 클릭으로 집고 놓는 소지품, 장비 영역과 키워드 대화입니다.

## 첫 번째 삶

1. 서쪽 농장주 **미라**를 클릭해 다가간 뒤 아르바이트를 받습니다.
2. 작물을 클릭하면 이동해 머리 위 스킬 풍선을 띄우고 작업합니다. 기본값은 **작물당 3회, 회당 2.4초, 감자 2개**입니다. 스킬과 장착 도구, 럭키 보너스로 결과가 달라집니다.
3. **바닥에 떨어진 감자를 클릭해 줍습니다.** 수확만으로 소지품에 들어가지는 않습니다.
4. **18:00 전**에 감자 6개를 납품해 45G·경험치·경력을 받고 **첫 일자리** 퀘스트 보상도 받습니다.
5. 스킬 창에서 AP로 승급하고, **노아**에게 거래하며, **레나**의 여관에서 다음 날을 맞습니다. 무료 잠자리는 에너지 70, 10G 객실은 100으로 06:00에 시작합니다.
6. 첫 퀘스트 보상과 **레벨 3** 조건을 갖추면 북쪽 **기억의 우물**에서 환생할 수 있습니다.

기본 시간은 실제 1초에 게임 3분이며 대화 중에는 멈춥니다. 일반 정보 창을 여는 동안에는 흐릅니다. 자정이나 에너지 고갈 시 다음 날 에너지 45로 시작합니다. 잠을 자면 작물이 다시 준비됩니다.

환생하면 현재 레벨·경험치·소지품·골드·장비·진행 중인 의뢰를 초기화하고, 스킬·AP·누적 레벨·경력·이야기 완료·세계 기여를 유지합니다. **100G와 감자 20개**를 기부하면 다음 환생 때 기존 Scene의 확장 농장이 활성화되어 작물이 **12개에서 24개**로 늘고 의뢰 규모도 커집니다.

## 저장과 검증

`Application.persistentDataPath/workerborn-village-v1.json`에 15초마다, 주요 행동 후, 앱 일시정지·종료 시 저장합니다. 저장에는 아이템 배치·수량·장비, 위치, 시간, 성장, 의뢰, 퀘스트, 세계 변화, 작물의 고유 ID별 남은 횟수와 바닥 물품이 포함됩니다. 이전 지갑 방식의 골드는 금화 아이템으로 변환합니다. `.bak` 백업 복구도 지원합니다. 맵 배치나 작물 설정을 바꾸어 새 게임을 확인하려면 `Village session/Load Saved Game`을 꺼서 시험할 수 있습니다.

```bash
dotnet run --project Tests/HarvestLogic/HarvestLogic.csproj -- --noheader --nocolor --workers=0
python -m pip install -r Tests/requirements.txt
python Tests/validate_harvest_assets.py
```

.NET NUnit 테스트는 수확·시점 줌 계산·아이템 배치·거래의 수량 보존·장비·저장 변환·시간·마감·보상·성장·환생 규칙을 검증합니다. Unity EditMode에는 외형 파츠 조합과 본 연결 검증도 있으며, PlayMode에는 이동·대화·수확·납품·환생·저장 재로드 검증이 있습니다. 테스트 저장 폴더는 플레이어 데이터와 분리됩니다.

이 작업 환경에는 Unity Editor가 없어 실제 Unity 6 임포트·PlayMode 실행·미리보기 및 화면 검증은 수행하지 못했습니다. .NET 테스트, C# 구문·참조 컴파일, Scene 및 에셋 연결 정적 검사를 수행했습니다.

원본 에셋과 `SampleScene`은 보존합니다. `HarvestPrototype`은 기존 수확 전용 데모입니다. `Tools/author_village_scene.py`는 예제 Scene을 처음 작성한 명시적 오프라인 도구이며 Unity에서 실행되지 않습니다. 재실행은 예제 배치를 덮어쓰므로 일반 편집에는 Scene과 Inspector를 사용하세요.
