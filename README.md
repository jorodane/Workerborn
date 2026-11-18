# WorkerBorn · 솔바람 마을

직접 일하며 생활 스킬과 경력을 쌓고, 환생을 거쳐 발전한 마을로 돌아오는 Unity 프로토타입입니다.

Unity **6000.3.5f2**, URP **17.3.0**, Input System **1.17.0**을 사용합니다. `Assets/Workerborn/Scenes/VillagePrototype.unity`를 열어 Play하거나 **Workerborn → Village Prototype → Play Village**를 선택하세요. 빌드의 첫 Scene도 동일합니다.

## Unity에서 직접 수정하기

마을은 Scene에 배치된 **완성된 프리팹 인스턴스**로 구성합니다. Play에서 마을이나 창 레이아웃을 새로 만들지 않습니다. Scene에서 위치·회전과 개별 값을 바꾸거나, Prefab Mode에서 공통 모양·Collider·UI 구성을 수정하세요.

| 수정 대상 | 에셋 |
| --- | --- |
| 소지품·스킬·퀘스트·캐릭터·지도·상점·대화 등 각 창 | `Assets/Workerborn/Prefabs/UI/*Window.prefab` |
| 퀘스트 스크롤·툴팁·아이템 메뉴 | `Prefabs/UI/QuestScrollWindow.prefab`, `ItemTooltip.prefab`, `ItemContext.prefab` |
| 시계·미니맵·하단 메뉴·월드 이름 레이어 | `Prefabs/UI/VillageHUD.prefab` |
| 아이템 칸·아이콘·목록 행·버튼·이름 박스·대상 연결선 | `Prefabs/UI/InventoryCell.prefab`, `ItemTile.prefab`, `ListRow.prefab`, `ActionButton.prefab`, `PickupName.prefab`, `TargetLink.prefab` |
| 창과 위젯 프리팹 연결 목록 | `Data/VillageUI.asset` |
| 주인공과 NPC 전체 | `Prefabs/Characters/Player.prefab`, `Mira.prefab`, `Noah.prefab`, `Lena.prefab` |
| 게시판·우물·몬스터 예제 | `Prefabs/Interaction/Board.prefab`, `Shrine.prefab`, `Monster.prefab` |
| 감자 작물 전체 | `Prefabs/Crops/Potato.prefab` |
| 바닥 아이템 전체 | `Prefabs/Items/potato.prefab`, `bread.prefab`, `hoe.prefab`, `bag.prefab`, `gold.prefab` |
| 건물·가로등·나무·소품과 충돌 영역 | `Prefabs/World/*.prefab` |
| NPC 설정·대화·일자리·퀘스트·거래 품목 | `Data/NPCs`, `Data/Dialogues`, `Data/Jobs`, `Data/Quests`, `Data/Items` |
| NPC와 주인공의 부위별 외형·기존 애니메이션 | `Data/Characters/*.asset`, `Data/Characters/Parts/*.asset` |
| 시간·휴식·성장·환생 기본값 | `Data/VillageSettings.asset` |
| 수확 횟수·작업 시간·드롭 수·바닥 아이템 프리팹 | `Data/Potato.asset` |
| 카툰 재질과 원본 재질의 대응 | `Data/ToonPalette.asset`, `Materials/Toon/*.mat` |
| 외곽선 Renderer Feature와 렌더 파이프라인 | `Rendering/PC_Renderer.asset`, `Mobile_Renderer.asset`, 각 `*_Pipeline.asset` |

표의 짧은 경로는 모두 `Assets/Workerborn/` 아래에 있습니다. UI 창에는 제목 표시줄뿐 아니라 실제 내부 TextMeshProUGUI·Image·Button·RawImage·ScrollRect와 참조가 들어 있습니다. 실행 중에는 값과 이벤트를 연결하고, 목록 행·격자 아이콘처럼 수량이 바뀌는 요소만 지정된 위젯 프리팹으로 복제합니다. 프리팹에서 정한 위치·색·폰트·투명도·버튼 구성을 창을 열 때 지우고 다시 만들지 않습니다. 소지품 셀 크기는 `InventoryWindow` 안의 GridLayoutGroup에서 조정합니다.


마을은 `Prefabs/World/ForestTerrain.prefab`과 `World/ForestVillageTerrain.asset`에 저장한 **120×100m Unity Terrain** 위에 있습니다. Terrain 도구로 높이·지형 페인팅을 직접 수정할 수 있습니다. 기존 FarmCrops의 잔디·흙·자갈 TerrainLayer와 FlatKit Valley의 지형 머티리얼을 재사용합니다. 이전 평면 Ground·Paths·Soil row 오브젝트는 비활성 상태로 보관합니다. 숲의 나무 127개·관목 36개·바위 10개는 Synty 원본에 연결된 완성 프리팹 인스턴스입니다. 농장과 건물 기초는 완만한 평지로 다듬고 주변은 낮은 능선으로 둘렀습니다. 높이 수정 후 **VillageNavigation Inspector의 “현재 Scene의 길찾기 데이터 저장” 버튼**으로 이동 영역을 다시 베이크할 수 있습니다.

수확 중에는 `Prefabs/UI/SkillBalloon.prefab`의 **원형 아이콘과 위에서 시계방향으로 채워지는 게이지**만 표시합니다. 아이콘·크기·색·높이는 프리팹에서 수정합니다. 이동 중 새 목적지를 클릭해도 걷기 애니메이션의 재생 위치를 초기화하지 않습니다.

NPC 설정 에셋에서는 외형을 펼쳐 머리·의상·눈·눈썹·입·양손 파츠를 고르고 3D 미리보기를 드래그·휠로 회전·확대할 수 있습니다. 주인공은 `Data/Characters/Player_Default.asset`을 같은 형식으로 사용합니다. 파츠가 비어 있으면 기본 프리팹의 부분을 유지하고, 스킨 메시의 본은 이름으로 연결합니다. 공통 파츠 에셋은 사용하는 외형에 공유되므로 개인 변형은 복제해서 사용하세요. 외형 변경과 새 프리팹 배치 시 캐릭터 외형은 **Edit 모드에서** 갱신합니다.

상호작용은 공통 `WorldInteractable`을 상속합니다.

- `VillageInteractable`: NPC·게시판·우물. NPC의 ScriptableObject로 대화·역할을 설정합니다.
- `HarvestableCrop`: 이동·수확. 작물 ScriptableObject로 횟수·시간·드롭을 설정합니다.
- `GroundPickup`: 1m 안에서는 별도 줍기 동작 없이 즉시 획득합니다. 멀리 있는 물품은 먼저 접근하며, 범위 안의 물품 획득은 진행 중인 수확·이동을 취소하지 않습니다. 아이템·수량·시각 오브젝트·비행 시간을 프리팹에 저장합니다.
- `MonsterInteractable`: Ctrl 선택과 접근. 도착 후 행동은 공통 완료 이벤트 또는 `OnArrived` 재정의로 확장합니다. 전투는 구현하지 않았습니다.

공통 상호작용 거리·이름 위치·요청/완료 UnityEvent를 Inspector에서 설정할 수 있습니다. 새 유형은 `Interact`를 재정의하고 기존 `HarvestWorker` 명령을 연결합니다. 몬스터 예제는 제공된 도마뱀 에셋을 사용하며 Scene에는 자동 배치하지 않습니다. 프리팹을 직접 배치해 선택과 접근을 시험할 수 있습니다.

작물과 Scene에 놓은 아이템은 고유 ID로 저장합니다. 복제하면 Editor에서 새 ID를 부여해 서로 다른 개체의 수확 상태가 공유되지 않게 합니다. 새 외형·NPC·아이템 등의 데이터는 **Create → Workerborn**에서 생성합니다. NPC 프리팹의 CharacterActor와 VillageInteractable에 같은 NPC 에셋을 연결하세요. 현재 농장 일자리는 한 종류와 그 경력을 공유합니다.

길찾기는 배치한 바닥 Collider와 `VillageNavigationBlocker` 상자로 계산합니다. 건물을 움직일 때 자식 장애물도 함께 움직입니다. `Village session/VillageNavigation` Inspector에서 경로를 베이크할 수 있으며, 비어 있으면 Play 시 경로 데이터만 계산합니다.

## 카툰 렌더링과 대상·아이템 표시

준비된 **FlatKit/Stylized Surface** 셰이더로 프로젝트 전용 카툰 재질을 만들었습니다. 원본 텍스처·색·UV·알파 컷아웃은 유지하고, 기존 FlatKit **ObjectOutlineRendererFeature**를 PC·모바일 Renderer에 연결했습니다. 원본 에셋팩의 재질은 수정하지 않습니다. 외형 미리보기와 새로 배치한 프리팹도 `ToonPalette` 매핑을 사용합니다. 새로운 원본 재질을 쓸 때는 카툰 재질을 직접 지정하거나 매핑에 추가하세요.

월드 대상에 마우스를 올리거나 Alt 이름 박스를 가리키면 같은 `PointerTargeting`이 대상의 외곽선을 강조합니다. 누르고 있는 동안에는 대상 강조색이 바뀌고, **Alt가 눌려 있을 때만** 이름 박스의 배경·호버·누름 색과 클릭 영역이 활성화됩니다. 평소에는 원래 투영 위치에 텍스트만 표시합니다. 커서 우하단에는 “대화하기”, “수확하기”, “줍기” 같은 행동만 표시합니다. 수확이 끝난 작물은 자신의 Collider를 비활성화하므로 마우스 레이캐스트에서도 제외되며, 다음 날 수확 횟수를 초기화하면 다시 감지됩니다.

Ctrl을 누르면 화면에 보이는 NPC·몬스터 중 커서와 가장 가까운 대상의 윤곽을 강조합니다. 깊이/ID 마스크로 보이는 실루엣을 선택하며, 선은 대상의 **캡슐 중심점**을 향합니다. 매 프레임 실루엣을 마스킹해 캐릭터 위에는 선이 그려지지 않습니다. Ctrl+클릭도 같은 상호작용 경로를 사용합니다. 색·굵기·ID 읽기 빈도는 `Village session/PointerTargeting`, 선 모양은 `TargetLink.prefab`에서 수정합니다. 비동기 GPU 읽기를 지원하지 않는 기기는 동기 읽기를 사용합니다.

입력은 **Input System의 `Input/Workerborn.inputactions`**에서 Gameplay·Camera·UI 맵으로 수정합니다. Scene의 `WorkerbornInput`과 EventSystem의 `InputSystemUIInputModule`이 이 에셋을 공유합니다. UI 입력 참조도 `Input/UI/*.asset`으로 연결돼 있습니다. 장치 API를 직접 조회하는 게임 코드는 액션 조회로 교체했습니다.

게임 내 텍스트는 TMP입니다. `Fonts/WorkerbornKorean-SDF.asset`은 포함된 한글 OTF를 사용하는 동적 SDF 폰트이며 여러 아틀라스를 지원합니다. 이름표·행동 안내의 검은 외곽선은 `Fonts/WorkerbornKorean-NameOutline.mat`, 일반 UI 외곽선은 폰트 에셋의 Material에서 조정합니다.

카메라는 지정한 줌 거리를 유지합니다. `Main camera/CameraVisibility`가 플레이어를 가리는 일반 배경의 렌더러를 메인 카메라에서만 숨깁니다. 배경 판정에는 Renderer bounds와 두 시선 지점을 사용합니다. 갑자기 사라지는 대신 지정한 시간에 걸쳐 몸체와 외곽선이 함께 페이드됩니다. 임시 머티리얼과 색상은 메인 카메라의 렌더 동안에만 적용하고 복구합니다. NPC·작물·아이템이 플레이어를 가려도 숨기지 않으며, 캐릭터·아이템은 여유 공간을 확장한 캡슐과 모델의 전체 bounds, 카메라 near plane까지 함께 판정해 관통 전에 페이드를 시작합니다. 감자의 회전·부유, 머리카락·모자·장비도 판정에 포함됩니다. 숨긴 배경은 마우스 대상 감지에서 건너뛰고, 미니맵·초상화·충돌·내비게이션은 그대로 작동합니다. 캡슐 크기는 각 캐릭터·아이템 프리팹에서 수정합니다.

바닥 아이템 이름은 항상 표시합니다. **Alt를 누르는 동안** 검은 반투명 이름 박스가 나타나고, 박스를 클릭해도 실제 아이템과 같은 경로로 접근·획득합니다. 획득 거리는 아이템 프리팹의 공통 Interaction Distance에서 수정하며 기본값은 1m입니다. 떨어지는 도중에도 획득할 수 있습니다. **Alt를 누르는 동안만** 박스는 화면 가장자리와 겹침을 보정하며, 화면상 높이가 같은 항목은 **생성 순서대로 위→아래** 정렬합니다. 한 화면의 높이보다 큰 더미는 Alt를 누르고 이름 위에서 휠을 돌려 세로로 살펴볼 수 있습니다. 글꼴·여백·일반·호버·누름 색상과 투명도는 `PickupName.prefab`에서 조정합니다.

## 조작

| 입력 | 동작 |
| --- | --- |
| 좌클릭 | 이동, 작물 수확, 바닥 물품으로 접근·획득, NPC와 대화 |
| 우클릭 또는 휠 버튼 드래그 | 3인칭 자유 시점의 좌우·상하 회전 |
| 마우스 휠 | 줌. 기본 거리 범위 1.8~22m |
| Ctrl / Ctrl+클릭 | 가까운 NPC·몬스터의 윤곽과 연결선 / 해당 대상에게 접근 |
| Alt / Alt 이름 박스 클릭 | 바닥 물품의 검정 반투명 이름 박스 표시 / 해당 물품 줍기 |
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
python Tests/validate_prefab_contracts.py
python Tests/validate_camera_graphs.py
```

.NET NUnit 테스트 76개는 수확·시점 줌·이름 박스 배치와 생성 순서·윤곽의 가까운 점·아이템 배치·수량 거래·장비·저장 변환·시간·마감·보상·성장·환생 규칙을 검증합니다. Unity EditMode에는 외형 파츠 조합·본 연결·프리팹의 사전 배치와 UI 구성 검증이 있으며, PlayMode에는 이동·대화·수확·납품·환생·저장 재로드·프리팹 창 연결·이름 클릭 즉시 줍기·수확 유지·고갈 작물의 Collider·이름 박스 상태 색상·카메라 가림 검증이 있습니다. 테스트 저장 폴더는 플레이어 데이터와 분리됩니다.

이 작업 환경에는 Unity Editor가 없어 실제 Unity 6 임포트·PlayMode 실행·미리보기 및 화면 검증은 수행하지 못했습니다. .NET 테스트, C# 구문·참조 컴파일, Scene 및 에셋 연결 정적 검사를 수행했습니다.

원본 에셋과 `SampleScene`은 보존합니다. `HarvestPrototype`은 기존 수확 전용 데모입니다. `Tools/author_*`는 예제 에셋을 작성할 때 사용한 명시적 오프라인 도구입니다. Unity의 import·Play에서는 실행되지 않습니다. 재실행은 예제 배치를 덮어쓰므로 일반 작업에는 Scene과 Prefab Mode를 사용하세요.
